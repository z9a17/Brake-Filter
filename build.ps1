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
$checksumFile = Join-Path $releaseDirectory "SHA256SUMS.txt"
$versionNode = Select-Xml -LiteralPath $sourceProject -XPath "/Project/PropertyGroup/Version"
if ($null -eq $versionNode) {
    throw "Version is missing from $sourceProject"
}
$releaseZip = Join-Path $releaseDirectory "Brake-Filter-v$($versionNode.Node.InnerText).zip"

dotnet restore $testProject --locked-mode
if ($LASTEXITCODE -ne 0) {
    throw "Locked dependency restore failed with exit code $LASTEXITCODE."
}

dotnet build $sourceProject --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "Source build failed with exit code $LASTEXITCODE."
}

dotnet run --project $testProject --configuration $Configuration --no-restore
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

# Use a fixed ZIP timestamp so identical DLL input produces identical packaging.
$zipStream = [System.IO.File]::Open(
    $releaseZip,
    [System.IO.FileMode]::CreateNew,
    [System.IO.FileAccess]::ReadWrite,
    [System.IO.FileShare]::None)
try {
    $archive = [System.IO.Compression.ZipArchive]::new(
        $zipStream,
        [System.IO.Compression.ZipArchiveMode]::Create,
        $false)
    try {
        $entry = $archive.CreateEntry(
            "BrakeFilter.dll",
            [System.IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = [System.DateTimeOffset]::new(
            1980, 1, 1, 0, 0, 0, [System.TimeSpan]::Zero)
        $entryStream = $entry.Open()
        try {
            $inputStream = [System.IO.File]::OpenRead($releaseDll)
            try {
                $inputStream.CopyTo($entryStream)
            }
            finally {
                $inputStream.Dispose()
            }
        }
        finally {
            $entryStream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    $zipStream.Dispose()
}

$checksums = @($releaseDll, $releaseZip) |
    Sort-Object -Property { Split-Path -Leaf $_ } |
    ForEach-Object {
        $hash = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $(Split-Path -Leaf $_)"
    }
[System.IO.File]::WriteAllText(
    $checksumFile,
    (($checksums -join "`n") + "`n"),
    [System.Text.UTF8Encoding]::new($false))

Write-Output "Built plugin: $releaseDll"
Write-Output "Installable ZIP: $releaseZip"
Write-Output "Checksums: $checksumFile"
Get-FileHash -LiteralPath $releaseDll, $releaseZip, $checksumFile -Algorithm SHA256 |
    Select-Object Path, Hash
