$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $compiler)) { throw 'Der .NET Framework 4.8 C#-Compiler wurde nicht gefunden.' }
$output = Join-Path $PSScriptRoot 'LimitLens.Tests.exe'
$source = Get-ChildItem (Join-Path $root 'LimitLens') -Filter '*.cs' | ForEach-Object FullName
$source += Join-Path $PSScriptRoot 'TestProgram.cs'
& $compiler /nologo /target:exe /optimize+ "/out:$output" /main:LimitLensTests.TestProgram /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll $source
if ($LASTEXITCODE -ne 0) { throw 'Test-Build fehlgeschlagen.' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Tests fehlgeschlagen.' }
Remove-Item -LiteralPath $output -Force -ErrorAction SilentlyContinue
