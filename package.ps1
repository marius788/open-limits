$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$version = '1.2.0'
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist "LimitLens-$version"
$archive = Join-Path $dist "LimitLens-$version-win-x64.zip"

& (Join-Path $root 'build.ps1')
if (-not (Test-Path (Join-Path $root 'LimitLens.exe'))) { throw 'LimitLens.exe fehlt nach dem Build.' }
if (Test-Path $dist) { Remove-Item -LiteralPath $dist -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item (Join-Path $root 'LimitLens.exe') $stage
Copy-Item (Join-Path $root 'README.md') $stage
Copy-Item (Join-Path $root 'LICENSE') $stage
Copy-Item (Join-Path $root 'SECURITY.md') $stage
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Value ($hash + '  ' + (Split-Path $archive -Leaf)) -Encoding ascii
Write-Host "Paket erstellt: $archive"
