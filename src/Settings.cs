using LiveSplit.UI;
using System;
using System.Drawing;
using System.Windows.Forms;
using System.Xml;

namespace LiveSplit.ASLUpdater.src
{
    [System.ComponentModel.DesignerCategory("")]
    public partial class Settings : UserControl
    {
        public bool AutoUpdateSilently { get; set; } = false;
        public bool CreateBackup { get; set; } = true;

        private CheckBox chkAutoUpdate;
        private CheckBox chkCreateBackup;
        private Button btnCheckNow;
        private Label lblStatusVal;

        public event EventHandler ManualCheckRequested;

        public Settings()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Size = new Size(475, 180);
            this.MinimumSize = new Size(475, 180);

            var mainFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                Padding = new Padding(10)
            };

            var grpSettings = new GroupBox { Text = "ASL Updater Options", Width = 450, Height = 150 };

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(10)
            };

            chkAutoUpdate = new CheckBox
            {
                Text = "Automatically download updates.",
                AutoSize = true,
                Checked = AutoUpdateSilently
            };
            chkAutoUpdate.CheckedChanged += (s, e) => AutoUpdateSilently = chkAutoUpdate.Checked;

            chkCreateBackup = new CheckBox
            {
                Text = "Create backup (.asl.bak) before replacing script file",
                AutoSize = true,
                Checked = CreateBackup
            };
            chkCreateBackup.CheckedChanged += (s, e) => CreateBackup = chkCreateBackup.Checked;

            btnCheckNow = new Button
            {
                Text = "Check for Updates Now",
                AutoSize = true,
                Margin = new Padding(0, 5, 0, 5)
            };
            btnCheckNow.Click += (s, e) => ManualCheckRequested?.Invoke(this, EventArgs.Empty);

            var pnlStatus = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            var lblStatusTitle = new Label { Text = "Status: ", AutoSize = true, Font = new Font(this.Font, FontStyle.Bold) };
            lblStatusVal = new Label { Text = "Idle", AutoSize = true, ForeColor = Color.Gray };
            pnlStatus.Controls.Add(lblStatusTitle);
            pnlStatus.Controls.Add(lblStatusVal);

            grid.Controls.Add(chkAutoUpdate, 0, 0);
            grid.Controls.Add(chkCreateBackup, 0, 1);
            grid.Controls.Add(btnCheckNow, 0, 2);
            grid.Controls.Add(pnlStatus, 0, 3);

            grpSettings.Controls.Add(grid);
            mainFlow.Controls.Add(grpSettings);
            this.Controls.Add(mainFlow);
        }

        public void SetStatus(string text, Color color)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke((Action)(() => SetStatus(text, color)));
                return;
            }
            if (lblStatusVal != null)
            {
                lblStatusVal.Text = text;
                lblStatusVal.ForeColor = color;
            }
        }

        public void SetSettings(XmlNode node)
        {
            var element = (XmlElement)node;
            AutoUpdateSilently = SettingsHelper.ParseBool(element["AutoUpdateSilently"], false);
            CreateBackup = SettingsHelper.ParseBool(element["CreateBackup"], true);

            chkAutoUpdate.Checked = AutoUpdateSilently;
            chkCreateBackup.Checked = CreateBackup;
        }

        public XmlNode GetSettings(XmlDocument document)
        {
            var parent = document.CreateElement("Settings");
            SettingsHelper.CreateSetting(document, parent, "AutoUpdateSilently", AutoUpdateSilently);
            SettingsHelper.CreateSetting(document, parent, "CreateBackup", CreateBackup);
            return parent;
        }
    }
}