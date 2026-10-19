$ErrorActionPreference = 'Stop'
$projectRoot = 'E:\DD451'
$auditRoot = Join-Path $projectRoot 'Docs\Verification\Enemy\TurningVision'
$initialSnapshot = Join-Path $auditRoot 'InitialCapturedHashes-20261019T080703.json'
$finalSnapshot = Join-Path $auditRoot 'FinalProtectionAudit.json'
if (!(Test-Path -LiteralPath $initialSnapshot)) {
    Copy-Item -LiteralPath $finalSnapshot -Destination $initialSnapshot
}
$started = [DateTime]::UtcNow.ToString('o')
$before = @(Get-Content -LiteralPath (Join-Path $auditRoot 'BeforeHashes.json') -Raw | ConvertFrom-Json)
$latestScenes = @(Get-Content -LiteralPath (Join-Path $auditRoot 'LatestUserSceneHashes.json') -Raw | ConvertFrom-Json)
$sceneManifest = @{}
foreach ($entry in $latestScenes) { $sceneManifest[$entry.Path] = $entry.Hash }
$controllerPath = Join-Path $projectRoot 'Assets\Scripts\Enemy\Drone\MonoBehaviour\DroneController.cs'
$shelterPath = Join-Path $projectRoot 'Assets\Scenes\Shelter.unity'
$preservedScene = Join-Path $auditRoot 'Before\LatestPreserved-20261019T080721120.unity'
$preservedHash = (Get-FileHash -LiteralPath $preservedScene -Algorithm SHA256).Hash
$rows = @(
    foreach ($entry in $before) {
        $relative = $entry.Path.Substring($projectRoot.Length + 1)
        $file = Get-Item -LiteralPath $entry.Path
        $currentHash = (Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash
        $metaPath = $entry.Path + '.meta'
        $metaHash = (Get-FileHash -LiteralPath $metaPath -Algorithm SHA256).Hash
        $beforeMetaPath = Join-Path $auditRoot ('Before\' + $relative + '.meta')
        $beforeMetaHash = (Get-FileHash -LiteralPath $beforeMetaPath -Algorithm SHA256).Hash
        $expectedHash = $entry.Hash
        $baselineSource = 'BeforeHashes.json'
        if ($sceneManifest.ContainsKey($entry.Path)) {
            $expectedHash = $sceneManifest[$entry.Path]
            $baselineSource = 'LatestUserSceneHashes.json'
        }
        $effectiveHash = $expectedHash
        $effectiveSource = $baselineSource
        if ($entry.Path -eq $shelterPath) {
            $effectiveHash = $preservedHash
            $effectiveSource = 'Before/LatestPreserved-20261019T080721120.unity (latest coordinated save; provenance below)'
        }
        [pscustomobject]@{
            Path = $entry.Path
            CurrentHash = $currentHash
            ExpectedHash = $expectedHash
            BaselineSource = $baselineSource
            MatchesBaseline = $currentHash -eq $expectedHash
            MatchesOriginalBefore = $currentHash -eq $entry.Hash
            EffectiveProtectionHash = $effectiveHash
            EffectiveProtectionSource = $effectiveSource
            MatchesEffectiveProtection = $currentHash -eq $effectiveHash
            LastWriteTimeUtc = $file.LastWriteTimeUtc.ToString('o')
            MetaHash = $metaHash
            MetaMatchesBefore = $metaHash -eq $beforeMetaHash
            Guid = ((Select-String -LiteralPath $metaPath -Pattern '^guid:').Line -replace '^guid:\s*','')
            ObservedUtc = [DateTime]::UtcNow.ToString('o')
        }
    }
)
$testPath = Join-Path $projectRoot 'Assets\Tests\Enemy\PlayMode\DroneTurningVisionTests.cs'
$newTest = [pscustomobject]@{
    Path = $testPath
    Hash = (Get-FileHash -LiteralPath $testPath -Algorithm SHA256).Hash
    MetaHash = (Get-FileHash -LiteralPath ($testPath + '.meta') -Algorithm SHA256).Hash
    Guid = ((Select-String -LiteralPath ($testPath + '.meta') -Pattern '^guid:').Line -replace '^guid:\s*','')
    LastWriteTimeUtc = (Get-Item -LiteralPath $testPath).LastWriteTimeUtc.ToString('o')
    ObservedUtc = [DateTime]::UtcNow.ToString('o')
}
$sceneCopies = @(
    Get-ChildItem -LiteralPath (Join-Path $auditRoot 'Before') -Filter '*.unity' -Recurse | ForEach-Object {
        [pscustomobject]@{ Path = $_.FullName; Hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    }
)
$intermediateHash = '19AE02119BB1A0F75F22E21D870F98BA1878D7C51BD1CF113C35E2286C2BA9E5'
$snapshot = [pscustomobject]@{
    Task = '09H TurningVision'
    CaptureStartedUtc = $started
    CaptureCompletedUtc = [DateTime]::UtcNow.ToString('o')
    Method = 'File-only SHA256; latest scene manifest takes priority over original baseline, with latest coordinated Shelter saved copy recorded separately. No Unity calls, no Assets writes, no tests rerun.'
    InitialSnapshotPreservedAt = $initialSnapshot
    ExpectedChangedProductionPath = $controllerPath
    ExistingFiles = $rows
    NewTest = $newTest
    ExistingMetaMatchCount = @($rows | Where-Object MetaMatchesBefore).Count
    ManifestMatchCount = @($rows | Where-Object MatchesBaseline).Count
    EffectiveProtectionMatchCount = @($rows | Where-Object MatchesEffectiveProtection).Count
    BaselineCount = $rows.Count
    ChangedExistingPathsVsManifest = @($rows | Where-Object { !$_.MatchesBaseline } | ForEach-Object Path)
    UnexpectedChangedExistingPaths = @($rows | Where-Object { !$_.MatchesEffectiveProtection -and $_.Path -ne $controllerPath } | ForEach-Object Path)
    LatestShelterSave = [pscustomobject]@{
        PreservedCopy = $preservedScene
        PreservedHash = $preservedHash
        CurrentMatchesPreservedCopy = ($rows | Where-Object Path -eq $shelterPath).CurrentHash -eq $preservedHash
        Provenance = 'Parent reports fresh Shelter was clean at approximately 08:06 UTC; an OpenScene namespace typo left a later brief enter/exit Play unguarded in Shelter. After exiting, dirty=true. At 08:07:21 parent saved scene-as-copy, then original scene, then opened EnemyTest. Dirty state could relate to ongoing edits or Play; cause is not established by this file audit.'
        PreviousObservedHash = $intermediateHash
        PreviousObservedLastWriteUtc = '2026-10-19T08:06:18.5474234Z'
        PreviousObservationSource = 'InitialCapturedHashes-20261019T080703.json'
        MatchingPreviousByteCopiesInTurningVisionBefore = @($sceneCopies | Where-Object Hash -eq $intermediateHash | ForEach-Object Path)
        DiffLimit = 'No matching byte copy of the 08:06:18 version was found under TurningVision/Before; its SHA256 proves change but cannot reconstruct a line diff.'
        EarlierAvailableByteComparison = 'Against Before/UserScene-20261019T080122148.unity (08:01:22, FF7FDB...), five YAML values differ: object 1834287629 active 0->1, associated Light enabled 0->1, color {0,0.23602962,0.41509432,1}->{0,0,0,1}, intensity 0.1->7.29; Transform of GameObject 1380149357730246 local Y 0.41->-0.09. This older comparison cannot attribute which changes occurred at 08:06 or 08:07.'
    }
}
$snapshot | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $finalSnapshot -Encoding utf8

$sources = @()
$allCases = @()
foreach ($sourceName in @('AfterFix-9-First.xml','Related-36.xml')) {
    $sourcePath = Join-Path $auditRoot $sourceName
    [xml]$xml = Get-Content -LiteralPath $sourcePath -Raw
    $run = $xml.'test-run'
    $sources += [pscustomobject]@{
        File = $sourceName; Path = $sourcePath
        Sha256 = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
        StartUtc = $run.'start-time'; EndUtc = $run.'end-time'
        Result = $run.result
        Total = [int]$run.total; Passed = [int]$run.passed; Failed = [int]$run.failed; Skipped = [int]$run.skipped
    }
    foreach ($case in $xml.SelectNodes('//test-case')) {
        $failureNode = $case.SelectSingleNode('failure/message')
        $message = if ($null -eq $failureNode) { $null } else { $failureNode.InnerText }
        $allCases += [pscustomobject]@{
            FullName = $case.fullname
            Result = $case.result
            Source = $sourceName
            StartUtc = $case.'start-time'
            EndUtc = $case.'end-time'
            DurationSeconds = [double]$case.duration
            FailureMessage = $message
        }
    }
}
$selectedPassed = @($allCases | Where-Object Result -eq 'Passed' | Sort-Object FullName)
$distinctPassed = @($selectedPassed | Select-Object -ExpandProperty FullName -Unique)
$latestByName = @{}
foreach ($case in $allCases) { $latestByName[$case.FullName] = $case }
$historicalFailures = @(
    foreach ($case in ($allCases | Where-Object Result -ne 'Passed')) {
        [pscustomobject]@{
            FullName = $case.FullName; Source = $case.Source; Result = $case.Result
            FailureMessage = $case.FailureMessage
            SupersededBy = $latestByName[$case.FullName]
        }
    }
)
$editPath = Join-Path $auditRoot 'EditMode-4.json'
$editData = (Get-Content -LiteralPath $editPath -Raw | ConvertFrom-Json).structuredContent.result.data
if ($sources[0].Passed -ne 8 -or $sources[1].Passed -ne 36 -or $distinctPassed.Count -ne 44 -or $selectedPassed.Count -ne 44) { throw 'Unexpected PlayMode evidence counts' }
if ($editData.result.summary.total -ne 4 -or $editData.result.summary.passed -ne 4) { throw 'Unexpected EditMode evidence counts' }
$coverage = [pscustomobject]@{
    Task = '09H TurningVision'
    GeneratedUtc = [DateTime]::UtcNow.ToString('o')
    EvidenceOnlyNoTestsRerun = $true
    IsSingleRun = $false
    SelectionRule = 'Select the 8 Passed test-case elements from AfterFix-9-First.xml and all 36 Passed elements from Related-36.xml. Exact fullname is identity; the passing sets have no overlap. The one earlier failed test is retained below and was passed in Related-36.xml. A four-variant coroutine remains one test-case.'
    Summary = [pscustomobject]@{
        DistinctPassed = $distinctPassed.Count
        SelectedPassedExecutions = $selectedPassed.Count
        DuplicateSelectedPassedNames = $selectedPassed.Count - $distinctPassed.Count
        RawExecutionsAcrossSources = $allCases.Count
        HistoricalFailedExecutions = $historicalFailures.Count
        DistinctCasesAcrossSources = $latestByName.Count
        LatestFailed = @($latestByName.Values | Where-Object Result -ne 'Passed').Count
    }
    Sources = $sources
    SelectedPassedTests = $selectedPassed
    HistoricalFailures = $historicalFailures
    EditMode = [pscustomobject]@{
        Source = 'EditMode-4.json'; Path = $editPath
        Sha256 = (Get-FileHash -LiteralPath $editPath -Algorithm SHA256).Hash
        JobId = $editData.job_id; Status = $editData.status
        StartUtc = [DateTimeOffset]::FromUnixTimeMilliseconds($editData.started_unix_ms).UtcDateTime.ToString('o')
        EndUtc = [DateTimeOffset]::FromUnixTimeMilliseconds($editData.finished_unix_ms).UtcDateTime.ToString('o')
        Summary = $editData.result.summary
        Tests = $editData.result.results
    }
}
$coverage | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath (Join-Path $auditRoot 'PlayMode-FinalCoverage.json') -Encoding utf8

$report = @"
09H TurningVision final file audit
Capture UTC: $($snapshot.CaptureStartedUtc) through $($snapshot.CaptureCompletedUtc)
File-only audit. No Unity calls, Assets writes, or test reruns.

Baseline scope: 19 existing files from BeforeHashes.json; LatestUserSceneHashes.json has scene priority. Original manifests are unchanged.
19/19 existing meta files match retained Before copies. 17/19 existing file hashes match the manifests. The two manifest differences are expected DroneController.cs and the explicitly recorded later Shelter save. With the latest preserved Shelter copy as the current protection reference, all 18 protected existing files other than the intentionally changed Controller match. Unexpected differences: $($snapshot.UnexpectedChangedExistingPaths.Count).

Controller SHA256: $(($rows | Where-Object Path -eq $controllerPath).CurrentHash)
Reviewed change: freeze smooth yaw updates while Alert, and clear turnVelocity when entering Alert. Perception continues to run. New test file: $testPath
New test SHA256: $($newTest.Hash)
New test GUID: $($newTest.Guid)
Drone/Player prefabs, DroneConfig.asset, 02C PlayerController/PlayerConfigSO/PlayerFallState, all other listed production files and existing tests match their 09H baseline. Ruins, Bootstrap, and EnemyTest match the latest applicable manifest.

Shelter latest SHA256: $preservedHash
Current Shelter equals Before/LatestPreserved-20261019T080721120.unity byte-for-byte by SHA256. Parent reports clean Shelter at about 08:06, then an unintended brief enter/exit Play due to an OpenScene namespace typo; dirty=true after stopping. Parent saved a copy and original at 08:07:21 before opening EnemyTest. This is a known save during the session; the cause of dirty state is not established and is not attributed to an external editor.
Initial 08:07:03 snapshot is retained in InitialCapturedHashes-20261019T080703.json. It observed the 08:06:18 Shelter SHA256 $intermediateHash. No corresponding full byte copy was found under TurningVision/Before, so a direct line diff against that intermediate version is unavailable.
Against the available 08:01:22 copy FF7FDB..., only five YAML values differ: object 1834287629 active 0->1; its Light enabled 0->1, color (0,.23602962,.41509432,1)->(0,0,0,1), intensity .1->7.29; Transform for GameObject 1380149357730246 local Y .41->-.09. These cannot be assigned specifically to the 08:06 or 08:07 save. No scene file was changed by this audit.

PlayMode coverage: 44 distinct passed test cases assembled from two saved result files, not one 44-case run. AfterFix-9-First.xml contributes 8 passed; Related-36.xml contributes 36 passed; exact fullname sets have no overlap. Across the raw files there are 45 executions, including one historical failure of MidTurnDiscoveryStillRespondsToRealLossAndCoverTimeout subsequently passed in Related-36.xml. Latest per-case results are all Passed. Per-case source mapping, source hashes/times, and the superseded failure are in PlayMode-FinalCoverage.json.
EditMode: EditMode-4.json reports 4/4 passed; its source hash, job ID, timestamps, and individual names are included in the same coverage JSON. These statements describe retained test evidence and do not claim a rerun against any later rendering or scene edits.
"@
$report | Set-Content -LiteralPath (Join-Path $auditRoot 'FinalProtectionAudit.txt') -Encoding utf8
[pscustomobject]@{SnapshotUtc=$snapshot.CaptureCompletedUtc; MetaMatches=$snapshot.ExistingMetaMatchCount; ManifestMatches=$snapshot.ManifestMatchCount; EffectiveMatches=$snapshot.EffectiveProtectionMatchCount; UnexpectedChanges=$snapshot.UnexpectedChangedExistingPaths; ShelterHash=$preservedHash; Coverage=$coverage.Summary; EditMode=$coverage.EditMode.Summary} | ConvertTo-Json -Depth 7
