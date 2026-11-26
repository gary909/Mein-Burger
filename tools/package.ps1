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
"@ | Set-Content -LiteralPath (Join-Path $dist "README.txt") -Encoding UTF8

if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}
Compress-Archive -Path (Join-Path $dist "*") -DestinationPath $zip -CompressionLevel Optimal

$size = (Get-ChildItem -LiteralPath $dist -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host "Runtime package: $dist"
Write-Host "Files: $((Get-ChildItem -LiteralPath $dist -Recurse -File | Measure-Object).Count)"
Write-Host "Uncompressed size: $([Math]::Round($size / 1MB, 2)) MB"
Write-Host "Zip: $zip ($([Math]::Round((Get-Item -LiteralPath $zip).Length / 1MB, 2)) MB)"
