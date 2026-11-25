param([string]$OutputPath, [string]$NativeCompiler)
$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sourceRoot = Join-Path $PSScriptRoot 'src'
$outputFile = Join-Path $PSScriptRoot 'NexusAnimaFPS.exe'
if ($OutputPath) { $outputFile = $OutputPath }
$sources = Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' | ForEach-Object { $_.FullName }
$iconFile = Join-Path $PSScriptRoot 'assets\NexusAnima.ico'
if (-not $NativeCompiler) {
    $nativeCommand = Get-Command 'tcc.exe' -ErrorAction SilentlyContinue
    if ($nativeCommand) { $NativeCompiler = $nativeCommand.Source }
}
if (-not $NativeCompiler -or -not (Test-Path -LiteralPath $NativeCompiler)) {
    throw 'Compiling from source requires 64-bit TinyCC. Pass -NativeCompiler C:\path\to\tcc.exe. End users do not need a compiler.'
}
$nativeDirectory = Join-Path $PSScriptRoot 'native\bin'
New-Item -ItemType Directory -Path $nativeDirectory -Force | Out-Null
$nativeOutput = Join-Path $nativeDirectory 'NexusAnimaFpsWorker.dll'
$nativeSource = Join-Path $PSScriptRoot 'native\worker.c'
& $NativeCompiler -shared -Wall -o $nativeOutput $nativeSource -lkernel32
if ($LASTEXITCODE -ne 0) { throw "Native compile failed: $LASTEXITCODE" }
$bootstrapObject=Join-Path $nativeDirectory 'bootstrap.o'
$bootstrapOutput=Join-Path $nativeDirectory 'NexusAnimaBootstrap.bin'
& $NativeCompiler -c -Wall -o $bootstrapObject (Join-Path $PSScriptRoot 'native\bootstrap.c')
if ($LASTEXITCODE -ne 0) { throw "Startup helper compile failed: $LASTEXITCODE" }
& (Join-Path $PSScriptRoot 'native\extract-bootstrap.ps1') -ObjectPath $bootstrapObject -OutputPath $bootstrapOutput
& $compilerPath /nologo /target:winexe /platform:x64 /optimize+ /utf8output "/out:$outputFile" "/win32manifest:$sourceRoot\app.manifest" "/win32icon:$iconFile" "/resource:$iconFile,NexusAnima.ico" "/resource:$nativeOutput,NexusAnimaFpsWorker.dll" "/resource:$bootstrapOutput,NexusAnimaBootstrap.bin" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll $sources
if ($LASTEXITCODE -ne 0) { throw "Compile failed: $LASTEXITCODE" }
Write-Output $outputFile
