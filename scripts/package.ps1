param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (!$SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
$build = Join-Path $root 'artifacts\app'
$destination = [IO.Path]::GetFullPath((Join-Path $root 'dist\FocusOnYourWork'))
$expected = [IO.Path]::GetFullPath($root).TrimEnd('\') + '\dist\FocusOnYourWork'
if ($destination -ne $expected) { throw 'Package destination is outside the expected workspace folder.' }
$runtimeFiles = @('FocusOnYourWork.exe','FocusOnYourWork.exe.config','FocusOnYourWork.Core.dll','FocusOnYourWork.NativeHost.exe','Theme.xaml')
foreach ($name in $runtimeFiles + @('Art')) {
    if (!(Test-Path -LiteralPath (Join-Path $build $name))) { throw "Build output missing: $name" }
}
foreach ($process in @(Get-Process -Name FocusOnYourWork,FocusOnYourWork.NativeHost -ErrorAction SilentlyContinue)) {
    if ($process.Path -and $process.Path.StartsWith($destination + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Exit the packaged client and disconnect its Edge extension before packaging again.'
    }
}
$registrationPath = Join-Path $destination 'native-host.json'
$registration = if (Test-Path -LiteralPath $registrationPath) { [IO.File]::ReadAllText($registrationPath) } else { $null }
if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }
New-Item -ItemType Directory -Force -Path $destination | Out-Null
foreach ($name in $runtimeFiles) {
    Copy-Item -LiteralPath (Join-Path $build $name) -Destination (Join-Path $destination $name)
}
Copy-Item -LiteralPath (Join-Path $build 'Art') -Destination (Join-Path $destination 'Art') -Recurse
Copy-Item -LiteralPath (Join-Path $root 'extension') -Destination (Join-Path $destination 'extension') -Recurse
$scriptOut = Join-Path $destination 'scripts'
New-Item -ItemType Directory -Path $scriptOut | Out-Null
foreach ($name in @('register-edge.ps1','unregister-edge.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $scriptOut $name) }
if ($null -ne $registration) { [IO.File]::WriteAllText($registrationPath, $registration, [Text.UTF8Encoding]::new($false)) }
Write-Host "Portable app ready: $destination\FocusOnYourWork.exe"
