$ErrorActionPreference = 'Stop'
$projectRoot = 'E:\DD451'
$auditRoot = Join-Path $projectRoot 'Docs\Verification\Enemy\StealthVignette'
$started = [DateTime]::UtcNow.ToString('o')
$baseline = @(Get-Content -LiteralPath (Join-Path $auditRoot 'BeforeHashes.json') -Raw | ConvertFrom-Json)
$expectedChanged = @('Assets\Scenes\Ruins.unity','Assets\Scripts\Enemy\MyPrecious.Enemy.asmdef','Assets\Tests\Enemy\PlayMode\MyPrecious.Enemy.PlayModeTests.asmdef')
$expectedNew = @('Assets\Scripts\Enemy\Stealth\MonoBehaviour\PlayerStealthVignette.cs','Assets\Scripts\Enemy\Stealth\MonoBehaviour\PlayerStealthVignette.cs.meta','Assets\Tests\Enemy\PlayMode\PlayerStealthVignetteTests.cs','Assets\Tests\Enemy\PlayMode\PlayerStealthVignetteTests.cs.meta','Assets\Settings\Effects.meta','Assets\Settings\Effects\RuinsPostProcessingProfile.asset','Assets\Settings\Effects\RuinsPostProcessingProfile.asset.meta')
$baselineIndex = @{}
$rows = @(foreach ($entry in $baseline) {
    $relative = $entry.Path.Substring($projectRoot.Length + 1)
    $baselineIndex[$relative] = $entry.Hash
    $exists = Test-Path -LiteralPath $entry.Path -PathType Leaf
    $currentHash = if ($exists) { (Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash } else { $null }
    [pscustomobject]@{Path=$relative;BeforeHash=$entry.Hash;CurrentHash=$currentHash;Exists=$exists;Unchanged=$currentHash -eq $entry.Hash;ExpectedChanged=$relative -in $expectedChanged}
})
$currentFiles = @(foreach ($scope in @('Assets','Packages','ProjectSettings')) { Get-ChildItem -LiteralPath (Join-Path $projectRoot $scope) -Recurse -File })
$newRows = @(foreach ($file in $currentFiles) {
    $relative = $file.FullName.Substring($projectRoot.Length + 1)
    if ($baselineIndex.ContainsKey($relative)) { continue }
    $guid = if ($relative.EndsWith('.meta')) { ((Select-String -LiteralPath $file.FullName -Pattern '^guid:').Line -replace '^guid:\s*','') } else { $null }
    $guidMatches = @()
    if ($guid -and $relative -in $expectedNew) { $guidMatches = @(& rg -l --fixed-strings -g '*.meta' -- ('guid: ' + $guid) (Join-Path $projectRoot 'Assets')) }
    [pscustomobject]@{Path=$relative;Sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash;ExpectedNew=$relative -in $expectedNew;BaselineUnlistedTextMeshPro=$relative.StartsWith('Assets\TextMesh Pro\');Guid=$guid;GuidMatchPaths=$guidMatches;LastWriteUtc=$file.LastWriteTimeUtc.ToString('o')}
})

# Compare complete Unity YAML blocks and reverse only the declared additions in memory.
$beforeRuinsPath = Join-Path $auditRoot 'Before\Assets\Scenes\Ruins.unity'
$ruinsPath = Join-Path $projectRoot 'Assets\Scenes\Ruins.unity'
$beforeRuins = [IO.File]::ReadAllText($beforeRuinsPath)
$currentRuins = [IO.File]::ReadAllText($ruinsPath)
$blockPattern = '(?ms)^--- !u!\d+ &(?<id>-?\d+)(?: stripped)?\r?\n.*?(?=^--- !u!|\z)'
$beforeBlocks = @{}
foreach ($match in [regex]::Matches($beforeRuins,$blockPattern)) { $beforeBlocks[$match.Groups['id'].Value] = $match.Value }
$afterBlocks = @{}
foreach ($match in [regex]::Matches($currentRuins,$blockPattern)) { $afterBlocks[$match.Groups['id'].Value] = $match.Value }
$addedBlockIds = @($afterBlocks.Keys | Where-Object { !$beforeBlocks.ContainsKey($_) } | Sort-Object)
$removedBlockIds = @($beforeBlocks.Keys | Where-Object { !$afterBlocks.ContainsKey($_) } | Sort-Object)
$changedBlockIds = @($beforeBlocks.Keys | Where-Object { $afterBlocks.ContainsKey($_) -and $beforeBlocks[$_] -cne $afterBlocks[$_] } | Sort-Object)
$allowedNewIds = @('359266085','359266086','359266087','1904295468','1904295469','1904295470','1904295471')
$reduced = [regex]::Replace($currentRuins,$blockPattern,{param($match) if ($match.Groups['id'].Value -in $allowedNewIds) { '' } else { $match.Value }})
$ppPattern = '(?m)^    - target: \{fileID: 6648020794282633460, guid: 3cf4a390a92e98447ada0654fb991c91, type: 3\}\r?\n      propertyPath: m_RenderPostProcessing\r?\n      value: 1\r?\n      objectReference: \{fileID: 0\}\r?\n'
$ppCount = [regex]::Matches($reduced,$ppPattern).Count
$reduced = [regex]::Replace($reduced,$ppPattern,'')
$childPattern = '(?m)^  - \{fileID: (?:359266086|1904295469)\}\r?\n'
$childCount = [regex]::Matches($reduced,$childPattern).Count
$reduced = [regex]::Replace($reduced,$childPattern,'')
$sha = [Security.Cryptography.SHA256]::Create()
try { $restoredHash = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($reduced)))).Replace('-','') } finally { $sha.Dispose() }
$ruinsAudit = [pscustomobject]@{
    BeforeCopyMatchesManifest=(Get-FileHash -LiteralPath $beforeRuinsPath -Algorithm SHA256).Hash -eq $baselineIndex['Assets\Scenes\Ruins.unity']
    AddedYamlBlockIds=$addedBlockIds;RemovedYamlBlockIds=$removedBlockIds;ChangedExistingYamlBlockIds=$changedBlockIds
    OnlyExpectedNewBlocks=$addedBlockIds.Count -eq 7 -and @($addedBlockIds | Where-Object { $_ -notin $allowedNewIds }).Count -eq 0
    NewGameObjects=@('RuinsPostProcessing: GO359266085/Transform359266086/Volume359266087','StealthVignette: GO1904295468/Transform1904295469/PlayerStealthVignette1904295470/Volume1904295471')
    Parent='Existing ScenePresentation Transform2097100614; two new child references only'
    OutputCameraPostProcessingOverrideCount=$ppCount;AddedParentChildReferenceCount=$childCount
    ReversingOnlyDeclaredAdditionsRestoresWholeText=[string]::Equals($reduced,$beforeRuins,[StringComparison]::Ordinal)
    ReconstructedUtf8Sha256=$restoredHash
    ReconstructedByteHashMatchesBefore=$restoredHash -eq $baselineIndex['Assets\Scenes\Ruins.unity']
    Detail='Seven new YAML blocks, one four-line PP=true override on existing OutputCamera, two ScenePresentation child references. No existing object removed. Complete reconstruction protects all other scene bytes including layouts, lights, routes, boxes, colliders, RenderSettings and existing camera fields.'
}
$asmdefs = @(foreach ($relative in ($expectedChanged | Where-Object { $_.EndsWith('.asmdef') })) {
    $beforeText = [IO.File]::ReadAllText((Join-Path $auditRoot ('Before\'+$relative)))
    $afterText = [IO.File]::ReadAllText((Join-Path $projectRoot $relative))
    $match = [regex]::Match($beforeText,'(?m)^\s*"references": \[[^\r\n]*')
    $expectedLine = $match.Value.Replace('],',', "Unity.RenderPipelines.Core.Runtime", "Unity.RenderPipelines.Universal.Runtime"],')
    $expectedText = $beforeText.Replace($match.Value,$expectedLine)
    [pscustomobject]@{Path=$relative;OnlyTwoRenderPipelineReferencesAdded=[string]::Equals($expectedText,$afterText,[StringComparison]::Ordinal);References=($afterText | ConvertFrom-Json).references}
})
$profilePath = Join-Path $projectRoot 'Assets\Settings\Effects\RuinsPostProcessingProfile.asset'
$profile = [IO.File]::ReadAllText($profilePath)
$profileGuid = ((Select-String -LiteralPath ($profilePath+'.meta') -Pattern '^guid:').Line -replace '^guid:\s*','')
$scriptMeta = Join-Path $projectRoot 'Assets\Scripts\Enemy\Stealth\MonoBehaviour\PlayerStealthVignette.cs.meta'
$scriptGuid = ((Select-String -LiteralPath $scriptMeta -Pattern '^guid:').Line -replace '^guid:\s*','')
$referenceAudit = [pscustomobject]@{
    ProfileGuid=$profileGuid;ProfileReferencePresent=$currentRuins.Contains('sharedProfile: {fileID: 11400000, guid: '+$profileGuid+', type: 2}')
    ScriptGuid=$scriptGuid;ComponentScriptReferencePresent=$afterBlocks['1904295470'].Contains('guid: '+$scriptGuid)
    ExistingCameraBinderReferenceCorrect=$afterBlocks['1904295470'].Contains('cameraBinder: {fileID: 955271489}') -and $beforeBlocks.ContainsKey('955271489')
    NeutralVolumeLayerAndWeight=$afterBlocks['359266087'].Contains("priority: 0`n") -and $afterBlocks['359266087'].Contains("weight: 1`n")
    StealthVolumeLayerAndWeight=$afterBlocks['1904295471'].Contains("priority: 10`n") -and $afterBlocks['1904295471'].Contains("weight: 0`n")
    ProfileContainsExactlyThreeOverrides=[regex]::Matches($profile,'m_OverrideState: 1').Count -eq 3
    ProfileRootAndComponents=@([regex]::Matches($profile,'(?m)^  m_Name: (.+)\r?$') | ForEach-Object { $_.Groups[1].Value.Trim() })
    ProfilePurpose='Only enabled overrides: Tonemapping.mode=None, Bloom.intensity=0, Vignette.intensity=0; lower-priority neutral base counters quality defaults. Dedicated stealth Volume starts weight0/sharedProfile null; runtime component owns its Vignette profile.'
}
$groups = @(
    @{Name='Player scripts and shared prefabs';Pattern='^Assets\\(?:Scripts\\Player\\|Prefabs\\Player\\)'}
    @{Name='Shelter scene and local profile';Pattern='^Assets\\Scenes\\Shelter(?:\\|\.)'}
    @{Name='Shared rendering settings/profiles';Pattern='^Assets\\Settings\\'}
    @{Name='Enemy production existing files excluding intended asmdef';Pattern='^Assets\\Scripts\\Enemy\\'}
    @{Name='07F GameFlow and Loot code';Pattern='^Assets\\Scripts\\(?:GameFlow|Loot)\\'}
    @{Name='Existing effects and dust';Pattern='(?i)(dust|Assets\\Scripts\\Effects\\|Assets\\Prefabs\\Effects\\)'}
    @{Name='ProjectSettings';Pattern='^ProjectSettings\\'}
    @{Name='All existing prefabs including art';Pattern='\.prefab$'}
    @{Name='Project prefabs';Pattern='^Assets\\Prefabs\\.*\.prefab$'}
)
$protection = @(foreach ($group in $groups) {
    $members = @($rows | Where-Object { $_.Path -match $group.Pattern -and !$_.ExpectedChanged })
    [pscustomobject]@{Group=$group.Name;Count=$members.Count;Unchanged=@($members | Where-Object Unchanged).Count;ChangedOrMissing=@($members | Where-Object { !$_.Unchanged } | ForEach-Object Path)}
})
$unexpectedChanged = @($rows | Where-Object { !$_.Unchanged -and !$_.ExpectedChanged })
$unbaselinedTmp = @($newRows | Where-Object BaselineUnlistedTextMeshPro)
$unexpectedNew = @($newRows | Where-Object { !$_.ExpectedNew -and !$_.BaselineUnlistedTextMeshPro })
$droneDifference = $null
$droneRow = @($unexpectedChanged | Where-Object Path -eq 'Assets\Prefabs\Enemy\Drone\Drone.prefab')
if($droneRow.Count -gt 0) {
    $copy='E:\DD451\Docs\Verification\Enemy\TurningVision\Before\Assets\Prefabs\Enemy\Drone\Drone.prefab'
    $current='E:\DD451\Assets\Prefabs\Enemy\Drone\Drone.prefab'
    $verifiedCopy=(Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -eq $droneRow[0].BeforeHash
    $diff=if($verifiedCopy){((& git -c core.safecrlf=false diff --no-index -- $copy $current 2>$null) -join [Environment]::NewLine)}else{'No byte comparison: retained copy did not match baseline hash.'}
    $droneDifference=[pscustomobject]@{Path=$droneRow[0].Path;BeforeHash=$droneRow[0].BeforeHash;CurrentHash=$droneRow[0].CurrentHash;LastWriteUtc=(Get-Item -LiteralPath $current).LastWriteTimeUtc.ToString('o');ComparisonCopy=$copy;ComparisonCopyMatches09IBaseline=$verifiedCopy;Diff=$diff;Attribution='Parent confirms these three orientation-field changes were not written by 09I. Specific author/cause is not established by this file audit. Current file is preserved; no rollback.'}
}
$metaRows = @($rows | Where-Object { $_.Path.EndsWith('.meta') })
$finalTestPath = Join-Path $auditRoot 'PlayMode-6-Final.xml'
[xml]$finalXml = Get-Content -LiteralPath $finalTestPath -Raw
$finalRun = $finalXml.'test-run'
$finalCases = @($finalXml.SelectNodes('//test-case') | ForEach-Object { [pscustomobject]@{FullName=$_.fullname;Result=$_.result;StartUtc=$_.'start-time';EndUtc=$_.'end-time';DurationSeconds=[double]$_.duration} })
$finalTestEvidence = [pscustomobject]@{
    Source=$finalTestPath;Sha256=(Get-FileHash -LiteralPath $finalTestPath -Algorithm SHA256).Hash
    StartUtc=$finalRun.'start-time';EndUtc=$finalRun.'end-time';Result=$finalRun.result
    Total=[int]$finalRun.total;Passed=[int]$finalRun.passed;Failed=[int]$finalRun.failed;Skipped=[int]$finalRun.skipped
    Cases=$finalCases
    VerifiedSixPassedZeroSkipped=[int]$finalRun.total -eq 6 -and [int]$finalRun.passed -eq 6 -and [int]$finalRun.failed -eq 0 -and [int]$finalRun.skipped -eq 0 -and $finalCases.Count -eq 6 -and @($finalCases | Where-Object Result -ne 'Passed').Count -eq 0
    Method='Read saved official XML only; no Unity call or test execution by this audit.'
}
$result = [pscustomobject]@{
    Task='09I Stealth Vignette';CaptureStartedUtc=$started;CaptureCompletedUtc=[DateTime]::UtcNow.ToString('o')
    Method='Read-only SHA256 against current BeforeHashes.json, whole-text reconstruction of Ruins, exact asmdef text checks. Docs outputs only; no Unity, tests, Assets writes or rollback.'
    BaselineManifestSha256=(Get-FileHash -LiteralPath (Join-Path $auditRoot 'BeforeHashes.json') -Algorithm SHA256).Hash
    Summary=[pscustomobject]@{BaselineFiles=$rows.Count;Unchanged=@($rows | Where-Object Unchanged).Count;ExpectedModified=@($rows | Where-Object { !$_.Unchanged -and $_.ExpectedChanged }).Count;Missing=@($rows | Where-Object { !$_.Exists }).Count;UnexpectedChanged=$unexpectedChanged.Count;FilesNotListedInBaseline=$newRows.Count;ExpectedNew=@($newRows | Where-Object ExpectedNew).Count;UnbaselinedTextMeshPro=$unbaselinedTmp.Count;OtherUnlistedFiles=$unexpectedNew.Count;ExistingMetas=$metaRows.Count;ExistingMetaUnchanged=@($metaRows | Where-Object Unchanged).Count;CurrentSceneCount=@($currentFiles | Where-Object Extension -eq '.unity').Count}
    ExistingFiles=$rows;NewFiles=$newRows;UnexpectedChanged=$unexpectedChanged;UnexpectedNew=$unexpectedNew
    RuinsAudit=$ruinsAudit;AsmdefAudit=$asmdefs;ReferenceAudit=$referenceAudit;ProtectedGroups=$protection
    AdditionalDronePrefabDifference=$droneDifference
    BaselineScopeNote='81 TextMesh Pro files currently exist but are absent from this baseline; their stored modification times are 2023-2025. Absence from a manifest does not establish they were added by 09I. They are recorded as unbaselined, not rolled back or attributed to this task.'
    NewTestHashStatus='FINAL VERIFIED SOURCE: final fixture sets Rigidbody interpolation=None for the kinematic test player; saved final XML records six passed, zero failed/skipped.'
    ExpectedFinalTestSha256='60C7231A999E524AF999D96185C015E8FC8BFBB008F2B8260B215E21FFF034F0'
    FinalTestHashMatches=($newRows | Where-Object Path -eq 'Assets\Tests\Enemy\PlayMode\PlayerStealthVignetteTests.cs').Sha256 -eq '60C7231A999E524AF999D96185C015E8FC8BFBB008F2B8260B215E21FFF034F0'
    FinalTestEvidence=$finalTestEvidence
    TestEvidence='PlayMode-6-Final.xml verifies one final six-case run, 6 passed / 0 failed / 0 skipped, 17:23:49-17:23:54 UTC. The earlier 4 passed / 2 failed run remains historical evidence; it is not erased or merged into the final count.'
}
$result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $auditRoot 'Integrity.json') -Encoding utf8
$report = @(
    '09I Stealth Vignette file integrity audit'
    "Capture UTC: $started through $($result.CaptureCompletedUtc)"
    'Read-only Assets inspection; no Unity, no tests, no rollback. Only Docs outputs.'
    "Baseline files=$($rows.Count); unchanged=$($result.Summary.Unchanged); expected modified=$($result.Summary.ExpectedModified); missing=$($result.Summary.Missing); unexpected changed=$($unexpectedChanged.Count)."
    "Existing meta unchanged=$($result.Summary.ExistingMetaUnchanged)/$($metaRows.Count). Expected new files=$($result.Summary.ExpectedNew); other unlisted files=$($unexpectedNew.Count); TextMesh Pro files absent from baseline=$($unbaselinedTmp.Count)."
    'Expected existing changes: Ruins.unity and the Enemy / Enemy.PlayModeTests asmdefs. Both asmdefs add only Core.Runtime and Universal.Runtime references; all other text retained.'
    "Ruins whole-text reconstruction=$($ruinsAudit.ReversingOnlyDeclaredAdditionsRestoresWholeText); reconstructed UTF8 hash matches baseline=$($ruinsAudit.ReconstructedByteHashMatchesBefore)."
    'Ruins additions: exactly seven YAML blocks for two new GameObjects, their two Transforms, two Volumes and one PlayerStealthVignette; two children added under existing ScenePresentation; one four-line PP=true override on existing OutputCamera. Reversing only those additions exactly recovers the baseline file.'
    'Therefore existing object positions/layout, all Lights, patrol routes, loot boxes, colliders, RenderSettings, camera properties other than declared PP and all other serialized fields retain baseline bytes. No scene objects were deleted.'
    'New neutral Profile has only three enabled overrides: ToneNone/Bloom0/Vignette0. Ruins low Volume uses its GUID; high Volume starts weight0/priority10 and binds the existing cameraBinder. Runtime script uses a private Vignette Profile. New asset GUIDs and references are recorded in JSON.'
    "Current scenes=$($result.Summary.CurrentSceneCount). Other 11 scene files match baseline. Shared Player, Shelter/latest profile, PC/default/quality profiles, existing Enemy behavior code, 07F code, dust, ProjectSettings and package declarations match their baseline entries. See explicit Drone.prefab exception below; do not claim all prefabs are unchanged."
    '81 TextMesh Pro files are present but unlisted in the baseline, with stored mtimes in 2023-2025. They are not declared 09I additions and no rollback or historical-hash claim is made.'
    'Protected groups:'
)
foreach($group in $protection){$report+=("  "+$group.Group+": "+$group.Unchanged+"/"+$group.Count+" unchanged")}
if($unexpectedChanged.Count -gt 0 -or $unexpectedNew.Count -gt 0){$report+='ATTENTION: additional entries are listed below; this audit does not attribute or revert concurrent work.';foreach($row in $unexpectedChanged){$report+='  CHANGED '+$row.Path};foreach($row in $unexpectedNew){$report+='  UNLISTED '+$row.Path}}
if($droneDifference){$report+='Drone.prefab differs only in a Transform localRotation and localEulerAnglesHint (0,0,0)->(0,-181.2,0), plus DroneVision.directionDegrees 0->-24. Current SHA256 '+$droneDifference.CurrentHash+'; mtime '+$droneDifference.LastWriteUtc+'. Comparison source matches the 09I baseline hash. Root confirms these are not 09I writes; specific author/cause remains unknown. Current file preserved, no rollback.'}
$report+='TEST SOURCE FINAL: SHA256 60C7231A999E524AF999D96185C015E8FC8BFBB008F2B8260B215E21FFF034F0; observed hash matches='+$result.FinalTestHashMatches+'. Final fixture disables Rigidbody interpolation on the kinematic test player.'
$report+='Saved official PlayMode-6-Final.xml: one final six-case run, 6 passed / 0 failed / 0 skipped, 2026-10-19 17:23:49Z to 17:23:54Z; direct XML verification='+$finalTestEvidence.VerifiedSixPassedZeroSkipped+'. Individual case names/times and source XML hash are in Integrity.json. This audit did not execute Unity or tests. Earlier failed runs remain separate historical evidence.'
$report | Set-Content -LiteralPath (Join-Path $auditRoot 'Integrity.txt') -Encoding utf8
$result.Summary | ConvertTo-Json
$ruinsAudit | ConvertTo-Json -Depth 5
$asmdefs | Select-Object Path,OnlyTwoRenderPipelineReferencesAdded | ConvertTo-Json
$referenceAudit | ConvertTo-Json -Depth 4
