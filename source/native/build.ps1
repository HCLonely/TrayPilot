param([string]$OutputDirectory = "$PSScriptRoot/bin")
$ErrorActionPreference = 'Stop'
$vswhere = "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'Install Visual Studio C++ build tools and Windows SDK to build TrayPilot.Xaml.dll.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$outputPath = (Resolve-Path -LiteralPath $OutputDirectory).Path
$sourcePath = Join-Path $PSScriptRoot 'TrayPilot.Xaml.cpp'
$commandFile = Join-Path $outputPath 'compile.cmd'
@"
@echo off
call "$vs\Common7\Tools\VsDevCmd.bat" -arch=x64 -host_arch=x64 >nul
if errorlevel 1 exit /b %errorlevel%
cl /nologo /std:c++20 /EHsc /O2 /MT /LD /DUNICODE /D_UNICODE "$sourcePath" /Fo"$outputPath\TrayPilot.Xaml.obj" /link /OUT:"$outputPath\TrayPilot.Xaml.dll" /IMPLIB:"$outputPath\TrayPilot.Xaml.lib" /EXPORT:DllGetClassObject,PRIVATE /EXPORT:DllCanUnloadNow,PRIVATE runtimeobject.lib windowsapp.lib ole32.lib oleaut32.lib user32.lib dbghelp.lib
"@ | Set-Content -LiteralPath $commandFile -Encoding ascii
& $env:ComSpec /d /c $commandFile
if ($LASTEXITCODE) { throw "Native build failed: $LASTEXITCODE" }
