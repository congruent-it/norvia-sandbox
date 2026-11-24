#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy smoke run of the sandbox on Kubernetes (#932, ADR 0053, not in CI): every application, then a booking, tracking and a Proof of delivery.
.DESCRIPTION
    Run it after every `helm upgrade --install` of deploy/helm/norvia, from anywhere that can reach the cluster's ingress and
    has kubectl pointed at it:

      infra/smoke-test-kubernetes.ps1 -BaseDomain norvia.example.org

    It drives one Shipment from booking to delivery through the ingress hostnames, as the Personas would, and fails loudly with
    the step that broke:
      1. every application the chart deploys (Get-SmokeApplications: the Partner API, the TMS, ERP and Driver app APIs and UIs, the Integration
         console, the Estate health check, the Partner portal and the Simulator) answers /health on its ingress hostname (TLS from
         cert-manager, routed by the controller), and none is skipped for being missing: the first that does not answer fails the run;
      2. Brouwer Dranken books Rotterdam to Dusseldorf through the Partner API and is given a Transport Order number;
      3. the Transport Order reaches the TMS, which is told, as the Planner, to assign it to Van Dijk Transport and release it
         (a Carrier booking number comes back); the booking worker needs only the Service Bus emulators for that;
      4. Van Dijk Transport confirms the Carrier booking, with a Carrier booking reference of its own, and the Shipment is
         Booked (the Carrier answer path: Partner API, the Service Bus emulator, the TMS);
      5. Van Dijk Transport sends PU, DEP and POD tracking events, each accepted (202 Accepted, with a correlation ID), and the
         Shipment moves through Collected, In transit and Delivered (the tracking queue and the tracking worker);
      6. Van Dijk Transport sends a Proof of delivery, accepted (202 Accepted), and Brouwer Dranken reads it on its
         Transport Order, with its file, through the Partner API (Azurite, the queue and the Proof of delivery worker).
    Before them, two checks of #938. The ingress check reads every application's Ingress and fails one that is not restricted to the
    private ranges (the chart's internal-only middleware, or ingress-nginx's whitelist); this script reaches the ingress from inside
    those ranges, so what it can check is what the controller is told. -SkipIngressCheck leaves it out, for `edge.internal.controller=none`,
    where an internal ingress class is the restriction and cannot be read. The isolation check: a throwaway pod in a namespace it
    creates for the purpose connects to every application, SQL Server and the two Service Bus emulators of the sandbox and is refused. It first connects from inside the sandbox's namespace,
    so a probe that cannot run is not taken for a refusal. A cluster whose CNI does not enforce NetworkPolicy fails here, and
    LocalAuth:Allowed must not be set on it. It needs to create and delete a namespace and run pods in it; -SkipIsolationCheck
    leaves it out, and the check then is yours.

    No Azure and no Entra: the script signs the Personas' and the Planner's tokens itself, with the key the chart keeps in the
    Secret local-tokens, as the local stand-ins do, and takes the subscription keys from the Secret subscription-keys. It
    therefore needs `kubectl get secret` rights in the namespace. It prints only what the checks need: never a body, a
    token or a key. It books a new Transport Order on every run and leaves it behind; the sandbox is rebuilt from its seed file.
    Not covered here: that a Message trail of the run is found in the Aspire dashboard.
.PARAMETER BaseDomain
    The chart's edge.baseDomain. Left out, it is read from the Ingress of the Partner API.
.PARAMETER Namespace
    The namespace the chart is installed in. Defaults to norvia.
.PARAMETER Insecure
    The ingress is plain http (edge.tls.enabled=false).
.PARAMETER SkipCertificateCheck
    Accept a certificate that does not verify (a self-signed one on a test cluster).
.PARAMETER ProbeImage
    The image of the isolation check's probe pods: one with /agnhost, the Kubernetes e2e test image. Set it to a mirror when the
    cluster cannot pull from registry.k8s.io.
.PARAMETER Agents
    The chart is installed with agents.enabled: the MCP stand-in (agent-mcp) is one more application to reach and to isolate.
.PARAMETER SkipIngressCheck
    Leave out the check that every Ingress is restricted to the private ranges.
.PARAMETER SkipIsolationCheck
    Leave out the check that a pod in another namespace is refused.
.PARAMETER TimeoutSeconds
    How long each wait for the sandbox to catch up may take. Defaults to 180.
