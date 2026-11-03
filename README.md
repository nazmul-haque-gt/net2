# NetSpeedTray
Tiny Windows tray app: live upload/download speed, per-app usage history.

## Build the .exe with GitHub
1. Create a GitHub repo and upload everything in this folder (keep the `.github` folder).
2. Open the **Actions** tab -> **Build NetSpeedTray** -> wait for the green check
   (or click **Run workflow**).
3. Open the finished run -> **Artifacts** -> download `NetSpeedTray`. Inside is `NetSpeedTray.exe`
   (standalone, no .NET install needed).

## Use
- Left-click tray icon: usage history. Right-click: color, units, bits/bytes, order, autostart.
- Per-app history needs admin: right-click -> "Enable per-app tracking".
- Data is stored in %AppData%\NetSpeedTray.

## If per-app tracking fails
Right-click the tray icon -> "Per-app tracking failed - show details" (also saved to
%AppData%\NetSpeedTray\tracking-error.log). Try the `NetSpeedTray-folder` artifact too.
