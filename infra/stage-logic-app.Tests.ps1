#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

BeforeAll {
    $script:stage = Join-Path $PSScriptRoot 'stage-logic-app.ps1'
    $script:project = Join-Path $PSScriptRoot '../src/Integration/DriverReportsLogicApp'

    function script:Get-StagedWorkflow([string] $AsFound) {
        $zip = Join-Path ([IO.Path]::GetTempPath()) "stage-$([guid]::NewGuid().ToString('N')).zip"
        $previous = $env:ESTATE_ASFOUND
        try {
            $env:ESTATE_ASFOUND = $AsFound
            & $script:stage $script:project $zip host.json driver-reports | Out-Null
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $archive = [IO.Compression.ZipFile]::OpenRead($zip)
            try {
                $entry = $archive.GetEntry('driver-reports/workflow.json')
                (New-Object IO.StreamReader($entry.Open())).ReadToEnd()
            }
            finally { $archive.Dispose() }
        }
        finally {
            $env:ESTATE_ASFOUND = $previous
            Remove-Item $zip -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'stage-logic-app.ps1' {
    It 'keeps the tracked properties and the report-outcomes calls when nothing is toggled' {
        $workflow = Get-StagedWorkflow ''
        $workflow | Should -Match 'trackedProperties'
        $workflow | Should -Match '/v1/report-outcomes'
    }

    It 'leaves the tracked properties out of a listed workflow' {
        Get-StagedWorkflow '{"logicAppsWithoutTrackedProperties":["driver-reports"]}' | Should -Not -Match 'trackedProperties'
    }

    It 'turns the report-outcomes calls into a step that answers 200' {
        $workflow = Get-StagedWorkflow '{"noReportBackToDriverApp":true}'
        $workflow | Should -Not -Match '"uri"\s*:\s*"[^"]*/v1/report-outcomes'
        $workflow | Should -Match '"statusCode": 200'
    }

    It 'does not change the sources' {
        $before = (Get-FileHash (Join-Path $script:project 'driver-reports/workflow.json')).Hash
        $null = Get-StagedWorkflow '{"noReportBackToDriverApp":true,"logicAppsWithoutTrackedProperties":["driver-reports"]}'
        (Get-FileHash (Join-Path $script:project 'driver-reports/workflow.json')).Hash | Should -Be $before
    }
}
