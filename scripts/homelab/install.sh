#!/bin/bash
# First installation only; credentials and starting the service are separate steps.
set -Eeuo pipefail
[[ $(id -u) == 0 && $(hostname) == homelab-server ]] || {
    echo 'Run as root on homelab-server.' >&2
    exit 1
}
source_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
for binary in /usr/bin/python3 /usr/bin/cloudflared /usr/bin/systemctl /usr/bin/systemd-analyze; do
    [[ -x $binary ]] || { echo "Missing prerequisite: $binary" >&2; exit 1; }
done
for file in quick_tunnel.py helpdesk-demo.service demo.env.example; do
    [[ -f $source_directory/$file && ! -L $source_directory/$file ]] || {
        echo "Missing or symlinked source file: $file" >&2
        exit 1
    }
done
for path in /opt/helpdesk-demo /etc/helpdesk-demo /etc/systemd/system/helpdesk-demo.service; do
    if [[ -e $path || -L $path ]]; then
        echo "Already exists. Stop and inspect: $path" >&2
        exit 1
    fi
done
systemctl is-enabled --quiet helpdesk-web-boot.service
python3 -c 'import ast, pathlib, sys; ast.parse(pathlib.Path(sys.argv[1]).read_text())' "$source_directory/quick_tunnel.py"
systemd-analyze verify --man=no "$source_directory/helpdesk-demo.service"

install -d -o root -g root -m 0755 /opt/helpdesk-demo
install -d -o root -g root -m 0700 /etc/helpdesk-demo
install -o root -g root -m 0644 "$source_directory/quick_tunnel.py" /opt/helpdesk-demo/quick_tunnel.py
install -o root -g root -m 0644 "$source_directory/helpdesk-demo.service" /etc/systemd/system/helpdesk-demo.service
install -o root -g root -m 0600 "$source_directory/demo.env.example" /etc/helpdesk-demo/config
systemctl daemon-reload
echo 'Installed only. Save the dedicated GitHub token privately, then enable helpdesk-demo.service.'
