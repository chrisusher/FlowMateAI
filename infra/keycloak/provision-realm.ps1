#!/usr/bin/env pwsh
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Get-RequiredEnvironmentValue([string] $Name) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Set $Name before provisioning."
    }

    return $value.Trim()
}

function ConvertTo-FormValue([string] $Value) {
    return [Uri]::EscapeDataString($Value).Replace('%20', '+')
}

$base = (Get-RequiredEnvironmentValue 'KEYCLOAK_URL').TrimEnd('/')
$realm = [Environment]::GetEnvironmentVariable('KEYCLOAK_REALM')
if ([string]::IsNullOrWhiteSpace($realm)) { $realm = 'flowmate' }
$realm = $realm.Trim()
$adminUser = Get-RequiredEnvironmentValue 'KEYCLOAK_ADMIN_USERNAME'
$adminPassword = Get-RequiredEnvironmentValue 'KEYCLOAK_ADMIN_PASSWORD'

function Invoke-KeycloakRequest(
    [string] $Method,
    [string] $Path,
    [string] $Token,
    [object] $Payload,
    [hashtable] $Form,
    [switch] $AllowMissing
) {
    $headers = @{ Accept = 'application/json' }
    if ($Token) { $headers.Authorization = "Bearer $Token" }
    $parameters = @{ Uri = "$base$Path"; Method = $Method; Headers = $headers; TimeoutSec = 20 }

    if ($null -ne $Payload) {
        $headers['Content-Type'] = 'application/json'
        $parameters.Body = ConvertTo-Json -InputObject $Payload -Depth 100 -Compress
        $parameters.ContentType = 'application/json'
    }
    elseif ($null -ne $Form) {
        $parameters.ContentType = 'application/x-www-form-urlencoded'
        $parameters.Body = (($Form.GetEnumerator() | ForEach-Object {
                    "$(ConvertTo-FormValue $_.Key)=$(ConvertTo-FormValue ([string]$_.Value))"
                }) -join '&')
    }

    try {
        $response = Invoke-WebRequest @parameters
        $body = $response.Content
        return [pscustomobject]@{
            Status = [int]$response.StatusCode
            Data   = if ([string]::IsNullOrWhiteSpace($body)) { $null } else { ConvertFrom-Json -InputObject $body -AsHashtable }
        }
    }
    catch {
        $response = $_.Exception.Response
        $status = if ($null -ne $response) { [int]$response.StatusCode } else { 0 }
        if ($AllowMissing -and $status -eq 404) {
            return [pscustomobject]@{ Status = 404; Data = $null }
        }

        $detail = $_.ErrorDetails.Message
        if ([string]::IsNullOrWhiteSpace($detail) -and $null -ne $response) {
            if ($response.Content) {
                $detail = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            }
        }
        if ($null -eq $detail) { $detail = '' }
        throw "Keycloak $Method $Path failed ($status): $($detail.Substring(0, [Math]::Min(500, $detail.Length)))"
    }
}

$tokenResult = (Invoke-KeycloakRequest -Method POST -Path '/realms/master/protocol/openid-connect/token' -Token '' -Form @{
        grant_type = 'password'; client_id = 'admin-cli'; username = $adminUser; password = $adminPassword
    }).Data
$token = $tokenResult.access_token
$root = '/admin/realms'
$quotedRealm = [Uri]::EscapeDataString($realm)
$currentResponse = Invoke-KeycloakRequest -Method GET -Path "$root/$quotedRealm" -Token $token -AllowMissing
$realmSettings = @{
    realm = $realm; enabled = $true; sslRequired = 'external'; registrationAllowed = $true
    registrationEmailAsUsername = $true; loginWithEmailAllowed = $true; duplicateEmailsAllowed = $false
    verifyEmail = $true; resetPasswordAllowed = $true; bruteForceProtected = $true; failureFactor = 5
    waitIncrementSeconds = 60; maxFailureWaitSeconds = 900; passwordPolicy = 'length(15) and notUsername'
    accessTokenLifespan = 300; ssoSessionIdleTimeout = 1800; ssoSessionMaxLifespan = 28800
}
if ($currentResponse.Status -eq 404) {
    $null = Invoke-KeycloakRequest -Method POST -Path $root -Token $token -Payload $realmSettings
}
else {
    $updatedRealm = @{} + $currentResponse.Data
    foreach ($key in $realmSettings.Keys) { $updatedRealm[$key] = $realmSettings[$key] }
    $null = Invoke-KeycloakRequest -Method PUT -Path "$root/$quotedRealm" -Token $token -Payload $updatedRealm
}

function Get-KeycloakClients([string] $ClientId) {
    $encoded = [Uri]::EscapeDataString($ClientId)
    return (Invoke-KeycloakRequest -Method GET -Path "$root/$quotedRealm/clients?clientId=$encoded" -Token $token).Data
}

