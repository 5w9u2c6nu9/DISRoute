#!/usr/bin/env bash
set -euo pipefail
if [[ ${EUID:-$(id -u)} -ne 0 ]]; then echo "Run as root." >&2; exit 2; fi
SOURCE=${1:-}
if [[ -z "$SOURCE" || ! -x "$SOURCE/DISRoute.Server" ]]; then echo "Usage: $0 <published-server-directory>" >&2; exit 2; fi
id -u disroute >/dev/null 2>&1 || useradd --system --home /var/lib/disroute --shell /usr/sbin/nologin disroute
install -d -o root -g root -m 0755 /opt/disroute/server /etc/disroute
install -d -o disroute -g disroute -m 0700 /var/lib/disroute /var/log/disroute /run/disroute /etc/disroute/secrets
cp -a "$SOURCE"/. /opt/disroute/server/
chown -R root:root /opt/disroute/server
find /opt/disroute/server -type d -exec chmod 0755 {} +
find /opt/disroute/server -type f -exec chmod 0644 {} +
chmod 0755 /opt/disroute/server/DISRoute.Server
install -o root -g root -m 0644 "$(dirname "$0")/disroute-server.service" /etc/systemd/system/disroute-server.service
if [[ ! -f /etc/disroute/appsettings.Production.json ]]; then
  install -o root -g disroute -m 0640 "$(dirname "$0")/../common/appsettings.Production.template.json" /etc/disroute/appsettings.Production.json
fi
systemctl daemon-reload
systemctl enable disroute-server.service
cat <<MSG
DISRoute Server installed. Provision /etc/disroute/appsettings.Production.json and 0600/0400 secret files under /etc/disroute/secrets, then run:
  systemctl start disroute-server
  systemctl status disroute-server
MSG
