#!/usr/bin/env bash
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
fixture=$(mktemp -d)
mkdir -p "$fixture/bin" "$fixture/app"
cp "$root/scripts/linux/open.sh" "$fixture/app/open.sh"
export DESKSYNC_TEST_DIR="$fixture"
cat > "$fixture/bin/curl" <<'MOCK'
#!/usr/bin/env bash
[ -f "$DESKSYNC_TEST_DIR/ready" ] || exit 7
printf '{"app":"desksync","version":"test"}'
MOCK
cat > "$fixture/bin/systemctl" <<'MOCK'
#!/usr/bin/env bash
case "$*" in
 *is-enabled*) [ "${DESKSYNC_TEST_SYSTEMD:-no}" = yes ] ;;
 *start*) echo service-start >> "$DESKSYNC_TEST_DIR/events"; touch "$DESKSYNC_TEST_DIR/ready" ;;
esac
MOCK
cat > "$fixture/bin/xdg-open" <<'MOCK'
#!/usr/bin/env bash
echo "open:$1" >> "$DESKSYNC_TEST_DIR/events"
MOCK
cat > "$fixture/app/start.sh" <<'MOCK'
#!/usr/bin/env bash
echo fallback-start >> "$DESKSYNC_TEST_DIR/events"
[ "${DESKSYNC_TEST_FAIL:-no}" = yes ] || touch "$DESKSYNC_TEST_DIR/ready"
MOCK
printf '#!/usr/bin/env bash\nexit 0\n' > "$fixture/bin/sleep"
printf '#!/usr/bin/env bash\nexit 0\n' > "$fixture/bin/notify-send"
chmod +x "$fixture/bin/"* "$fixture/app/"*.sh
export PATH="$fixture/bin:$PATH"
export DESKSYNC_TEST_SYSTEMD=no
bash "$fixture/app/open.sh"
test "$(cat "$fixture/events")" = $'fallback-start\nopen:http://127.0.0.1:5392'
echo 'PASS Stopped fallback starts before the dashboard opens'
: > "$fixture/events"
bash "$fixture/app/open.sh"
test "$(cat "$fixture/events")" = 'open:http://127.0.0.1:5392'
echo 'PASS Running dashboard opens without a duplicate process'
rm "$fixture/ready"
: > "$fixture/events"
export DESKSYNC_TEST_SYSTEMD=yes
bash "$fixture/app/open.sh"
test "$(cat "$fixture/events")" = $'service-start\nopen:http://127.0.0.1:5392'
echo 'PASS Enabled service starts before the dashboard opens'
rm "$fixture/ready"
: > "$fixture/events"
export DESKSYNC_TEST_SYSTEMD=no DESKSYNC_TEST_FAIL=yes
if bash "$fixture/app/open.sh" 2> "$fixture/error"; then echo 'Expected startup failure' >&2; exit 1; fi
! grep -q '^open:' "$fixture/events"
grep -q 'diagnose.sh' "$fixture/error"
echo 'PASS Startup failure reports diagnostics and never opens a broken page'
echo "4 launcher checks passed. Isolated fixture: $fixture"
