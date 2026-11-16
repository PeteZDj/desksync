# Desksync

Your files. Your server. A Windows desktop and server app by Afrosoftware, with encrypted synchronization powered by Syncthing.

[Download Windows setup 0.3.0](https://afrosoftware.com/downloads/desksync/Desksync-Setup-0.3.0-win-x64.exe) · [Checksum](https://afrosoftware.com/downloads/desksync/Desksync-Setup-0.3.0-win-x64.exe.sha256) · [Software page](https://afrosoftware.com/software/desksync)

## What is new in 0.3

- Live upload/download queues show filenames, receiving devices, transferred bytes, percentages and network traffic. A queued transfer is labelled as waiting, not reported as actively transferring. Remote block progress is approximate until the receiver acknowledges completion.
- Click the Home file count to open your folder, storage to view its settings, and Devices online to open the device list. Select a device for its completion, pending items and current health.
- The tray shows current transfer state and filenames. Its icon animates during reported transfers and shows pause/error badges. Left-click opens the current status; right-click includes uploads/downloads, devices, pause/resume and Scan now.
- Folder errors, missing permissions, paused/unshared remote folders and low disk space appear on Home and in Notifications. Connected devices with pending work are never labelled up to date solely because they have no pending bytes; zero-byte changes count too.
- The inherited 1% free-space limits are migrated once to fixed reserves: **2 GB for the sync folder and 1 GB for the private sync index**. On a 1.8 TB disk the old threshold could require about 18 GB free before even indexing files. Non-default custom limits are preserved; the reserve is never disabled.
- Afrosoftware-connected desktops report limited health information to their private owner server: counts, disk space, sync errors, version and migration outcome. Reports are bound to the authenticated device, expire from the live view after 90 seconds, and contain no account token, engine key, file contents or raw logs. Self-hosted connections continue to work without this hosted reporting.

### If a 0.2 desktop is connected but files do not move

Install 0.3 on both ends, keeping the existing folder and identity. If the old app is stuck in an engine error, use Settings → Check for updates → Restart & install, or quit its tray and run the installer. Older versions defer automatic installation while the engine is errored; 0.3 permits a verified update while the engine is safely idle or blocked by an error.

Open Notifications for the exact blocker, then use **Scan now**. If the drive has less than the fixed reserve, free space or choose an empty folder on another local drive in Settings; do not remove the folder marker or reset the database. Folder moves copy files and preserve the originals. Check the live queue and the receiver's completion, then verify a small file in both directions. Synchronization is two-way, including deletions.

## What is new in 0.2

Patch 0.2.1 corrects first-time server role selection and grants the sync service access when you move its folder. Updater staging is explicitly excluded from folder choices.

- A redesigned desktop with original Desksync icons, Home, Sync activity, Devices, Notifications and Settings.
- A first-run wizard: choose **Desktop** or **Server**, select your folder, then connect.
- **Sign in with Afrosoftware** for our private installation. Google sign-in happens in your regular browser. After explicit approval, both devices connect automatically; no long pairing-code exchange is needed.
- Your signed-in name and photo appear in the top-right account menu. Desktop tokens are encrypted using Windows DPAPI for the current user. Browser passwords never enter the app.
- Real file counts, folder bytes, physical disk usage, peer connection status and recent local/received file events. A local edit is labelled as a local change, not falsely claimed to be an upload receipt.
- The system tray includes open folder, pause/resume, activity, settings, update checks and quit/close controls. File, Edit, View and Help menus provide standard desktop actions.
- Automatic updates with signed release metadata, archive verification, startup checks and rollback. Updates preserve the sync folder, engine identity and account state.
- A Windows service component for self-hosted servers. New server setups no longer require NSSM. Existing NSSM-managed installations remain compatible.

This is an **unsigned Windows x64 preview**. Windows SmartScreen may show an unknown-publisher warning. Signing update metadata is separate from a publicly trusted Windows code-signing certificate; this release does not claim a trusted Windows publisher. Verify your download from the official source.

## Upgrade from 0.1

Quit the old tray app and install the current version once. Existing files, device certificates and folder identity are preserved. The old 0.1 app did not include an updater, so it cannot install this upgrade automatically. Subsequent releases can update from the tray app.

The installer includes the .NET runtime. Microsoft Edge WebView2 is needed for the interface; setup downloads it from Microsoft and checks the Microsoft signature if it is missing. Windows Server 2025 and Windows 11 x64 are the initial targets. There are no Android, iPhone, Linux or macOS desktop releases in this version.

## Our private Afrosoftware installation

1. Install Desksync on your Windows desktop.
2. Choose **This is my desktop**, then choose your local folder (`C:\Desksync` by default).
3. Choose **Afrosoftware** and **Sign in & connect**.
4. In your normal browser, sign in with your Afrosoftware account, using Google or an existing verified administrator login. Confirm that the computer name and short request code match, then approve the computer.
5. Return to Desksync. Your profile appears and the server connection is configured automatically. Keep both devices online for file transfer.

This hosted installation is owner-only. Signing in as an unrelated Google user does not grant access to our files. Other users can run their own servers as described below. The desktop sign-in uses a narrowly scoped device authorization flow; this is not a public general-purpose OAuth/OpenID provider for arbitrary third-party applications.

## Set up your own cloud or dedicated Windows server

You do not need an Afrosoftware account, IIS, FTP, Node.js or a paid cloud-storage plan. You need your own Windows server and Windows desktops with storage and network access.

### On the server

1. Install Desksync and choose **This is my server** in the wizard.
2. Choose a dedicated shared folder. Only this folder is shared; Desksync does not automatically expose websites, repositories, your profile or the whole drive.
3. Leave **Run as a Windows service** enabled for an unattended server. Windows will request administrator permission.
4. Finish setup. Desksync installs the service under the low-privilege LocalService account with a dedicated service SID and private configuration under `C:\ProgramData\Desksync`.
5. The server dashboard/tray starts when the installing owner signs in. It uses an interactive logon task without storing a password. The sync service continues when the owner signs out; Windows services cannot display a tray icon in a signed-out session.
6. In **Devices → Connection options**, save the server’s `.desksync` connection file and transfer it to your desktop through a trusted channel.

### On each desktop

1. Install Desksync. Choose **This is my desktop**, your local folder, and **My own server**.
2. Finish the wizard. Under **Server connection**, open the server’s `.desksync` file.
3. Save this computer’s connection file and open it on the server to approve this desktop. Both sides must approve each other before files transfer.
4. Verify that both computers show a connected device. Create a harmless file on each side and check both transfers before adding important files.

Connection files contain a device identity and folder ID, not a password or access grant by themselves. Verify the device through a trusted channel. Approving a device grants read, write and delete access to the shared folder. Raw codes remain available only in Advanced options for compatibility.

### Server administration and storage

- **Settings** shows the folder location, server connection, account, startup behavior, retained versions and updater controls.
- Changing an existing folder location requires an empty destination. Desksync pauses synchronization, copies the existing files, keeps the originals, then reconfigures the engine. Close applications editing those files first. The preview refuses to relocate junctions/symlinks.
- The storage meter shows physical disk capacity and available space. Desksync folder bytes are shown separately. It does not invent an account quota or pretend the entire disk is dedicated to Desksync.
- Server configuration/API keys and app accounts are outside the shared folder. The engine administration API stays on `127.0.0.1:5391`; do not expose it publicly.
- Desktop configuration stays under `%LocalAppData%\Afrosoftware\Desksync`; account and update state are under `%LocalAppData%\Afrosoftware\DesksyncDesktop`.

## Automatic updates

The tray application checks signed metadata on the official website and published GitHub releases at startup and every four hours while running. Automatic downloads/installations are enabled by default and can be disabled in Settings. You can also choose **Help → Check for updates**. The app shows update state and download progress; it waits for the local sync engine to become idle before an automatic restart.

Before running an update, Desksync verifies signed release metadata with an embedded RSA public key, checks the package size and SHA-256, and rejects unsafe archive paths and non-newer versions. The updater keeps the archive open against modification while verifying and extracting it. Server staging and helper binaries use a protected Program Files location. Sync data, private engine configuration and account tokens are not part of application replacement.

The previous application files are retained for rollback. The new version must complete native-engine and UI startup checks. If startup fails, the updater restores the previous application and blocks automatic retries of that version to prevent a restart loop. Failed downloads or signature checks leave the current version in place. A server dashboard closed or signed out does not check for app updates until it runs again; the background sync service keeps running.

For release maintainers, `scripts/sign-release.ps1` reads a private signing key **outside this repository**. That key must match `src/Shared/UpdatePublicKey.pem`. Never commit or distribute the private key. Fork maintainers must supply their own release key and review the update URL policy if publishing their own distribution.

## How file synchronization works

- Files are ordinary local files in File Explorer, available offline. This release mirrors the complete shared folder. Streaming, virtual-drive placeholders and selective on-demand downloads are not included.
- Edits, additions, renames and deletions travel both ways when devices reconnect. Concurrent edits can create `.sync-conflict-*` copies; compare and resolve them yourself.
- Syncthing provides encrypted device-to-device connections. Public discovery and encrypted relays help devices reach each other. Relay operators cannot read file contents but can observe network/device metadata. Router port mapping is disabled by Desksync.
- Direct connections generally use TCP/UDP 22000; local discovery uses UDP 21027. Outbound discovery/relay traffic must be permitted. No new public FTP or administration port is required. See [Syncthing’s firewall guide](https://docs.syncthing.net/users/firewall.html).
- Staggered versioning retains remotely replaced/deleted files for up to 30 days in `.stversions`. It does not capture every local edit or replace an independent backup.
- Removing a device stops future transfers but cannot erase files it already downloaded. Do not synchronize live databases, virtual machines or secrets without an application-specific plan.

## Build and test

Requirements: Windows x64, .NET 10 SDK, PowerShell 7, and internet access for official dependencies.

```powershell
.\scripts\fetch-engine.ps1
dotnet run --project tests\Desksync.Tests.csproj -c Release
node tests\ui-state.cjs
.\scripts\test-updater.ps1
.\scripts\build-v02.ps1
```

The build pins Syncthing 2.1.7 and checks its official archive checksum. It produces the self-contained installer and update ZIP under `artifacts`. Installer payloads contain the app, service, updater, engine, instructions and license notices, never production configuration or device identities.

The updater test builds isolated application fixtures and an ephemeral test key to exercise successful replacement and failed-start rollback. It does not alter the running production sync service or user files.

## Source and licenses

The Afrosoftware wrapper is MIT licensed. The bundled, unmodified Syncthing 2.1.7 engine is MPL-2.0; its license and [exact upstream source](https://github.com/syncthing/syncthing/tree/v2.1.7) are provided. Microsoft .NET and WebView2 license notices are included. Syncthing, Google and Dropbox do not endorse Desksync.
