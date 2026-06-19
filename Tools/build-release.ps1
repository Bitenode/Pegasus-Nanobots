# Builds a mod.io-ready zip with all required mod root folders.
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$modRoot = Split-Path $PSScriptRoot -Parent

if (-not $Version) {
    $metaPath = Join-Path $modRoot 'metadata.mod'
    if (Test-Path $metaPath) {
        $xml = [xml](Get-Content $metaPath -Raw)
        $Version = $xml.ModMetadata.ModVersion
    }
    if (-not $Version) { $Version = '0.0.0' }
}

$zipName = "PegasusNanobots_$Version.zip"
$zipPath = Join-Path $modRoot $zipName
$staging = Join-Path $env:TEMP ("PegasusNanobots_$Version_" + [guid]::NewGuid().ToString('N'))

$includes = @(
    'metadata.mod',
    'Data',
    'Models',
    'Sounds',
    'Textures'
)

New-Item -ItemType Directory -Path $staging -Force | Out-Null
try {
    foreach ($item in $includes) {
        $source = Join-Path $modRoot $item
        if (-not (Test-Path $source)) {
            throw "Missing required mod content: $item"
        }
        Copy-Item -Path $source -Destination (Join-Path $staging $item) -Recurse -Force
    }

    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zipPath -Force

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    $entries = $zip.Entries.Count
    $hasModels = ($zip.Entries | Where-Object { $_.FullName -like 'Models*' }).Count
    $hasSounds = ($zip.Entries | Where-Object { $_.FullName -like 'Sounds*' }).Count
    $hasTextures = ($zip.Entries | Where-Object { $_.FullName -like 'Textures*' }).Count
    $zip.Dispose()

    Write-Output "Created: $zipPath"
    Write-Output "Size: $((Get-Item $zipPath).Length) bytes"
    Write-Output "Entries: $entries (Models: $hasModels, Sounds: $hasSounds, Textures: $hasTextures)"
}
finally {
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
}
