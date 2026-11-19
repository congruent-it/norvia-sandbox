# The versions of the tools the Kubernetes slices (#923) need, in one place. The dev container (.devcontainer/) and CI both install
# them with infra/install-tools.ps1, so a test that passes in the container passes in CI. Bump a version here and nowhere else.
# The .NET SDK is not listed: global.json owns it.
@{
    # PowerShell is installed by .devcontainer/Dockerfile, which reads this line; CI uses the runner's own pwsh (7.4 or later, ADR 0048).
    PowerShell        = '7.7.0'
    Helm              = '4.3.2'
    Kubectl           = '1.37.2'
    Kubeconform       = '0.8.0'
    # Bicep publishes no checksum file, so the SHA-256 of each binary (the digest GitHub shows for the release asset) is pinned here with it.
    # The estate's architecture tests run `bicep snapshot`. CI's job for them gets the CLI from `az bicep install`, which the Bicep tests also find.
    Bicep             = '0.49.2'
    BicepSha256       = @{
        amd64 = '0ab9de90c2493f206f8281bae74f9ee2d5ed3605548dab1adf41fef185e1f7c5'
        arm64 = '48d8f152bd78c8848a41a07ac2d12b573306d9329fc961a85354a98a95cc275e'
    }
    # The modules infra/test-scripts.ps1 needs.
    PSScriptAnalyzer  = '1.24.0'
    Pester            = '5.7.1'
}
