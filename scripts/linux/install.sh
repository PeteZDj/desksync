#!/usr/bin/env bash
set -euo pipefail
umask 077
if [ "$(id -u)" = 0 ]; then echo 'Install as your regular Linux user, without sudo.' >&2; exit 1; fi
source_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
install_dir="$HOME/.local/opt/desksync"
case "$HOME" in *\"*|*\\*|*%*|*$'\n'*) echo 'Unsupported characters in home path.' >&2; exit 1;; esac
for binary in desksync syncthing; do test -f "$source_dir/$binary" || { echo "Missing $binary" >&2; exit 1; }; done
if [ -L "$install_dir" ]; then echo 'Refusing a symlinked install directory.' >&2; exit 1; fi
have_systemd=false
if command -v systemctl >/dev/null && systemctl --user show-environment >/dev/null 2>&1; then have_systemd=true; fi
if $have_systemd; then systemctl --user stop desksync.service 2>/dev/null || true; fi
# Stop only a previous fallback process whose executable belongs to this install.
if [ -f "$install_dir/dashboard.pid" ]; then
 old_pid=$(cat "$install_dir/dashboard.pid")
 if [[ "$old_pid" =~ ^[0-9]+$ ]] && [ "$(readlink "/proc/$old_pid/exe" 2>/dev/null || true)" = "$install_dir/desksync" ]; then
  kill "$old_pid"
  for _ in {1..50}; do kill -0 "$old_pid" 2>/dev/null || break; sleep 0.1; done
  if kill -0 "$old_pid" 2>/dev/null; then echo 'Existing Desksync has not stopped. Retry later.' >&2; exit 1; fi
 fi
fi
for executable in /proc/[0-9]*/exe; do
 if [ "$(readlink "$executable" 2>/dev/null || true)" = "$install_dir/syncthing" ]; then
  engine_pid=${executable#/proc/}; engine_pid=${engine_pid%/exe}
  kill "$engine_pid" 2>/dev/null || true
 fi
done
for _ in {1..50}; do
 running=false
 for executable in /proc/[0-9]*/exe; do [ "$(readlink "$executable" 2>/dev/null || true)" != "$install_dir/syncthing" ] || running=true; done
 $running || break
 sleep 0.1
done
if $running; then echo 'Sync engine has not stopped. Retry later.' >&2; exit 1; fi
mkdir -p "$install_dir" "$HOME/.local/share/applications" "$HOME/.config/autostart"
if [ "$source_dir" != "$install_dir" ]; then
 for name in desksync syncthing 'lib*.so' '*.txt' LINUX-SETUP.md desksync.ico; do
  for file in "$source_dir"/$name; do [ ! -f "$file" ] || cp -- "$file" "$install_dir/"; done
 done
fi
chmod 700 "$install_dir/desksync" "$install_dir/syncthing"
cat > "$install_dir/start.sh" <<'START'
#!/usr/bin/env bash
set -euo pipefail
umask 077
dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
if [ -f "$dir/dashboard.pid" ]; then
 pid=$(cat "$dir/dashboard.pid")
 if [[ "$pid" =~ ^[0-9]+$ ]] && [ "$(readlink "/proc/$pid/exe" 2>/dev/null || true)" = "$dir/desksync" ]; then exit 0; fi
fi
nohup "$dir/desksync" --no-browser > "$dir/dashboard.log" 2>&1 &
echo $! > "$dir/dashboard.pid"
START
chmod 700 "$install_dir/start.sh"
if $have_systemd; then
 mkdir -p "$HOME/.config/systemd/user"
 cat > "$HOME/.config/systemd/user/desksync.service" <<SERVICE
[Unit]
Description=Desksync private folder synchronization
After=network.target
[Service]
ExecStart="$install_dir/desksync" --no-browser
Restart=on-failure
RestartSec=5
UMask=0077
KillMode=control-group
[Install]
WantedBy=default.target
SERVICE
 systemctl --user daemon-reload
 systemctl --user enable --now desksync.service
else
 "$install_dir/start.sh"
 cat > "$HOME/.config/autostart/desksync.desktop" <<AUTOSTART
[Desktop Entry]
Type=Application
Name=Desksync background sync
Exec="$install_dir/start.sh"
Terminal=false
AUTOSTART
 echo 'No user systemd manager: started a background process and enabled desktop-session autostart.'
 echo 'Cloud computers may stop background processes between sessions. Verify after reconnecting.'
fi
cat > "$HOME/.local/share/applications/desksync.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Desksync
Comment=Open your private file sync dashboard
Exec=xdg-open http://127.0.0.1:5392
Icon=$install_dir/desksync.ico
Terminal=false
Categories=Network;FileTransfer;
DESKTOP
ready=false
for _ in {1..80}; do
 if curl --fail --silent http://127.0.0.1:5392/ >/dev/null; then ready=true; break; fi
 sleep 0.25
done
if ! $ready; then echo 'Dashboard did not start. Inspect journalctl --user -u desksync or ~/.local/opt/desksync/dashboard.log for missing dependencies or a port conflict.' >&2; exit 1; fi
echo 'Open http://127.0.0.1:5392 on this Linux computer to finish setup.'
echo 'Sync folder defaults to ~/Desksync. Private state is in ${XDG_STATE_HOME:-~/.local/state}/desksync.'
