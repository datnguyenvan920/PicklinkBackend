<#
.SYNOPSIS
    Simulates a SePay webhook call (correct HMAC-SHA256 signing) against a running
    PicklinkBackend instance, so you can verify the SePay integration end-to-end
    without waiting for a real bank transfer.

.DESCRIPTION
    Reproduces exactly what SePayWebhookSecurity.Verify expects:
      X-SePay-Timestamp: <unix seconds>
      X-SePay-Signature: sha256=<hex HMAC-SHA256 of "{timestamp}.{rawBody}">

.PARAMETER Url
    Full webhook URL, e.g. https://your-domain/api/payments/webhooks/sepay
    or http://localhost:5000/api/payments/webhooks/sepay for local testing.

.PARAMETER Secret
    The SePay webhook secret configured in appsettings / SePay:WebhookSecret env var.
    Pass it via -Secret, or better: set $env:SEPAY_TEST_SECRET beforehand and omit
    this param, so the secret never ends up in shell history.

.PARAMETER AccountNumber
    The receiving bank account number exactly as stored on the Payment row
    (Payment.BankAccountNumber) you're trying to confirm.

.PARAMETER TransferContent
    The exact transfer content/code of the pending payment you want to confirm
    (matches Payment.TransferContent, or a PLG-xxxxxxxxxxxxxxxx ticket code).

.PARAMETER Amount
    Transfer amount in VND. Must exactly match the payment's expected amount or
    the backend will treat it as a mismatch and flag it for manual review instead
    of auto-confirming.

.EXAMPLE
    $env:SEPAY_TEST_SECRET = "your-real-secret"
    ./test-sepay-webhook.ps1 -Url "https://api.yourdomain.com/api/payments/webhooks/sepay" `
        -AccountNumber "0123456789" -TransferContent "PLG-ABCD1234EFGH5678" -Amount 50000

.NOTES
    WARNING: pointing this at a real environment with a TransferContent that
    matches a real pending payment WILL mark that payment as Paid, exactly like
    a real SePay callback. Only use test/staging payment codes, or amounts you
    expect to reconcile.
#>
param(
    [Parameter(Mandatory = $true)][string]$Url,
    [string]$Secret = $env:SEPAY_TEST_SECRET,
    [Parameter(Mandatory = $true)][string]$AccountNumber,
    [Parameter(Mandatory = $true)][string]$TransferContent,
    [Parameter(Mandatory = $true)][decimal]$Amount,
    [long]$TransactionId = (Get-Date -UFormat %s -Millisecond 0)
)

if ([string]::IsNullOrWhiteSpace($Secret)) {
    Write-Error "No secret provided. Pass -Secret or set `$env:SEPAY_TEST_SECRET first."
    exit 1
}

$body = [ordered]@{
    id              = $TransactionId
    gateway         = "TestBank"
    transactionDate = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    accountNumber   = $AccountNumber
    subAccount      = ""
    code            = $TransferContent
    content         = $TransferContent
    transferType    = "in"
    description     = "$TransferContent test webhook"
    transferAmount  = $Amount
    accumulated     = $Amount
    referenceCode   = "TEST$TransactionId"
} | ConvertTo-Json -Compress

$timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$signingInput = "$timestamp.$body"

$hmac = New-Object System.Security.Cryptography.HMACSHA256
$hmac.Key = [System.Text.Encoding]::UTF8.GetBytes($Secret)
$hashBytes = $hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($signingInput))
$hex = ($hashBytes | ForEach-Object { $_.ToString("x2") }) -join ""
$signature = "sha256=$hex"

Write-Host "POST $Url"
Write-Host "Body: $body"
Write-Host "X-SePay-Timestamp: $timestamp"
Write-Host "X-SePay-Signature: $signature"
Write-Host "---"

try {
    $response = Invoke-WebRequest -Uri $Url -Method Post -Body $body `
        -ContentType "application/json" `
        -Headers @{ "X-SePay-Timestamp" = "$timestamp"; "X-SePay-Signature" = $signature } `
        -UseBasicParsing
    Write-Host "Status: $($response.StatusCode)"
    Write-Host $response.Content
}
catch {
    $resp = $_.Exception.Response
    if ($resp) {
        $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
        Write-Host "Status: $([int]$resp.StatusCode)"
        Write-Host $reader.ReadToEnd()
    } else {
        Write-Error $_.Exception.Message
    }
}
