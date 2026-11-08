param([switch]$Test, [switch]$CleanOldVersions)
$ErrorActionPreference = 'Stop'
$projectPath = $PSScriptRoot
$currentVersion = '1.16'
$outputName = 'SpaceKat小助手-' + $currentVersion + '.exe'
if ($CleanOldVersions -and !$Test) { throw '清理旧版前必须通过检查，请同时指定 -Test。' }
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath (Join-Path $frameworkPath 'csc.exe'))) {
    $frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compilerPath = Join-Path $frameworkPath 'csc.exe'
if (!(Test-Path -LiteralPath $compilerPath)) { throw '找不到 Windows .NET Framework 编译器。请先安装 .NET Framework 4.8。' }
$references = @(
    (Join-Path $frameworkPath 'System.dll'),
    (Join-Path $frameworkPath 'System.Core.dll'),
    (Join-Path $frameworkPath 'System.IO.Compression.dll'),
    (Join-Path $frameworkPath 'System.Xaml.dll'),
    (Join-Path $frameworkPath 'WPF\WindowsBase.dll'),
    (Join-Path $frameworkPath 'WPF\PresentationCore.dll'),
    (Join-Path $frameworkPath 'WPF\PresentationFramework.dll')
)
$compilerArgs = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output', '/codepage:65001', '/langversion:5')
$compilerArgs += '/out:' + (Join-Path $projectPath $outputName)
$compilerArgs += '/win32manifest:' + (Join-Path $projectPath 'src\app.manifest')
$iconPath = Join-Path $projectPath 'src\Assets\joystick.ico'
$compilerArgs += '/win32icon:' + $iconPath
$compilerArgs += '/resource:' + $iconPath + ',SpaceKat.Joystick.ico'
$compilerArgs += '/resource:' + (Join-Path $projectPath 'src\MainWindow.xaml') + ',MainWindow.xaml'
foreach ($fileName in @('stick-settings.txt', 'wheel-settings.txt', 'mouse-settings.txt', 'button-mapping.txt', 'button-actions.txt', 'gamepad-button-actions.txt', 'theme-settings.txt')) {
    $compilerArgs += '/resource:' + (Join-Path $projectPath ('src\Defaults\' + $fileName)) + ',SpaceKatMonitor.Defaults.' + $fileName
}
$compilerArgs += $references | ForEach-Object { '/reference:' + $_ }
$compilerArgs += Get-ChildItem -LiteralPath (Join-Path $projectPath 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
$compilerArgs += Get-ChildItem -LiteralPath (Join-Path $projectPath 'tests') -Filter '*.cs' | ForEach-Object { $_.FullName }
& $compilerPath @compilerArgs
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Copy-Item -LiteralPath (Join-Path $projectPath 'src\app.config') -Destination (Join-Path $projectPath ($outputName + '.config')) -Force
Write-Host ('已生成：' + (Join-Path $projectPath $outputName))
if ($Test) {
    $testOutput = Join-Path $projectPath 'test-results.txt'
    $testProcess = Start-Process -FilePath (Join-Path $projectPath $outputName) -ArgumentList '--self-test' -WindowStyle Hidden -PassThru -Wait
    Get-Content -LiteralPath $testOutput
    if ($testProcess.ExitCode -ne 0) { throw '解码测试失败。' }
}
if ($CleanOldVersions) {
    & (Join-Path $projectPath 'clean-old-versions.ps1') -CurrentVersion $currentVersion
}
