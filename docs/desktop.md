# Desksync desktop 0.4.0 preview — Linux and macOS

This release is a standalone desktop application with its own window and icon, Home, activity, devices, notifications, settings, native folder/connection-file dialogs, desktop menus and tray/menu-bar controls. It embeds the dashboard inside Electron; a separate Chrome tab is not required. The .NET and Syncthing components are bundled.

## Downloads

- [Linux x64 installer (.deb)](https://afrosoftware.com/downloads/desksync/Desksync-0.4.0-linux-amd64.deb)
- [Linux ARM64 installer (.deb)](https://afrosoftware.com/downloads/desksync/Desksync-0.4.0-linux-arm64.deb)
- [Mac Apple silicon (.dmg)](https://afrosoftware.com/downloads/desksync/Desksync-0.4.0-mac-arm64.dmg)
- [Mac Intel (.dmg)](https://afrosoftware.com/downloads/desksync/Desksync-0.4.0-mac-x64.dmg)

Each download has a `.sha256` file at the same address with `.sha256` appended. Verify the downloaded package against it. These desktop previews update manually; the Windows release and its signed updater remain separate.

## Linux installation

On Ubuntu/Debian, install the package matching `uname -m` (`x86_64` → amd64; `aarch64` → arm64):

```bash
sudo apt install ./Desksync-0.4.0-linux-amd64.deb
```

Then select **Desksync** in the desktop application menu. Pin that application to your dock if desired. Run the app as your normal desktop user. Do not run Electron as root or disable its sandbox. Tray visibility depends on your Linux desktop supporting StatusNotifier/AppIndicator icons.

When upgrading the old browser-only 0.3 preview, stop its background host first: `systemctl --user disable --now desksync` if installed as a service. Otherwise stop the old dashboard PID in `~/.local/opt/desksync/dashboard.pid`. Keep `~/Desksync` and `~/.local/state/desksync`: these contain your files and identity and are reused by the desktop app. Remove the old browser-only autostart entry if present. If an old process still holds the private-state lock, the new app reports it; do not delete the lock or private identity to work around it.

To start the desktop app at login on Linux, Settings → Enable auto-start installs a user desktop-session autostart entry. Closing the window hides it while syncing; **Quit Desksync** stops the desktop app and its sync engine. If your desktop has no tray support, use its application icon to show the window again.

## macOS installation

Open the matching DMG, drag **Desksync** into **Applications**, then open it. Use the Apple silicon package on M-series Macs and the Intel package on Intel Macs.

These builds are ad-hoc signed for local integrity, **not notarized or signed with an Apple Developer ID**. macOS may block opening a downloaded preview; see [Apple's guidance for opening an app from an unidentified developer](https://support.apple.com/guide/mac-help/open-a-mac-app-from-an-unknown-developer-mh40616/mac). A trusted publisher/notarized release needs an Apple signing identity. The source and checksums are available for inspection.

Private application state is in `~/Library/Application Support/Afrosoftware/Desksync`; sync files default to `~/Desksync`. Settings can enable startup at login. The menu-bar icon has pause/resume, scan, folder, devices and quit controls.

## Connect and verify

Choose your dedicated local folder, select **Afrosoftware**, and **Sign in & connect**. Your normal browser opens for sign-in; check the short request code before approving. Your owner account is required to join the private server. Alternatively use a server connection file and approve the new computer on your server.

Approving a computer grants two-way read/write/delete access to the shared folder. Keep separate backups. Test uniquely named harmless files in both directions before relying on sync. Avoid filenames that collide by case or are invalid on Windows when sharing across systems.

On an OpenAI Dot computer, install and launch on the **visible Linux desktop**, not in an unrelated tool container. The desktop window and backend run together. Cloud-computer background-process persistence still depends on the environment.

## Startup and privacy

The local engine client bypasses environment proxies, validates JSON responses, and never sends its API key to a configured proxy. The embedded dashboard stays on loopback, with a per-launch token and origin checks. Startup failures appear inside the app and can be retried. External navigation is restricted to the sign-in and project help links.

Unix account sessions are stored in a user-only file (directory 0700/file 0600), not an OS keyring. Your user and root can read them. Do not share private configuration or copy device identities across machines.

## Build and verification

On the matching Linux/macOS architecture with .NET 10 and Node.js 24:

```bash
cd src/Desksync.Desktop
npm ci
cd ../..
node scripts/prepare-desktop.cjs
node --test tests/desktop.test.cjs
cd src/Desksync.Desktop
npx electron-builder --linux --publish never  # use --mac on macOS
```

The **Desktop installers** GitHub Actions workflow builds Linux x64/ARM64 and macOS Intel/Apple silicon separately, runs each desktop on its native host, captures the rendered setup window, and packages installers with checksums. Its smoke test uses deliberately unusable proxy settings to check local startup still works. This does not replace owner sign-in and cross-device transfer checks on your actual computers.
