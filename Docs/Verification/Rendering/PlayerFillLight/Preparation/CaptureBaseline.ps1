$ErrorActionPreference = 'Stop'
$taskRoot = 'E:\DD451'
$taskOut = Join-Path $taskRoot 'Docs\Verification\Rendering\PlayerFillLight'
$taskManifest = Join-Path $taskOut 'BeforeHashes.json'
if (Test-Path -LiteralPath $taskManifest) { throw 'Baseline already exists; do not overwrite.' }
$taskPaths = @(& rg --files Assets/Scripts Assets/Scenes Assets/Prefabs Assets/Data Assets/Settings Assets/Art/Materials Assets/Tests ProjectSettings Packages)
$taskRows = foreach ($taskPath in ($taskPaths | Sort-Object -Unique)) {
    [pscustomobject]@{ path=$taskPath.Replace('\','/'); sha256=(Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash }
}
[pscustomobject]@{ capturedUtc=[DateTime]::UtcNow.ToString('o'); files=$taskRows } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $taskManifest -Encoding utf8
$taskBefore = Join-Path $taskOut 'Before'
New-Item -ItemType Directory -Path $taskBefore -Force | Out-Null
Copy-Item -LiteralPath 'Assets/Prefabs/Player/Player.prefab' -Destination (Join-Path $taskBefore 'Player.prefab')
Copy-Item -LiteralPath 'Assets/Prefabs/Player/Player.prefab.meta' -Destination (Join-Path $taskBefore 'Player.prefab.meta')
Copy-Item -LiteralPath 'ProjectSettings/TagManager.asset' -Destination (Join-Path $taskBefore 'TagManager.asset')
[pscustomobject]@{manifest=$taskManifest;count=$taskRows.Count} | ConvertTo-Json
