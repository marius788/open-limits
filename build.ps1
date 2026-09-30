$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $compiler)) { throw 'Der .NET Framework 4.8 C#-Compiler wurde nicht gefunden.' }
$outputFile = Join-Path $root 'LimitLens.exe'
$iconFile = Join-Path $root 'assets\LimitLens.ico'
$files = Get-ChildItem (Join-Path $root 'LimitLens') -Filter '*.cs' | ForEach-Object FullName
if (-not (Test-Path $iconFile)) { throw 'Das App-Icon fehlt. Zuerst tools\build-icon.ps1 ausführen.' }
& $compiler /nologo /target:winexe /optimize+ "/out:$outputFile" "/win32manifest:$(Join-Path $root 'app.manifest')" "/win32icon:$iconFile" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll $files
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen.' }
Write-Host "Erstellt: $outputFile"
