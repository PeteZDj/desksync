#!/usr/bin/env bash
set -euo pipefail
umask 077
dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
url=http://127.0.0.1:5392
ready() {
 curl --noproxy '*' --connect-timeout 1 --max-time 2 --fail --silent "$url/health" 2>/dev/null | grep -q '"app":"desksync"'
}
if ! ready; then
 if command -v systemctl >/dev/null && systemctl --user is-enabled --quiet desksync.service 2>/dev/null; then
  systemctl --user start desksync.service || true
 else
  "$dir/start.sh"
 fi
 for _ in {1..120}; do ready && break; sleep 0.25; done
fi
if ! ready; then
 message='The DeskSync dashboard did not start on this desktop. Run ~/.local/opt/desksync/diagnose.sh and inspect the startup log. Keep the dashboard private.'
 printf '%s\n' "$message" >&2
 if command -v notify-send >/dev/null; then notify-send 'DeskSync needs attention' "$message" || true; fi
 exit 1
fi
exec xdg-open "$url"
