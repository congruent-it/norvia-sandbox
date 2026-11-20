#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

# The manifest the dev container and CI install from (#993).
BeforeAll {
    $script:manifestPath = Join-Path $PSScriptRoot 'tool-versions.psd1'
    $script:versions = Import-PowerShellDataFile $script:manifestPath
    $script:root = Split-Path $PSScriptRoot -Parent
}

Describe 'tool-versions.psd1' {
    It 'pins <_> to an exact version' -ForEach 'PowerShell', 'Helm', 'Kubectl', 'Kubeconform', 'Yq', 'Bicep', 'PSScriptAnalyzer', 'Pester' {
        $script:versions[$_] | Should -Match '^\d+\.\d+\.\d+$'
    }

    It 'pins a SHA-256 for the <_> Bicep binary' -ForEach 'amd64', 'arm64' {
        $script:versions.BicepSha256[$_] | Should -Match '^[0-9a-f]{64}$'
    }

    It 'pins a SHA-256 for the <_> yq binary' -ForEach 'amd64', 'arm64' {
        $script:versions.YqSha256[$_] | Should -Match '^[0-9a-f]{64}$'
    }

    It 'pins a PowerShell the scripts accept (#Requires -Version 7.4)' {
        [version]$script:versions.PowerShell | Should -BeGreaterOrEqual ([version]'7.4.0')
    }

    It 'has a PowerShell line the install script can read with sed' {
        $script = Get-Content (Join-Path $script:root 'infra/install-powershell.sh') -Raw
        $pattern = [regex]::Match($script, 'sed -nE "([^"]+)"').Groups[1].Value
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

Describe 'the Forgejo runner image' {
    BeforeAll { $script:runnerDockerfile = Get-Content (Join-Path $script:root '.forgejo/runner-image/Dockerfile') -Raw }

    It 'installs PowerShell and its tools the way the dev container does' -ForEach '.devcontainer/Dockerfile', '.forgejo/runner-image/Dockerfile' {
        $dockerfile = Get-Content (Join-Path $script:root $_) -Raw
        $dockerfile | Should -Match 'install-powershell\.sh'
        $dockerfile | Should -Match 'install-tools\.ps1'
    }

    It 'builds from the repository root, which its COPY lines assume' {
        $script:runnerDockerfile | Should -Match 'COPY infra/tool-versions\.psd1'
        $script:runnerDockerfile | Should -Match 'COPY \.forgejo/runner-image/start-dockerd\.sh'
    }

    It 'is rebuilt when the manifest or the install scripts change' {
        $workflow = Get-Content (Join-Path $script:root '.forgejo/workflows/runner-image.yml') -Raw
        'infra/tool-versions.psd1', 'infra/install-tools.ps1', 'infra/install-powershell.sh', 'global.json' | ForEach-Object { $workflow | Should -Match ([regex]::Escape($_)) }
    }
}
