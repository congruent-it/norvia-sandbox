# The dev container

A dev container (`.devcontainer/`) carries the tools the Kubernetes slices of [ADR 0053](../adr/0053-kubernetes-runs-the-local-topology.md) need, so nobody installs `helm`, `kubectl` or a cluster on their own machine. All it asks of the host is a container runtime and an editor that supports dev containers (VS Code, JetBrains, or `devcontainer up`).

| Tool | Version from | Used by |
|---|---|---|
| .NET SDK | [global.json](../../global.json), through the `10.0` image | building and testing the .NET code |
| PowerShell 7 | [infra/tool-versions.psd1](../../infra/tool-versions.psd1) | every script ([ADR 0048](../adr/0048-scripts-are-powershell-7-not-bash.md)) |
| PSScriptAnalyzer, Pester | `tool-versions.psd1` | `pwsh infra/test-scripts.ps1` |
| `helm` | `tool-versions.psd1` | the chart's template tests (`helm template`, `helm lint`), which are offline |
| `kubeconform` | `tool-versions.psd1` | validating the rendered manifests |
| `kubectl` | `tool-versions.psd1` | the smoke script (#932) |

## Run the checks

Inside the container:

```powershell
pwsh infra/test-scripts.ps1
```

## One set of versions

`infra/tool-versions.psd1` is the only place a version lives. The image build and the `powershell` job of [CI](../../.github/workflows/ci.yml) both run `infra/install-tools.ps1`, which checks each download (and the image build, PowerShell's) against the checksum its publisher posts, so a test that passes in the container passes in CI. To bump a tool, change the manifest. CI's runner brings its own `pwsh` (7.4 or later) and, unlike the container, the Azure CLI; the Pester tests stand in for `az` when it is missing.

## Not in the container

- **A cluster.** The chart is tested offline. #932's smoke run needs a throwaway K3S (k3d or a K3S container) and starts it itself; it is not part of the image, and nobody's daily cluster lives here.
- **Docker.** Aspire's own container needs are the host's ([running-locally.md](running-locally.md)), and the container does not change how the estate runs under Aspire.
- **The Azure CLI and `azd`.** Only the scripts that deploy to Azure call them, and those are run from the host, as before.
