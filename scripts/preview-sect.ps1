param([ValidateSet('New','Ascension','Pity','Themes')][string]$Scenario = 'Ascension', [switch]$PrepareOnly)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') {
    $previewArgs = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$PSCommandPath,'-Scenario',$Scenario)
    if ($PrepareOnly) { $previewArgs += '-PrepareOnly' }
    & (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') @previewArgs
    if ($LASTEXITCODE -ne 0) { throw 'Preview preparation failed.' }
    return
}
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root 'artifacts\app\FocusOnYourWork.exe'
if (!(Test-Path -LiteralPath $app)) { & (Join-Path $PSScriptRoot 'build.ps1') }
if (!$PrepareOnly -and (Get-Process -Name FocusIsland,FocusOnYourWork -ErrorAction SilentlyContinue)) { throw 'Exit the running client from its tray menu before opening an isolated preview. No running session was stopped.' }
Add-Type -Path (Join-Path $root 'artifacts\app\FocusOnYourWork.Core.dll')
$dataPath = Join-Path $root ('artifacts\sect-preview\' + $Scenario + '-' + [Guid]::NewGuid().ToString('N'))
$save = New-Object FocusOnYourWork.Core.SaveData
$save.SectName = '隔离试用宗门'
if ($Scenario -ne 'New') {
    $save.WalletUnits = 10000L * 60000
    [FocusOnYourWork.Core.SectGame]::ChooseStarter($save, 'sword')
    if ($Scenario -eq 'Pity') {
        0..3 | ForEach-Object { [void][FocusOnYourWork.Core.SectGame]::Draw($save, ('preview-' + $_), 0) }
    } elseif ($Scenario -eq 'Themes') {
        1..5 | ForEach-Object { [void][FocusOnYourWork.Core.SectGame]::Draw($save, ('preview-' + $_), $_) }
        [FocusOnYourWork.Core.SectGame]::Progress($save.Sect, [FocusOnYourWork.Core.SectGame]::AscensionMs * 6, [DateTime]::UtcNow.ToString('o'), '外观试用样例，并非真实投入')
        [FocusOnYourWork.Core.SectGame]::UpgradeMountain($save)
        [FocusOnYourWork.Core.SectGame]::UpgradeMountain($save)
    } else {
        [void][FocusOnYourWork.Core.SectGame]::Draw($save, 'preview-herb', 1)
        [void][FocusOnYourWork.Core.SectGame]::Draw($save, 'preview-star', 2)
        [FocusOnYourWork.Core.SectGame]::UpgradeHousing($save)
        [FocusOnYourWork.Core.SectGame]::UpgradeMountain($save)
        $save.Sect.Roster[0].CultivationMs = [FocusOnYourWork.Core.SectGame]::AscensionMs - 60000
    }
    $save.WalletUnits = 600L * 60000
}
[FocusOnYourWork.Core.SectGame]::Validate($save.Sect)
$store = New-Object FocusOnYourWork.Core.SaveStore($dataPath)
$store.Save($save)
Write-Host "Isolated $Scenario preview saved to: $dataPath"
if ($PrepareOnly) { return }
$previousDataDirectory = $env:FOCUS_ISLAND_DATA_DIR
try {
    $env:FOCUS_ISLAND_DATA_DIR = $dataPath
    Start-Process -FilePath $app -WorkingDirectory (Split-Path -Parent $app) | Out-Null
} finally { $env:FOCUS_ISLAND_DATA_DIR = $previousDataDirectory }
