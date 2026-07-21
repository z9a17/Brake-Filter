param(
    [ValidateSet("Release", "Debug")]
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = [System.IO.Path]::GetFullPath($PSScriptRoot)
$sourceProject = Join-Path $root "source\BrakeFilter.csproj"
$testProject = Join-Path $root "tests\BrakeFilter.Tests.csproj"
$metadataFile = Join-Path $root "metadata.json"
$builtDll = Join-Path $root "source\bin\$Configuration\net8.0\BrakeFilter.dll"
$releaseDirectory = Join-Path $root "release"
$releaseDll = Join-Path $releaseDirectory "BrakeFilter.dll"
$checksumFile = Join-Path $releaseDirectory "SHA256SUMS.txt"
$versionNode = Select-Xml -LiteralPath $sourceProject -XPath "/Project/PropertyGroup/Version"
if ($null -eq $versionNode) {
    throw "Version is missing from $sourceProject"
}
$version = $versionNode.Node.InnerText
$releaseZip = Join-Path $releaseDirectory "Brake-Filter-v$version.zip"

if (-not (Test-Path -LiteralPath $metadataFile -PathType Leaf)) {
    throw "Plugin metadata is missing from $metadataFile"
}

$metadata = Get-Content -Raw -LiteralPath $metadataFile | ConvertFrom-Json
$expectedMetadata = [ordered]@{
    Name = "Brake Filter"
    Owner = "z9a17"
    PluginVersion = "$version.0"
    SupportedDriverVersion = "0.6.7.0"
    RepositoryUrl = "https://github.com/z9a17/Brake-Filter"
    WikiUrl = "https://github.com/z9a17/Brake-Filter#readme"
    LicenseIdentifier = "MIT"
}
foreach ($property in $expectedMetadata.GetEnumerator()) {
    if ($metadata.($property.Key) -ne $property.Value) {
        throw "metadata.json property '$($property.Key)' must be '$($property.Value)'."
    }
}
if ([string]::IsNullOrWhiteSpace($metadata.Description)) {
    throw "metadata.json must contain a useful Description."
}

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

# Use fixed ZIP timestamps so identical input produces identical packaging.
function Add-DeterministicZipEntry {
    param(
        [Parameter(Mandatory)]
        [System.IO.Compression.ZipArchive] $Archive,
        [Parameter(Mandatory)]
        [string] $SourcePath,
        [Parameter(Mandatory)]
        [string] $EntryName,
        [switch] $NormalizeText
    )

    $entry = $Archive.CreateEntry(
        $EntryName,
        [System.IO.Compression.CompressionLevel]::Optimal)
    $entry.LastWriteTime = [System.DateTimeOffset]::new(
        1980, 1, 1, 0, 0, 0, [System.TimeSpan]::Zero)
    $entryStream = $entry.Open()
    try {
        if ($NormalizeText) {
            $text = [System.IO.File]::ReadAllText($SourcePath)
            $normalizedText = $text.Replace("`r`n", "`n").Replace("`r", "`n")
            $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($normalizedText)
            $entryStream.Write($bytes, 0, $bytes.Length)
        }
        else {
            $inputStream = [System.IO.File]::OpenRead($SourcePath)
            try {
                $inputStream.CopyTo($entryStream)
            }
            finally {
                $inputStream.Dispose()
            }
        }
    }
    finally {
        $entryStream.Dispose()
    }
}

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
        Add-DeterministicZipEntry $archive $releaseDll "BrakeFilter.dll"
        Add-DeterministicZipEntry $archive $metadataFile "metadata.json" -NormalizeText
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    $zipStream.Dispose()
}

$readStream = [System.IO.File]::OpenRead($releaseZip)
try {
    $package = [System.IO.Compression.ZipArchive]::new(
        $readStream,
        [System.IO.Compression.ZipArchiveMode]::Read,
        $false)
    try {
        $entryNames = @($package.Entries | ForEach-Object FullName | Sort-Object)
        if ($entryNames.Count -ne 2 -or
            $entryNames[0] -ne "BrakeFilter.dll" -or
            $entryNames[1] -ne "metadata.json") {
            throw "Release ZIP must contain only BrakeFilter.dll and metadata.json at its top level."
        }
    }
    finally {
        $package.Dispose()
    }
}
finally {
    $readStream.Dispose()
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
Write-Output "OTD metadata: $metadataFile"
Write-Output "Checksums: $checksumFile"
Get-FileHash -LiteralPath $releaseDll, $releaseZip, $checksumFile -Algorithm SHA256 |
    Select-Object Path, Hash
