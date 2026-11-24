param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sourceRoot = Join-Path $PSScriptRoot 'src'
$outputFile = Join-Path $PSScriptRoot 'NexusAnimaFPS.exe'
if ($OutputPath) { $outputFile = $OutputPath }
$sources = Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' | ForEach-Object { $_.FullName }
$iconFile = Join-Path $PSScriptRoot 'assets\NexusAnima.ico'
& $compilerPath /nologo /target:winexe /platform:x64 /optimize+ /utf8output "/out:$outputFile" "/win32manifest:$sourceRoot\app.manifest" "/win32icon:$iconFile" "/resource:$iconFile,NexusAnima.ico" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll $sources
if ($LASTEXITCODE -ne 0) { throw "Compile failed: $LASTEXITCODE" }
Write-Output $outputFile
