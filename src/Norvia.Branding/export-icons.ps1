#Requires -Version 7.4
<#
.SYNOPSIS
    Exports the other Norvia icon formats from wwwroot/favicon.svg, which is the source.
.DESCRIPTION
    Needs rsvg-convert (librsvg) and ImageMagick (magick) on the PATH, on Windows too.
    The ICO and the Apple touch icon cannot follow the browser's colour scheme, so they sit on a white tile that reads on any tab or home screen.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

foreach ($tool in 'rsvg-convert', 'magick') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "$tool is required but was not found on the PATH."
    }
}

# Runs a native command and fails on a non-zero exit code.
function Invoke-Native {
    param([Parameter(Mandatory)][string] $Command, [string[]] $Arguments)
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE." }
}

$wwwroot = Join-Path $PSScriptRoot 'wwwroot'
$work = Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetRandomFileName())
New-Item -ItemType Directory -Path $work | Out-Null
Push-Location $wwwroot
try {
    # The mark on a white rounded tile: the tile is the background, the SVG is drawn inside it with a margin.
    function New-Tile {
        param([int] $Size)
        $margin = [int][math]::Floor($Size / 8)
        $radius = [int][math]::Floor($Size / 5)
        $inner = $Size - 2 * $margin
        $mark = Join-Path $work "mark-$Size.png"
        Invoke-Native rsvg-convert @('--width', $inner, '--height', $inner, 'favicon.svg', '-o', $mark)
        Invoke-Native magick @('-size', "${Size}x${Size}", 'xc:none', '-fill', 'white',
            '-draw', "roundrectangle 0,0 $($Size - 1),$($Size - 1) $radius,$radius",
            $mark, '-gravity', 'center', '-composite', (Join-Path $work "tile-$Size.png"))
    }

    foreach ($size in 16, 32, 48) { New-Tile $size }
    Invoke-Native magick @((Join-Path $work 'tile-16.png'), (Join-Path $work 'tile-32.png'), (Join-Path $work 'tile-48.png'), 'favicon.ico')

    # iOS fills transparency with black and rounds the corners itself, so the touch icon is a full-bleed white square.
    $touchMark = Join-Path $work 'touch-mark.png'
    Invoke-Native rsvg-convert @('--width', 140, '--height', 140, 'favicon.svg', '-o', $touchMark)
    Invoke-Native magick @('-size', '180x180', 'xc:white', $touchMark, '-gravity', 'center', '-composite', 'apple-touch-icon.png')
}
finally {
    Pop-Location
    Remove-Item -Path $work -Recurse -Force -ErrorAction SilentlyContinue
}
