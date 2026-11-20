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
COPY infra/install-powershell.sh /tmp/install-powershell.sh
RUN /tmp/install-powershell.sh /tmp/tool-versions.psd1 && rm /tmp/install-powershell.sh

COPY infra/install-tools.ps1 /tmp/install-tools.ps1
RUN pwsh -NoProfile -File /tmp/install-tools.ps1 && rm /tmp/install-tools.ps1 /tmp/tool-versions.psd1

ARG USERNAME=vscode
# The base image ships an "ubuntu" user at UID/GID 1000, the same UID as the usual host user, so the workspace bind mount is writable. Rename it instead of adding a second one.
RUN groupmod --new-name "$USERNAME" ubuntu \
    && usermod --login "$USERNAME" --home "/home/$USERNAME" --move-home ubuntu \
    && echo "$USERNAME ALL=(root) NOPASSWD:ALL" > /etc/sudoers.d/$USERNAME \
    && chmod 0440 /etc/sudoers.d/$USERNAME
USER $USERNAME
