param([Parameter(Mandatory=$true)][string]$NativeCompiler)
$ErrorActionPreference='Stop'
$repositoryRoot=Split-Path -Parent $PSScriptRoot
$testOutput=Join-Path $PSScriptRoot 'bin'
$mockDirectory=Join-Path $testOutput 'native-test'
New-Item -ItemType Directory -Path $mockDirectory -Force | Out-Null
& (Join-Path $repositoryRoot 'build.ps1') -OutputPath (Join-Path $testOutput 'NexusAnimaFPS.exe') -NativeCompiler $NativeCompiler
$workerSource=Get-Content -LiteralPath (Join-Path $repositoryRoot 'native\worker.c') -Raw
$signatures=[regex]::Matches($workerSource,'static const BYTE signature[0-3]\[\]=\{[^}]+\};') | ForEach-Object {$_.Value}
[IO.File]::WriteAllLines((Join-Path $testOutput 'mock_profile.h'),$signatures,[Text.Encoding]::ASCII)
& $NativeCompiler -shared -Wall '-I' $testOutput -o (Join-Path $mockDirectory 'UnityPlayer.dll') (Join-Path $PSScriptRoot 'mock_unity.c') -lkernel32
if($LASTEXITCODE -ne 0){throw 'Mock engine compilation failed.'}
& $NativeCompiler '-Wl,-subsystem=windows' -Wall -o (Join-Path $mockDirectory 'NexusAnima.exe') (Join-Path $PSScriptRoot 'mock_game.c') -lkernel32
if($LASTEXITCODE -ne 0){throw 'Mock process compilation failed.'}
$csharpCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source=Join-Path $repositoryRoot 'src'
$native=Join-Path $repositoryRoot 'native\bin'
foreach($suite in @('WorkerTests','StartupWorkerTests')){
    $executable=Join-Path $testOutput ($suite+'.exe')
    & $csharpCompiler /nologo /target:exe /platform:x64 /optimize+ /utf8output "/out:$executable" "/resource:$native\NexusAnimaFpsWorker.dll,NexusAnimaFpsWorker.dll" "/resource:$native\NexusAnimaBootstrap.bin,NexusAnimaBootstrap.bin" /reference:System.dll /reference:System.Core.dll /reference:System.Runtime.Serialization.dll (Join-Path $PSScriptRoot ($suite+'.cs')) "$source\Native.cs" "$source\Profile.cs" "$source\Session.cs" "$source\WorkerBridge.cs"
    if($LASTEXITCODE -ne 0){throw "$suite compilation failed."}
    & $executable | Tee-Object -FilePath (Join-Path $testOutput ($suite+'.txt'))
    if($LASTEXITCODE -ne 0){throw "$suite failed."}
}
