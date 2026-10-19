$ErrorActionPreference = 'Stop'
$projectRoot = 'E:/DD451'
$stageRoot = Join-Path $projectRoot 'Docs/Verification/Enemy/AlertFill/AIStaging'
$integrationRoot = Join-Path $projectRoot 'Docs/Verification/Enemy/AlertFill/Integration'
$releasePhrase = '视觉Assets和Editor已释放，009可以接入'
foreach ($record in @('Docs/Handoffs/Effects_DroneAlertFill.txt', 'Docs/Verification/Enemy/EditorCoordination.txt'))
{
    if (!(Test-Path -LiteralPath (Join-Path $projectRoot $record)) -or
        !(Get-Content -LiteralPath (Join-Path $projectRoot $record) -Raw).Contains($releasePhrase))
    {
        throw "Missing explicit visual release: $record"
    }
}
$manifest = Get-Content -LiteralPath (Join-Path $stageRoot 'StagedManifest.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest)
{
    $source = Join-Path $stageRoot $entry.path
    $destination = Join-Path $projectRoot $entry.path
    if ((Get-FileHash -LiteralPath $source).Hash -ne $entry.stagedHash) { throw "Staged file changed: $($entry.path)" }
    if ($entry.newFile)
    {
        if (Test-Path -LiteralPath $destination) { throw "New file now exists: $($entry.path)" }
    }
    elseif ((Get-FileHash -LiteralPath $destination).Hash -ne $entry.currentHash)
    {
        throw "Actual source changed since review: $($entry.path)"
    }
}
$backupRoot = Join-Path $integrationRoot 'Before'
if (Test-Path -LiteralPath $backupRoot) { throw 'Do not overwrite an existing deployment backup.' }
$backupHashes = foreach ($entry in $manifest)
{
    if ($entry.newFile) { continue }
    foreach ($relative in @($entry.path, ($entry.path + '.meta')))
    {
        $source = Join-Path $projectRoot $relative
        $backup = Join-Path $backupRoot $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $backup) | Out-Null
        Copy-Item -LiteralPath $source -Destination $backup
        [pscustomobject]@{ path = $relative; hash = (Get-FileHash -LiteralPath $source).Hash }
    }
}
$backupHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $integrationRoot 'BeforeHashes.json') -Encoding UTF8
foreach ($entry in $manifest)
{
    Copy-Item -LiteralPath (Join-Path $stageRoot $entry.path) -Destination (Join-Path $projectRoot $entry.path)
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $integrationRoot 'DeployedManifest.json') -Encoding UTF8
Write-Output "Deployed $($manifest.Count) owned files after actual-content/meta backup; visual files untouched."
