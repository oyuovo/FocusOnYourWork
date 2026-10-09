$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$key = 'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\cn.focusisland.desktop'
if (Test-Path -LiteralPath $key) {
    $expected = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts\app\native-host.json'))
    $actual = (Get-Item -LiteralPath $key).GetValue('')
    if ($actual -ne $expected) { throw 'Registration points to another installation; it was not removed.' }
    Remove-Item -LiteralPath $key
}
Write-Host 'This installation is unregistered. Your island save has been kept.'
