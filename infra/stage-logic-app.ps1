#Requires -Version 7.4
<#
.SYNOPSIS
    Zips a Logic App project for deployment (infra/deploy-*-logic-app.ps1), leaving the tracked properties out of the
    workflows that the as-found toggle logicAppsWithoutTrackedProperties lists, and replacing the calls that tell the
    Driver app what became of a report with an answer that says all is well when the toggle noReportBackToDriverApp is
    set (docs/operations/as-found.md). The sources are never changed.
.DESCRIPTION
    The toggles are read from ESTATE_ASFOUND, which azd keeps in its environment; unset is the fixed state.
.EXAMPLE
    ./stage-logic-app.ps1 src/Integration/BookingLogicApp /tmp/booking.zip host.json parameters.json carrier-booking lib
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)][string] $Project,
    [Parameter(Mandatory, Position = 1)][string] $ZipFile,
    [Parameter(Mandatory, Position = 2, ValueFromRemainingArguments)][string[]] $Include
)
$ErrorActionPreference = 'Stop'
# Workflow nodes have no fixed set of properties, and a script inherits the strict mode of the module that calls it.
Set-StrictMode -Off

function Edit-JsonNode {
    # Visits every object of a parsed JSON document, the walk of the jq version.
    param($Node, [scriptblock] $Visit)
    if ($Node -is [pscustomobject]) {
        & $Visit $Node
        foreach ($property in @($Node.PSObject.Properties)) { Edit-JsonNode $property.Value $Visit }
    }
    elseif ($Node -is [Collections.IList]) {
        foreach ($item in $Node) { Edit-JsonNode $item $Visit }
    }
}

function Edit-Workflow {
    param([string] $Path, [scriptblock] $Visit)
    $workflow = Get-Content -Path $Path -Raw | ConvertFrom-Json -Depth 100
    Edit-JsonNode $workflow $Visit
    $workflow | ConvertTo-Json -Depth 100 | Set-Content -Path $Path -Encoding utf8NoBOM
}

$work = Join-Path ([IO.Path]::GetTempPath()) "stage-logic-app-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null
try {
    Copy-Item -Path (Join-Path $Project '*') -Destination $work -Recurse -Force

    $asFound = $env:ESTATE_ASFOUND ? ($env:ESTATE_ASFOUND | ConvertFrom-Json) : $null

    foreach ($flow in @($asFound.logicAppsWithoutTrackedProperties)) {
        $workflowFile = Join-Path $work $flow 'workflow.json'
        if ($flow -and (Test-Path -Path $workflowFile -PathType Leaf)) {
            Edit-Workflow $workflowFile { param($node) $node.PSObject.Properties.Remove('trackedProperties') }
            Write-Host "Leaving the tracked properties out of $flow (as-found)."
        }
    }

    # No report back to the Driver app (P12): the report-outcomes calls are not made. Each becomes a step that answers 200
    # and does nothing, so the rest of the workflow, which reads the answer, runs as it does when the Driver app is well.
    $driverReports = Join-Path $work 'driver-reports' 'workflow.json'
    if ($asFound.noReportBackToDriverApp -eq $true -and (Test-Path -Path $driverReports -PathType Leaf)) {
        Edit-Workflow $driverReports {
            param($node)
            if ($node.type -eq 'Http' -and ([string]$node.inputs.uri).Contains('/v1/report-outcomes')) {
                $node.type = 'Compose'
                $node.inputs = [pscustomobject]@{ statusCode = 200 }
            }
        }
        Write-Host 'Leaving out the reports to the Driver app (as-found).'
    }

    Remove-Item -Path $ZipFile -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path ($Include | ForEach-Object { Join-Path $work $_ }) -DestinationPath $ZipFile
}
finally {
    Remove-Item -Path $work -Recurse -Force -ErrorAction SilentlyContinue
}
