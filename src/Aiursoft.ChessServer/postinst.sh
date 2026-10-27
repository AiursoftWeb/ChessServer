#!/bin/sh
set -e
mkdir -p /var/lib/aiursoft-chessserver
chown www-data:www-data /var/lib/aiursoft-chessserver

# Bootstrap config if missing (matching Docker entrypoint pattern)
if [ ! -f /etc/aiursoft-chessserver/appsettings.json ]; then
    cp /usr/share/aiursoft-chessserver/appsettings.json /etc/aiursoft-chessserver/appsettings.json
fi
# Replace /usr/share copy with symlink to /etc
rm -f /usr/share/aiursoft-chessserver/appsettings.json
ln -sf /etc/aiursoft-chessserver/appsettings.json /usr/share/aiursoft-chessserver/appsettings.json
