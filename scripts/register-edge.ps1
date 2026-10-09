param([Parameter(Mandatory=$true)][ValidatePattern('^[a-p]{32}$')][string]$ExtensionId)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$hostExe = Join-Path $root 'artifacts\app\FocusIsland.NativeHost.exe'
if (!(Test-Path -LiteralPath $hostExe)) { throw 'Build the app first: .\scripts\build.ps1' }
$manifestPath = Join-Path $root 'artifacts\app\native-host.json'
$manifest = @{
    name = 'cn.focusisland.desktop'
    description = 'Focus Island local page companion'
    path = [System.IO.Path]::GetFullPath($hostExe)
    type = 'stdio'
    allowed_origins = @("chrome-extension://$ExtensionId/")
} | ConvertTo-Json -Depth 3
[System.IO.File]::WriteAllText($manifestPath, $manifest, [System.Text.UTF8Encoding]::new($false))
$key = 'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\cn.focusisland.desktop'
New-Item -Path $key -Force | Out-Null
Set-Item -LiteralPath $key -Value ([System.IO.Path]::GetFullPath($manifestPath))
Write-Host 'Registered for the current Windows user only. Reopen the extension popup to connect.'
Write-Host $manifestPath
