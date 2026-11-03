$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'CadBatch.bundle'
if (-not (Test-Path -LiteralPath (Join-Path $source 'PackageContents.xml'))) { throw 'Run this script from the extracted release folder.' }
if (Get-Process -Name acad -ErrorAction SilentlyContinue) { throw 'Close AutoCAD before installing/updating CadBatch.' }
$pluginRoot = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'
$target = Join-Path $pluginRoot 'CadBatch.bundle'
New-Item -ItemType Directory -Force -Path $pluginRoot | Out-Null
$resolvedRoot = [IO.Path]::GetFullPath($pluginRoot).TrimEnd('\') + '\'
$resolvedTarget = [IO.Path]::GetFullPath($target)
if (-not $resolvedTarget.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolvedTarget) -ne 'CadBatch.bundle') { throw 'Unexpected package path.' }
if (Test-Path -LiteralPath $target) {
    $backup = Join-Path $pluginRoot ('CadBatch.backup-' + [Guid]::NewGuid().ToString('N'))
    if (-not ([IO.Path]::GetFullPath($backup)).StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected backup path.' }
    Move-Item -LiteralPath $target -Destination $backup
    Write-Output "Previous own package saved: $backup"
}
Copy-Item -LiteralPath $source -Destination $target -Recurse
Write-Output "Installed: $target"
Write-Output 'Start AutoCAD, then launch CAD Batch and connect to the running CAD session.'
