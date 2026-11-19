#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy smoke run of the sandbox on Kubernetes (#932, ADR 0053, not in CI): a booking, tracking and a Proof of delivery.
.DESCRIPTION
    Run it after every `helm upgrade --install` of deploy/helm/norvia, from anywhere that can reach the cluster's ingress and
    has kubectl pointed at it:

      infra/smoke-test-kubernetes.ps1 -BaseDomain norvia.example.org

    It drives one Shipment from booking to delivery through the ingress hostnames, as the Personas would, and fails loudly with
    the step that broke:
      1. the Partner API and the TMS API answer on their ingress hostnames (TLS from cert-manager, routed by the controller);
      2. Brouwer Dranken books Rotterdam to Dusseldorf through the Partner API and is given a Transport Order number;
      3. the Transport Order reaches the TMS, which is told, as the Planner, to assign it to Van Dijk Transport and release it
         (a Carrier booking number comes back); the booking worker needs only the Service Bus emulators for that;
      4. Van Dijk Transport confirms the Carrier booking, with a Carrier booking reference of its own, and the Shipment is
         Booked (the Carrier answer path: Partner API, the Service Bus emulator, the TMS);
      5. Van Dijk Transport sends PU, DEP and POD tracking events, each accepted (202 Accepted, with a correlation ID), and the
         Shipment moves through Collected, In transit and Delivered (the tracking queue and the tracking worker);
      6. Van Dijk Transport sends a Proof of delivery, accepted (202 Accepted), and Brouwer Dranken reads it on its
         Transport Order, with its file, through the Partner API (Azurite, the queue and the Proof of delivery worker).
    A step that needs an application the chart does not deploy yet (the TMS API, the Partner portal) fails with that
    application's own error until it is deployed; that is the check.

    No Azure and no Entra: the script signs the Personas' and the Planner's tokens itself, with the key the chart keeps in the
    Secret local-tokens, as the local stand-ins do, and takes the subscription keys from the Secret subscription-keys. It
    therefore needs `kubectl get secret` rights in the namespace. It prints only what the checks need: never a body, a
    token or a key. It books a new Transport Order on every run and leaves it behind; the sandbox is rebuilt from its seed file.
    Not covered here: that a Message trail of the run is found in the Aspire dashboard (the Integration console is not in the
    chart yet), which comes with it.
.PARAMETER BaseDomain
    The chart's edge.baseDomain. Left out, it is read from the Ingress of the Partner API.
.PARAMETER Namespace
    The namespace the chart is installed in. Defaults to norvia.
.PARAMETER Insecure
    The ingress is plain http (edge.tls.enabled=false).
.PARAMETER SkipCertificateCheck
    Accept a certificate that does not verify (a self-signed one on a test cluster).
.PARAMETER TimeoutSeconds
    How long each wait for the sandbox to catch up may take. Defaults to 180.
#>
[CmdletBinding()]
param(
    [string] $BaseDomain = $env:NORVIA_BASE_DOMAIN,
    [string] $Namespace = 'norvia',
    [switch] $Insecure,
    [switch] $SkipCertificateCheck,
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
$acceptAnyCertificate = $SkipCertificateCheck.IsPresent
$run = [DateTime]::UtcNow.ToString('yyyyMMddHHmmss')

function Get-SecretValue([string] $Secret, [string] $Key) {
    $encoded = & kubectl get secret $Secret --namespace $Namespace --output "jsonpath={.data['$Key']}" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "kubectl could not read the Secret $Secret in namespace ${Namespace}: $encoded" }
    if (-not $encoded) { throw "the Secret $Secret has no key $Key" }
    [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(($encoded -join '')))
}

if (-not $BaseDomain) {
    $ingressHost = & kubectl get ingress partner-api --namespace $Namespace --output 'jsonpath={.spec.rules[0].host}' 2>&1
    if ($LASTEXITCODE -ne 0 -or -not $ingressHost) { Write-Fail "no -BaseDomain, and the Ingress partner-api in namespace $Namespace could not be read: $ingressHost" }
    $BaseDomain = ($ingressHost -join '') -replace '^partner-api\.', ''
}
$partnerApi = Get-IngressUrl -Application 'partner-api' -BaseDomain $BaseDomain -Insecure:$Insecure
$tms = Get-IngressUrl -Application 'tms-api' -BaseDomain $BaseDomain -Insecure:$Insecure
Write-Host "Partner API: $partnerApi"
Write-Host "TMS API: $tms"

