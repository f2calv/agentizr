#!/usr/bin/env pwsh
#Requires -Version 7.4

<#
.SYNOPSIS
Creates the execution and administration identities for one Agent Runtime tenant.

.DESCRIPTION
Creates agentizr-<Tenant>-exec with only the agent.execute app role and a certificate credential,
and agentizr-<Tenant>-admin with the definition read, publish and activate app roles and no
standing credential. The execution certificate is generated in memory and its combined PEM is
written straight to Key Vault with the host's settings under <ExecSecretPrefix>--*. Both client IDs
are mapped to the tenant as TenantCallers--<Tenant>--0 (execution) and --1 (administration).

Re-running is safe: existing applications, service principals, role assignments and secrets are
reused. A certificate is only issued when the execution application has none. Secret values travel
in request bodies only; nothing is printed or written to disk.

.PARAMETER EntraTenantId
Microsoft Entra tenant identifier.

.PARAMETER Tenant
Agent Runtime tenant identifier.

.PARAMETER KeyVaultName
Key Vault that holds the runtime caller mappings and the host's execution identity.

.PARAMETER ExecSecretPrefix
Secret name prefix the host maps onto AgentRuntimeAzureAuthConfig, for example AgentRuntime--MyApp--Exec.

.EXAMPLE
./New-AgentRuntimeIdentities.ps1 -EntraTenantId <tenant-guid> -Tenant <tenant> -KeyVaultName <vault> -ExecSecretPrefix AgentRuntime--MyApp--Exec -WhatIf
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)]
    [guid] $EntraTenantId,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9-]+$')]
    [string] $Tenant,

    [Parameter(Mandatory)]
    [string] $KeyVaultName,

    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9-]+$')]
    [string] $ExecSecretPrefix,

    [ValidateRange(1, 730)]
    [int] $CertificateValidityDays = 365,

    [string] $ApiApplicationName = 'agentizr-api',

    [string] $CallerSecretPrefix = 'AgentRuntime--Agentizr--TenantAuthenticationConfig--TenantCallers'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
$PSNativeCommandUseErrorActionPreference = $false

function Get-AzToken([string] $Resource) {
    $token = & az account get-access-token --tenant $EntraTenantId --resource $Resource --query accessToken --output tsv
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) {
        throw "Unable to acquire a token for $Resource from Azure CLI."
    }
    return $token
}

function Invoke-Graph([string] $Method, [string] $Path, [object] $Body) {
    $parameters = @{ Method = $Method; Uri = "https://graph.microsoft.com/v1.0/$Path"; Headers = $script:GraphHeaders }
    if ($null -ne $Body) {
        $parameters.ContentType = 'application/json'
        $parameters.Body = $Body | ConvertTo-Json -Depth 10 -Compress
    }
    try {
        return Invoke-RestMethod @parameters
    }
    catch {
        throw "Graph $Method $($Path.Split('?')[0]) failed: $($_.ErrorDetails.Message)"
    }
}

function Get-VaultSecret([string] $Name) {
    try {
        return Invoke-RestMethod -Uri "$script:VaultUri/secrets/$Name`?api-version=7.4" -Headers $script:VaultHeaders
    }
    catch {
        if ($_.Exception.Response.StatusCode -eq 404) { return $null }
        throw "Key Vault read of '$Name' failed: $($_.ErrorDetails.Message)"
    }
}

function Set-VaultSecret([string] $Name, [string] $Value) {
    if (!$PSCmdlet.ShouldProcess("$KeyVaultName/$Name", 'Set secret')) { return }
    $null = Invoke-RestMethod -Method PUT -Uri "$script:VaultUri/secrets/$Name`?api-version=7.4" `
        -Headers $script:VaultHeaders -ContentType 'application/json' -Body (@{ value = $Value } | ConvertTo-Json -Compress)
}

function Get-OrNewApplication([string] $DisplayName) {
    $existing = @((Invoke-Graph GET "applications?`$filter=displayName eq '$DisplayName'").value)
    if ($existing.Count -gt 1) { throw "More than one application is named $DisplayName." }
    if ($existing.Count -eq 1) {
        Write-Host "Reusing application $DisplayName."
        $application = $existing[0]
    }
    elseif ($PSCmdlet.ShouldProcess($DisplayName, 'Create application')) {
        $application = Invoke-Graph POST 'applications' @{ displayName = $DisplayName; signInAudience = 'AzureADMyOrg' }
        Write-Host "Created application $DisplayName."
    }
    else {
        return $null
    }

    $principals = @((Invoke-Graph GET "servicePrincipals?`$filter=appId eq '$($application.appId)'").value)
    $principal = if ($principals.Count -eq 1) { $principals[0] } else { Invoke-Graph POST 'servicePrincipals' @{ appId = $application.appId } }
    return [pscustomobject]@{ Application = $application; Principal = $principal }
}

