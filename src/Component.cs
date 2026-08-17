using LiveSplit.Model;
using LiveSplit.UI;
using LiveSplit.UI.Components;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

namespace LiveSplit.ASLUpdater.src
{
    public class Component : IComponent
    {
        private enum UpdateResult
        {
            NoUrl,
            UpToDate,
            Declined,
            Updated,
            InvalidContent
        }

        private readonly LiveSplitState state;
        private readonly Settings settings;

        private bool startupChecked;
        private bool isWorking;

        public string ComponentName => "ASL Updater";

        public float VerticalHeight => 0f;
        public float HorizontalWidth => 0f;
        public float MinimumWidth => 0f;
        public float MinimumHeight => 0f;
        public float PaddingTop => 0;
        public float PaddingBottom => 0;
        public float PaddingLeft => 0;
        public float PaddingRight => 0;
        public IDictionary<string, Action> ContextMenuControls => null;

        public Component(LiveSplitState state)
        {
            this.state = state;
            settings = new Settings();

            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            settings.ManualCheckRequested += OnManualCheckRequested;
        }

        private void OnManualCheckRequested(object sender, EventArgs e)
        {
            _ = CheckForUpdatesAsync(true);
        }

        public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
        {
            if (startupChecked) return;
            startupChecked = true;
            _ = CheckForUpdatesAsync(false);
        }

        public async Task CheckForUpdatesAsync(bool manual)
        {
            if (isWorking) return;
            isWorking = true;

            settings.SetStatus("Scanning active ASL script...", Color.Yellow);

            try
            {
                var scripts = GetLoadedAslComponents();
                if (scripts.Count == 0)
                {
                    HandleNoComponentsFound(manual);
                    return;
                }

                var (updated, declined, noUrl, invalid, availableVer) = await ProcessAllScriptsAsync(scripts);
                RenderStatusFeedback(manual, updated, declined, noUrl, invalid, availableVer, scripts.Count);
            }
            catch (Exception ex)
            {
                settings.SetStatus("Error checking for updates.", Color.Crimson);
                if (manual)
                    MessageBox.Show("Error checking for ASL updates:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                isWorking = false;
            }
        }

        private void HandleNoComponentsFound(bool manual)
        {
            settings.SetStatus("No loaded Scriptable Auto Splitter found in Layout.", Color.Crimson);
            if (manual)
                MessageBox.Show("No active Scriptable Auto Splitter component was found in your layout.", "ASL Updater", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async Task<(int Updated, int Declined, int NoUrl, int Invalid, string AvailableVer)> ProcessAllScriptsAsync(List<(IComponent Comp, string Path)> scripts)
        {
            int updated = 0, declined = 0, noUrl = 0, invalid = 0;
            string availableVer = null;

            using (var handler = new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 10 })
            using (var http = new HttpClient(handler))
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) LiveSplit-ASLAutoUpdate");

                foreach (var (comp, path) in scripts)
                {
                    var (res, verStr) = await ProcessScriptCheckAsync(http, comp, path);
                    if (res == UpdateResult.Updated) updated++;
                    else if (res == UpdateResult.Declined)
                    {
                        declined++;
                        if (string.IsNullOrEmpty(availableVer)) availableVer = verStr;
                    }
                    else if (res == UpdateResult.NoUrl) noUrl++;
                    else if (res == UpdateResult.InvalidContent) invalid++;
                }
            }

            return (updated, declined, noUrl, invalid, availableVer);
        }

