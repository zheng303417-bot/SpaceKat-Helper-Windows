param(
    [Parameter(Mandatory = $true)] [string]$BlenderPath
)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$app = [Reflection.Assembly]::LoadFrom((Join-Path $project 'SpaceKat小助手-1.18.exe'))
$flags = [Reflection.BindingFlags]'NonPublic, Static'
$bridge = $app.GetType('SpaceKatMonitor.NavigationBridge', $true)
$target = [Enum]::Parse($app.GetType('SpaceKatMonitor.NavigationTarget', $true), 'Blender')
$runId = [Guid]::NewGuid().ToString('N')
$name = 'Local\SpaceKat.Navigation.smoke.' + $runId
$out = Join-Path $project ('dist\verify-1.18\blender-' + $runId)
New-Item -ItemType Directory -Path $out | Out-Null
$zipPath = Join-Path $out 'addon.zip'
$zip = [IO.File]::Create($zipPath)
try { $bridge.GetMethod('ExportAddIn', $flags).Invoke($null, @($zip, $target)) }
finally { $zip.Dispose() }
Expand-Archive -LiteralPath $zipPath -DestinationPath $out
$publisher = $bridge.GetConstructors([Reflection.BindingFlags]'NonPublic, Instance')[0].Invoke(@($name))
$publish = $bridge.GetMethod('Publish', [Reflection.BindingFlags]'NonPublic, Instance')
$tick = $bridge.GetMethod('GetTickCount64', $flags)
$axes = [float[]]@(0.6, -0.5, 0.8, 0.7, -0.6, 0.9)
$test = Join-Path $project 'tests\blender_navigation_smoke.py'
$stdout = Join-Path $out 'stdout.txt'
$stderr = Join-Path $out 'stderr.txt'
$env:BLENDER_USER_RESOURCES = Join-Path $out 'profile'
$env:BLENDER_USER_EXTENSIONS = Join-Path $env:BLENDER_USER_RESOURCES 'extensions'
try {
    $job = Start-Process -FilePath $BlenderPath -ArgumentList @('-b', '--factory-startup', '--disable-autoexec', '--python-exit-code', '2',
        '--python', ('"' + $test + '"'), '--', ('"' + $out + '"'), ('"' + $name + '"')) -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $null = $job.Handle
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while (!$job.HasExited -and $deadline.Elapsed.TotalSeconds -lt 25) {
        $publish.Invoke($publisher, @($target, $tick.Invoke($null, @()), $axes))
        Start-Sleep -Milliseconds 16
    }
    if (!$job.HasExited) { $job.Kill(); throw 'Blender smoke test timed out' }
    $job.WaitForExit()
    Get-Content -LiteralPath $stdout,$stderr
    if ($job.ExitCode -ne 0 -or !(Select-String -LiteralPath $stdout -Pattern '^PASS Blender ' -Quiet)) {
        throw ('Blender smoke test failed; exit code: ' + $job.ExitCode)
    }
}
finally { $publisher.Dispose() }
