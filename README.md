# LiveSplit.ASLUpdater
A DLL component for LiveSplit that automatically checks for updates of your loaded `.asl` script

# [DIRECT DOWNLOAD `LiveSplit.ASLUpdater.dll`](https://github.com/oJumpy/LiveSplit.ASLUpdater/releases/download/v1.0/LiveSplit.ASLUpdater.dll)

### Why?
Whenever `.asl` scripts get updated, speedrunners usually have to manually visit GitHub, download the new `.asl` file, and replace old one with the new one. This component automatically checks your loaded `.asl` scripts on startup, will prompt you when an update is available, downloads the new version, and loads it in LiveSplit when downloaded.
As long as the creator of the `.asl` script has enabled update support.

---

## Installation

1. Download [`LiveSplit.ASLUpdater.dll`](https://github.com/oJumpy/LiveSplit.ASLUpdater/releases/latest/download/LiveSplit.ASLUpdater.dll) from the Releases tab.
2. Place the `.dll` file into your LiveSplit installation folder inside the `Components` directory.
3. Restart LiveSplit.

---

## Setup Instructions

### For Speedrunners

1. Open LiveSplit.
2. Right-click LiveSplit -> **Edit Layout...** -> [Click the **+** button] -> **Other** -> **ASL Updater**.
3. Double-click on `ASL Updater` to open its settings.
4. **Options:**
   - **Automatically download updates:** Check this if you want updates to download automatically without prompting you.
   - **Create backup (.asl.bak) before replacing script file:** Keeps a backup copy of your previous script before applying an update.
   - **Check for Updates Now:** Click this button at any time to manually check for updates.
5. Click **OK** and save your layout.

---

## For ASL Script Creator / Developers

To make your `.asl` script compatible with the ASL Updater, add a single header line near the top of your `.asl` file pointing to your GitHub repository:

```csharp
// UpdateUrl: https://github.com/YourUsername/YourRepository
```

## VirusScan

> [!NOTE]
> Because this plugin is an unsigned custom `.dll` file, you can view the clean scan results showing the file is safe below:

* [VirusTotal Scan Results](https://www.virustotal.com/gui/file/d1fb56db699535cba85c46863380d18104cd811b0b47c450feb6f5e5b47c3d01?nocache=1)
* [Kaspersky OpenTip Scan Results](https://opentip.kaspersky.com/D1FB56DB699535CBA85C46863380D18104CD811B0B47C450FEB6F5E5B47C3D01/results?tab=upload)

---
