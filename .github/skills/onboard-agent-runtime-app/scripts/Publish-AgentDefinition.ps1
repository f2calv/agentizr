#!/usr/bin/env pwsh
#Requires -Version 7.4

<#
.SYNOPSIS
Publishes and activates Agent Runtime definitions for one tenant.

.DESCRIPTION
Installs a short-lived in-memory certificate on the tenant's administration application, acquires
a token, publishes each definition file as an inactive snapshot, activates them (retrying in passes
so delegates activate before the agents that call them) and reads back the active versions. The
certificate is removed in a finally block. No credential is printed or written to disk.

Each definition file holds an object with agentName, definitionVersion, schemaVersion and
definition properties.

.PARAMETER EntraTenantId
Microsoft Entra tenant identifier.

.PARAMETER Tenant
Agent Runtime tenant identifier; the administration application is agentizr-<Tenant>-admin.

.PARAMETER DefinitionPath
One or more definition JSON files.

.PARAMETER ChangeReason
Audit reason recorded with publication and activation. Never include secrets or message content.

.PARAMETER ApiBaseAddress
Runtime base address. When omitted, a port-forward to the in-cluster runtime is opened.

.EXAMPLE
./Publish-AgentDefinition.ps1 -EntraTenantId <tenant-guid> -Tenant <tenant> -DefinitionPath ./CommsAgent.json -ChangeReason 'Initial tutor definition.' -WhatIf
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)]
    [guid] $EntraTenantId,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9-]+$')]
    [string] $Tenant,

    [Parameter(Mandatory)]
    [string[]] $DefinitionPath,

    [Parameter(Mandatory)]
    [string] $ChangeReason,

    [uri] $ApiBaseAddress,

    [string] $Namespace = 'agentizr',

    [int] $LocalPort = 5094
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
$PSNativeCommandUseErrorActionPreference = $false

function Invoke-AzJson {
    param([Parameter(Mandatory)][string[]] $Arguments)

    $output = & az @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI failed: az $($Arguments[0..1] -join ' ')"
    }
    return ($output -join "`n") | ConvertFrom-Json
}

function Set-ApplicationKeyCredential {
    param([Parameter(Mandatory)][string] $ObjectId, [Parameter(Mandatory)][AllowEmptyCollection()][object[]] $KeyCredential,
        [Parameter(Mandatory)][hashtable] $Headers)

    $null = Invoke-RestMethod -Method PATCH -Uri "https://graph.microsoft.com/v1.0/applications/$ObjectId" `
        -Headers $Headers -ContentType 'application/json' `
        -Body (@{ keyCredentials = $KeyCredential } | ConvertTo-Json -Depth 5 -Compress)
}

