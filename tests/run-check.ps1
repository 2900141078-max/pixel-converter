param(
    [Parameter(Mandatory=$true)][string]$FirstDng,
    [Parameter(Mandatory=$true)][string]$SecondDng,
    [Parameter(Mandatory=$true)][string]$OutputFolder,
    [Parameter(Mandatory=$true)][string]$ReportPath,
    [string]$AppFolder = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $AppFolder) { $AppFolder = Join-Path $root 'dist\RawJpgStudio' }
$appFolder = $AppFolder
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs = @('PresentationFramework','PresentationCore','WindowsBase','System.Xaml') | ForEach-Object {
    Get-ChildItem (Join-Path $env:WINDIR 'Microsoft.NET\assembly') -Recurse -Filter "$_.dll" |
        Where-Object FullName -Match 'GAC_64|GAC_MSIL' | Select-Object -First 1 -ExpandProperty FullName
}
$harness = Join-Path $appFolder 'DragConvertCheck.exe'
$compileArgs = @('/nologo','/target:winexe','/platform:x64',('/out:' + $harness))
$compileArgs += @($refs | ForEach-Object { '/reference:' + $_ })
$compileArgs += '/reference:' + (Join-Path $appFolder 'RawJpgStudio.exe')
$compileArgs += Join-Path $PSScriptRoot 'DragConvertCheck.cs'
& $compiler @compileArgs
if ($LASTEXITCODE -ne 0) { throw 'Harness compilation failed.' }
New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null
$arguments = @($FirstDng,$SecondDng,$OutputFolder,$ReportPath) | ForEach-Object { '"' + $_ + '"' }
$run = Start-Process -FilePath $harness -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
if ($run.ExitCode -ne 0) { throw "Harness failed: $($run.ExitCode); see $ReportPath" }
Get-Content -LiteralPath $ReportPath
Remove-Item -LiteralPath $harness -Force