function Upsert-ApiAudienceScope {
    $scopes = (Invoke-KeycloakRequest -Method GET -Path "$root/$quotedRealm/client-scopes?search=flowmate-api-audience" -Token $token).Data
    $scope = @{
        name            = 'flowmate-api-audience'
        description     = 'Adds the FlowMate API audience to browser access tokens'
        protocol        = 'openid-connect'
        attributes      = @{ 'include.in.token.scope' = 'true'; 'display.on.consent.screen' = 'false' }
        protocolMappers = @(@{
                name = 'FlowMate API audience'; protocol = 'openid-connect'; protocolMapper = 'oidc-audience-mapper'; consentRequired = $false
                config = @{ 'included.client.audience' = 'flowmate-api'; 'id.token.claim' = 'false'; 'access.token.claim' = 'true' }
            })
    }
    $matching = @($scopes | Where-Object { $_.name -eq $scope.name })
    if ($matching.Count -gt 0) {
        $scope.id = $matching[0].id
        $null = Invoke-KeycloakRequest -Method PUT -Path "$root/$quotedRealm/client-scopes/$($scope.id)" -Token $token -Payload $scope
        return $scope.id
    }

    $null = Invoke-KeycloakRequest -Method POST -Path "$root/$quotedRealm/client-scopes" -Token $token -Payload $scope
    $scopes = (Invoke-KeycloakRequest -Method GET -Path "$root/$quotedRealm/client-scopes?search=flowmate-api-audience" -Token $token).Data
    $matching = @($scopes | Where-Object { $_.name -eq $scope.name })
    if ($matching.Count -eq 0) { throw 'Keycloak did not create the FlowMate API audience scope.' }
    return $matching[0].id
}

function Upsert-KeycloakClient([string] $ClientId, [hashtable] $Representation) {
    $found = @(Get-KeycloakClients $ClientId)
    if ($found.Count -gt 0) {
        $Representation.id = $found[0].id
        $null = Invoke-KeycloakRequest -Method PUT -Path "$root/$quotedRealm/clients/$($Representation.id)" -Token $token -Payload $Representation
        return $Representation.id
    }

    $null = Invoke-KeycloakRequest -Method POST -Path "$root/$quotedRealm/clients" -Token $token -Payload $Representation
    $found = @(Get-KeycloakClients $ClientId)
    if ($found.Count -eq 0) { throw "Keycloak did not create client $ClientId." }
    return $found[0].id
}