function Invoke-Call {
    # One HTTP call. Never throws for a status; returns { Status, Body (parsed, or $null), CorrelationId }.
    param([string] $Method, [string] $Uri, [hashtable] $Headers = @{}, $Body = $null)
    $arguments = @{ Method = $Method; Uri = $Uri; Headers = $Headers; SkipHttpErrorCheck = $true; TimeoutSec = 30 }
    if ($script:acceptAnyCertificate) { $arguments.SkipCertificateCheck = $true }
    if ($null -ne $Body) { $arguments.Body = ($Body | ConvertTo-Json -Depth 20 -Compress); $arguments.ContentType = 'application/json' }
    $response = Invoke-WebRequest @arguments
    $parsed = try { $response.Content | ConvertFrom-Json -Depth 20 -AsHashtable } catch { $null }
    [pscustomobject]@{ Status = [int] $response.StatusCode; Body = $parsed; CorrelationId = @($response.Headers['X-Correlation-ID'])[0] }
}

function Format-Answer($Answer) {
    # What a failing call says, without a body: the status, and the problem's title or code when it has one.
    $detail = if ($Answer.Body -is [hashtable]) { ($Answer.Body['title'], $Answer.Body['code'], $Answer.Body['text'], $Answer.Body['message'] | Where-Object { $_ }) -join ', ' }
    "answered $($Answer.Status)$($detail ? " ($detail)" : '')"
}

# The credentials, from the cluster's Secrets.
$signingKey = $null
$subscriptionKeys = @{}
Invoke-SmokeStep 'Read the signing key and the Personas'' subscription keys from the cluster' {
    $script:signingKey = Get-SecretValue 'local-tokens' 'signing-key'
    foreach ($persona in $shipperPersona, $carrierPersona) { $script:subscriptionKeys[$persona] = Get-SecretValue 'subscription-keys' "PartnerApi__SubscriptionKeys__$persona" }
}

$issuer = 'https://norvia.local/tokens'
function Get-PersonaHeader([string] $Persona, [string[]] $Roles) {
    @{
        Authorization               = 'Bearer ' + (Get-LocalToken -SigningKey $signingKey -Issuer $issuer -Audience 'api://norvia-partner-api' -ClientId "norvia-local-persona-$Persona" -Roles $Roles)
        'Ocp-Apim-Subscription-Key' = $subscriptionKeys[$Persona]
    }
}
$shipperHeaders = Get-PersonaHeader $shipperPersona @('TransportOrders.Submit', 'Tracking.Read')
$carrierHeaders = Get-PersonaHeader $carrierPersona @('CarrierBookings.Respond', 'Tracking.Submit', 'ProofsOfDelivery.Submit')
# The Planner's actions in the TMS UI are the manage role, which the Simulator's identity holds locally.
$plannerHeaders = @{ Authorization = 'Bearer ' + (Get-LocalToken -SigningKey $signingKey -Issuer $issuer -Audience 'api://norvia-tms' -ClientId 'norvia-local-simulator' -Roles @('TransportOrders.Manage')) }

function Get-Shipment([string] $ShipmentNumber) {
    $answer = Invoke-Call GET "$tms/v1/shipments/$ShipmentNumber" $plannerHeaders
    if ($answer.Status -eq 200) { $answer.Body }
}

# 1. The ingress routes to both applications.
Invoke-SmokeStep 'The Partner API and the TMS API answer on their ingress hostnames' {
    foreach ($app in @(@{ Name = 'Partner API'; Url = $partnerApi }, @{ Name = 'TMS API'; Url = $tms })) {
        $health = Wait-ForValue -Description "the $($app.Name) to answer /health at $($app.Url)" -Attempts $attempts -IntervalSeconds $interval -Condition {
            try { $answer = Invoke-Call GET "$($app.Url)/health"; if ($answer.Status -eq 200) { $answer } } catch { $script:lastError = $_.Exception.Message }
        } -Describe { $script:lastError }
        $null = $health
    }
} | Out-Null

# 2. Brouwer Dranken books.
$bookingReference = "SMOKE-K8S-$run"
$orderNumber = Invoke-SmokeStep "Brouwer Dranken books Rotterdam to Dusseldorf (booking reference $bookingReference)" {
    $booking = Get-Content -Raw (Join-Path $PSScriptRoot '../contracts/partner-api/samples/bookings/full.booking.json') | ConvertFrom-Json -AsHashtable
    $booking.bookingRef = $bookingReference
    $answer = Invoke-Call POST "$partnerApi/partner/v1/transport-orders" $shipperHeaders $booking
    if ($answer.Status -lt 200 -or $answer.Status -ge 300) { throw "the Partner API $(Format-Answer $answer)" }
    $number = $answer.Body['norviaOrderNo']
    if ($number -notmatch '^TO-[0-9]+$') { throw "the Partner API accepted the booking (status $($answer.Status)) but gave no Transport Order number" }
    $number
}
Write-Host "Transport Order: $orderNumber"

