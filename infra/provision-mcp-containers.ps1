#!/usr/bin/env pwsh
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

foreach ($name in @('AZURE_RESOURCE_GROUP', 'COSMOS_ACCOUNT', 'COSMOS_DATABASE')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        throw "Set $name before provisioning."
    }
}

$resourceGroup = $env:AZURE_RESOURCE_GROUP
$account = $env:COSMOS_ACCOUNT
$database = $env:COSMOS_DATABASE

function Invoke-AzureCli([string[]] $Arguments) {
    & az @Arguments *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI command failed with exit code ${LASTEXITCODE}: az $($Arguments -join ' ')"
    }
}

function Ensure-CosmosContainer([string] $Name, [int] $Ttl) {
    $showArguments = @(
        'cosmosdb', 'sql', 'container', 'show',
        '--resource-group', $resourceGroup,
        '--account-name', $account,
        '--database-name', $database,
        '--name', $Name
    )
    & az @showArguments *> $null
    if ($LASTEXITCODE -eq 0) {
        Invoke-AzureCli (@(
            'cosmosdb', 'sql', 'container', 'update',
            '--resource-group', $resourceGroup,
            '--account-name', $account,
            '--database-name', $database,
            '--name', $Name,
            '--ttl', "$Ttl"
        ))
        return
    }

    Invoke-AzureCli (@(
        'cosmosdb', 'sql', 'container', 'create',
        '--resource-group', $resourceGroup,
        '--account-name', $account,
        '--database-name', $database,
        '--name', $Name,
        '--partition-key-path', '/userId',
        '--ttl', "$Ttl"
    ))
}

Ensure-CosmosContainer -Name 'McpKeys' -Ttl -1
Ensure-CosmosContainer -Name 'McpUsage' -Ttl 172800
