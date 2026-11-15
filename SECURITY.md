# Reporting and operating securely

Do not post engine API keys, `config.xml`, private certificate/key files, account cookies, pairing screenshots containing private filenames, or synchronized documents in a public issue. Report a vulnerability privately to `support@afrosoftware.com`.

Verify the release checksum and official source before running an unsigned preview. The optional Windows service installer runs under the low-privilege LocalService account with the dedicated service SID `NT SERVICE\DesksyncSync`, grants that SID access to its private configuration and shared folder, and limits its administrative interface to loopback. The private configuration and newly created server folder are restricted to administrators, SYSTEM, the installing user and the service SID. Existing folder permissions are preserved; review them if reusing a directory.

The first hosted console is owner-only. A successful Afrosoftware login does not grant access to someone else's storage. Desktop peers must be explicitly approved on both ends. Revocation prevents future transfers but cannot erase previously downloaded files.

Public discovery and encrypted relays are enabled in the engine. Router port mapping is disabled. If your organization disallows public discovery, configure private discovery/static peer addresses through Syncthing's documented settings and verify connectivity. Do not expose port 5391 or share the private engine profile.

Back up data independently. The preview has no automatic wrapper/engine updater; administrators must install reviewed releases and monitor upstream Syncthing security notices. The 30-day version history covers remotely replaced/deleted files and is not a full backup or ransomware-recovery guarantee.
