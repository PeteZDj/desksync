# When a paired device appears offline

An offline peer label means this computer has no sync transport connection to that peer. It does not establish that the peer's service is stopped. Check both ends before resetting an identity or deleting configuration.

1. Confirm the sync service/app is running, neither device nor folder is paused, both computers approve the same device IDs, and the shared folder IDs match.
2. Check global discovery and relay errors. An HTTP 403 from discovery needs investigation; repeating a scan will not repair a blocked network request.
3. For the default Windows server, allow inbound TCP/UDP 22000 for the installed `syncthing.exe`. A working outbound connection to one desktop does not prove other devices can connect inbound. Verify the public port from an external machine. Existing installations made before this firewall fix may need the rules added once.
4. If the network requires a proxy, note that Syncthing's sync transport uses `ALL_PROXY`/`all_proxy`, separately from `HTTP_PROXY` and `HTTPS_PROXY`. Syncthing 2.1 supports SOCKS5 and HTTP/HTTPS proxies. Proxy policy may still reject a connection to port 22000. See [Syncthing's proxy documentation](https://docs.syncthing.net/users/proxying.html), including its warning about combining HTTP proxy variables with ALL_PROXY.
5. For an approved peer, a static `tcp://SERVER_IP:22000` address can avoid dependency on discovery. Verify the server's identity before configuring it. This does not bypass blocked TCP access, replace mutual device approval, or change the folder identity.

The administration API must remain on loopback (normally 127.0.0.1:5391). Do not publish it to resolve sync connectivity.

For a browser certificate warning, inspect the actual issuer, fingerprint, names and dates before signing in. A public website can present a valid certificate while a cloud browser sees a different one through its network path, or lacks the necessary trusted issuer. Do not bypass Chrome's warning or turn off TLS verification. Compare certificates through independent validated HTTPS connections.

After reconnecting, verify uniquely named harmless files in both directions and compare contents. A sign-in, an approved device entry, or a local file count alone is not proof that files transferred.
