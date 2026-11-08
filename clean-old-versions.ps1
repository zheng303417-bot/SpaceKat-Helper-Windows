[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Low')]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+(?:\.\d+){0,2}$')]
    [string]$CurrentVersion
)

$ErrorActionPreference = 'Stop'
$projectDirectory = Get-Item -LiteralPath $PSScriptRoot -ErrorAction Stop
$projectPath = [IO.Path]::GetFullPath($projectDirectory.FullName)
$currentVersionValue = [version]$CurrentVersion
$currentName = 'SpaceKat小助手-' + $CurrentVersion + '.exe'
$currentPath = Join-Path $projectPath $currentName
if (!(Test-Path -LiteralPath $currentPath -PathType Leaf)) {
    throw ('未找到需要保留的当前版本，未清理任何文件：' + $currentPath)
}

# Deliberately scan only direct children. Source, settings, diagnostic logs,
# firmware and archives never enter the allowlist.
$candidates = @()
foreach ($file in Get-ChildItem -LiteralPath $projectPath -File) {
    $isOld = $false
    if ($file.Name -match '^(?:SpaceKatMonitor|SpaceKat小助手)(?:-(?<version>\d+\.\d+(?:\.\d+){0,2}))?\.exe(?:\.config)?$') {
        $fileVersion = $Matches['version']
        $isOld = [String]::IsNullOrEmpty($fileVersion) -or ([version]$fileVersion -lt $currentVersionValue)
    }
    elseif ($file.Name -match '^preview-(?:.+-)?(?<version>\d+\.\d+(?:\.\d+){0,2})\.(?:png|txt)$') {
        $isOld = [version]$Matches['version'] -lt $currentVersionValue
    }
    elseif ($file.Name -match '^preview-demo\.(?:png|txt)$') {
        $isOld = $true
    }
    elseif ($file.Name -match '^(?:button-fix-(?:live|normal)|live-check)\.png$') {
        $isOld = $true
    }
    if ($isOld) { $candidates += $file }
}

$removed = @()
$skipped = @()
$planned = @()
$removedBytes = [long]0
$blockedExecutables = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)

# Remove a program before its matching config. When a running old program is
# locked, leave its config in place and continue removing unrelated old files.
$ordered = $candidates | Sort-Object @{ Expression = { if ($_.Name.EndsWith('.exe', [StringComparison]::OrdinalIgnoreCase)) { 0 } else { 1 } } }, Name
foreach ($file in $ordered) {
    $targetPath = [IO.Path]::GetFullPath($file.FullName)
    $targetParent = [IO.Path]::GetDirectoryName($targetPath)
    if (![String]::Equals($targetParent, $projectPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw ('目标不在程序目录的直接子级，未执行删除：' + $targetPath)
    }
    if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        $skipped += $file.Name
        Write-Warning ('跳过链接文件：' + $file.Name)
        continue
    }
    if ($file.Name.EndsWith('.exe.config', [StringComparison]::OrdinalIgnoreCase)) {
        $programName = $file.Name.Substring(0, $file.Name.Length - '.config'.Length)
        if ($blockedExecutables.Contains($programName)) {
            $skipped += $file.Name
            continue
        }
    }
    if (!$PSCmdlet.ShouldProcess($targetPath, '删除已被新版替代的程序或预览')) {
        $planned += $file.Name
        if (!$WhatIfPreference -and $file.Name.EndsWith('.exe', [StringComparison]::OrdinalIgnoreCase)) {
            [void]$blockedExecutables.Add($file.Name)
        }
        continue
    }
    try {
        Remove-Item -LiteralPath $targetPath -Force -ErrorAction Stop
        $removed += $file.Name
        $removedBytes += $file.Length
    }
    catch {
        $skipped += $file.Name
        if ($file.Name.EndsWith('.exe', [StringComparison]::OrdinalIgnoreCase)) {
            [void]$blockedExecutables.Add($file.Name)
        }
        Write-Warning ('未能删除 ' + $file.Name + '。若旧版仍在运行，请关闭后再次清理。原因：' + $_.Exception.Message)
    }
}

[PSCustomObject]@{
    CurrentVersion = $CurrentVersion
    RemovedFiles = $removed
    RemovedBytes = $removedBytes
    SkippedFiles = $skipped
    PlannedFiles = $planned
}