function ConvertTo-Base64Url([byte[]] $Bytes) {
    [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Get-AdminToken {
    param([guid] $ClientId, [string] $Scope, [Security.Cryptography.X509Certificates.X509Certificate2] $Certificate,
        [Security.Cryptography.RSA] $PrivateKey)

    $tokenUri = "https://login.microsoftonline.com/$EntraTenantId/oauth2/v2.0/token"
    for ($attempt = 1; ; $attempt++) {
        $now = [DateTimeOffset]::UtcNow
        $header = @{ alg = 'RS256'; typ = 'JWT'; x5t = ConvertTo-Base64Url $Certificate.GetCertHash() } | ConvertTo-Json -Compress
        $payload = @{
            aud = $tokenUri; iss = "$ClientId"; sub = "$ClientId"; jti = [guid]::NewGuid().ToString()
            nbf = $now.AddMinutes(-1).ToUnixTimeSeconds(); exp = $now.AddMinutes(10).ToUnixTimeSeconds()
        } | ConvertTo-Json -Compress
        $unsigned = "$(ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($header))).$(ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($payload)))"
        $signature = $PrivateKey.SignData([Text.Encoding]::UTF8.GetBytes($unsigned),
            [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        try {
            return (Invoke-RestMethod -Method POST -Uri $tokenUri -ContentType 'application/x-www-form-urlencoded' -Body @{
                    client_id             = "$ClientId"
                    client_assertion      = "$unsigned.$(ConvertTo-Base64Url $signature)"
                    client_assertion_type = 'urn:ietf:params:oauth:client-assertion-type:jwt-bearer'
                    grant_type            = 'client_credentials'
                    scope                 = $Scope
                }).access_token
        }
        catch {
            #A new key credential takes a while to propagate through Entra.
            if ($attempt -ge 20) { throw 'Microsoft Entra did not accept the ephemeral admin certificate.' }
            Start-Sleep -Seconds 15
        }
    }
}

$definitions = @($DefinitionPath | ForEach-Object {
        $definition = Get-Content -LiteralPath $_ -Raw | ConvertFrom-Json -Depth 100
        foreach ($property in 'agentName', 'definitionVersion', 'schemaVersion', 'definition') {
            if ($null -eq $definition.PSObject.Properties[$property]) {
                throw "Definition file '$_' has no '$property' property."
            }
        }
        $definition
    })

$apiApplications = @(Invoke-AzJson @('ad', 'app', 'list', '--filter', "displayName eq 'agentizr-api'", '--output', 'json'))
$adminApplications = @(Invoke-AzJson @('ad', 'app', 'list', '--filter', "displayName eq 'agentizr-$Tenant-admin'", '--output', 'json'))
if ($apiApplications.Count -ne 1 -or $adminApplications.Count -ne 1) {
    throw "Expected exactly one agentizr-api and one agentizr-$Tenant-admin application."
}
$apiApplication = $apiApplications[0]
$adminApplication = $adminApplications[0]
if (@($adminApplication.keyCredentials).Count -ne 0) {
    throw "agentizr-$Tenant-admin already has a credential; refusing to replace it."
}

Write-Host "Definitions to publish for tenant '$Tenant':"
$definitions | ForEach-Object { Write-Host "  $($_.agentName) $($_.definitionVersion)" }
if (!$PSCmdlet.ShouldProcess("tenant '$Tenant'", "Publish and activate $($definitions.Count) definition(s)")) {
    return
}

$graphHeaders = $null
$headers = $null
$certificate = $null
$privateKey = $null
$credentialInstalled = $false
$portForward = $null
try {
    $graphToken = & az account get-access-token --resource-type ms-graph --query accessToken --output tsv
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($graphToken)) {
        throw 'Unable to acquire a Microsoft Graph token from Azure CLI.'
    }
    $graphHeaders = @{ Authorization = "Bearer $graphToken" }
    $graphToken = $null

    $privateKey = [Security.Cryptography.RSA]::Create(3072)
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new("CN=agentizr-$Tenant-admin-ephemeral", $privateKey,
        [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $notBefore = [DateTimeOffset]::UtcNow.AddMinutes(-5)
    $certificate = $request.CreateSelfSigned($notBefore, $notBefore.AddHours(1))
    Set-ApplicationKeyCredential -ObjectId $adminApplication.id -Headers $graphHeaders -KeyCredential @(@{
            customKeyIdentifier = [Convert]::ToBase64String($certificate.GetCertHash())
            displayName         = 'agent-runtime-definition-publication'
            startDateTime       = $certificate.NotBefore.ToUniversalTime().ToString('o')
            endDateTime         = $certificate.NotAfter.ToUniversalTime().ToString('o')
            key                 = [Convert]::ToBase64String($certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
            type                = 'AsymmetricX509Cert'
            usage               = 'Verify'
        })
    $credentialInstalled = $true

    $token = Get-AdminToken -ClientId $adminApplication.appId -Scope "api://$($apiApplication.appId)/.default" `
        -Certificate $certificate -PrivateKey $privateKey
    $headers = @{ Authorization = "Bearer $token" }
    $token = $null

    if ($null -eq $ApiBaseAddress) {
        $portForward = Start-Process kubectl -PassThru -WindowStyle Hidden `
            -ArgumentList @('-n', $Namespace, 'port-forward', 'deployment/agentizr', "${LocalPort}:8080")
        $ApiBaseAddress = "http://127.0.0.1:$LocalPort"
        for ($attempt = 1; ; $attempt++) {
            try { $null = Invoke-WebRequest "$($ApiBaseAddress.AbsoluteUri.TrimEnd('/'))/healthz" -TimeoutSec 5; break }
            catch {
                if ($attempt -ge 30) { throw 'The runtime port-forward never became reachable.' }
                Start-Sleep -Seconds 2
            }
        }
    }
    $agentsUri = "$($ApiBaseAddress.AbsoluteUri.TrimEnd('/'))/api/v1/agents"

    foreach ($definition in $definitions) {
        $body = @{
            definitionVersion = $definition.definitionVersion
            schemaVersion     = $definition.schemaVersion
            definition        = $definition.definition
            changeReason      = $ChangeReason
        } | ConvertTo-Json -Depth 100 -Compress
        $response = Invoke-WebRequest -Method POST -Uri "$agentsUri/$([Uri]::EscapeDataString($definition.agentName))/definitions" `
            -Headers $headers -ContentType 'application/json' -Body $body -SkipHttpErrorCheck
        if ($response.StatusCode -notin 201, 409) {
            throw "Publication of '$($definition.agentName)' failed with HTTP $($response.StatusCode): $($response.Content)"
        }
        Write-Host "Published $($definition.agentName) $($definition.definitionVersion) (HTTP $($response.StatusCode))."
    }

    $pending = [Collections.Generic.List[object]]::new([object[]] $definitions)
    while ($pending.Count -gt 0) {
        $activated = 0
        foreach ($definition in @($pending)) {
            $uri = "$agentsUri/$([Uri]::EscapeDataString($definition.agentName))/definitions/$([Uri]::EscapeDataString($definition.definitionVersion))/activate"
            $response = Invoke-WebRequest -Method PUT -Uri $uri -Headers $headers -ContentType 'application/json' `
                -Body (@{ changeReason = $ChangeReason } | ConvertTo-Json -Compress) -SkipHttpErrorCheck
            if ($response.StatusCode -eq 204) {
                $null = $pending.Remove($definition)
                $activated++
                Write-Host "Activated $($definition.agentName) $($definition.definitionVersion)."
            }
            elseif ($response.StatusCode -ne 400) {
                throw "Activation of '$($definition.agentName)' failed with HTTP $($response.StatusCode): $($response.Content)"
            }
        }
        if ($activated -eq 0) {
            throw "No definition could be activated; unresolved: $($pending.agentName -join ', ')."
        }
    }

    foreach ($definition in $definitions) {
        $response = Invoke-WebRequest -Method GET -Headers $headers -SkipHttpErrorCheck `
            -Uri "$agentsUri/$([Uri]::EscapeDataString($definition.agentName))/definitions/active"
        if ($response.StatusCode -ne 200 -or ($response.Content | ConvertFrom-Json).definitionVersion -ne $definition.definitionVersion) {
            throw "Active definition verification failed for '$($definition.agentName)'."
        }
    }
    Write-Host "Published and activated $($definitions.Count) definition(s) for tenant '$Tenant'."
}
finally {
    if ($null -ne $portForward -and -not $portForward.HasExited) { $portForward.Kill() }
    if ($credentialInstalled) {
        Set-ApplicationKeyCredential -ObjectId $adminApplication.id -Headers $graphHeaders -KeyCredential @()
    }
    $headers = $null
    $graphHeaders = $null
    if ($null -ne $certificate) { $certificate.Dispose() }
    if ($null -ne $privateKey) { $privateKey.Dispose() }
}
