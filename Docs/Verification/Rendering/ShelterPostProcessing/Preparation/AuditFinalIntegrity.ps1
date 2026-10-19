$ErrorActionPreference = 'Stop'
$taskRoot = 'E:\DD451'
$taskDocs = Join-Path $taskRoot 'Docs\Verification\Rendering\ShelterPostProcessing'
$taskManifest = Get-Content -LiteralPath (Join-Path $taskDocs 'BeforeHashes.json') -Raw | ConvertFrom-Json
$taskAllowed = @('Assets/Scenes/Shelter.unity', 'Assets/Scenes/Shelter/Global Volume Profile.asset')
$taskChecks = [System.Collections.Generic.List[object]]::new()
function Add-TaskCheck([string]$name, [bool]$passed, $detail) {
    $taskChecks.Add([pscustomobject]@{ name=$name; passed=$passed; detail=$detail })
}
function Get-TaskHash([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
function Get-TaskBlocks([string]$source) {
    $result = @{}
    foreach ($match in [regex]::Matches($source, '(?ms)^--- !u!114 &(?<id>-?\d+)\r?\n.*?(?=^--- !u!|\z)')) {
        $result[$match.Groups['id'].Value] = $match.Value
    }
    return $result
}
function Get-TaskParameter([string]$block, [string]$field) {
    $match = [regex]::Match($block, '(?m)^  ' + [regex]::Escape($field) + ':\r?\n    m_OverrideState: (?<override>\d+)\r?\n    m_Value: (?<value>[^\r\n]+)')
    if (!$match.Success) { throw "Missing parameter: $field" }
    return [pscustomobject]@{ override=$match.Groups['override'].Value -eq '1'; value=$match.Groups['value'].Value }
}
$taskFiles = @(
    foreach ($entry in $taskManifest.files) {
        $filePath = Join-Path $taskRoot $entry.path
        $exists = Test-Path -LiteralPath $filePath -PathType Leaf
        $hash = if ($exists) { Get-TaskHash $filePath } else { $null }
        [pscustomobject]@{path=$entry.path;beforeSha256=$entry.sha256;afterSha256=$hash;exists=$exists;changed=$hash -ne $entry.sha256;allowedChange=$taskAllowed -contains $entry.path}
    }
)
$taskChanged = @($taskFiles | Where-Object changed)
$taskUnexpected = @($taskChanged | Where-Object { !$_.allowedChange })
$taskMetas = @($taskFiles | Where-Object { $_.path.EndsWith('.meta') })
Add-TaskCheck 'manifestCount' ($taskFiles.Count -eq 642) $taskFiles.Count
Add-TaskCheck 'allBaselineFilesExist' (@($taskFiles | Where-Object { !$_.exists }).Count -eq 0) @($taskFiles | Where-Object { !$_.exists }).path
Add-TaskCheck 'onlyTwoAllowedFilesChanged' ($taskChanged.Count -eq 2 -and $taskUnexpected.Count -eq 0) $taskChanged.path
Add-TaskCheck 'allBaselineMetasUnchanged' (@($taskMetas | Where-Object changed).Count -eq 0) $taskMetas.Count
$taskBackupFiles = @(
    foreach ($relative in @($taskAllowed + @($taskAllowed | ForEach-Object { $_ + '.meta' }))) {
        $backup = Join-Path (Join-Path $taskDocs 'Before') $relative
        $baseline = $taskFiles | Where-Object path -eq $relative
        $backupHash = Get-TaskHash $backup
        [pscustomobject]@{path=$relative;sha256=$backupHash;matchesManifest=$backupHash -eq $baseline.beforeSha256}
    }
)
Add-TaskCheck 'fourRealBackupsMatchBaseline' (@($taskBackupFiles | Where-Object { !$_.matchesManifest }).Count -eq 0) $taskBackupFiles.Count
$taskScenePath = Join-Path $taskRoot $taskAllowed[0]
$taskOldScenePath = Join-Path (Join-Path $taskDocs 'Before') $taskAllowed[0]
$taskSceneBytes = [IO.File]::ReadAllBytes($taskScenePath)
$taskScene = [Text.Encoding]::UTF8.GetString($taskSceneBytes)
$taskNewline = if ($taskScene.Contains("`r`n")) { "`r`n" } else { "`n" }
$taskInsertion = (@(
    '    - target: {fileID: 6648020794282633460, guid: 3cf4a390a92e98447ada0654fb991c91, type: 3}',
    '      propertyPath: m_RenderPostProcessing',
    '      value: 1',
    '      objectReference: {fileID: 0}'
) -join $taskNewline) + $taskNewline
$taskInsertionCount = [regex]::Matches($taskScene, [regex]::Escape($taskInsertion)).Count
$taskRestoredSceneBytes = [Text.Encoding]::UTF8.GetBytes($taskScene.Replace($taskInsertion, ''))
$taskSceneOnlyOverride = $taskInsertionCount -eq 1 -and [Convert]::ToBase64String($taskRestoredSceneBytes) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($taskOldScenePath))
Add-TaskCheck 'sceneExactFourLineInsertionOnly' $taskSceneOnlyOverride $taskInsertion
$taskProfile = [IO.File]::ReadAllText((Join-Path $taskRoot $taskAllowed[1]))
$taskOldProfile = [IO.File]::ReadAllText((Join-Path (Join-Path $taskDocs 'Before') $taskAllowed[1]))
$taskBlocks = Get-TaskBlocks $taskProfile
$taskOldBlocks = Get-TaskBlocks $taskOldProfile
$taskRootBlock = $taskBlocks['11400000']
$taskComponentIds = @([regex]::Matches($taskRootBlock, '(?m)^  - \{fileID: (?<id>-?\d+)\}') | ForEach-Object { $_.Groups['id'].Value })
$taskComponents = @(
    foreach ($id in $taskComponentIds) {
        $block = $taskBlocks[$id]
        [pscustomobject]@{fileId=$id;exists=$null -ne $block;name=[regex]::Match($block,'(?m)^  m_Name: ([^\r\n]+)').Groups[1].Value;scriptGuid=[regex]::Match($block,'m_Script: \{fileID: 11500000, guid: ([a-f0-9]+), type: 3\}').Groups[1].Value;active=[regex]::IsMatch($block,'(?m)^  active: 1\r?$')}
    }
)
Add-TaskCheck 'profileHasFourUniqueResolvedComponents' ($taskBlocks.Count -eq 5 -and $taskComponents.Count -eq 4 -and @($taskComponentIds | Select-Object -Unique).Count -eq 4 -and @($taskComponents | Where-Object { !$_.exists }).Count -eq 0) $taskComponents
$taskExpectedComponents = @(
    @{name='Bloom';id='6176611016742131025';guid='0b2db86121404754db890f4c8dfe81b2';package='com.unity.render-pipelines.universal@6c02063a0a76/Runtime/Overrides/Bloom.cs.meta'},
    @{name='ColorAdjustments';id='197306635505997875';guid='66f335fb1ffd8684294ad653bf1c7564';package='com.unity.render-pipelines.universal@6c02063a0a76/Runtime/Overrides/ColorAdjustments.cs.meta'},
    @{name='Tonemapping';id='6444782141353133585';guid='97c23e3b12dc18c42a140437e53d3951';package='com.unity.render-pipelines.universal@6c02063a0a76/Runtime/Overrides/Tonemapping.cs.meta'},
    @{name='Vignette';id='-5535927733993135367';guid='899c54efeace73346a0a16faa3afe726';package='com.unity.render-pipelines.universal@6c02063a0a76/Runtime/Overrides/Vignette.cs.meta'}
)
foreach ($expected in $taskExpectedComponents) {
    $actual = $taskComponents | Where-Object name -eq $expected.name
    $packageMeta = [IO.File]::ReadAllText((Join-Path $taskRoot ('Library/PackageCache/' + $expected.package)))
    Add-TaskCheck ('componentReference_' + $expected.name) ($actual.fileId -ceq $expected.id -and $actual.scriptGuid -ceq $expected.guid -and $actual.active -and $packageMeta.Contains('guid: ' + $expected.guid)) $actual
}
$taskRootWithoutNew = $taskRootBlock
foreach ($id in @('197306635505997875','6444782141353133585','-5535927733993135367')) { $taskRootWithoutNew = [regex]::Replace($taskRootWithoutNew, '(?m)^  - \{fileID: ' + $id + '\}\r?\n', '') }
Add-TaskCheck 'profileRootOnlyAddsThreeComponentReferences' ($taskRootWithoutNew -ceq $taskOldBlocks['11400000']) $null
$taskBloom = $taskBlocks['6176611016742131025']
$taskOldBloomExpected = [regex]::Replace($taskOldBlocks['6176611016742131025'], '(?m)(^  threshold:\r?\n)    m_OverrideState: 0(\r?\n)    m_Value: 5\.68', '${1}    m_OverrideState: 1${2}    m_Value: 1')
Add-TaskCheck 'existingBloomOnlyChangesThresholdOverrideAndValue' ($taskBloom -ceq $taskOldBloomExpected) @{before=@{override=$false;value=5.68};after=@{override=$true;value=1};intensity=0.1;tint='0.3018868,0.2748309,0.2748309,1 preserved'}
$taskSettings = [ordered]@{
    postExposure=(Get-TaskParameter $taskBlocks['197306635505997875'] 'postExposure');contrast=(Get-TaskParameter $taskBlocks['197306635505997875'] 'contrast');colorFilter=(Get-TaskParameter $taskBlocks['197306635505997875'] 'colorFilter');hueShift=(Get-TaskParameter $taskBlocks['197306635505997875'] 'hueShift');saturation=(Get-TaskParameter $taskBlocks['197306635505997875'] 'saturation');tone=(Get-TaskParameter $taskBlocks['6444782141353133585'] 'mode');vignette=(Get-TaskParameter $taskBlocks['-5535927733993135367'] 'intensity');bloom=(Get-TaskParameter $taskBloom 'intensity');bloomThreshold=(Get-TaskParameter $taskBloom 'threshold')
}
$taskExpectedValues = @{postExposure='-0.2';contrast='8';colorFilter='{r: 1, g: 1, b: 1, a: 1}';hueShift='0';saturation='0';tone='0';vignette='0';bloom='0.1';bloomThreshold='1'}
foreach ($key in $taskExpectedValues.Keys) { Add-TaskCheck ('parameter_' + $key) ($taskSettings[$key].override -and $taskSettings[$key].value -ceq $taskExpectedValues[$key]) $taskSettings[$key] }
$taskProfileMeta = [IO.File]::ReadAllText((Join-Path $taskRoot ($taskAllowed[1]+'.meta')))
Add-TaskCheck 'profileGuidAndSceneReferencePreserved' ($taskProfileMeta.Contains('guid: 76ef7695bb051d746bed768237e73e75') -and $taskScene.Contains('sharedProfile: {fileID: 11400000, guid: 76ef7695bb051d746bed768237e73e75, type: 2}')) '76ef7695bb051d746bed768237e73e75'
$taskRuntime = Join-Path $taskDocs 'Runtime-91fbe323'
$taskFixedBefore = Get-Content -LiteralPath (Join-Path $taskRuntime 'FixedPhase.json') -Raw | ConvertFrom-Json
$taskFixedAfter = Get-Content -LiteralPath (Join-Path $taskRuntime 'FixedPhaseAfter.json') -Raw | ConvertFrom-Json
$taskBeforeMetadata = Get-Content -LiteralPath (Join-Path $taskRuntime '06-FixedPhase-Before-metadata.json') -Raw | ConvertFrom-Json
$taskAfterMetadata = Get-Content -LiteralPath (Join-Path $taskRuntime '07-FixedPhase-After-metadata.json') -Raw | ConvertFrom-Json
Add-TaskCheck 'fixedPhaseSameGameTimeAndFiveLights' ($taskFixedBefore.timeScale -eq 0 -and $taskFixedBefore.gameTime -eq $taskFixedAfter.gameTime -and $taskFixedBefore.lights.Count -eq 5 -and ($taskFixedBefore.lights | ConvertTo-Json -Depth 5 -Compress) -ceq ($taskFixedAfter.lights | ConvertTo-Json -Depth 5 -Compress)) @{gameTime=$taskFixedBefore.gameTime;lights=$taskFixedBefore.lights}
$taskCameraBefore = $taskBeforeMetadata.sample.camera | Select-Object * -ExcludeProperty postProcessing
$taskCameraAfter = $taskAfterMetadata.sample.camera | Select-Object * -ExcludeProperty postProcessing
Add-TaskCheck 'fixedPhaseSameCameraExceptPPToggle' (($taskCameraBefore | ConvertTo-Json -Depth 8 -Compress) -ceq ($taskCameraAfter | ConvertTo-Json -Depth 8 -Compress) -and !$taskBeforeMetadata.sample.camera.postProcessing -and $taskAfterMetadata.sample.camera.postProcessing) @{before=$taskBeforeMetadata.sample.camera;after=$taskAfterMetadata.sample.camera}
Add-TaskCheck 'fixedPhaseSamePlayerEnvironmentAndEffectiveStack' (($taskBeforeMetadata.sample.player | ConvertTo-Json -Depth 8 -Compress) -ceq ($taskAfterMetadata.sample.player | ConvertTo-Json -Depth 8 -Compress) -and ($taskBeforeMetadata.sample.environment | ConvertTo-Json -Depth 8 -Compress) -ceq ($taskAfterMetadata.sample.environment | ConvertTo-Json -Depth 8 -Compress) -and ($taskBeforeMetadata.sample.effectiveStack | ConvertTo-Json -Depth 8 -Compress) -ceq ($taskAfterMetadata.sample.effectiveStack | ConvertTo-Json -Depth 8 -Compress)) 'Before stack contains configured values but camera PP=false, so those effects are not rendered in Before.'
Add-Type -AssemblyName System.Drawing
$taskRegions = @(@{name='right_floor';x0=1160;y0=790;x1=1700;y1=1020},@{name='wood_crate';x0=884;y0=758;x1=1146;y1=936},@{name='right_wall';x0=1100;y0=250;x1=1400;y1=410},@{name='player_with_small_background_border';x0=620;y0=588;x1=704;y1=786})
$taskImageStats = @(
    foreach ($name in @('06-FixedPhase-Before','07-FixedPhase-After')) {
        $path = Join-Path $taskRuntime ($name+'-1920x1080.png')
        $bitmap = [Drawing.Bitmap]::new($path)
        try {
            Add-TaskCheck ($name+'_dimensions') ($bitmap.Width -eq 1920 -and $bitmap.Height -eq 1080) @($bitmap.Width,$bitmap.Height)
            $regions = @(
                foreach ($region in $taskRegions) {
                    $sum=0.0; $count=0; $nearBlack=0; $exactBlack=0
                    for ($y=$region.y0;$y -lt $region.y1;$y+=2) { for ($x=$region.x0;$x -lt $region.x1;$x+=2) {
                        $pixel=$bitmap.GetPixel($x,$y);$luma=.2126*$pixel.R+.7152*$pixel.G+.0722*$pixel.B
                        $sum+=$luma;$count++;if($luma -lt 12){$nearBlack++};if(($pixel.R+$pixel.G+$pixel.B) -eq 0){$exactBlack++}
                    } }
                    [pscustomobject]@{region=$region;sampleCount=$count;meanSrgbLuma255=[math]::Round($sum/$count,4);under12Percent=[math]::Round(100*$nearBlack/$count,4);exactBlackPercent=[math]::Round(100*$exactBlack/$count,4)}
                }
            )
            [pscustomobject]@{path=$path;sha256=(Get-TaskHash $path);regions=$regions}
        } finally { $bitmap.Dispose() }
    }
)
$taskPassed = @($taskChecks | Where-Object { !$_.passed }).Count -eq 0
$taskReport = [ordered]@{
    status=$(if($taskPassed){'PASS'}else{'FAIL'});capturedUtc=[DateTime]::UtcNow.ToString('o');scope='Read-only disk audit; no Unity calls; only this task Docs outputs written.';baseline='Docs/Verification/Rendering/ShelterPostProcessing/BeforeHashes.json';baselineCapturedUtc=$taskManifest.capturedUtc;manifestCount=$taskFiles.Count;unchangedCount=@($taskFiles | Where-Object { !$_.changed }).Count;changedCount=$taskChanged.Count;metaCount=$taskMetas.Count;unexpectedChanges=$taskUnexpected;changedFiles=$taskChanged;backups=$taskBackupFiles;checks=$taskChecks;profileSettings=$taskSettings;imageStatisticsMethod='Every second pixel in declared rectangular ROIs; luma=0.2126R+0.7152G+0.0722B on encoded 8-bit sRGB. Not photometric luminance; rectangular regions may include background.';fixedPhaseImages=$taskImageStats;visualReview=@('Both 1920x1080 PNGs opened with view_image. Player silhouette and green highlight, blue container ribs, yellow chest edges, shelves and floor remain readable.','After is moderately darker with deeper shadows; no broad gray veil observed. Wooden crate remains a naturally dark asset but its silhouette and top edges stay visible.','Warm bloom remains localized around ceiling fixtures. Overlay HUD remains readable.','FixedPhase.json and FixedPhaseAfter.json show identical five light IDs/intensities/enabled flags and gameTime 91.67061; screenshot metadata verifies same camera/player/environment. This replaces the initial 03 image comparison whose left lamp was at a different flicker phase.');limitations=@('Proves the 642 manifest files and recorded fixed-phase comparison at this capture time, not all unlisted project files.','Runtime records are read from root-produced evidence, not newly sampled by this auditor.','No native keyboard/mouse, automated test run, build or Editor release claim is made here.');files=$taskFiles
}
$taskReport | ConvertTo-Json -Depth 18 | Set-Content -LiteralPath (Join-Path $taskDocs 'FinalIntegrity.json') -Encoding utf8
$taskText = [System.Collections.Generic.List[string]]::new()
$taskText.Add('Shelter Post-processing 最终磁盘完整性与固定灯光画面对比')
$taskText.Add('结果：'+$taskReport.status+'；UTC：'+$taskReport.capturedUtc)
$taskText.Add('范围：只读Assets/ProjectSettings/Packages与既有证据；未调用Unity，只写本任务Docs。')
$taskText.Add("BeforeHashes基线：$($taskFiles.Count)项；未改$($taskReport.unchangedCount)；变化$($taskChanged.Count)；未授权漂移$($taskUnexpected.Count)；$($taskMetas.Count)个meta全部逐SHA-256核对。")
$taskText.Add('四份真实Before备份均重新校验与manifest一致。完整逐文件hash见FinalIntegrity.json.files。')
$taskText.Add('Shelter.unity：精确只新增4行m_RenderPostProcessing=1实例覆盖；将该四行移除后与真实Before逐字节一致。现有用户灯光、位置/对象、交互ID/内容、相机/Cinemachine/边界、Volume层级、天空盒引用等场景其余字节全部保留。')
$taskText.Add('Global Volume Profile.asset：原GUID 76ef7695bb051d746bed768237e73e75、root fileID 11400000、旧Bloom fileID 6176611016742131025保留；四个component引用唯一且均可解析，脚本GUID与本机URP包meta一致。')
$taskText.Add('Profile root仅增三条子资产引用；旧Bloom只将threshold OverrideState 0→1、value 5.68→1，原intensity .1、tint及其它Bloom字段逐块保持。新增ColorAdjustments：exposure -.2、contrast 8、saturation/hue 0、filter白；Tonemapping=None；Vignette intensity=0；明确相关override均true。')
$taskText.Add('固定灯相位：FixedPhase/FixedPhaseAfter的gameTime均91.67061，五盏灯的id/intensity/enabled完全一致。两截图metadata同相机id/位置/旋转/FOV/尺寸、同玩家/环境，唯一相机字段差异为PP false→true；均1920×1080。Before虽已存在配置stack但PP关闭，因此效果未渲染。')
$taskText.Add('图像已分别用view_image实看：B(-.2EV/+8contrast)保留玩家轮廓/绿色高光、蓝集装箱条纹、黄箱边沿、货架和地板层次；整体适度昏暗，无新增全屏灰幕。木箱本身较黑，但轮廓及上缘仍可辨。Bloom集中灯泡附近，HUD清晰。该固定相位对照取代初始03图左灯闪烁状态不同的参数对比。')
$taskText.Add('像素统计：指定矩形每隔2像素采样，8-bit sRGB加权亮度近似，非真实光度；near-black阈值12只用于比较，不等同全黑/视觉失败。')
foreach ($stat in $taskImageStats) { foreach ($region in $stat.regions) { $taskText.Add([IO.Path]::GetFileName($stat.path)+' | '+$region.region.name+' | mean='+$region.meanSrgbLuma255+' | under12%='+$region.under12Percent+' | RGB000%='+$region.exactBlackPercent) } }
$taskText.Add('限制：仅证明642项基线和该时刻固定相位帧；运行状态来自root保存证据，本审计未重新运行Play/测试，不宣称原生键鼠、构建或Editor释放。')
if (!$taskPassed) { foreach ($failed in @($taskChecks | Where-Object { !$_.passed })) { $taskText.Add('FAILED CHECK: '+$failed.name) } }
$taskText | Set-Content -LiteralPath (Join-Path $taskDocs 'FinalIntegrity.txt') -Encoding utf8
[pscustomobject]@{status=$taskReport.status;fileCount=$taskFiles.Count;changed=$taskChanged.path;unexpected=$taskUnexpected.path;metaCount=$taskMetas.Count;failedChecks=@($taskChecks | Where-Object { !$_.passed });imageStats=$taskImageStats} | ConvertTo-Json -Depth 8
