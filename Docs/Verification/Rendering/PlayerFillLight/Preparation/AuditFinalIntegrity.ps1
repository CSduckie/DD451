$ErrorActionPreference = 'Stop'
$taskRoot = 'E:\DD451'
$taskDocs = Join-Path $taskRoot 'Docs\Verification\Rendering\PlayerFillLight'
$taskManifest = Get-Content -LiteralPath (Join-Path $taskDocs 'BeforeHashes.json') -Raw | ConvertFrom-Json
$taskAllowed = @('Assets/Prefabs/Player/Player.prefab','ProjectSettings/TagManager.asset')
$taskChecks = [Collections.Generic.List[object]]::new()
function Add-TaskCheck([string]$name,[bool]$passed,$detail) { $taskChecks.Add([pscustomobject]@{name=$name;passed=$passed;detail=$detail}) }
function Get-TaskHash([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
function Get-TaskBlocks([string]$source) {
    $blocks=@{}
    foreach($match in [regex]::Matches($source,'(?ms)^--- !u!(?<type>\d+) &(?<id>-?\d+)\r?\n.*?(?=^--- !u!|\z)')) {
        $blocks[$match.Groups['id'].Value]=[pscustomobject]@{id=$match.Groups['id'].Value;classId=$match.Groups['type'].Value;text=$match.Value}
    }
    return $blocks
}
function Get-TaskScalar([string]$block,[string]$field) {
    $match=[regex]::Match($block,'(?m)^  '+[regex]::Escape($field)+': (?<value>[^\r\n]+)')
    if(!$match.Success){throw "Missing field $field"};return $match.Groups['value'].Value
}
function Test-TaskBytes([string]$source,[string]$referencePath) { return [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($source)) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($referencePath)) }
$taskFiles=@(foreach($entry in $taskManifest.files){
    $path=Join-Path $taskRoot $entry.path;$exists=Test-Path -LiteralPath $path -PathType Leaf;$hash=if($exists){Get-TaskHash $path}else{$null}
    [pscustomobject]@{path=$entry.path;beforeSha256=$entry.sha256;afterSha256=$hash;exists=$exists;changed=$hash -ne $entry.sha256;allowedChange=$taskAllowed -contains $entry.path}
})
$taskChanged=@($taskFiles|Where-Object changed);$taskUnexpected=@($taskChanged|Where-Object{!$_.allowedChange});$taskMetas=@($taskFiles|Where-Object{$_.path.EndsWith('.meta')})
$taskCurrentPaths=@(& rg --files Assets/Scripts Assets/Scenes Assets/Prefabs Assets/Data Assets/Settings Assets/Art/Materials Assets/Tests ProjectSettings Packages | ForEach-Object{$_.Replace('\','/')} | Sort-Object -Unique)
$taskAdded=@($taskCurrentPaths | Where-Object { $taskFiles.path -notcontains $_ })
Add-TaskCheck 'baselineCount743' ($taskFiles.Count -eq 743) $taskFiles.Count
Add-TaskCheck 'allBaselineFilesExist' (@($taskFiles|Where-Object{!$_.exists}).Count -eq 0) @($taskFiles|Where-Object{!$_.exists}).path
Add-TaskCheck 'onlyTwoAllowedFilesChanged' ($taskChanged.Count -eq 2 -and $taskUnexpected.Count -eq 0) $taskChanged.path
Add-TaskCheck 'noAddedFilesInBaselineSourceDirectories' ($taskAdded.Count -eq 0) $taskAdded
Add-TaskCheck 'allMetasUnchanged' (@($taskMetas|Where-Object changed).Count -eq 0) $taskMetas.Count
$taskBackups=@(foreach($relative in @($taskAllowed + @('Assets/Prefabs/Player/Player.prefab.meta'))){
    $path=Join-Path (Join-Path $taskDocs 'Before') ([IO.Path]::GetFileName($relative));$hash=Get-TaskHash $path;$baseline=$taskFiles|Where-Object path -eq $relative
    [pscustomobject]@{path=$relative;backupPath=$path;sha256=$hash;matchesManifest=$hash -ceq $baseline.beforeSha256}
})
Add-TaskCheck 'threeRealBackupsMatchBaseline' (@($taskBackups|Where-Object{!$_.matchesManifest}).Count -eq 0) $taskBackups
$taskPrefabPath=Join-Path $taskRoot $taskAllowed[0];$taskOldPrefabPath=Join-Path $taskDocs 'Before\Player.prefab'
$taskPrefab=[Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($taskPrefabPath));$taskOldPrefab=[Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($taskOldPrefabPath))
$taskBlocks=Get-TaskBlocks $taskPrefab;$taskOldBlocks=Get-TaskBlocks $taskOldPrefab
$taskRootId='7325440935213355398';$taskLightGo='3003108033146804890';$taskLightTransform='4857002540760190402';$taskLight='807658987487291623';$taskAdditional='1701464540767969168'
$taskAddedIds=@($taskBlocks.Keys|Where-Object{!$taskOldBlocks.ContainsKey($_)});$taskExpectedAdded=@($taskLightGo,$taskLightTransform,$taskLight,$taskAdditional)
Add-TaskCheck 'exactFourNewBlocksAndNoRemovedBlocks' ($taskAddedIds.Count -eq 4 -and @($taskAddedIds|Where-Object{$taskExpectedAdded -notcontains $_}).Count -eq 0 -and @($taskOldBlocks.Keys|Where-Object{!$taskBlocks.ContainsKey($_)}).Count -eq 0) $taskAddedIds
$taskRendererIds=@('1788104202260102186','6622203793980637620')
$taskReversed=$taskPrefab
foreach($id in $taskAddedIds){$taskReversed=$taskReversed.Replace($taskBlocks[$id].text,'')}
foreach($id in $taskRendererIds){
    $block=$taskBlocks[$id].text;$old=$taskOldBlocks[$id].text
    $reversed=[regex]::Replace($block,'(?m)^  m_RenderingLayerMask: 3\r?$',{param($match) $match.Value.Replace(': 3',': 1')})
    Add-TaskCheck ('rendererOnlyMaskChanged_'+$id) ($taskBlocks[$id].classId -eq '23' -and (Get-TaskScalar $old 'm_RenderingLayerMask') -eq '1' -and (Get-TaskScalar $block 'm_RenderingLayerMask') -eq '3' -and $reversed -ceq $old) @{before=1;after=3}
    $taskReversed=$taskReversed.Replace($block,$reversed)
}
$taskRootBlock=$taskBlocks[$taskRootId].text
$taskRootReversed=[regex]::Replace($taskRootBlock,'(?m)^  - \{fileID: '+$taskLightTransform+'\}\r?\n','')
Add-TaskCheck 'rootOnlyAddsLightTransformChild' ($taskRootReversed -ceq $taskOldBlocks[$taskRootId].text) $taskLightTransform
$taskReversed=$taskReversed.Replace($taskRootBlock,$taskRootReversed)
Add-TaskCheck 'reverseAllowedPrefabDiffEqualsBeforeByteForByte' (Test-TaskBytes $taskReversed $taskOldPrefabPath) 'Remove 4 new YAML blocks and root child, reverse only two Renderer masks 3->1; reconstructed bytes must equal real Before.'
$taskOriginalBlockAudit=@(foreach($id in $taskOldBlocks.Keys|Sort-Object){[pscustomobject]@{id=$id;classId=$taskOldBlocks[$id].classId;changed=$taskBlocks[$id].text -cne $taskOldBlocks[$id].text;expectedChange=($id -eq $taskRootId -or $taskRendererIds -contains $id)}})
Add-TaskCheck 'allOtherOriginalComponentsAndGameObjectsUnchanged' (@($taskOriginalBlockAudit|Where-Object{$_.changed -and !$_.expectedChange}).Count -eq 0) $taskOriginalBlockAudit
$taskGoBlock=$taskBlocks[$taskLightGo].text;$taskTransformBlock=$taskBlocks[$taskLightTransform].text;$taskLightBlock=$taskBlocks[$taskLight].text;$taskAdditionalBlock=$taskBlocks[$taskAdditional].text
$taskGoComponents=@([regex]::Matches($taskGoBlock,'(?m)^  - component: \{fileID: (?<id>-?\d+)\}')|ForEach-Object{$_.Groups['id'].Value})
Add-TaskCheck 'newLightHasExactlyTransformLightAndURPData' ($taskGoComponents.Count -eq 3 -and ($taskGoComponents -join ',') -ceq (@($taskLightTransform,$taskLight,$taskAdditional)-join ',') -and $taskBlocks[$taskLightTransform].classId -eq '4' -and $taskBlocks[$taskLight].classId -eq '108' -and $taskBlocks[$taskAdditional].classId -eq '114') $taskGoComponents
Add-TaskCheck 'allPrefabGameObjectsUseOrdinaryLayerZero' (@($taskBlocks.Values|Where-Object{$_.classId -eq '1' -and (Get-TaskScalar $_.text 'm_Layer') -ne '0'}).Count -eq 0) 'Rendering Layer mask changes do not change ordinary physics/GameObject layers.'
$taskLightFields=[ordered]@{name=(Get-TaskScalar $taskGoBlock 'm_Name');active=(Get-TaskScalar $taskGoBlock 'm_IsActive');parent=(Get-TaskScalar $taskTransformBlock 'm_Father');localPosition=(Get-TaskScalar $taskTransformBlock 'm_LocalPosition');localRotation=(Get-TaskScalar $taskTransformBlock 'm_LocalRotation');localScale=(Get-TaskScalar $taskTransformBlock 'm_LocalScale');enabled=(Get-TaskScalar $taskLightBlock 'm_Enabled');type=(Get-TaskScalar $taskLightBlock 'm_Type');color=(Get-TaskScalar $taskLightBlock 'm_Color');intensity=(Get-TaskScalar $taskLightBlock 'm_Intensity');range=(Get-TaskScalar $taskLightBlock 'm_Range');bakeType=(Get-TaskScalar $taskLightBlock 'm_Lightmapping');bounce=(Get-TaskScalar $taskLightBlock 'm_BounceIntensity');lightRenderingMask=(Get-TaskScalar $taskLightBlock 'm_RenderingLayerMask');colorTemperatureEnabled=(Get-TaskScalar $taskLightBlock 'm_UseColorTemperature');customShadowLayers=(Get-TaskScalar $taskAdditionalBlock 'm_CustomShadowLayers');urpVersion=(Get-TaskScalar $taskAdditionalBlock 'm_Version')}
$taskExpectedFields=@{name='PlayerFillLight';active='1';parent='{fileID: 7325440935213355398}';localPosition='{x: 0, y: 1.1, z: -2}';localRotation='{x: 0, y: 0, z: 0, w: 1}';localScale='{x: 1, y: 1, z: 1}';enabled='1';type='2';color='{r: 1, g: 1, b: 1, a: 1}';intensity='1.2';range='4.5';bakeType='4';bounce='0';lightRenderingMask='2';colorTemperatureEnabled='0';customShadowLayers='0';urpVersion='4'}
foreach($key in $taskExpectedFields.Keys){Add-TaskCheck ('lightField_'+$key) ($taskLightFields[$key] -ceq $taskExpectedFields[$key]) $taskLightFields[$key]}
Add-TaskCheck 'lightHasNoShadowsAndOrdinaryCullingAll' ([regex]::IsMatch($taskLightBlock,'(?m)^  m_Shadows:\r?\n    m_Type: 0\r?$') -and [regex]::IsMatch($taskLightBlock,'(?m)^  m_CullingMask:\r?\n    serializedVersion: 2\r?\n    m_Bits: 4294967295\r?$')) 'No shadows; cullingMask all ordinary layers; isolation is Rendering Layer bit 1 (mask2).'
Add-TaskCheck 'urpCurrentRenderingLayerMaskIs2' ([regex]::IsMatch($taskAdditionalBlock,'(?m)^  m_RenderingLayersMask:\r?\n    serializedVersion: 0\r?\n    m_Bits: 2\r?$')) 'URP serialization version4 uses m_RenderingLayersMask; obsolete m_LightLayerMask/m_RenderingLayers fields remaining 1 are not current effective mask.'
$taskUrpGuid='474bcb49853aa07438625e644c072ee6';$taskUrpMeta=[IO.File]::ReadAllText((Join-Path $taskRoot 'Library/PackageCache/com.unity.render-pipelines.universal@6c02063a0a76/Runtime/UniversalAdditionalLightData.cs.meta'))
Add-TaskCheck 'URPAdditionalLightScriptGuidResolves' ($taskAdditionalBlock.Contains('m_Script: {fileID: 11500000, guid: '+$taskUrpGuid+', type: 3}') -and $taskUrpMeta.Contains('guid: '+$taskUrpGuid)) $taskUrpGuid
$taskPrefabMeta=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Prefabs/Player/Player.prefab.meta'))
Add-TaskCheck 'PlayerPrefabGuidPreserved' ($taskPrefabMeta.Contains('guid: 4a37e015a99af1948851c34d90ec57de')) '4a37e015a99af1948851c34d90ec57de'
$taskTag=[Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes((Join-Path $taskRoot $taskAllowed[1])));$taskOldTag=[Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes((Join-Path $taskDocs 'Before\TagManager.asset')))
$taskOldRendering=[regex]::Match($taskOldTag,'(?ms)^  m_RenderingLayers:\r?\n(?<body>.*)\z').Groups['body'].Value;$taskRendering=[regex]::Match($taskTag,'(?ms)^  m_RenderingLayers:\r?\n(?<body>.*)\z').Groups['body'].Value
$taskOldNames=@([regex]::Matches($taskOldRendering,'(?m)^  - (?<name>[^\r\n]*)')|ForEach-Object{$_.Groups['name'].Value});$taskNames=@([regex]::Matches($taskRendering,'(?m)^  - (?<name>[^\r\n]*)')|ForEach-Object{$_.Groups['name'].Value})
$taskTagStrictOnlyName=$taskTag -ceq $taskOldTag.Replace('  - Light Layer 1','  - PlayerLighting')
$taskTagNote=@{strictOnlyIndex1NameChanged=$taskTagStrictOnlyName;beforeSerializedVersion=(Get-TaskScalar $taskOldTag 'serializedVersion');afterSerializedVersion=(Get-TaskScalar $taskTag 'serializedVersion');beforeRenderingLayerCount=$taskOldNames.Count;afterRenderingLayerCount=$taskNames.Count;removedIndices=@(8..31);removedValues=@($taskOldNames|Select-Object -Skip 8);ordinaryLayersTagsSortingUnchanged=$false;explanation='Saved TagManager also migrated serializedVersion 2->3 and omitted 24 empty trailing Rendering Layer names (indices8-31). No non-empty layer name except index1 changed. This is reported explicitly, not described as a one-string byte diff.'}
$taskOldTagPrefix=$taskOldTag.Substring(0,$taskOldTag.IndexOf('  m_RenderingLayers:'));$taskTagPrefix=$taskTag.Substring(0,$taskTag.IndexOf('  m_RenderingLayers:'))
$taskTagNote.ordinaryLayersTagsSortingUnchanged=$taskTagPrefix -ceq $taskOldTagPrefix.Replace('  serializedVersion: 2','  serializedVersion: 3')
$taskExpectedTag=$taskOldTag.Replace('  serializedVersion: 2','  serializedVersion: 3').Replace('  - Light Layer 1','  - PlayerLighting')
$taskExpectedTag=[regex]::Replace($taskExpectedTag,'(?m)(?:^  - \r?\n){24}\z','')
Add-TaskCheck 'TagManagerOnlyNameAndKnownSerializationChanges' ($taskTag -ceq $taskExpectedTag -and $taskOldNames.Count -eq 32 -and $taskNames.Count -eq 8 -and @($taskOldNames|Select-Object -Skip 8|Where-Object{$_ -ne ''}).Count -eq 0) $taskTagNote
Add-TaskCheck 'ordinaryLayersTagsSortingUnchanged' $taskTagNote.ordinaryLayersTagsSortingUnchanged 'Compared complete prefix excluding only recorded serializedVersion2->3.'
$taskRenderingNameAudit=@(for($index=0;$index -lt 8;$index++){[pscustomobject]@{index=$index;bitMask=([uint32]1 -shl $index);before=$taskOldNames[$index];after=$taskNames[$index];matchesExpected=($taskNames[$index] -ceq $(if($index -eq 1){'PlayerLighting'}else{$taskOldNames[$index]}))}})
Add-TaskCheck 'allEightExistingNamedRenderingLayerIndicesPreservedExceptRename' (@($taskRenderingNameAudit|Where-Object{!$_.matchesExpected}).Count -eq 0) $taskRenderingNameAudit
Add-TaskCheck 'newLightAndChangedRendererMasksUseNoRemovedEmptySlots' ((([uint32]2 -bor [uint32]3) -band [uint32]4294967040) -eq 0) 'Only mask2 for new Light/URP and mask3 for the two existing Renderers; original component byte audit excludes all other receiver/light changes.'
$taskTagNote.explanation='Root confirmed preserving Unity official API save normalization: serializedVersion2->3 and omission of unused empty Rendering Layer names index8-31. Existing named bits remain, except index1 rename. No new/changed component uses removed empty slots.'
$taskPassed=@($taskChecks|Where-Object{!$_.passed}).Count -eq 0
$taskStatus=if(!$taskPassed){'FAIL'}elseif(!$taskTagStrictOnlyName){'PASS_WITH_SERIALIZATION_NOTE'}else{'PASS'}
$taskReport=[ordered]@{status=$taskStatus;capturedUtc=[DateTime]::UtcNow.ToString('o');baseline='Docs/Verification/Rendering/PlayerFillLight/BeforeHashes.json';baselineCapturedUtc=$taskManifest.capturedUtc;scope='Disk-only audit; no Unity calls, no production file writes, Docs only.';manifestCount=$taskFiles.Count;unchangedCount=@($taskFiles|Where-Object{!$_.changed}).Count;changedCount=$taskChanged.Count;metaCount=$taskMetas.Count;unexpectedFileChanges=$taskUnexpected;addedFilesInBaselineDirectories=$taskAdded;changedFiles=$taskChanged;realBackups=$taskBackups;checks=$taskChecks;prefabOriginalBlockAudit=$taskOriginalBlockAudit;prefabNewBlockIds=$taskAddedIds;lightSettings=$taskLightFields;tagManagerSerializationNote=$taskTagNote;limitations=@('TagManager is not literally a one-name-only diff; version and trailing empty-name serialization changes are explicitly recorded.','This report does not claim action/scene-flow runtime acceptance, rendering isolation, native input or Editor release; those are separate root verification.','Manifest directories were also checked for added files; directories outside that scope are not covered.');files=$taskFiles}
$taskReport|ConvertTo-Json -Depth 15|Set-Content -LiteralPath (Join-Path $taskDocs 'FinalIntegrity.json') -Encoding utf8
$taskText=[Collections.Generic.List[string]]::new()
$taskText.Add('PlayerFillLight 最终磁盘完整性审计')
$taskText.Add('结果：'+$taskStatus+'；UTC：'+$taskReport.capturedUtc)
$taskText.Add("基线$($taskFiles.Count)项：未变$($taskReport.unchangedCount)，仅2文件变化，意外文件漂移$($taskUnexpected.Count)，新增文件$($taskAdded.Count)；$($taskMetas.Count)份meta均SHA-256一致。")
$taskText.Add('三份真实Before原件与manifest哈希一致；Player.prefab GUID保留4a37e015a99af1948851c34d90ec57de。完整逐文件前后hash见FinalIntegrity.json.files。')
$taskText.Add('Prefab反向去除4个新增YAML块、root新增children引用，并把两个MeshRenderer mask3还原1后，与真实Before逐字节相同。所有原GameObject普通Layer、组件、物理/碰撞、角色/输入/交互、材质/模型/相机目标字段均保持。')
$taskText.Add('新增块仅PlayerFillLight GameObject、Transform、Light、UniversalAdditionalLightData；根下local(0,1.1,-2)，identity rotation，scale1，GameObject Layer0，白色Point、intensity1.2、range4.5、m_Lightmapping4(Realtime)、shadows None、bounce0；Light mask2及URP当前m_RenderingLayersMask.bits2。普通cullingMask仍all。两个Renderer 1→3因此保留Default位并增加PlayerLighting位。')
$taskText.Add('URP组件脚本GUID与本机包meta核对一致；版本4读取当前m_RenderingLayersMask，保留的旧m_LightLayerMask/m_RenderingLayers=1不代表当前effective mask。')
$taskText.Add('TagManager注意：除RenderingLayer index1 Light Layer1→PlayerLighting外，还保存了serializedVersion2→3，并省略原index8–31的24个空RenderingLayer名称。故不能称为“只有一个字符串改动”。原8个有效RenderingLayer索引/位保持，index0及2–7名称保留；普通layers、tags、sorting完整前缀一致（除记录的version）。root已明确保留官方API保存产生的格式归一化；新增Light mask2与修改Renderer mask3均未使用这些空槽，所有其它组件由逐字节审计证明不变。')
$taskText.Add('边界：本报告只读磁盘，无Unity调用，无Assets/ProjectSettings写入，只写本任务Docs。动作/流程/渲染隔离/原生输入与Editor释放由root单独验证，不在本报告声称完成。')
if(!$taskPassed){foreach($failure in @($taskChecks|Where-Object{!$_.passed})){$taskText.Add('FAILED: '+$failure.name)}}
$taskText|Set-Content -LiteralPath (Join-Path $taskDocs 'FinalIntegrity.txt') -Encoding utf8
[pscustomobject]@{status=$taskStatus;count=$taskFiles.Count;unchanged=$taskReport.unchangedCount;changed=$taskChanged.path;metas=$taskMetas.Count;failedChecks=@($taskChecks|Where-Object{!$_.passed});tagManagerNote=$taskTagNote}|ConvertTo-Json -Depth 7
