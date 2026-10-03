$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$publishRoot = Join-Path $repoRoot '.build-output\Test-Web'
$contentRoot = Join-Path $publishRoot 'wwwroot'
$frameworkRoot = Join-Path $contentRoot '_framework'

Push-Location $repoRoot

try {
    dotnet publish Web\Web.csproj -c Release --no-restore -o $publishRoot
    
    if ($LASTEXITCODE -ne 0) { 
        throw 'Web publish failed.' 
    }

    $loader = Get-ChildItem $frameworkRoot -File -Filter 'blazor.webassembly.*.js' |
    Where-Object { $_.Name -match '^blazor\.webassembly\.[a-z0-9]+\.js$' } |
    Select-Object -First 1

    if (-not $loader) { 
        throw 'Could not find the fingerprinted Blazor WebAssembly loader.' 
    }

    Copy-Item $loader.FullName (Join-Path $frameworkRoot 'blazor.webassembly.js') -Force
    $dotnetBootstrap = Get-ChildItem $frameworkRoot -File -Filter 'dotnet.*.js' |
    Where-Object { $_.Name -match '^dotnet\.[a-z0-9]+\.js$' } |
    Select-Object -First 1

    if (-not $dotnetBootstrap) { 
        throw 'Could not find the fingerprinted .NET JavaScript bootstrap.' 
    }
    Copy-Item $dotnetBootstrap.FullName (Join-Path $frameworkRoot 'dotnet.js') -Force

    $appSettingsPath = Join-Path $publishRoot 'appsettings.json'
    $appSettings = Get-Content $appSettingsPath -Raw | ConvertFrom-Json
    $appSettings.ApiBaseUrl = 'https://flowmateai-test-api.azurewebsites.net/'
    $appSettings | ConvertTo-Json -Depth 32 | Set-Content $appSettingsPath -Encoding utf8

    $indexPath = Join-Path $contentRoot 'index.html'
    $index = Get-Content $indexPath -Raw
    $index = $index.Replace('_framework/blazor.webassembly.js', "_framework/$($loader.Name)")
    Set-Content -Path $indexPath -Value $index -NoNewline

    Copy-Item $appSettingsPath (Join-Path $contentRoot 'appsettings.json') -Force

    $deploymentToken = az staticwebapp secrets list `
        --name flowmateai-test `
        --resource-group rg-flowmateai-TEST `
        --query properties.apiKey `
        --output tsv

    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($deploymentToken)) {
        throw 'Could not retrieve the Static Web Apps deployment token.'
    }

    $env:SWA_CLI_DEPLOYMENT_TOKEN = $deploymentToken

    try {
        npx --yes @azure/static-web-apps-cli@2.0.2 deploy $contentRoot `
            --env production `
            --app-name flowmateai-test `
            --resource-group rg-flowmateai-TEST `
            --swa-config-location $contentRoot
        
        if ($LASTEXITCODE -ne 0) {
            throw 'Static Web Apps deployment failed.' 
        }
    }
    finally {
        Remove-Item Env:SWA_CLI_DEPLOYMENT_TOKEN -ErrorAction SilentlyContinue
    }
}
finally {
    Pop-Location
}
