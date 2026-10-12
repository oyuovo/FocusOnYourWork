param([switch]$Performance, [switch]$WarmOnly, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (!$SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
$output = Join-Path $root 'artifacts\presentation-check'
$arguments = @($output)
if ($WarmOnly) { $arguments += '--warm-performance' } elseif ($Performance) { $arguments += '--performance' }
& (Join-Path $root 'artifacts\app\FocusOnYourWork.PresentationTests.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Presentation checks failed.' }
