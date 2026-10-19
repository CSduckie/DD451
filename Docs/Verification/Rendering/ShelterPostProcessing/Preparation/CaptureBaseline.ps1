$ErrorActionPreference = 'Stop'
$projectRoot = 'E:/DD451'
$verificationRoot = Join-Path $projectRoot 'Docs/Verification/Rendering/ShelterPostProcessing'
$coordinationText = Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'Docs/Verification/Enemy/EditorCoordination.txt')
if (-not $coordinationText.Contains('009 NIGHT DEFAULT EXPLICIT RELEASE TO SHELTER POSTPROCESS')) {
    throw '009 explicit release is absent; do not capture the final baseline yet.'
}
$baselinePath = Join-Path $verificationRoot 'BeforeHashes.json'
if (Test-Path -LiteralPath $baselinePath) { throw 'Final baseline already exists; do not overwrite.' }
$files = @(& rg --files Assets/Scenes Assets/Prefabs Assets/Settings Assets/Scripts Assets/Tests Assets/Art/Materials/Environment ProjectSettings Packages)
if ($LASTEXITCODE -ne 0) { throw 'Protected file inventory failed.' }
$entries = foreach ($relativePath in ($files | Sort-Object -Unique)) {
    $absolutePath = Join-Path $projectRoot $relativePath
    [ordered]@{ path = $relativePath.Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $absolutePath -Algorithm SHA256).Hash }
}
$beforeRoot = Join-Path $verificationRoot 'Before'
$copyPaths = @('Assets/Scenes/Shelter.unity', 'Assets/Scenes/Shelter.unity.meta', 'Assets/Scenes/Shelter/Global Volume Profile.asset', 'Assets/Scenes/Shelter/Global Volume Profile.asset.meta')
foreach ($relativePath in $copyPaths) {
    $destinationPath = Join-Path $beforeRoot $relativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $destinationPath) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot $relativePath) -Destination $destinationPath
}
[ordered]@{ capturedUtc = [DateTime]::UtcNow.ToString('o'); files = @($entries) } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $baselinePath -Encoding utf8
Write-Output ('Captured {0} protected files and 4 original asset/meta copies.' -f $entries.Count)
