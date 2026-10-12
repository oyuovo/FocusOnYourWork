$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1')
& (Join-Path $root 'artifacts\app\FocusOnYourWork.Tests.exe') (Join-Path $root 'artifacts\test-data')
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
node --test (Join-Path $root 'tests\extension.test.cjs') (Join-Path $root 'tests\worker.test.cjs')
if ($LASTEXITCODE -ne 0) { throw 'Extension tests failed.' }
