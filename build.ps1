param([switch]$Test, [switch]$CleanOldVersions, [switch]$Package)
$ErrorActionPreference = 'Stop'
$projectPath = $PSScriptRoot
$currentVersion = '1.31'
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
$navigationResources = @('spacekat_bridge.py', 'blender\__init__.py',
    'freecad\SpaceKatNavigation.FCMacro', 'rhino\SpaceKatNavigation.py',
    'fusion\SpaceKatFusion\SpaceKatFusion.py', 'fusion\SpaceKatFusion\SpaceKatFusion.manifest', 'fusion\SpaceKatFusion\camera.py')
foreach ($relativeFile in $navigationResources) {
    $compilerArgs += '/resource:' + (Join-Path $projectPath ('navigation\' + $relativeFile)) + ',Navigation.' + $relativeFile.Replace('\', '.')
}
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
    $testProcess = Start-Process -FilePath (Join-Path $projectPath $outputName) -ArgumentList @('--self-test', '--output', ('"' + $testOutput + '"')) -WindowStyle Hidden -PassThru -Wait
    Get-Content -LiteralPath $testOutput
    if ($testProcess.ExitCode -ne 0) { throw '自测失败。' }
}
if ($CleanOldVersions) {
    & (Join-Path $projectPath 'clean-old-versions.ps1') -CurrentVersion $currentVersion
}
if ($Package) {
    $distPath = Join-Path $projectPath 'dist'
    New-Item -ItemType Directory -Force -Path $distPath | Out-Null
    $packagePath = Join-Path $distPath ('SpaceKat-Helper-Windows-' + $currentVersion + '.zip')
    $packageFiles = @($outputName, ($outputName + '.config'), '启动上位机.cmd', '演示模式.cmd', 'README.md', '使用说明.md',
        'native\LICENSE-ViGEmClient.txt', 'native\SOURCES.md', 'native\x64\ViGEmClient.dll', 'native\x86\ViGEmClient.dll')
    $packageFiles += $navigationResources | Where-Object { $_.StartsWith('fusion\') } | ForEach-Object { 'navigation\' + $_ }
    foreach ($file in $packageFiles) {
        if (!(Test-Path -LiteralPath (Join-Path $projectPath $file) -PathType Leaf)) { throw "缺少打包文件 $file" }
    }
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $packageStream = [IO.File]::Create($packagePath)
    try {
        $archive = [IO.Compression.ZipArchive]::new($packageStream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($file in $packageFiles) {
                [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $projectPath $file), $file.Replace('\', '/'))
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $packageStream.Dispose() }
    Write-Host ('已打包：' + $packagePath)
}
