#!/usr/bin/env bash
set -uo pipefail
printf 'DeskSync Linux diagnostic (no account tokens or private configuration)\n'
printf 'User: '; id -un
printf 'Architecture: '; uname -m
printf 'Hostname: '; hostname
printf 'Network namespace: '; readlink /proc/self/ns/net || true
printf 'Distribution: '; if [ -r /etc/os-release ]; then . /etc/os-release; printf '%s\n' "$PRETTY_NAME"; else uname -s; fi
printf '\nDirect local dashboard check:\n'
curl --noproxy '*' --connect-timeout 2 --max-time 3 --fail --show-error http://127.0.0.1:5392/health || true
printf '\nListening ports 5391/5392 in this environment:\n'
if command -v ss >/dev/null; then ss -ltn '( sport = :5391 or sport = :5392 )'; fi
printf '\nUser service status:\n'
if command -v systemctl >/dev/null; then systemctl --user show desksync.service -p ActiveState -p SubState -p Result -p ExecMainStatus --no-pager 2>&1 || true; fi
printf '\nFallback process:\n'
dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
if [ -r "$dir/dashboard.pid" ]; then
 pid=$(cat "$dir/dashboard.pid")
 if [[ "$pid" =~ ^[0-9]+$ ]]; then ps -p "$pid" -o pid=,comm=,stat= || true; fi
fi
printf '\nNext: run this script from the terminal ON THE VISIBLE DESKTOP.\n'
printf 'If an agent terminal works but desktop Chrome refuses the connection, compare their hostnames and network namespaces. Do not bind publicly to bridge environments.\n'
printf 'Startup logs: journalctl --user -u desksync -n 60, or %s/dashboard.log\n' "$dir"
