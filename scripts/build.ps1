param([switch]$CoreOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'artifacts\app'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler was not found.' }
$refs = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
if (!(Test-Path -LiteralPath $refs)) { throw 'Install the .NET Framework 4.8 Developer Pack to build this project.' }
function Compile([string]$name, [string]$kind, [string[]]$sources, [string[]]$references, [string[]]$extra = @()) {
    $arguments = @('/nologo', '/utf8output', '/codepage:65001', '/noconfig', '/nostdlib+', '/langversion:5', '/warn:4', '/warnaserror+', '/optimize+', '/debug:pdbonly', "/target:$kind", "/out:$out\$name")
    foreach ($reference in @('mscorlib.dll','System.dll','System.Core.dll','System.Xml.dll') + $references) {
        $refPath = if ([System.IO.Path]::IsPathRooted($reference)) { $reference } else { Join-Path $refs $reference }
        $arguments += "/reference:$refPath"
    }
    & $compiler @arguments @extra @sources
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $name" }
}
$core = @(Get-ChildItem -LiteralPath (Join-Path $root 'src\Core') -Filter '*.cs' | ForEach-Object FullName)
Compile 'FocusIsland.Core.dll' 'library' $core @('System.Web.Extensions.dll')
$testSources = @(Get-ChildItem -LiteralPath (Join-Path $root 'tests') -Filter '*.cs' | ForEach-Object FullName)
Compile 'FocusIsland.Tests.exe' 'exe' $testSources @("$out\FocusIsland.Core.dll", 'System.Web.Extensions.dll')
if (!$CoreOnly) {
    $desktop = @(Get-ChildItem -LiteralPath (Join-Path $root 'src\Desktop') -Filter '*.cs' | ForEach-Object FullName)
    Compile 'FocusIsland.exe' 'winexe' $desktop @("$out\FocusIsland.Core.dll", 'PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll','System.Xaml.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.Security.dll') @("/resource:$root\src\Desktop\MainWindow.xaml,MainWindow.xaml", "/win32manifest:$root\src\Desktop\app.manifest")
    $hostSources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src\NativeHost') -Filter '*.cs' | ForEach-Object FullName)
    Compile 'FocusIsland.NativeHost.exe' 'exe' $hostSources @("$out\FocusIsland.Core.dll", 'System.Web.Extensions.dll')
    Compile 'FocusIsland.ReadingTarget.exe' 'winexe' @("$root\tests\fixtures\ReadingTarget.cs") @('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll','System.Xaml.dll')
    Copy-Item -LiteralPath (Join-Path $root 'src\Desktop\App.config') -Destination (Join-Path $out 'FocusIsland.exe.config') -Force
}
Write-Host "Build succeeded: $out"
