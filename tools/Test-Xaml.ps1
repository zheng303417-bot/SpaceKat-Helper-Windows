param([string]$Path)
# Loads MainWindow.xaml the same way the app does, so markup errors surface without a blocking dialog.
$ErrorActionPreference = 'Stop'
if (!$Path) { $Path = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\MainWindow.xaml' }
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml
$stream = [IO.File]::OpenRead($Path)
try {
    $root = [Windows.Markup.XamlReader]::Load($stream)
    Write-Host ('XAML OK: ' + $root.GetType().Name + ', ' + $root.Resources.Count + ' resources')
}
finally { $stream.Dispose() }
