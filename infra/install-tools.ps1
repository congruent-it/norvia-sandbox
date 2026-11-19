#Requires -Version 7.4
<#
.SYNOPSIS
    Installs the pinned Kubernetes tools (helm, kubectl, kubeconform, bicep) and the PowerShell modules the script gate needs.
.DESCRIPTION
    The versions come from infra/tool-versions.psd1. The dev container's image build and the CI workflow both run this script, so
    the two have the same tools. Linux only (amd64 or arm64). Each download is checked against the checksum its publisher posts.
    A tool already at the pinned version is left alone.
.PARAMETER BinDir
    Where the binaries go. Default /usr/local/bin (needs write access there; CI uses a directory on the PATH).
.PARAMETER Tool
    Which to install: Helm, Kubectl, Kubeconform, Bicep, Modules. Default: all.
#>
[CmdletBinding()]
param(
    [string] $BinDir = '/usr/local/bin',
    [ValidateSet('Helm', 'Kubectl', 'Kubeconform', 'Bicep', 'Modules')]
    [string[]] $Tool = @('Helm', 'Kubectl', 'Kubeconform', 'Bicep', 'Modules')
)
$ErrorActionPreference = 'Stop'

if (-not $IsLinux) { throw 'install-tools.ps1 installs Linux binaries; run it in the dev container or on a Linux runner.' }
$versions = Import-PowerShellDataFile (Join-Path $PSScriptRoot 'tool-versions.psd1')
$arch = switch ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
    'X64' { 'amd64' }
    'Arm64' { 'arm64' }
    default { throw "Unsupported architecture $_" }
}
$work = Join-Path ([IO.Path]::GetTempPath()) "norvia-tools-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null

function Save-Download {
    # Downloads $Url and fails unless its SHA-256 equals the one in $ChecksumUrl (a file whose line for $Name, or whose only line, starts with the hash).
    param([string] $Url, [string] $ChecksumUrl, [string] $Name)
    $file = Join-Path $work $Name
    Invoke-WebRequest -Uri $Url -OutFile $file
    $lines = (Invoke-RestMethod -Uri $ChecksumUrl) -split "`n" | Where-Object { $_.Trim() }
    $line = $lines | Where-Object { $_ -match "\b$([regex]::Escape($Name))\b" } | Select-Object -First 1
    # kubectl's .sha256 holds the bare hash, alone on its line. Any other file must name the asset, or the download fails.
    if (-not $line -and @($lines).Count -eq 1 -and $lines -match '^[0-9a-fA-F]{64}$') { $line = $lines }
    if (-not $line) { throw "No checksum for $Name in $ChecksumUrl" }
    $expected = ($line.Trim() -split '\s+')[0]
    $actual = (Get-FileHash -Algorithm SHA256 -Path $file).Hash
    if ($actual -ne $expected) { throw "Checksum mismatch for $Url (expected $expected, got $actual)" }
    $file
}

function Test-InstalledVersion {
    param([string] $Command, [string[]] $Arguments, [string] $Version)
    $path = Join-Path $BinDir $Command
    if (-not (Test-Path $path)) { return $false }
    return ((& $path @Arguments 2>&1) -join ' ') -match "\bv?$([regex]::Escape($Version))\b"
}

function Install-Binary {
    param([string] $Source, [string] $Name)
    $target = Join-Path $BinDir $Name
    Copy-Item -Path $Source -Destination $target -Force
    chmod 0755 $target
}

try {
    if ($Tool -contains 'Helm' -and -not (Test-InstalledVersion 'helm' @('version', '--short') $versions.Helm)) {
        $name = "helm-v$($versions.Helm)-linux-$arch.tar.gz"
        $file = Save-Download "https://get.helm.sh/$name" "https://get.helm.sh/$name.sha256sum" $name
        tar -xzf $file -C $work
        Install-Binary (Join-Path $work "linux-$arch/helm") 'helm'
    }

    if ($Tool -contains 'Kubectl' -and -not (Test-InstalledVersion 'kubectl' @('version', '--client') $versions.Kubectl)) {
        $base = "https://dl.k8s.io/release/v$($versions.Kubectl)/bin/linux/$arch/kubectl"
        $file = Save-Download $base "$base.sha256" 'kubectl'
        Install-Binary $file 'kubectl'
    }

    if ($Tool -contains 'Kubeconform' -and -not (Test-InstalledVersion 'kubeconform' @('-v') $versions.Kubeconform)) {
        $name = "kubeconform-linux-$arch.tar.gz"
        $release = "https://github.com/yannh/kubeconform/releases/download/v$($versions.Kubeconform)"
        $file = Save-Download "$release/$name" "$release/CHECKSUMS" $name
        tar -xzf $file -C $work kubeconform
        Install-Binary (Join-Path $work 'kubeconform') 'kubeconform'
    }

    if ($Tool -contains 'Bicep' -and -not (Test-InstalledVersion 'bicep' @('--version') $versions.Bicep)) {
        # Pinned by the hash in the manifest: the release has no checksum file.
        $name = "bicep-linux-$($arch -eq 'amd64' ? 'x64' : $arch)"
        $file = Join-Path $work $name
        Invoke-WebRequest -Uri "https://github.com/Azure/bicep/releases/download/v$($versions.Bicep)/$name" -OutFile $file
        $actual = (Get-FileHash -Algorithm SHA256 -Path $file).Hash
        if ($actual -ne $versions.BicepSha256[$arch]) { throw "Checksum mismatch for $name (expected $($versions.BicepSha256[$arch]), got $actual)" }
        Install-Binary $file 'bicep'
    }

    if ($Tool -contains 'Modules') {
        $scope = (id -u) -eq '0' ? 'AllUsers' : 'CurrentUser'
        Set-PSResourceRepository -Name PSGallery -Trusted
        Install-PSResource -Name PSScriptAnalyzer -Version $versions.PSScriptAnalyzer -Repository PSGallery -Scope $scope -Quiet
        Install-PSResource -Name Pester -Version $versions.Pester -Repository PSGallery -Scope $scope -Quiet
    }
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}

foreach ($t in $Tool | Where-Object { $_ -ne 'Modules' }) {
    $cmd = $t.ToLowerInvariant()
    Write-Host "$cmd $(& (Join-Path $BinDir $cmd) $(if ($cmd -eq 'kubeconform') { '-v' } elseif ($cmd -eq 'bicep') { '--version' } elseif ($cmd -eq 'helm') { 'version', '--short' } else { 'version', '--client' }) 2>&1 | Select-Object -First 1)"
}