$redirectUris = @(([Environment]::GetEnvironmentVariable('FLOWMATE_FRONTEND_REDIRECT_URIS') -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$webOrigins = @(([Environment]::GetEnvironmentVariable('FLOWMATE_FRONTEND_ORIGINS') -split ',') | ForEach-Object { $_.Trim().TrimEnd('/') } | Where-Object { $_ })
if ($redirectUris.Count -eq 0 -or $webOrigins.Count -eq 0) {
    throw 'Set exact FLOWMATE_FRONTEND_REDIRECT_URIS and FLOWMATE_FRONTEND_ORIGINS allow-lists.'
}

$null = Upsert-ApiAudienceScope
$null = Upsert-KeycloakClient 'flowmate-web' @{
    clientId = 'flowmate-web'; name = 'FlowMate browser application'; enabled = $true
    protocol = 'openid-connect'; publicClient = $true; standardFlowEnabled = $true
    implicitFlowEnabled = $false; directAccessGrantsEnabled = $false; serviceAccountsEnabled = $false
    redirectUris = $redirectUris; webOrigins = $webOrigins
    defaultClientScopes = @('profile', 'email', 'flowmate-api-audience')
    attributes = @{ 'pkce.code.challenge.method' = 'S256'; 'post.logout.redirect.uris' = '+' }
}
$null = Upsert-KeycloakClient 'flowmate-api' @{
    clientId = 'flowmate-api'; name = 'FlowMate API audience'; enabled = $true
    protocol = 'openid-connect'; bearerOnly = $true; publicClient = $false
    standardFlowEnabled = $false; implicitFlowEnabled = $false; directAccessGrantsEnabled = $false
}
$cliRepresentation = @{
    clientId = 'flowmate-cli'; name = 'FlowMate administrative CLI'; enabled = $true
    protocol = 'openid-connect'; publicClient = $false; standardFlowEnabled = $false
    implicitFlowEnabled = $false; directAccessGrantsEnabled = $false; serviceAccountsEnabled = $true
}
$cliSecret = [Environment]::GetEnvironmentVariable('KEYCLOAK_CLI_CLIENT_SECRET')
if (-not [string]::IsNullOrWhiteSpace($cliSecret)) { $cliRepresentation.secret = $cliSecret }
$cliClient = Upsert-KeycloakClient 'flowmate-cli' $cliRepresentation

$serviceAccount = (Invoke-KeycloakRequest -Method GET -Path "$root/$quotedRealm/clients/$cliClient/service-account-user" -Token $token).Data
$managementClients = @(Get-KeycloakClients 'realm-management')
if ($managementClients.Count -eq 0) { throw 'Keycloak realm-management client is missing.' }
$managementId = [Uri]::EscapeDataString($managementClients[0].id)
$rolePath = "$root/$quotedRealm/users/$($serviceAccount.id)/role-mappings/clients/$managementId"
$assigned = (Invoke-KeycloakRequest -Method GET -Path $rolePath -Token $token).Data
$available = (Invoke-KeycloakRequest -Method GET -Path "$rolePath/available" -Token $token).Data
$requiredRoles = @('query-users', 'view-users')
$assignedNames = @($assigned | ForEach-Object { $_.name })
$rolesToAssign = @($available | Where-Object { $_.name -in $requiredRoles -and $_.name -notin $assignedNames })
$resolvedNames = @($assignedNames + @($rolesToAssign | ForEach-Object { $_.name }) | Select-Object -Unique)
if (@($requiredRoles | Where-Object { $_ -notin $resolvedNames }).Count -gt 0) {
    throw 'Could not find both query-users and view-users realm-management roles.'
}
if ($rolesToAssign.Count -gt 0) {
    $null = Invoke-KeycloakRequest -Method POST -Path $rolePath -Token $token -Payload $rolesToAssign
}

$providers = @(
    @{ Alias = 'google'; ProviderId = 'google'; Issuer = $null },
    @{ Alias = 'github'; ProviderId = 'github'; Issuer = $null },
    @{ Alias = 'microsoft-personal'; ProviderId = 'oidc'; Issuer = 'https://login.microsoftonline.com/consumers/v2.0' },
    @{ Alias = 'microsoft-work-school'; ProviderId = 'oidc'; Issuer = 'https://login.microsoftonline.com/organizations/v2.0' }
)
foreach ($provider in $providers) {
    $prefix = 'IDP_' + $provider.Alias.ToUpperInvariant().Replace('-', '_')
    $clientId = [Environment]::GetEnvironmentVariable("${prefix}_CLIENT_ID")
    $clientSecret = [Environment]::GetEnvironmentVariable("${prefix}_CLIENT_SECRET")
    $hasClientId = -not [string]::IsNullOrWhiteSpace($clientId)
    $hasClientSecret = -not [string]::IsNullOrWhiteSpace($clientSecret)
    if ($hasClientId -ne $hasClientSecret) { throw "Set both ${prefix}_CLIENT_ID and ${prefix}_CLIENT_SECRET, or neither." }
    if (-not $hasClientId) { continue }

    $config = @{ clientId = $clientId.Trim(); clientSecret = $clientSecret.Trim(); clientAuthMethod = 'client_secret_post' }
    if ($provider.Issuer) {
        $rootIssuer = $provider.Issuer -replace '/v2\.0$', ''
        $config.issuer = $provider.Issuer
        $config.authorizationUrl = "$rootIssuer/oauth2/v2.0/authorize"
        $config.tokenUrl = "$rootIssuer/oauth2/v2.0/token"
        $config.userInfoUrl = 'https://graph.microsoft.com/oidc/userinfo'
        $config.defaultScope = 'openid profile email'
    }
    $representation = @{
        alias = $provider.Alias; providerId = $provider.ProviderId; enabled = $true; trustEmail = $false
        storeToken = $false; addReadTokenRoleOnCreate = $false; config = $config
    }
    $encodedAlias = [Uri]::EscapeDataString($provider.Alias)
    $providerPath = "$root/$quotedRealm/identity-provider/instances/$encodedAlias"
    $existingProvider = Invoke-KeycloakRequest -Method GET -Path $providerPath -Token $token -AllowMissing
    if ($null -eq $existingProvider.Data) {
        $null = Invoke-KeycloakRequest -Method POST -Path "$root/$quotedRealm/identity-provider/instances" -Token $token -Payload $representation
    }
    else {
        $updatedProvider = @{} + $existingProvider.Data
        foreach ($key in $representation.Keys) { $updatedProvider[$key] = $representation[$key] }
        $null = Invoke-KeycloakRequest -Method PUT -Path $providerPath -Token $token -Payload $updatedProvider
    }
}

$smtp = @{}
foreach ($name in @('host', 'port', 'from', 'fromDisplayName', 'replyTo', 'auth', 'user', 'password', 'ssl', 'starttls')) {
    $envName = 'SMTP_' + $name.ToUpperInvariant().Replace('-', '_')
    $value = [Environment]::GetEnvironmentVariable($envName)
    if ($null -eq $value) { $value = '' }
    $smtp[$name] = $value.Trim()
}
if ($smtp.host -or $smtp.from) {
    if (-not $smtp.host -or -not $smtp.from) { throw 'SMTP_HOST and SMTP_FROM are both required to configure email actions.' }
    $currentRealm = (Invoke-KeycloakRequest -Method GET -Path "$root/$quotedRealm" -Token $token).Data
    $updatedRealm = @{} + $currentRealm
    $updatedRealm.smtpServer = $smtp
    $null = Invoke-KeycloakRequest -Method PUT -Path "$root/$quotedRealm" -Token $token -Payload $updatedRealm
}

Write-Output "Reconciled realm '$realm' in place. Existing accounts and signing keys were preserved."
