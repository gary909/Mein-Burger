param(
    [string]$OutputDirectory = "dist",
    [string]$ZipPath = "mein-burger-runtime.zip"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root $OutputDirectory
$zip = Join-Path $root $ZipPath

if (Test-Path -LiteralPath $dist) {
    Remove-Item -LiteralPath $dist -Recurse -Force
}

New-Item -ItemType Directory -Path (Join-Path $dist "data") -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root "index.html") -Destination $dist
Copy-Item -LiteralPath (Join-Path $root "data/questions.js") -Destination (Join-Path $dist "data")
Copy-Item -LiteralPath (Join-Path $root "data/vocabulary.js") -Destination (Join-Path $dist "data")
Copy-Item -LiteralPath (Join-Path $root "data/i18n.js") -Destination (Join-Path $dist "data")
Copy-Item -LiteralPath (Join-Path $root "data/images") -Destination (Join-Path $dist "data") -Recurse

@"
Mein-Burger runtime package
Open index.html in a browser. Keep the data folder next to it.
No server or build step is required.

Sources and licenses
- Questions: official BAMF catalogue "Gesamtfragenkatalog zum Test Leben in Deutschland und zum Einbürgerungstest", Stand 07.05.2025.
  https://www.bamf.de/SharedDocs/Anlagen/DE/Integration/Einbuergerung/gesamtfragenkatalog-lebenindeutschland.html
- Answers, English translations, explanations and remaining images: abdullahbutt/leben-in-deutschland-test, licensed CC BY-NC 4.0 (attribution, non-commercial use).
- The state coat-of-arms and state map images, plus selected question images, are redrawn replacements for this project.
- BB01 is corrected to Bild 1 according to the official BAMF page.
- This is an independent learning app, not an official BAMF product.
"@ | Set-Content -LiteralPath (Join-Path $dist "README.txt") -Encoding UTF8

if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem -LiteralPath $dist -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($dist.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive,
            $_.FullName,
            $relative,
            [System.IO.Compression.CompressionLevel]::Optimal
        ) | Out-Null
    }
}
finally {
    $archive.Dispose()
}

$size = (Get-ChildItem -LiteralPath $dist -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host "Runtime package: $dist"
Write-Host "Files: $((Get-ChildItem -LiteralPath $dist -Recurse -File | Measure-Object).Count)"
Write-Host "Uncompressed size: $([Math]::Round($size / 1MB, 2)) MB"
Write-Host "Zip: $zip ($([Math]::Round((Get-Item -LiteralPath $zip).Length / 1MB, 2)) MB)"
