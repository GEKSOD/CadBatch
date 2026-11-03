param([string]$AssemblyDirectory = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $AssemblyDirectory) { $AssemblyDirectory = Join-Path $projectRoot 'src\CadBatch.AutoCAD\bin\Release\net48' }
$outputDirectory = Join-Path $projectRoot 'artifacts\verification\net48-pdf'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $AssemblyDirectory -Filter '*.dll' -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $outputDirectory -Force
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$facade = Join-Path $projectRoot '.packages\microsoft.netframework.referenceassemblies.net48\1.0.3\build\.NETFramework\v4.8\Facades\netstandard.dll'
$runner = Join-Path $outputDirectory 'PdfFrameworkSmoke.exe'
& $compiler /nologo /target:exe ('/out:' + $runner) ('/reference:' + (Join-Path $outputDirectory 'CadBatch.Pdf.dll')) ('/reference:' + (Join-Path $outputDirectory 'CadBatch.Core.dll')) ('/reference:' + $facade) (Join-Path $projectRoot 'tests\PdfFrameworkSmoke.cs')
if ($LASTEXITCODE -ne 0) { throw 'Framework PDF smoke compilation failed' }
$log = Join-Path $projectRoot 'artifacts\verification\net48-smoke.txt'
& $runner (Join-Path $projectRoot 'artifacts\verification\carrier-vector-test.pdf') (Join-Path $outputDirectory 'vector.pdf') 891 420 | Tee-Object -FilePath $log
if ($LASTEXITCODE -ne 0) { throw 'Framework PDF dependency smoke failed' }
$native = Join-Path $projectRoot 'artifacts\feedback\20261102-batch\CadBatch-control.dwg_006_А2.pdf'
if (Test-Path -LiteralPath $native) {
    & $runner $native (Join-Path $outputDirectory 'native.pdf') 594 420 | Tee-Object -FilePath $log -Append
    if ($LASTEXITCODE -ne 0) { throw 'Framework native-font PDF smoke failed' }
}
