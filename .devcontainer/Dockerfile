# The dev container for the Kubernetes slices (#923): the .NET SDK, PowerShell 7 (ADR 0048), helm, kubectl, kubeconform, bicep, Node (the stylesheet build) and the
# PowerShell modules the script gate needs. librsvg2-bin is rsvg-convert, which turns the diagram generators' SVG into the PNGs of the docs. Versions: global.json (the SDK, through the image tag's feature band) and
# infra/tool-versions.psd1 (everything else), which CI installs from too.
# The build context is the repository root (devcontainer.json).
FROM mcr.microsoft.com/dotnet/sdk:10.0

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl git librsvg2-bin tar xz-utils sudo \
    && rm -rf /var/lib/apt/lists/*

# PowerShell comes first because the install script is PowerShell. Its version is the PowerShell line of the manifest.
COPY infra/tool-versions.psd1 /tmp/tool-versions.psd1
RUN set -eux; \
    version="$(sed -nE "s/^[[:space:]]*PowerShell[[:space:]]*=[[:space:]]*'([0-9.]+)'.*/\1/p" /tmp/tool-versions.psd1)"; \
    arch="$(dpkg --print-architecture)"; \
    case "$arch" in amd64) arch=x64 ;; arm64) arch=arm64 ;; esac; \
    file="powershell-${version}-linux-${arch}.tar.gz"; \
    base="https://github.com/PowerShell/PowerShell/releases/download/v${version}"; \
    curl -fsSL "$base/$file" -o "/tmp/$file"; \
    curl -fsSL "$base/hashes.sha256" | iconv -f UTF-16 -t UTF-8 | tr -d '\r' > /tmp/hashes.sha256; \
    (cd /tmp && grep -F " *$file" hashes.sha256 | sha256sum -c -); \
    mv "/tmp/$file" /tmp/pwsh.tgz; \
    mkdir -p /opt/microsoft/powershell/7; \
    tar -xzf /tmp/pwsh.tgz -C /opt/microsoft/powershell/7; \
    chmod +x /opt/microsoft/powershell/7/pwsh; \
    ln -s /opt/microsoft/powershell/7/pwsh /usr/local/bin/pwsh; \
    rm /tmp/pwsh.tgz /tmp/hashes.sha256

COPY infra/install-tools.ps1 /tmp/install-tools.ps1
RUN pwsh -NoProfile -File /tmp/install-tools.ps1 && rm /tmp/install-tools.ps1 /tmp/tool-versions.psd1

ARG USERNAME=vscode
RUN useradd --create-home --shell /bin/bash "$USERNAME" \
    && echo "$USERNAME ALL=(root) NOPASSWD:ALL" > /etc/sudoers.d/$USERNAME \
    && chmod 0440 /etc/sudoers.d/$USERNAME
USER $USERNAME
