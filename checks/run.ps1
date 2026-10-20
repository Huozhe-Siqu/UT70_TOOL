$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$outputPath = Join-Path $taskRoot 'bin\checks'
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$exePath = Join-Path $outputPath 'UT70Dashboard.Checks.exe'
$arguments = @('/nologo', '/target:exe', '/platform:x64', '/optimize+', '/codepage:65001', ('/out:' + $exePath))
foreach ($name in @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll')) {
    $arguments += '/reference:' + (Join-Path $frameworkPath $name)
}
$arguments += '/reference:' + (Join-Path $taskRoot 'ATK_UT70DLL.dll')
foreach ($name in @('DashboardForm.cs', 'DashboardDeviceInfo.cs', 'DashboardLayout.cs', 'DashboardControls.cs', 'TelemetryChart.cs', 'TelemetryBuffer.cs', 'DisplayUnits.cs', 'checks\DashboardChecks.cs')) {
    $arguments += Join-Path $taskRoot $name
}
& (Join-Path $frameworkPath 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item -LiteralPath (Join-Path $taskRoot 'ATK_UT70DLL.dll') -Destination $outputPath -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'App.config') -Destination ($exePath + '.config') -Force
& $exePath
exit $LASTEXITCODE
