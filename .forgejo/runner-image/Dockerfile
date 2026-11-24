# The job image of the Forgejo CI runner (#1038, docs/operations/forgejo.md "Runner for CI"). A job runs in it, so it carries what the gates need
# without installing it each run: the .NET SDK, PowerShell and the pinned tools (infra/tool-versions.psd1, installed with infra/install-powershell.sh
# and infra/install-tools.ps1 as the dev container does), Node, jq, yq, git, and a Docker daemon that each job starts for itself (start-dockerd)
# so the AppHosts of parallel legs cannot clash on container names or ports.
# The build context is the repository root. Rebuilt by .forgejo/workflows/runner-image.yml when this folder, the install scripts, tool-versions.psd1
# or global.json changes.
# The SDK is the floating 10.0 tag: global.json pins 10.0.400 with rollForward latestFeature, so any 10.0 image with a feature band at or above it
# works. A rebuild takes whatever 10.0 is current; it does not follow global.json. Raise the tag if global.json ever moves to a new major or minor.
FROM mcr.microsoft.com/dotnet/sdk:10.0

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl git jq tar xz-utils sudo docker.io iptables \
    && rm -rf /var/lib/apt/lists/*

# Trust for the homelab CA that signs git.home.arpa (Forgejo and its registry). The job container does not inherit the host's trust, so without it
# `dotnet publish /t:PublishContainer` fails with CONTAINER1013 (SSL connection could not be established). Only the public certificate lives here.
# update-ca-certificates installs it into the system bundle, which .NET and curl read. Replace the file when the CA is rotated and rebuild the image.
COPY .forgejo/runner-image/homelab-ca.crt /usr/local/share/ca-certificates/homelab-ca.crt
RUN update-ca-certificates

# PowerShell first, because the install script is PowerShell. The same two steps as .devcontainer/Dockerfile; yq comes with the rest of the manifest.
COPY infra/tool-versions.psd1 /tmp/tool-versions.psd1
COPY infra/install-powershell.sh /tmp/install-powershell.sh
RUN /tmp/install-powershell.sh /tmp/tool-versions.psd1 && rm /tmp/install-powershell.sh

COPY infra/install-tools.ps1 /tmp/install-tools.ps1
RUN pwsh -NoProfile -File /tmp/install-tools.ps1 && rm /tmp/install-tools.ps1 /tmp/tool-versions.psd1

# The NuGet cache is a path the runner mounts as a volume (.forgejo/runner/config.yaml), so restored packages survive between runs.
ENV NUGET_PACKAGES=/nuget-cache \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
RUN mkdir -p /nuget-cache

# dockerd's storage cannot be overlay2 on top of the container's own overlayfs, and falls back to the very slow vfs. A volume here is a fresh,
# per-job disk (an anonymous volume, removed with the job container) that overlay2 can use.
VOLUME /var/lib/docker

COPY .forgejo/runner-image/start-dockerd.sh /usr/local/bin/start-dockerd
RUN chmod 0755 /usr/local/bin/start-dockerd
