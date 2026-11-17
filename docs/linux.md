# Legacy browser-only Linux preview

**For the installable desktop app with its own window, icon and tray controls, use the [0.4.0 desktop guide](desktop.md).** The 0.3 instructions below describe the earlier background service and browser dashboard.

# Desksync for Linux — 0.3.2 preview

This Linux desktop client connects to your existing Desksync server. Files live in `~/Desksync` by default. Home, activity, devices, account and settings open at **http://127.0.0.1:5392** in the Linux computer's browser. Closing the tab leaves synchronization running.

The packages include .NET 10 and unmodified Syncthing 2.1.7. Build targets are **Linux x64 and ARM64 with glibc**, intended for current Ubuntu/Debian/Fedora installations. Alpine/musl and 32-bit Linux are not supported. Native dependencies such as glibc, libstdc++, OpenSSL, zlib and ICU come from the distribution; see [Microsoft's Linux dependency guidance](https://learn.microsoft.com/en-us/dotnet/core/install/linux).

This early preview is cross-compiled on Windows. Installation, browser sign-in and transfers must still be verified on the target Linux computer. There is no native tray icon or signed automatic Linux updater yet. Windows service and updater behavior are unchanged.

Version 0.3.2 fixes the application launcher: choosing **Desksync** starts the local service or background process, waits for the dashboard health check, then opens the browser. The launcher now uses a Linux SVG icon. Pin this application entry to your desktop's dock if desired; the installer does not change dock preferences automatically.

## Install

Run as your normal Linux user, without `sudo`. This selects the architecture and verifies the package against the checksum served over HTTPS. You can inspect the extracted `install.sh` before running it.

```bash
set -e
case "$(uname -m)" in
  x86_64) arch=x64 ;;
  aarch64|arm64) arch=arm64 ;;
  *) echo 'Unsupported architecture'; exit 1 ;;
esac
download_dir=$(mktemp -d)
cd "$download_dir"
package="Desksync-0.3.2-linux-$arch.tar.gz"
base=https://afrosoftware.com/downloads/desksync
curl --fail --location --proto '=https' --tlsv1.2 -O "$base/$package"
curl --fail --location --proto '=https' --tlsv1.2 -O "$base/$package.sha256"
sha256sum -c "$package.sha256"
tar -xzf "$package"
bash "desksync-0.3.2-linux-$arch/install.sh"
```

Open **http://127.0.0.1:5392** on the Linux computer, choose your folder and **Sign in & connect**. Approve the request in your normal Afrosoftware browser session. Sign-in is owner-only: an unrelated Google account cannot join your server. Check the short request code. If a new tab is blocked, open the sign-in address shown by Desksync yourself.

For self-hosting, choose **My own server**, finish setup and open your Windows server's `.desksync` connection file under **Server connection**. Save the Linux computer's connection file and open it on the Windows server to approve it. Both sides must approve access. Connection files contain no passwords or account tokens.

## OpenAI Dot's cloud computer

Ask Dot to install this **on its own Linux cloud computer**, inspect `/etc/os-release` and `uname -m`, and open the dashboard in its own browser. Your Windows browser's `127.0.0.1` addresses Windows, not Dot's computer.

OpenAI documents that a [Dot has its own cloud computer](https://help.openai.com/en/articles/20001530-getting-started-with-your-dot). That documentation does not promise a specific distribution, systemd user manager or indefinite daemon uptime. The installer detects user systemd; otherwise it starts a background process and adds desktop-session autostart. Dot must verify that sync survives reconnects. This preview cannot guarantee cloud-computer uptime.

Use a dedicated folder. Approving Linux grants it read, write and delete access to the shared folder, including existing files. Test a uniquely named harmless text file in both directions and compare contents. Sync is not a backup.

## Startup, storage and troubleshooting

If Chrome shows **ERR_CONNECTION_REFUSED**, it has not reached DeskSync's dashboard. First run `~/.local/opt/desksync/open.sh` from the terminal **on the visible Linux desktop**. If that fails, run `~/.local/opt/desksync/diagnose.sh` there and inspect its service status and the startup log. If an agent's separate command environment can connect but visible Chrome cannot, compare hostnames and network namespaces between that environment and the visible desktop. Install and launch on the visible desktop; opening a public port does not solve the environment mismatch safely.

- Program: `~/.local/opt/desksync`. Private identity, configuration and account session: `${XDG_STATE_HOME:-~/.local/state}/desksync`. Keep private state outside the sync folder.
- Linux tokens use a user-only file (directory 0700, file 0600), **not Windows DPAPI or an OS keyring**. Root and your Linux user can read them. Never copy private state to another computer; each computer needs its own identity.
- Dashboard: loopback TCP 5392. Engine admin: loopback TCP 5391. Do not publish either through a proxy, firewall or tunnel. Encrypted discovery/relays work as on Windows; direct sync usually uses TCP/UDP 22000.
- Systemd: `systemctl --user status desksync`, `journalctl --user -u desksync -n 80`, `systemctl --user restart desksync`. Without it: `~/.local/opt/desksync/start.sh` starts sync; `~/.local/opt/desksync/dashboard.log` records startup errors.
- If a port is occupied, inspect the owning process first. An isolated instance can use `desksync --data /absolute/private/state --folder /absolute/dedicated/folder --port 15392 --engine-port 15391 --no-browser`. Different instances need different folders and identities.
- Install missing native dependencies using your distribution's package manager, then restart. No .NET SDK is needed for these packages.
- Linux is case sensitive; Windows usually is not. Avoid names differing only by case, Windows-reserved names and unsupported characters. Sync-folder paths containing symlinks are refused.
- The dashboard startup button explains the installed service. For manual updates, verify a newer package and rerun its installer. It stops processes from this installation before replacing binaries, retaining files and private state. This preview has no unattended Linux updater or rollback.
- Stop/disable a service with `systemctl --user disable --now desksync`. For the fallback, stop the dashboard PID in `dashboard.pid` and the engine whose executable is `~/.local/opt/desksync/syncthing`, and remove the autostart entry. Stopping only the dashboard can leave the engine syncing.
- Uninstalling binaries does not revoke server access. Disconnect first to revoke this computer; retain files and identity until you decide what to keep.

## Build and licenses

With .NET 10 and PowerShell 7 on the Windows build server:

```powershell
.\scripts\build-linux.ps1
```

Both tarballs and SHA-256 files appear under `artifacts`. Engine archive hashes are pinned from the official [Syncthing 2.1.7 release](https://github.com/syncthing/syncthing/releases/tag/v2.1.7). The wrapper is MIT; Syncthing is MPL-2.0 with its [exact source upstream](https://github.com/syncthing/syncthing/tree/v2.1.7). Included .NET components retain their Microsoft and third-party licenses.

Run the isolated launcher checks with `bash tests/linux-launcher.sh`. These use mock services/browser commands and do not alter the installed app.
