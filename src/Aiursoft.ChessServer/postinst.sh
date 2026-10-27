#!/bin/sh
set -e
mkdir -p /var/lib/aiursoft-chessServer
chown www-data:www-data /var/lib/aiursoft-chessServer

# Bootstrap config if missing (matching Docker entrypoint pattern)
if [ ! -f /etc/aiursoft-chessServer/appsettings.json ]; then
    cp /usr/share/aiursoft-chessServer/appsettings.json /etc/aiursoft-chessServer/appsettings.json
fi
# Replace /usr/share copy with symlink to /etc
rm -f /usr/share/aiursoft-chessServer/appsettings.json
ln -sf /etc/aiursoft-chessServer/appsettings.json /usr/share/aiursoft-chessServer/appsettings.json