        private async Task<(UpdateResult Result, string VersionStr)> ProcessScriptCheckAsync(HttpClient http, IComponent comp, string localPath)
        {
            if (!File.Exists(localPath))
                return (UpdateResult.UpToDate, null);

            string localContent = File.ReadAllText(localPath);
            string rawMetaUrl = ExtractUpdateUrl(localContent);
            if (string.IsNullOrWhiteSpace(rawMetaUrl))
                return (UpdateResult.NoUrl, null);

            if (!rawMetaUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Trace.WriteLine("[ASL-Updater] Rejected non-HTTPS UpdateUrl: " + rawMetaUrl);
                return (UpdateResult.NoUrl, null);
            }

            string targetUrl = ResolveGitHubUrl(rawMetaUrl);
            settings.SetStatus("Checking update for " + Path.GetFileName(localPath) + "...", Color.DarkOrange);

            var (remoteContent, remoteFileName, tagName) = await FetchRemoteScriptAsync(http, targetUrl);

            if (!IsValidAslContent(remoteContent, out string validationError))
            {
                System.Diagnostics.Trace.WriteLine("[ASL-Updater] Validation failed: " + validationError);
                return (UpdateResult.InvalidContent, null);
            }

            if (ComputeSha256(localContent) == ComputeSha256(remoteContent))
                return (UpdateResult.UpToDate, null);

            string displayVer = !string.IsNullOrEmpty(tagName) ? tagName : (!string.IsNullOrEmpty(remoteFileName) ? remoteFileName : Path.GetFileName(localPath));

            if (!settings.AutoUpdateSilently && !PromptUpdateConfirmation(localPath, remoteFileName))
                return (UpdateResult.Declined, displayVer);

            ApplyScriptUpdate(comp, localPath, remoteContent, remoteFileName);
            return (UpdateResult.Updated, displayVer);
        }

        private static bool IsValidAslContent(string content, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(content))
            {
                error = "Downloaded content is empty.";
                return false;
            }

            if (Encoding.UTF8.GetByteCount(content) > 5 * 1024 * 1024)
            {
                error = "Downloaded content exceeds 5MB size limit.";
                return false;
            }

            if (content.Contains("\0") || content.StartsWith("MZ"))
            {
                error = "Downloaded content is a binary executable, not a text script.";
                return false;
            }

            string trimmed = content.TrimStart();

            if (trimmed.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            {
                error = "Downloaded content is an HTML web page.";
                return false;
            }

            if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
            {
                error = "Downloaded content is raw JSON metadata, not an ASL script.";
                return false;
            }

            return true;
        }

