# Desksync

Your files. Your server. A Windows tray application and setup program by Afrosoftware.

Desksync synchronizes **one explicitly selected folder**, `C:\Desksync` by default, between approved Windows devices. It does not share your entire drive, website roots, repositories, mailbox administration, or other folders automatically.

## Windows preview 0.1.0

[Download Desksync Setup](https://afrosoftware.com/downloads/desksync/Desksync-Setup-0.1.0-win-x64.exe) · [SHA-256](https://afrosoftware.com/downloads/desksync/Desksync-Setup-0.1.0-win-x64.exe.sha256) · [Software page](https://afrosoftware.com/software/desksync)

The installer includes the .NET runtime and Syncthing. You do not need to install Node.js, .NET, FTP or IIS. This is an **unsigned Windows x64 preview**; Windows may show an unknown-publisher warning. Verify the checksum and obtain it from the official Afrosoftware download. Code signing, automatic app updates, mobile clients and Explorer placeholder files are not included in this release.

## Set up your own dedicated server and desktop

1. Download and install Desksync on a Windows server and your Windows desktop. Windows Server 2025 and Windows 11 x64 are the initial targets. The folder and installation are per machine/user, not a multi-tenant hosting service.
2. On the server, open Desksync and choose **Copy this device's code**.
3. On the desktop, choose **Join another folder** and paste the server's code. Joining an existing folder merges its contents with the local folder. Start with an empty desktop folder if you do not want pre-existing local files uploaded.
4. On the desktop, choose **Copy this device's code**. On the server choose **Approve a device** and paste that code.
5. Keep both devices running. Wait until both show a connected device and **Up to date**. Create a harmless test file on each side and verify both changes before adding important files.
6. Choose **Start at sign-in** on a desktop. Closing the window leaves synchronization running in the tray; **Quit Desksync** stops the desktop engine.

The pairing code contains a device identity and folder ID, not a password or a grant by itself. Both sides must explicitly trust each other. Approving a device grants read, write and delete access to that folder. Verify codes through a trusted channel. Device names are labels, not proof of identity.

## How synchronization works

- Encrypted peer-to-peer transport uses device certificates. Public discovery/relay services can help devices find and reach each other; a relay cannot read file contents. Discovery infrastructure can observe device/network metadata. Router port mapping is disabled by Desksync. No FTP service or public administration port is required.
- Files are ordinary files on disk, available offline in File Explorer. This release fully downloads the shared folder. It does not mount an on-demand cloud drive or selectively fetch individual placeholders.
- Both sides can edit, create, rename and delete. Changes transfer when both sides reconnect. Concurrent edits can produce `.sync-conflict-*` files; compare them yourself before deleting one.
- Staggered versioning retains remotely replaced/deleted files for up to 30 days in `.stversions`. It does not protect every local edit, an offline folder from loss, or malicious edits everywhere. Keep an independent backup.
- **Remove device** stops future transfers; it does not revoke copies already on that device. Do not share secrets, active databases, virtual-machine disks or a live Git metadata directory without an application-specific plan.
- File permissions, owners and Windows ACLs are not a portable backup contract. Test your own workload.

## Unattended Windows server

The optional `scripts/install-server.ps1` initializes a private machine-wide engine profile, creates only `C:\Desksync`, and registers the included engine using an existing NSSM installation. Run it elevated with the NSSM path. The service starts at boot, independently of RDP sign-in. Its control API stays on `127.0.0.1:5391`, protected by a private API key and GUI password. Engine keys and API credentials remain under `%ProgramData%\Desksync`; never copy that directory into your synced folder or public downloads.

```powershell
.\scripts\install-server.ps1 -NssmPath C:\tools\nssm.exe
```

For server UI access, run `Desksync.exe` elevated. It detects the `DesksyncSync` service and uses the machine-wide profile. Closing the server dashboard leaves its service running. Use the service manager to stop the server engine.

Outbound HTTPS/discovery and relay connections must be permitted by your network. Direct transfers usually use TCP/UDP 22000 when reachable; local discovery uses UDP 21027. Firewall/NAT policy may force a relay and reduce performance. Do not expose the loopback administration API. See [Syncthing's firewall guide](https://docs.syncthing.net/users/firewall.html).

## Afrosoftware account and privacy

The private hosted server console at `https://afrosoftware.com/desksync` uses your existing Afrosoftware account and server-verified Google sign-in. It is owner-only in the initial installation. Desktop file pairing is independent and works on your own server without an Afrosoftware account. This release does **not** claim to be a general-purpose OAuth/OpenID provider; a standards-based shared identity service is a separate component of the product roadmap.

## Build

Requirements: Windows x64, .NET 10 SDK, PowerShell 7 and internet access to the official Syncthing release. The build script pins Syncthing 2.1.7 and verifies its archive's SHA-256. No private configuration or production data is packaged.

```powershell
.\scripts\fetch-engine.ps1
dotnet run --project tests\Desksync.Tests.csproj
.\scripts\build.ps1
```

The self-contained application and installer are produced in `artifacts`. To uninstall a desktop preview: quit Desksync, remove its Start menu/desktop/startup shortcuts and its installation directory under `%LocalAppData%\Programs\Afrosoftware\Desksync`. Keep `C:\Desksync` to retain your files. Keep the private profile under `%LocalAppData%\Afrosoftware\Desksync` if you plan to reinstall with the same device identity. Delete that profile only if intentionally retiring the device.

## Source and licenses

Afrosoftware's wrapper source is MIT licensed. The bundled transfer engine is **Syncthing 2.1.7, MPL-2.0**, unmodified, with its license included. [Exact upstream source](https://github.com/syncthing/syncthing/tree/v2.1.7) · [Engine documentation](https://docs.syncthing.net/). Syncthing is not affiliated with or endorsing Desksync. .NET runtime notices are included with the distributed application. Source archives exclude credentials, engine identities, synchronized files and local data.
