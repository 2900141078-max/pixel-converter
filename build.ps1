$ErrorActionPreference = 'Stop'
$project = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework C# compiler not found.' }
$output = Join-Path $project 'dist\v1.1\RawJpgStudio'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$exe = Join-Path $output 'RawJpgStudio.exe'
$icon = Join-Path $project 'assets\aperture.ico'
if (-not (Test-Path -LiteralPath $icon)) { throw 'Run assets/make_icon.py before building.' }
$assemblyRoot = Join-Path $env:WINDIR 'Microsoft.NET\assembly'
function Get-AssemblyPath([string]$name) {
    $path = Get-ChildItem -LiteralPath $assemblyRoot -Recurse -Filter "$name.dll" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match 'GAC_64|GAC_MSIL' } | Select-Object -First 1 -ExpandProperty FullName
    if (-not $path) { throw "Required .NET assembly missing: $name" }
    return $path
}
& $compiler /nologo /target:winexe /platform:x64 /utf8output /out:$exe "/win32icon:$icon" `
    "/reference:$(Get-AssemblyPath PresentationFramework)" "/reference:$(Get-AssemblyPath PresentationCore)" `
    "/reference:$(Get-AssemblyPath WindowsBase)" "/reference:$(Get-AssemblyPath System.Xaml)" `
    (Join-Path $project 'RawJpgStudio.cs')
if ($LASTEXITCODE -ne 0) { throw 'C# build failed.' }
Copy-Item -LiteralPath (Join-Path $project 'MainWindow.xaml') -Destination $output -Force
Copy-Item -LiteralPath $icon -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $project 'assets\aperture-symbol.png') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $project 'README.md') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $project 'THIRD-PARTY-NOTICES.md') -Destination $output -Force
$sourceEngine = Join-Path $project 'vendor\rawtherapee\RawTherapee_5.13_win64_x86_64_release'
if (Test-Path -LiteralPath $sourceEngine) {
    $targetEngine = Join-Path $output 'engine'
    if (Test-Path -LiteralPath $targetEngine) { Remove-Item -LiteralPath $targetEngine -Recurse -Force }
    Copy-Item -LiteralPath $sourceEngine -Destination $targetEngine -Recurse
}
Write-Output $exe
