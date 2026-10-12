$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$key = 'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\cn.focusisland.desktop'
if (Test-Path -LiteralPath $key) {
    $expected = @('native-host.json','artifacts\app\native-host.json','dist\FocusOnYourWork\native-host.json') | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $root $_)) }
    $actual = (Get-Item -LiteralPath $key).GetValue('')
    if ($actual -notin $expected) { throw 'Registration points to another installation; it was not removed.' }
    Remove-Item -LiteralPath $key
}
Write-Host 'This installation is unregistered. Your focus and cultivation progress has been kept.'
