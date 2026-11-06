#Requires -Version 7.4
<#
.SYNOPSIS
    azd postdeploy hook: the databases' migrations, the Logic Apps, which need the address of the TMS just deployed, the
    sign-in callbacks, which need the demo UIs', the Integration console's, the Simulator's and the ERP's, and the
    Agents, in that order. The first step that fails fails the deployment.
.DESCRIPTION
    SKIP_POSTDEPLOY=true skips all of it, for a deployment of one app that changes none of that:
        SKIP_POSTDEPLOY=true azd deploy health-check-web
#>
$ErrorActionPreference = 'Stop'

if ($env:SKIP_POSTDEPLOY -eq 'true') {
    Write-Host 'SKIP_POSTDEPLOY is true: the migrations, the Logic Apps, the sign-in callbacks and the Agents are not deployed.'
    return
}

function Invoke-Step {
    param([Parameter(Mandatory)][string] $Script, [string[]] $Arguments = @())
    # The throw of a script's Write-Fail stops this one.
    & (Join-Path $PSScriptRoot $Script) @Arguments
}

Invoke-Step migrate.ps1 tms-migrations, 'the TMS database'
Invoke-Step migrate.ps1 partner-portal-migrations, "the Partner portal's database"
Invoke-Step migrate.ps1 driver-app-migrations, "the Driver app's database"
Invoke-Step migrate.ps1 simulator-migrations, "the Simulator's database"
Invoke-Step migrate.ps1 erp-migrations, "the ERP's database"
Invoke-Step deploy-intake-logic-app.ps1
Invoke-Step deploy-booking-logic-app.ps1
Invoke-Step deploy-shipper-notification-logic-app.ps1
Invoke-Step deploy-carrier-cancellation-logic-app.ps1
Invoke-Step deploy-trip-dispatch-logic-app.ps1
Invoke-Step deploy-driver-reports-logic-app.ps1
Invoke-Step deploy-conversation-sweeper-logic-app.ps1
Invoke-Step deploy-billing-logic-app.ps1
Invoke-Step deploy-carrier-cost-logic-app.ps1
Invoke-Step deploy-shipper-invoice-logic-app.ps1
Invoke-Step deploy-answer-logic-app.ps1
Invoke-Step deploy-carrier-invoice-logic-app.ps1
Invoke-Step deploy-carrier-invoice-outcome-logic-app.ps1
Invoke-Step deploy-tracking-logic-app.ps1
Invoke-Step deploy-proof-of-delivery-logic-app.ps1
Invoke-Step register-sign-in-callbacks.ps1
Invoke-Step deploy-agents.ps1
