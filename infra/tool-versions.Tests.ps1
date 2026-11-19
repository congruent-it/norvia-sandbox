#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

# The manifest the dev container and CI install from (#993).
BeforeAll {
    $script:manifestPath = Join-Path $PSScriptRoot 'tool-versions.psd1'
    $script:versions = Import-PowerShellDataFile $script:manifestPath
    $script:root = Split-Path $PSScriptRoot -Parent
}

Describe 'tool-versions.psd1' {
    It 'pins <_> to an exact version' -ForEach 'PowerShell', 'Helm', 'Kubectl', 'Kubeconform', 'Bicep', 'PSScriptAnalyzer', 'Pester' {
        $script:versions[$_] | Should -Match '^\d+\.\d+\.\d+$'
    }

    It 'pins a SHA-256 for the <_> Bicep binary' -ForEach 'amd64', 'arm64' {
        $script:versions.BicepSha256[$_] | Should -Match '^[0-9a-f]{64}$'
    }

    It 'pins a PowerShell the scripts accept (#Requires -Version 7.4)' {
        [version]$script:versions.PowerShell | Should -BeGreaterOrEqual ([version]'7.4.0')
    }

    It 'has a PowerShell line the Dockerfile can read with sed' {
        $dockerfile = Get-Content (Join-Path $script:root '.devcontainer/Dockerfile') -Raw
        $pattern = [regex]::Match($dockerfile, 'sed -nE "([^"]+)"').Groups[1].Value
        $pattern | Should -Not -BeNullOrEmpty
        $line = (Get-Content $script:manifestPath | Where-Object { $_ -match "^\s*PowerShell\s*=" })
        $line.Count | Should -Be 1
        $line | Should -Match "^\s*PowerShell\s*=\s*'$([regex]::Escape($script:versions.PowerShell))'"
    }
}

Describe 'the dev container' {
    It 'builds from the repository root so the image can copy the install script and manifest' {
        $config = Get-Content (Join-Path $script:root '.devcontainer/devcontainer.json') -Raw | ConvertFrom-Json
        $config.build.context | Should -Be '..'
    }

    It 'installs its tools with the script CI uses' {
        $dockerfile = Get-Content (Join-Path $script:root '.devcontainer/Dockerfile') -Raw
        $dockerfile | Should -Match 'install-tools\.ps1'
        (Get-Content (Join-Path $script:root '.github/workflows/ci.yml') -Raw) | Should -Match 'install-tools\.ps1'
    }
}
