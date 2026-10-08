<#
.SYNOPSIS
Publishes TrayPilot for Windows x64.
.EXAMPLE
.\scripts\build.ps1
.EXAMPLE
.\scripts\build.ps1 -Package lite -OutputDirectory output\lite
#>
[CmdletBinding()]
param(
    [ValidateSet('full', 'lite')]
    [string]$Package = 'full',

    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [string]$OutputDirectory = 'app'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

try {
    foreach ($command in @('dotnet', 'git')) {
        if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
            throw "Missing $command. Install it and ensure it is available in PATH."
        }
    }

    $sdks = & dotnet --list-sdks
    if ($LASTEXITCODE -ne 0 -or -not ($sdks | Where-Object { $_ -match '^([0-9]+)\.' -and [int]$Matches[1] -ge 10 })) {
        throw 'Install .NET 10 SDK or a newer SDK supporting net10.0-windows.'
    }

    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
        throw 'Install Visual Studio C++ x64 build tools and the Windows SDK.'
    }
    $visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ($LASTEXITCODE -ne 0 -or -not $visualStudio) {
        throw 'Install Visual Studio C++ x64 build tools and the Windows SDK.'
    }

    if (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
        $OutputDirectory = Join-Path $repositoryRoot $OutputDirectory
    }
    $OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
    $selfContained = if ($Package -eq 'full') { 'true' } else { 'false' }

    Write-Host "Building TrayPilot ($Package, $Configuration, win-x64)..."
    $publishArguments = @(
        'publish', (Join-Path $repositoryRoot 'source\TrayPilot.csproj'),
        '--configuration', $Configuration,
        '--runtime', 'win-x64',
        '--self-contained', $selfContained,
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '--output', $OutputDirectory
    )
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed (exit code $LASTEXITCODE)."
    }

    $executable = Join-Path $OutputDirectory 'TrayPilot.exe'
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "Build completed but the executable was not found: $executable"
    }
    Write-Host "Build succeeded: $executable" -ForegroundColor Green
    exit 0
} catch {
    [Console]::Error.WriteLine("Build failed: $($_.Exception.Message)")
    exit 1
}
