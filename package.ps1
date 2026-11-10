$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1') | Out-Null
$package = Join-Path $PSScriptRoot 'dist\RawJpgStudio-1.1-win-x64.zip'
if (Test-Path -LiteralPath $package) { Remove-Item -LiteralPath $package -Force }
Compress-Archive -LiteralPath (Join-Path $PSScriptRoot 'dist\v1.1\RawJpgStudio') -DestinationPath $package -CompressionLevel Optimal
Write-Output $package
