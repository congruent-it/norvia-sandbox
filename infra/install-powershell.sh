#!/usr/bin/env bash
# Installs the PowerShell of infra/tool-versions.psd1 (the PowerShell line) into /opt/microsoft/powershell/7 and links it as /usr/local/bin/pwsh.
# The dev container (.devcontainer/Dockerfile) and the Forgejo runner image (.forgejo/runner-image/Dockerfile) run it first, because
# infra/install-tools.ps1 is PowerShell. The download is checked against the SHA-256 PowerShell publishes. Linux, amd64 or arm64.
# Usage: install-powershell.sh <path to tool-versions.psd1>
set -euo pipefail

manifest="${1:?usage: install-powershell.sh <tool-versions.psd1>}"
version="$(sed -nE "s/^[[:space:]]*PowerShell[[:space:]]*=[[:space:]]*'([0-9.]+)'.*/\1/p" "$manifest")"
arch="$(dpkg --print-architecture)"
case "$arch" in amd64) arch=x64 ;; arm64) arch=arm64 ;; esac
file="powershell-${version}-linux-${arch}.tar.gz"
base="https://github.com/PowerShell/PowerShell/releases/download/v${version}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

curl -fsSL "$base/$file" -o "$work/$file"
curl -fsSL "$base/hashes.sha256" | iconv -f UTF-16 -t UTF-8 | tr -d '\r' > "$work/hashes.sha256"
(cd "$work" && grep -F " *$file" hashes.sha256 | sha256sum -c -)
mkdir -p /opt/microsoft/powershell/7
tar -xzf "$work/$file" -C /opt/microsoft/powershell/7
chmod +x /opt/microsoft/powershell/7/pwsh
ln -sf /opt/microsoft/powershell/7/pwsh /usr/local/bin/pwsh