#>
[CmdletBinding()]
param(
    [string] $BaseDomain = $env:NORVIA_BASE_DOMAIN,
    [string] $Namespace = 'norvia',
    [switch] $Insecure,
    [switch] $SkipCertificateCheck,
    [string] $ProbeImage = 'registry.k8s.io/e2e-test-images/agnhost:2.47',
    [switch] $Agents,
    [switch] $SkipIngressCheck,
    [switch] $SkipIsolationCheck,
    [ValidateRange(5, 3600)][int] $TimeoutSeconds = 180
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Kubernetes.psm1') -Force

$shipperPersona = 'brouwer-dranken'
$carrierPersona = 'van-dijk-transport'
$carrierId = 'CAR-0001'
$interval = 5
$attempts = [math]::Max(1, [int][math]::Ceiling($TimeoutSeconds / $interval))
$run = [DateTime]::UtcNow.ToString('yyyyMMddHHmmss')
# The module's helpers take these on every call.
$PSDefaultParameterValues['Invoke-Call:SkipCertificateCheck'] = $SkipCertificateCheck.IsPresent
$PSDefaultParameterValues['Get-SecretValue:Namespace'] = $Namespace
$PSDefaultParameterValues['Wait-ForValue:Attempts'] = $attempts
$PSDefaultParameterValues['Wait-ForValue:IntervalSeconds'] = $interval

if (-not $BaseDomain) {
    try { $ingressHost = Invoke-Kubectl get ingress partner-api --namespace $Namespace --output 'jsonpath={.spec.rules[0].host}' }
    catch { Write-Fail "no -BaseDomain, and the Ingress partner-api in namespace $Namespace could not be read: $($_.Exception.Message)" }
    if (-not $ingressHost) { Write-Fail "no -BaseDomain, and the Ingress partner-api in namespace $Namespace has no host" }
    $BaseDomain = $ingressHost -replace '^partner-api\.', ''
}
$partnerApi = Get-IngressUrl -Application 'partner-api' -BaseDomain $BaseDomain -Insecure:$Insecure
$tms = Get-IngressUrl -Application 'tms-api' -BaseDomain $BaseDomain -Insecure:$Insecure
$applications = @(Get-SmokeApplications -Agents:$Agents)
Write-Host "Partner API: $partnerApi"
Write-Host "TMS API: $tms"

# The credentials, from the cluster's Secrets.
$signingKey = $null
$subscriptionKeys = @{}
Invoke-SmokeStep 'Read the signing key and the Personas'' subscription keys from the cluster' {
    $script:signingKey = Get-SecretValue -Secret 'local-tokens' -Key 'signing-key'
    foreach ($persona in $shipperPersona, $carrierPersona) { $script:subscriptionKeys[$persona] = Get-SecretValue -Secret 'subscription-keys' -Key "PartnerApi__SubscriptionKeys__$persona" }
}

# The namespace is closed to the others (#938). The probe's namespace is made for the run and removed after it, whatever the answer.
if ($SkipIsolationCheck) { Write-Host 'Skipped: a pod in another namespace is refused (-SkipIsolationCheck)' }
else {
    $probeNamespace = "norvia-probe-$run".ToLowerInvariant()
    Invoke-SmokeStep "A pod in another namespace ($probeNamespace) is refused by all $($applications.Count) applications, SQL Server and the Service Bus emulators" {
        Invoke-Kubectl create namespace $probeNamespace | Out-Null
        try { Assert-Isolated -Namespace $Namespace -ProbeNamespace $probeNamespace -Target (Get-IsolationTargets -Namespace $Namespace -Agents:$Agents) -Image $ProbeImage }
        finally { Invoke-Kubectl delete namespace $probeNamespace --wait=false | Out-Null }
    } | Out-Null
}

# What src/Norvia.AppHost/LocalTokens.cs and values.yaml (localTokens, personas) hold; the chart's template tests compare those two.
$issuer = 'https://norvia.local/tokens'
$partnerApiAudience = 'api://norvia-partner-api'
$tmsAudience = 'api://norvia-tms'
function Get-PersonaHeader([string] $Persona, [string[]] $Roles) {
    @{
        Authorization               = 'Bearer ' + (Get-LocalToken -SigningKey $signingKey -Issuer $issuer -Audience $partnerApiAudience -ClientId "norvia-local-persona-$Persona" -Roles $Roles)
        'Ocp-Apim-Subscription-Key' = $subscriptionKeys[$Persona]
    }
}
$shipperHeaders = Get-PersonaHeader $shipperPersona @('TransportOrders.Submit', 'Tracking.Read')
$carrierHeaders = Get-PersonaHeader $carrierPersona @('CarrierBookings.Respond', 'Tracking.Submit', 'ProofsOfDelivery.Submit')
# The Planner's actions in the TMS UI are the manage role, which the Simulator's identity holds locally.
$plannerHeaders = @{ Authorization = 'Bearer ' + (Get-LocalToken -SigningKey $signingKey -Issuer $issuer -Audience $tmsAudience -ClientId 'norvia-local-simulator' -Roles @('TransportOrders.Manage')) }

function Get-PlannerHeader([string] $IdempotencyKey) { $plannerHeaders + @{ 'Idempotency-Key' = $IdempotencyKey } }

function Wait-ForShipmentStatus([string] $ShipmentNumber, [string] $Status, [string] $After = '') {
    Wait-ForValue -Description "Shipment $ShipmentNumber to be $Status$After" -Describe { "its status was $($script:seen)" } -Condition {
        $answer = Invoke-Call GET "$tms/v1/shipments/$ShipmentNumber" $plannerHeaders
        $script:seen = $answer.Status -eq 200 ? $answer.Body['status'] : "unreadable, the TMS $(Format-Answer $answer)"
        if (Test-SameStatus $script:seen $Status) { $true }
    }
}

# 0. Every Ingress is restricted to the LAN or VPN (#938): what no address outside the allowed ranges is let through is the controller's, so
# the script reads what the controller is told, for every application the chart exposes.
if ($SkipIngressCheck) { Write-Host 'Skipped: every Ingress is restricted to the private ranges (-SkipIngressCheck)' }
else {
    Invoke-SmokeStep "All $($applications.Count) Ingresses admit only the private ranges" {
        foreach ($application in $applications) { Assert-IngressInternal -Namespace $Namespace -Application $application }
    } | Out-Null
}

# 1. The ingress routes to every application, none skipped. Waits for a cold ingress or certificate, so a missing application takes the timeout to fail.
Invoke-SmokeStep "All $($applications.Count) applications answer on their ingress hostnames: $($applications -join ', ')" {
    foreach ($application in $applications) {
        $url = Get-IngressUrl -Application $application -BaseDomain $BaseDomain -Insecure:$Insecure
        $null = Wait-ForValue -Description "$application to answer /health at $url" -Condition {
            try { $answer = Invoke-Call GET "$url/health"; if ($answer.Status -eq 200) { $answer } else { $script:lastError = Format-Answer $answer } } catch { $script:lastError = $_.Exception.Message }
        } -Describe { $script:lastError }
    }
} | Out-Null

# 2. Brouwer Dranken books.
$bookingReference = "SMOKE-K8S-$run"
$orderNumber = Invoke-SmokeStep "Brouwer Dranken books Rotterdam to Dusseldorf (booking reference $bookingReference)" {
    $booking = Get-Content -Raw (Join-Path $PSScriptRoot '../contracts/partner-api/samples/bookings/full.booking.json') | ConvertFrom-Json -AsHashtable
    $booking.bookingRef = $bookingReference
    $answer = Invoke-Call POST "$partnerApi/partner/v1/transport-orders" $shipperHeaders $booking
    Assert-Accepted $answer -What 'the Partner API'
    $number = $answer.Body['norviaOrderNo']
    if ($number -notmatch '^TO-[0-9]+$') { throw "the Partner API accepted the booking (status $($answer.Status)) but gave no Transport Order number" }
    $number
}
Write-Host "Transport Order: $orderNumber"

# 3. The Planner assigns and releases.
$release = Invoke-SmokeStep "The Planner assigns Transport Order $orderNumber to Van Dijk Transport and releases its Shipment" {
    $assign = Wait-ForValue -Description "the TMS to take the assignment of $orderNumber" -Describe { $script:lastError } -Condition {
        $answer = Invoke-Call POST "$tms/v1/transport-orders/$orderNumber/assign" (Get-PlannerHeader "smoke-assign-$run") @{ assignedTo = 'Carrier'; carrierId = $carrierId }
        if ($answer.Status -eq 200) { return $answer }
        $script:lastError = "the TMS $(Format-Answer $answer)"
        # Not found, not yet accepted and a server error can pass as the sandbox catches up; the other refusals never will.
        $waitable = $answer.Status -in 404, 502, 503, 504 -or ($answer.Status -eq 409 -and $answer.Body -is [hashtable] -and $answer.Body['code'] -eq 'TransportOrderNotAccepted')
        if (-not $waitable) { throw $script:lastError }
    }
    $shipmentNumber = $assign.Body['shipmentNumber']
    $released = Invoke-Call POST "$tms/v1/shipments/$shipmentNumber/release" (Get-PlannerHeader "smoke-release-$run")
    if ($released.Status -ne 200) { throw "releasing Shipment $shipmentNumber, the TMS $(Format-Answer $released)" }
    [pscustomobject]@{ ShipmentNumber = $shipmentNumber; CarrierBookingNumber = $released.Body['carrierBookingNumber'] }
}
$shipmentNumber = $release.ShipmentNumber
$carrierBookingNumber = $release.CarrierBookingNumber
if (-not $carrierBookingNumber) { Write-Fail "Shipment ${shipmentNumber} was released but has no Carrier booking number" }
Write-Host "Shipment: $shipmentNumber, Carrier booking: $carrierBookingNumber"

# 4. The Carrier confirms.
$carrierReference = "SMOKE-VDT-$run"
Invoke-SmokeStep "Van Dijk Transport confirms Carrier booking $carrierBookingNumber and Shipment $shipmentNumber is Booked" {
    $answer = Invoke-Call POST "$partnerApi/partner/v1/carrier-answers" $carrierHeaders @{
        carrierBookingNumber = $carrierBookingNumber; decision = 'CONFIRMED'; bookingRef = $carrierReference
        plannedCollection    = [DateTimeOffset]::UtcNow.AddDays(1).ToString('yyyy-MM-ddTHH:mm:sszzz')
    }
    Assert-Accepted $answer -What 'the Partner API'
    Wait-ForShipmentStatus $shipmentNumber 'Booked'
} | Out-Null

# 5. Tracking.
$eventTime = [DateTime]::UtcNow.AddHours(-2)
$index = 0
foreach ($step in @(@{ Code = 'PU'; Status = 'Collected' }, @{ Code = 'DEP'; Status = 'In transit' }, @{ Code = 'POD'; Status = 'Delivered' })) {
    $index++
    Invoke-SmokeStep "Van Dijk Transport sends $($step.Code); the Shipment is $($step.Status)" {
        $answer = Invoke-Call POST "$partnerApi/partner/v1/carrier-tracking" $carrierHeaders @{
            bookingRef = $carrierReference; eventId = "SMOKE-$run-$index"; statusCode = $step.Code
            eventTime  = $eventTime.AddMinutes(10 * $index).ToString('yyyy-MM-ddTHH:mm:ss')
            place      = 'Rotterdam'; postcode = '3011 AA'; country = 'NLD'
        }
        Assert-Accepted $answer -What 'the Partner API' -Status 202 -RequireCorrelationId
        Wait-ForShipmentStatus $shipmentNumber $step.Status " (correlation ID $($answer.CorrelationId))"
    } | Out-Null
}

# 6. The Proof of delivery, and the tracking as the Shipper reads it.
Invoke-SmokeStep "Van Dijk Transport sends a Proof of delivery; Brouwer Dranken reads it and the tracking on $orderNumber" {
    $proof = Get-Content -Raw (Join-Path $PSScriptRoot '../contracts/partner-api/samples/carrier-proofs-of-delivery/clean-winter.carrier-proof-of-delivery.json') | ConvertFrom-Json -AsHashtable
    $proof.bookingRef = $carrierReference
    $proof.carrierBookingNumber = $carrierBookingNumber
    $proof.documentId = "SMOKE-POD-$run"
    $proof.deliveryTime = [DateTime]::UtcNow.AddHours(-2).ToString('yyyy-MM-ddTHH:mm:ss')
    $answer = Invoke-Call POST "$partnerApi/partner/v1/carrier-proofs-of-delivery" $carrierHeaders $proof
    Assert-Accepted $answer -What 'the Partner API' -Status 202 -RequireCorrelationId
    Wait-ForValue -Description "$orderNumber to read Delivered, with its tracking events and a Proof of delivery file, for the Shipper (correlation ID $($answer.CorrelationId))" -Describe { $script:seen } -Condition {
        $read = Invoke-Call GET "$partnerApi/partner/v1/transport-orders/$orderNumber" $shipperHeaders
        $script:seen = "the Partner API $(Format-Answer $read)"
        if ($read.Status -ne 200) { return }
        $pod = $read.Body['proofOfDelivery']
        if ($pod -and @($pod['files']).Count -gt 0 -and @($read.Body['events']).Count -ge 3 -and (Test-SameStatus $read.Body['status'] 'Delivered')) { $true }
    }
} | Out-Null

Write-Host ''
Write-Pass "The sandbox on $BaseDomain booked, tracked and delivered Transport Order $orderNumber (Shipment $shipmentNumber)"
