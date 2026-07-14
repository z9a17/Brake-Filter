param(
    [ValidateSet("Release", "Debug")]
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = [System.IO.Path]::GetFullPath($PSScriptRoot)
$sourceProject = Join-Path $root "source\BrakeFilter.csproj"
$testProject = Join-Path $root "tests\BrakeFilter.Tests.csproj"
$builtDll = Join-Path $root "source\bin\$Configuration\net8.0\BrakeFilter.dll"
$releaseDirectory = Join-Path $root "release"
$releaseDll = Join-Path $releaseDirectory "BrakeFilter.dll"
$releaseZip = Join-Path $releaseDirectory "Brake-Filter-v0.2.1.zip"

dotnet build $sourceProject --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Source build failed with exit code $LASTEXITCODE."
}

dotnet run --project $testProject --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Regression tests failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $builtDll -PathType Leaf)) {
    throw "Build did not produce $builtDll"
}

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
Copy-Item -LiteralPath $builtDll -Destination $releaseDll -Force

if (Test-Path -LiteralPath $releaseZip -PathType Leaf) {
    Remove-Item -LiteralPath $releaseZip -Force
}

Compress-Archive -LiteralPath $releaseDll -DestinationPath $releaseZip -CompressionLevel Optimal

Write-Output "Built plugin: $releaseDll"
Write-Output "Installable ZIP: $releaseZip"
Get-FileHash -LiteralPath $releaseDll, $releaseZip -Algorithm SHA256 |
    Select-Object Path, Hash
