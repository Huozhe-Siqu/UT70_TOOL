param([switch]$IfNeeded)

$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkPath 'csc.exe'
$outputPath = Join-Path $taskRoot 'bin\Release'
$outputExe = Join-Path $outputPath 'UT70Dashboard.exe'
$sourceNames = @('Program.cs', 'DashboardForm.cs', 'DashboardDeviceInfo.cs', 'DashboardLayout.cs', 'DashboardControls.cs', 'TelemetryBuffer.cs', 'TelemetryChart.cs', 'DisplayUnits.cs')
$sourcePaths = @($sourceNames | ForEach-Object { Join-Path $taskRoot $_ })
$inputs = $sourcePaths + @((Join-Path $taskRoot 'ATK_UT70DLL.dll'), (Join-Path $taskRoot 'App.config'), $PSCommandPath)

try {
    if ($IfNeeded -and (Test-Path -LiteralPath $outputExe)) {
        $builtAt = (Get-Item -LiteralPath $outputExe).LastWriteTimeUtc
        $changed = @($inputs | Where-Object { (Get-Item -LiteralPath $_).LastWriteTimeUtc -gt $builtAt })
        $missing = !(Test-Path -LiteralPath (Join-Path $outputPath 'ATK_UT70DLL.dll')) -or !(Test-Path -LiteralPath ($outputExe + '.config'))
        if ($changed.Count -eq 0 -and !$missing) { exit 0 }
    }
    if (!(Test-Path -LiteralPath $compiler)) { throw 'The 64-bit .NET Framework C# compiler was not found.' }
    $stagingPath = Join-Path $taskRoot 'bin\.build'
    New-Item -ItemType Directory -Path $stagingPath, $outputPath -Force | Out-Null
    $stagingExe = Join-Path $stagingPath 'UT70Dashboard.exe'
    $arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001', ('/out:' + $stagingExe))
    foreach ($reference in @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll')) {
        $arguments += '/reference:' + (Join-Path $frameworkPath $reference)
    }
    $arguments += '/reference:' + (Join-Path $taskRoot 'ATK_UT70DLL.dll')
    $arguments += $sourcePaths
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    try {
        Copy-Item -LiteralPath $stagingExe -Destination $outputExe -Force
        Copy-Item -LiteralPath (Join-Path $taskRoot 'ATK_UT70DLL.dll') -Destination $outputPath -Force
        Copy-Item -LiteralPath (Join-Path $taskRoot 'App.config') -Destination ($outputExe + '.config') -Force
    }
    catch { throw 'Could not update the application files. Close UT70 Power Lab and run start.bat again.' }
    Write-Host ('Build complete: ' + $outputExe)
}
catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
exit 0