        private bool PromptUpdateConfirmation(string localPath, string remoteFileName)
        {
            string currentFile = Path.GetFileName(localPath);
            string incomingFile = !string.IsNullOrEmpty(remoteFileName) ? remoteFileName : currentFile;

            settings.SetStatus("Update available for " + currentFile + "!", Color.OrangeRed);

            string prompt = !string.Equals(currentFile, incomingFile, StringComparison.OrdinalIgnoreCase)
                ? string.Format("An update is available for your auto-splitter script!\n\nCurrently Loaded: {0}\nNew Version Available: {1}\n\nDo you want to update now?", currentFile, incomingFile)
                : string.Format("An update is available for auto-splitter:\n\n{0}\n\nDo you want to update it now?", currentFile);

            var choice = MessageBox.Show(prompt, "ASL Update Available", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            return choice == DialogResult.Yes;
        }

        private void ApplyScriptUpdate(IComponent comp, string localPath, string content, string remoteFileName)
        {
            string writePath = localPath;

            if (!string.IsNullOrEmpty(remoteFileName))
            {
                string safeFileName = Path.GetFileName(remoteFileName);

                if (safeFileName.EndsWith(".asl", StringComparison.OrdinalIgnoreCase))
                {
                    string dir = Path.GetDirectoryName(localPath);
                    writePath = Path.Combine(dir ?? "", safeFileName);
                }
            }

            if (settings.CreateBackup && File.Exists(localPath))
            {
                File.Copy(localPath, localPath + ".bak", true);
            }

            File.WriteAllText(writePath, content, Encoding.UTF8);
            RepointAslComponent(comp, localPath, writePath);
        }

        private void RenderStatusFeedback(bool manual, int updated, int declined, int noUrl, int invalid, string availableVer, int total)
        {
            if (updated > 0)
            {
                settings.SetStatus("Successfully updated ASL script!", Color.ForestGreen);
                MessageBox.Show("Updated ASL script successfully!\n\nLiveSplit has loaded the new version.", "ASL Updater", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (declined > 0)
            {
                string statusMsg = !string.IsNullOrEmpty(availableVer) ? ("Update available (" + availableVer + ")") : "Update available.";
                settings.SetStatus(statusMsg, Color.OrangeRed);
                return;
            }

            if (invalid > 0)
            {
                settings.SetStatus("No valid .asl file found at UpdateUrl.", Color.Red);
                if (manual)
                    MessageBox.Show("Could not find a valid .asl script at the provided UpdateUrl.\n\nPlease check that the link points to a repository or release containing a .asl file.", "ASL Updater", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (noUrl == total)
            {
                settings.SetStatus("ASL Script does not support auto-updates", Color.Red);
                if (manual)
                    MessageBox.Show("The loaded ASL script does not support auto-updates because it lacks a '// UpdateUrl:' header at the top.", "ASL Updater", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            settings.SetStatus("Your ASL Script is up to date.", Color.ForestGreen);
            if (manual)
                MessageBox.Show("Your ASL script is already up to date.", "ASL Updater", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async Task<(string Content, string FileName, string TagName)> FetchRemoteScriptAsync(HttpClient http, string url)
        {
            using (var resp = await http.GetAsync(url))
            {
                resp.EnsureSuccessStatusCode();
                string payload = await resp.Content.ReadAsStringAsync();

                if (TryParseGitHubApiRelease(payload, out string dlUrl, out string parsedFile, out string tagName))
                {
                    System.Diagnostics.Trace.WriteLine("[ASL-Updater] GitHub API resolved to asset: " + dlUrl);
                    var (content, fn) = await DownloadAssetAsync(http, dlUrl, parsedFile);
                    return (content, fn, tagName);
                }

                return (payload, ExtractFileName(resp), null);
            }
        }

        private static async Task<(string Content, string FileName)> DownloadAssetAsync(HttpClient http, string assetUrl, string fallbackName)
        {
            using (var assetResp = await http.GetAsync(assetUrl))
            {
                assetResp.EnsureSuccessStatusCode();
                string scriptBody = await assetResp.Content.ReadAsStringAsync();
                string fileName = !string.IsNullOrEmpty(fallbackName) ? fallbackName : ExtractFileName(assetResp);
                return (scriptBody, fileName);
            }
        }

        private static string ExtractFileName(HttpResponseMessage msg)
        {
            string name = msg?.Content?.Headers?.ContentDisposition?.FileName;
            if (!string.IsNullOrEmpty(name))
                return name.Trim('"');

            var uri = msg?.RequestMessage?.RequestUri;
            return uri != null ? Path.GetFileName(uri.AbsolutePath) : null;
        }

        private static string ResolveGitHubUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            if (!url.Contains("github.com") || url.Contains("raw.githubusercontent") || url.Contains("/releases/download/"))
                return url;

            try
            {
                var uri = new Uri(url);
                var parts = uri.AbsolutePath.Trim('/').Split('/');
                if (parts.Length < 2) return url;

                string org = parts[0];
                string repo = parts[1];
                if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                    repo = repo.Substring(0, repo.Length - 4);

                return "https://api.github.com/repos/" + org + "/" + repo + "/releases/latest";
            }
            catch
            {
                return url;
            }
        }

        private static bool TryParseGitHubApiRelease(string raw, out string dlUrl, out string filename, out string tagName)
        {
            dlUrl = null;
            filename = null;
            tagName = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            var tagMatch = Regex.Match(raw, @"\""tag_name\""\s*:\s*\""([^\""]+)\""", RegexOptions.IgnoreCase);
            if (tagMatch.Success) tagName = tagMatch.Groups[1].Value;

            var matchA = Regex.Match(raw, @"\""name\""\s*:\s*\""([^\""]+\.asl)\""[\s\S]*?\""browser_download_url\""\s*:\s*\""([^\""]+)\""", RegexOptions.IgnoreCase);
            if (matchA.Success)
            {
                filename = matchA.Groups[1].Value;
                dlUrl = matchA.Groups[2].Value;
                return true;
            }

            var matchB = Regex.Match(raw, @"\""browser_download_url\""\s*:\s*\""([^\""]+)\""[\s\S]*?\""name\""\s*:\s*\""([^\""]+\.asl)\""", RegexOptions.IgnoreCase);
            if (matchB.Success)
            {
                dlUrl = matchB.Groups[1].Value;
                filename = matchB.Groups[2].Value;
                return true;
            }

            return false;
        }

        private void RepointAslComponent(IComponent comp, string oldPath, string newPath)
        {
            try
            {
                var doc = new XmlDocument();
                var node = comp.GetSettings(doc);
                if (node == null) return;

                var targetNode = node.SelectSingleNode("ScriptPath")
                              ?? node.SelectSingleNode("//ScriptPath")
                              ?? FindAslXmlNode(node, oldPath);

                if (targetNode == null) return;

                targetNode.InnerText = newPath;
                comp.SetSettings(node);
                System.Diagnostics.Trace.WriteLine("[ASL-Updater] Repointed script path: " + newPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("[ASL-Updater] Repoint error: " + ex.Message);
            }
        }

        private static XmlNode FindAslXmlNode(XmlNode root, string oldPath)
        {
            foreach (XmlNode child in root.ChildNodes)
            {
                string val = child.InnerText.Trim();
                if (string.Equals(val, oldPath, StringComparison.OrdinalIgnoreCase) || val.EndsWith(".asl", StringComparison.OrdinalIgnoreCase))
                    return child;
            }
            return null;
        }

        private List<(IComponent Comp, string Path)> GetLoadedAslComponents()
        {
            var list = new List<(IComponent, string)>();
            var comps = state?.Layout?.Components;
            if (comps == null) return list;

            foreach (var comp in comps)
            {
                if (!IsAutoSplitterComponent(comp)) continue;

                string path = ExtractPathFromComponent(comp);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    list.Add((comp, path));
            }
            return list;
        }

        private static bool IsAutoSplitterComponent(IComponent comp)
        {
            string name = comp.ComponentName ?? "";
            string typeName = comp.GetType().FullName ?? "";
            string asmName = comp.GetType().Assembly.GetName().Name ?? "";

            return name == "Scriptable Auto Splitter" ||
                   asmName == "LiveSplit.ScriptableAutoSplit" ||
                   typeName.IndexOf("ScriptableAutoSplit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("AutoSplit", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string ExtractPathFromComponent(IComponent comp)
        {
            string path = ScanObjectForAsl(comp);
            if (!string.IsNullOrEmpty(path)) return path;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var compType = comp.GetType();
            var settingsProp = compType.GetProperty("Settings", flags);
            var settingsField = compType.GetField("Settings", flags) ?? compType.GetField("settings", flags);
            object settingsObj = settingsProp?.GetValue(comp, null) ?? settingsField?.GetValue(comp);

            return ScanObjectForAsl(settingsObj);
        }

        private static string ScanObjectForAsl(object target)
        {
            if (target == null) return null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var type = target.GetType();

            foreach (var field in type.GetFields(flags))
            {
                if (field.FieldType == typeof(string) && field.GetValue(target) is string s && s.EndsWith(".asl", StringComparison.OrdinalIgnoreCase))
                    return s;
            }

            foreach (var prop in type.GetProperties(flags))
            {
                if (prop.PropertyType == typeof(string) && prop.CanRead && prop.GetValue(target, null) is string s && s.EndsWith(".asl", StringComparison.OrdinalIgnoreCase))
                    return s;
            }

            return null;
        }

        private static string ExtractUpdateUrl(string aslSource)
        {
            if (string.IsNullOrWhiteSpace(aslSource)) return null;

            var lines = aslSource.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            int limit = Math.Min(lines.Length, 30);

            for (int i = 0; i < limit; i++)
            {
                string text = lines[i].TrimStart('/', '*', ' ', '\t');
                if (text.StartsWith("UpdateUrl:", StringComparison.OrdinalIgnoreCase))
                    return text.Substring(10).Trim();
                if (text.StartsWith("@update-url", StringComparison.OrdinalIgnoreCase))
                    return text.Substring(11).Trim();
            }

            return null;
        }

        private static string ComputeSha256(string content)
        {
            using (var sha = SHA256.Create())
            {
                byte[] raw = sha.ComputeHash(Encoding.UTF8.GetBytes(content.Replace("\r\n", "\n").Trim()));
                return BitConverter.ToString(raw).Replace("-", "").ToLowerInvariant();
            }
        }

        public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clipRegion) { }
        public void DrawHorizontal(Graphics g, LiveSplitState state, float width, Region clipRegion) { }
        public Control GetSettingsControl(LayoutMode mode) => settings;
        public XmlNode GetSettings(XmlDocument doc) => settings.GetSettings(doc);
        public void SetSettings(XmlNode node) => settings.SetSettings(node);
        public void Dispose() { }
    }
}