# 3. The Planner assigns and releases.
$release = Invoke-SmokeStep "The Planner assigns Transport Order $orderNumber to Van Dijk Transport and releases its Shipment" {
    $assign = Wait-ForValue -Description "the TMS to take the assignment of $orderNumber" -Attempts $attempts -IntervalSeconds $interval -Describe { $script:lastError } -Condition {
        $headers = $plannerHeaders + @{ 'Idempotency-Key' = "smoke-assign-$run" }
        $answer = Invoke-Call POST "$tms/v1/transport-orders/$orderNumber/assign" $headers @{ assignedTo = 'Carrier'; carrierId = $carrierId }
        if ($answer.Status -eq 200) { return $answer }
        $script:lastError = "the TMS $(Format-Answer $answer)"
        if ($answer.Status -notin 404, 409, 502, 503, 504) { throw $script:lastError }
    }
    $shipmentNumber = $assign.Body['shipmentNumber']
    $headers = $plannerHeaders + @{ 'Idempotency-Key' = "smoke-release-$run" }
    $released = Invoke-Call POST "$tms/v1/shipments/$shipmentNumber/release" $headers
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
    if ($answer.Status -lt 200 -or $answer.Status -ge 300) { throw "the Partner API $(Format-Answer $answer)" }
    Wait-ForValue -Description "Shipment $shipmentNumber to be Booked" -Attempts $attempts -IntervalSeconds $interval -Describe { "its status was $($script:seen)" } -Condition {
        $script:seen = (Get-Shipment $shipmentNumber).status
        if (Test-Status $script:seen 'Booked') { $true }
    }
} | Out-Null

# 5. Tracking.
$eventTime = [DateTime]::UtcNow.AddHours(-2).ToString('yyyy-MM-ddTHH:mm:ss')
$index = 0
foreach ($step in @(@{ Code = 'PU'; Status = 'Collected' }, @{ Code = 'DEP'; Status = 'In transit' }, @{ Code = 'POD'; Status = 'Delivered' })) {
    $index++
    Invoke-SmokeStep "Van Dijk Transport sends $($step.Code); the Shipment is $($step.Status)" {
        $answer = Invoke-Call POST "$partnerApi/partner/v1/carrier-tracking" $carrierHeaders @{
            bookingRef = $carrierReference; eventId = "SMOKE-$run-$index"; statusCode = $step.Code
            eventTime  = ([DateTime]::Parse($eventTime).AddMinutes(10 * $index)).ToString('yyyy-MM-ddTHH:mm:ss')
            place      = 'Rotterdam'; postcode = '3011 AA'; country = 'NLD'
        }
        if ($answer.Status -ne 202) { throw "the Partner API $(Format-Answer $answer), not 202 Accepted" }
        if (-not $answer.CorrelationId) { throw 'the accepted call carries no X-Correlation-ID' }
        Wait-ForValue -Description "Shipment $shipmentNumber to be $($step.Status) (correlation ID $($answer.CorrelationId))" -Attempts $attempts -IntervalSeconds $interval `
            -Describe { "its status was $($script:seen)" } -Condition {
            $script:seen = (Get-Shipment $shipmentNumber).status
            if (Test-Status $script:seen $step.Status) { $true }
        }
    } | Out-Null
}

# 6. The Proof of delivery.
Invoke-SmokeStep "Van Dijk Transport sends a Proof of delivery; Brouwer Dranken reads it on $orderNumber" {
    $proof = Get-Content -Raw (Join-Path $PSScriptRoot '../contracts/partner-api/samples/carrier-proofs-of-delivery/clean-winter.carrier-proof-of-delivery.json') | ConvertFrom-Json -AsHashtable
    $proof.bookingRef = $carrierReference
    $proof.carrierBookingNumber = $carrierBookingNumber
    $proof.documentId = "SMOKE-POD-$run"
    $proof.deliveryTime = [DateTime]::UtcNow.AddHours(-2).ToString('yyyy-MM-ddTHH:mm:ss')
    $answer = Invoke-Call POST "$partnerApi/partner/v1/carrier-proofs-of-delivery" $carrierHeaders $proof
    if ($answer.Status -ne 202) { throw "the Partner API $(Format-Answer $answer), not 202 Accepted" }
    if (-not $answer.CorrelationId) { throw 'the accepted call carries no X-Correlation-ID' }
    Wait-ForValue -Description "the Proof of delivery of $orderNumber, with a file, in the Shipper's tracking (correlation ID $($answer.CorrelationId))" `
        -Attempts $attempts -IntervalSeconds $interval -Describe { $script:seen } -Condition {
        $read = Invoke-Call GET "$partnerApi/partner/v1/transport-orders/$orderNumber" $shipperHeaders
        $script:seen = "the Partner API $(Format-Answer $read)"
        $pod = $read.Status -eq 200 ? $read.Body['proofOfDelivery'] : $null
        if ($pod -and @($pod['files']).Count -gt 0 -and (Test-Status $read.Body['status'] 'Delivered')) { $true }
    }
} | Out-Null

Write-Host ''
Write-Pass "The sandbox on $BaseDomain booked, tracked and delivered Transport Order $orderNumber (Shipment $shipmentNumber)"
