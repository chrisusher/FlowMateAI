[CmdletBinding()]
param(
    [ValidateSet('Build', 'Component', 'Backend', 'CI')]
    [string]$Target = 'Build',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifactRoot = Join-Path $repositoryRoot 'artifacts/agent'
$packageRoot = Join-Path $artifactRoot 'packages'
$resultsRoot = Join-Path $artifactRoot 'TestResults'
$nugetConfig = Join-Path $repositoryRoot 'NuGet.config'
$solution = Join-Path $repositoryRoot 'FlowMateAI.slnx'

Set-Location $repositoryRoot

function Invoke-Dotnet {
    param([Parameter(Mandatory)][string[]]$Arguments)

    Write-Host ("dotnet " + ($Arguments -join ' ')) -ForegroundColor Cyan
    & dotnet @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet exited with code $LASTEXITCODE."
    }
}

function Assert-Docker {
    try {
        & docker info *> $null
    }
    catch {
        throw 'Docker Desktop is required for this verification target. Start Docker and retry.'
    }

    if ($LASTEXITCODE -ne 0) {
        throw 'Docker Desktop is required for this verification target. Start Docker and retry.'
    }
}

function Get-ProjectPath {
    param([Parameter(Mandatory)][ValidateSet('Component', 'Backend')][string]$Name)

    switch ($Name) {
        'Component' { 
            return 'Tests/ComponentTests/ComponentTests.csproj' 
        }
        'Backend' { 
            return 'Tests/Services.Tests/Services.Tests.csproj' 
        }
    }
}

function Restore-Target {
    param([Parameter(Mandatory)][string]$ProjectOrSolution)

    New-Item -ItemType Directory -Force -Path $artifactRoot, $packageRoot | Out-Null

    Invoke-Dotnet @(
        'restore', $ProjectOrSolution,
        '--configfile', $nugetConfig,
        '--packages', $packageRoot,
        '--artifacts-path', $artifactRoot,
        '--verbosity', 'minimal'
    )
}

function Build-Target {
    param([Parameter(Mandatory)][string]$ProjectOrSolution)

    Invoke-Dotnet @(
        'build', $ProjectOrSolution,
        '--no-restore',
        '--configuration', $Configuration,
        '--artifacts-path', $artifactRoot,
        '--verbosity', 'minimal'
    )
}

function Test-Target {
    param(
        [Parameter(Mandatory)][string]$Name,
        [string]$Filter
    )

    $project = Get-ProjectPath $Name
    $projectPath = Join-Path $repositoryRoot $project

    $testArguments = @(
        'test', $projectPath,
        '--no-build',
        '--no-restore',
        '--configuration', $Configuration,
        '--artifacts-path', $artifactRoot,
        '--logger', 'console;verbosity=minimal',
        '--logger', "trx;LogFileName=$Name.trx",
        '--results-directory', $resultsRoot
    )

    if ($Filter) {
        $testArguments += @('--filter', $Filter)
    }

    $testWorkingDirectory = Split-Path $projectPath -Parent
    Push-Location $testWorkingDirectory

    try {
        Invoke-Dotnet $testArguments
    }
    finally {
        Pop-Location
    }
}

switch ($Target) {
    'Build' {
        if (-not $NoRestore) { Restore-Target $solution }
        Build-Target $solution
    }
    'Component' {
        if (-not $NoRestore) { 
            Restore-Target (Get-ProjectPath 'Component') 
        }
        Build-Target (Get-ProjectPath 'Component')
        Test-Target 'Component'
    }
    'Backend' {
        Assert-Docker
        if (-not $NoRestore) { 
            Restore-Target (Get-ProjectPath 'Backend') 
        }
        Build-Target (Get-ProjectPath 'Backend')
        Test-Target 'Backend'
    }
    'CI' {
        if (-not $NoRestore) { 
            Restore-Target $solution 
        }
        Build-Target $solution
        Test-Target 'Component'
        Assert-Docker
        Test-Target 'Backend'
    }
}