function Grant-AppRole([object] $Identity, [string[]] $RoleValues) {
    $assigned = @((Invoke-Graph GET "servicePrincipals/$($Identity.Principal.id)/appRoleAssignments").value |
        Where-Object resourceId -EQ $script:ApiPrincipal.id | ForEach-Object appRoleId)
    foreach ($value in $RoleValues) {
        $role = @($script:ApiApplication.appRoles | Where-Object value -EQ $value)
        if ($role.Count -ne 1) { throw "$ApiApplicationName has no '$value' app role." }
        if ($assigned -contains $role[0].id) { continue }
        if ($PSCmdlet.ShouldProcess($Identity.Application.displayName, "Grant $value")) {
            $null = Invoke-Graph POST "servicePrincipals/$($Identity.Principal.id)/appRoleAssignments" @{
                principalId = $Identity.Principal.id; resourceId = $script:ApiPrincipal.id; appRoleId = $role[0].id
            }
        }
    }
}

function Set-CallerMapping([int] $Index, [string] $ClientId) {
    $name = "$CallerSecretPrefix--$Tenant--$Index"
    $current = Get-VaultSecret $name
    if ($null -ne $current -and $current.value -eq $ClientId) { return }
    if ($null -ne $current) { throw "$name already maps another client; resolve it manually." }
    Set-VaultSecret $name $ClientId
}

$script:GraphHeaders = @{ Authorization = "Bearer $(Get-AzToken 'https://graph.microsoft.com')" }
$script:VaultHeaders = @{ Authorization = "Bearer $(Get-AzToken 'https://vault.azure.net')" }
$script:VaultUri = "https://$KeyVaultName.vault.azure.net"

$apiApplications = @((Invoke-Graph GET "applications?`$filter=displayName eq '$ApiApplicationName'").value)
if ($apiApplications.Count -ne 1) { throw "Expected exactly one $ApiApplicationName application." }
$script:ApiApplication = $apiApplications[0]
$script:ApiPrincipal = @((Invoke-Graph GET "servicePrincipals?`$filter=appId eq '$($ApiApplication.appId)'").value)[0]

$exec = Get-OrNewApplication "agentizr-$Tenant-exec"
$admin = Get-OrNewApplication "agentizr-$Tenant-admin"
if ($null -eq $exec -or $null -eq $admin) { return }

if (@($admin.Application.keyCredentials).Count -ne 0 -or @($admin.Application.passwordCredentials).Count -ne 0) {
    throw "agentizr-$Tenant-admin has a standing credential; it must have none."
}

Grant-AppRole $exec @('agent.execute')
Grant-AppRole $admin @('agent.definition.read', 'agent.definition.publish', 'agent.definition.activate')

if (@($exec.Application.keyCredentials).Count -eq 0) {
    $privateKey = [Security.Cryptography.RSA]::Create(3072)
    try {
        $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new("CN=agentizr-$Tenant-exec",
            $privateKey, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $notBefore = [DateTimeOffset]::UtcNow.AddMinutes(-5)
        $certificate = $request.CreateSelfSigned($notBefore, $notBefore.AddDays($CertificateValidityDays))
        if ($PSCmdlet.ShouldProcess("agentizr-$Tenant-exec", 'Install certificate credential')) {
            $null = Invoke-Graph PATCH "applications/$($exec.Application.id)" @{
                keyCredentials = @(@{
                        customKeyIdentifier = [Convert]::ToBase64String($certificate.GetCertHash())
                        displayName         = "agent-runtime-exec-$($notBefore.ToString('yyyy-MM-dd'))"
                        endDateTime         = $certificate.NotAfter.ToUniversalTime().ToString('o')
                        key                 = [Convert]::ToBase64String($certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
                        startDateTime       = $certificate.NotBefore.ToUniversalTime().ToString('o')
                        type                = 'AsymmetricX509Cert'
                        usage               = 'Verify'
                    })
            }
            Set-VaultSecret "$ExecSecretPrefix--Certificate" ($certificate.ExportCertificatePem() + "`n" + $privateKey.ExportPkcs8PrivateKeyPem())
            Write-Host "Issued execution certificate, valid until $($certificate.NotAfter.ToString('yyyy-MM-dd'))."
        }
    }
    finally {
        $privateKey.Dispose()
    }
}
elseif ($null -eq (Get-VaultSecret "$ExecSecretPrefix--Certificate")) {
    throw "agentizr-$Tenant-exec has a certificate but $ExecSecretPrefix--Certificate is missing; rotate the credential."
}

$hostSettings = [ordered]@{
    Enabled  = 'true'
    TenantId = "$EntraTenantId"
    ClientId = $exec.Application.appId
    Scope    = "api://$($ApiApplication.appId)/.default"
}
foreach ($setting in $hostSettings.GetEnumerator()) {
    $current = Get-VaultSecret "$ExecSecretPrefix--$($setting.Key)"
    if ($null -eq $current -or $current.value -ne $setting.Value) {
        Set-VaultSecret "$ExecSecretPrefix--$($setting.Key)" $setting.Value
    }
}

Set-CallerMapping 0 $exec.Application.appId
Set-CallerMapping 1 $admin.Application.appId

Write-Host "Tenant '$Tenant': execution client $($exec.Application.appId), administration client $($admin.Application.appId)."
Write-Host 'Restart the runtime so it loads the caller mapping.'
