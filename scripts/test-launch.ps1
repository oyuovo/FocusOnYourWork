param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'package.ps1') -SkipBuild:$SkipBuild
$exe = Join-Path $root 'dist\FocusOnYourWork\FocusOnYourWork.exe'
$data = Join-Path $root ('artifacts\launch-check\' + [Guid]::NewGuid().ToString('N'))
& (Join-Path $root 'artifacts\app\FocusOnYourWork.LaunchTests.exe') $exe $data
if ($LASTEXITCODE -ne 0) { throw 'Packaged executable launch checks failed.' }
