# The versions of the tools the Kubernetes slices (#923) need, in one place. The dev container (.devcontainer/) and CI both install
# them with infra/install-tools.ps1, so a test that passes in the container passes in CI. Bump a version here and nowhere else.
# The .NET SDK is not listed: global.json owns it.
@{
    # PowerShell is installed by .devcontainer/Dockerfile, which reads this line; CI uses the runner's own pwsh (7.4 or later, ADR 0048).
    PowerShell        = '7.7.0'
    Helm              = '4.3.2'
    Kubectl           = '1.37.2'
    Kubeconform       = '0.8.0'
    # The modules infra/test-scripts.ps1 needs.
    PSScriptAnalyzer  = '1.24.0'
    Pester            = '5.7.1'
}
