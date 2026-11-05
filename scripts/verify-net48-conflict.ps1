$ErrorActionPreference = 'Stop'
# A host like AutoCAD with a binding redirect of Microsoft.Extensions.Logging.Abstractions to 1.1.1.
$projectRoot = Split-Path $PSScriptRoot -Parent
$plugin = Join-Path $projectRoot 'src\CadBatch.AutoCAD\bin\Release\net48'
$worker = Join-Path $projectRoot 'src\CadBatch.PdfWorker\bin\Release\net48\CadBatch.PdfWorker.exe'
$legacy = Join-Path $projectRoot '.packages\microsoft.extensions.logging.abstractions.legacy\1.1.1\x\lib\netstandard1.1\Microsoft.Extensions.Logging.Abstractions.dll'
if (-not (Test-Path -LiteralPath $legacy)) {
    # Fresh machines and CI: fetch the legacy package used only by this check.
    $package = Join-Path $projectRoot '.packages\microsoft.extensions.logging.abstractions.legacy\1.1.1'
    New-Item -ItemType Directory -Path $package -Force | Out-Null
    Invoke-WebRequest -UseBasicParsing -Uri 'https://www.nuget.org/api/v2/package/Microsoft.Extensions.Logging.Abstractions/1.1.1' -OutFile (Join-Path $package 'pkg.zip')
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $package 'pkg.zip'),(Join-Path $package 'x'))
}
$host48 = Join-Path $projectRoot 'artifacts\verification\net48-conflict'
if (Test-Path -LiteralPath $host48) { Remove-Item -LiteralPath $host48 -Recurse -Force }
$pluginCopy = Join-Path $host48 'plugin'
New-Item -ItemType Directory -Path $pluginCopy -Force | Out-Null
Get-ChildItem -LiteralPath $plugin -Filter '*.dll' -File | Where-Object { $_.Name -notlike 'Microsoft.Extensions.Logging.Abstractions.dll' } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $pluginCopy -Force }
Copy-Item -LiteralPath $legacy -Destination $host48 -Force
# Input pages are made in this PowerShell process, which has no redirect.
Get-ChildItem -LiteralPath $plugin -Filter '*.dll' | ForEach-Object { try { [void][Reflection.Assembly]::LoadFrom($_.FullName) } catch {} }
$seed = Join-Path $host48 'seed.txt'; Set-Content -LiteralPath $seed -Value 'seed'
foreach ($name in @('a','b')) { [CadBatch.Pdf.TargetSheetPdf]::WriteIul([CadBatch.Core.IulSheet]::Describe($seed,$name,$name,'1',$null),(Join-Path $host48 ($name + '.pdf'))) }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs = Join-Path $projectRoot '.packages\microsoft.netframework.referenceassemblies.net48\1.0.3\build\.NETFramework\v4.8'
$exe = Join-Path $host48 'PdfConflictSmoke.exe'
$arguments = @('/nologo','/target:exe','/platform:x64',('/out:' + $exe),('/reference:' + (Join-Path $refs 'Facades\netstandard.dll')),('/reference:' + (Join-Path $pluginCopy 'CadBatch.Pdf.dll')),('/reference:' + (Join-Path $pluginCopy 'CadBatch.Core.dll')),(Join-Path $projectRoot 'tests\PdfConflictSmoke.cs'))
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'PDF conflict smoke compilation failed' }
@'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup>
  <runtime>
    <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
      <probing privatePath="plugin"/>
      <dependentAssembly>
        <assemblyIdentity name="Microsoft.Extensions.Logging.Abstractions" publicKeyToken="adb9793829ddae60" culture="neutral"/>
        <bindingRedirect oldVersion="0.0.0.0-9.0.0.0" newVersion="1.1.1.0"/>
      </dependentAssembly>
    </assemblyBinding>
  </runtime>
</configuration>
'@ | Set-Content -LiteralPath ($exe + '.config') -Encoding UTF8
$process = Start-Process -FilePath $exe -ArgumentList @(('"' + $host48 + '"'),('"' + $worker + '"')) -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0) { Get-Content -LiteralPath (Join-Path $host48 'conflict.txt.error.txt'); throw 'PDF conflict smoke failed' }
Get-Content -LiteralPath (Join-Path $host48 'conflict.txt')
