param([ValidateRange(10,3600)][int]$Seconds = 600, [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Label = 'background', [string]$OutputDirectory, [int[]]$ProcessIds)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts\performance' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$samples = [System.Collections.Generic.List[object]]::new()
$previousCpu = @{}
$watch = [System.Diagnostics.Stopwatch]::StartNew()
$lastSeconds = 0.0
while ($watch.Elapsed.TotalSeconds -lt $Seconds) {
    $processes = if ($ProcessIds) { @(Get-Process -Id $ProcessIds -ErrorAction SilentlyContinue) } else { @(Get-Process -Name 'FocusOnYourWork','FocusOnYourWork.NativeHost' -ErrorAction SilentlyContinue) }
    if (!$processes) {
        if ($ProcessIds -and $samples.Count -gt 0) { break }
        throw 'No FocusOnYourWork app/host processes are running in this Windows session.'
    }
    $cpuDelta = 0.0
    $workingSet = 0L
    $committed = 0L
    foreach ($p in $processes) {
        $key = "$($p.Id):$($p.StartTime.Ticks)"
        $cpu = $p.TotalProcessorTime.TotalSeconds
        if ($previousCpu.ContainsKey($key)) { $cpuDelta += [Math]::Max(0, $cpu - $previousCpu[$key]) }
        $previousCpu[$key] = $cpu
        $workingSet += $p.WorkingSet64
        $committed += $p.PrivateMemorySize64
    }
    $now = $watch.Elapsed.TotalSeconds
    $elapsed = $now - $lastSeconds
    $privateWorking = $null
    try {
        $ids = @($processes.Id)
        $counters = Get-CimInstance -ClassName Win32_PerfFormattedData_PerfProc_Process -Filter "Name LIKE 'FocusOnYourWork%'"
        $matched = @($counters | Where-Object { $ids -contains [int]$_.IDProcess })
        if ($matched.Count -eq $ids.Count) { $privateWorking = ($matched | Measure-Object -Property WorkingSetPrivate -Sum).Sum }
    } catch { $privateWorking = $null }
    if ($lastSeconds -gt 0) {
        $samples.Add([PSCustomObject]@{
            ElapsedSeconds = [Math]::Round($now,2)
            IntervalSeconds = [Math]::Round($elapsed,3)
            ProcessIds = ($processes.Id -join ',')
            CpuPercentOfMachine = [Math]::Round(($cpuDelta / $elapsed / [Environment]::ProcessorCount * 100),4)
            WorkingSetMiB = [Math]::Round($workingSet / 1MB,2)
            PrivateCommittedMiB = [Math]::Round($committed / 1MB,2)
            PrivateWorkingSetMiB = if ($null -ne $privateWorking) { [Math]::Round($privateWorking / 1MB,2) } else { $null }
        })
    }
    $lastSeconds = $now
    Start-Sleep -Seconds 5
}
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$csv = Join-Path $OutputDirectory "$stamp-$Label.csv"
$samples | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding utf8
$intervalSum = ($samples | Measure-Object IntervalSeconds -Sum).Sum
$weightedCpu = ($samples | ForEach-Object { $_.CpuPercentOfMachine * $_.IntervalSeconds } | Measure-Object -Sum).Sum / $intervalSum
$summary = [PSCustomObject]@{
    Label = $Label
    ActualSeconds = [Math]::Round($watch.Elapsed.TotalSeconds,2)
    MeasuredSeconds = [Math]::Round($intervalSum,2)
    LogicalProcessors = [Environment]::ProcessorCount
    Samples = $samples.Count
    AverageCpuPercentOfMachine = [Math]::Round($weightedCpu,4)
    PeakWorkingSetMiB = ($samples | Measure-Object WorkingSetMiB -Maximum).Maximum
    PeakPrivateWorkingSetMiB = ($samples | Where-Object { $null -ne $_.PrivateWorkingSetMiB } | Measure-Object PrivateWorkingSetMiB -Maximum).Maximum
    PeakPrivateCommittedMiB = ($samples | Measure-Object PrivateCommittedMiB -Maximum).Maximum
    BrowserSharedProcessMemoryIncluded = $false
    Csv = $csv
}
$summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory "$stamp-$Label.json") -Encoding utf8
$summary | Format-List
