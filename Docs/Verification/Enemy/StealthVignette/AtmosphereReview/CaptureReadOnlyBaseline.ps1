$ErrorActionPreference='Stop'
$reviewRoot='E:\DD451\Docs\Verification\Enemy\StealthVignette\AtmosphereReview'
New-Item -ItemType Directory -Path $reviewRoot -Force | Out-Null
$paths=@('Assets/Settings/DefaultVolumeProfile.asset','Assets/Scenes/Shelter/Global Volume Profile.asset','Assets/Settings/UniversalRenderPipelineGlobalSettings.asset','Assets/Settings/PC_RPAsset.asset','Assets/Settings/PC_Renderer.asset','Assets/Prefabs/Player/PlayerCamera.prefab','Assets/Prefabs/Player/Player.prefab','Assets/Prefabs/Shelter/ShelterScreenDust.prefab','Assets/Art/Materials/Environment/ShelterScreenDust.mat','Assets/Scenes/Shelter.unity','Assets/Scenes/Ruins.unity','Assets/Scripts/GameFlow/MonoBehaviour/GameFlowSceneBindings.cs')
$hashes=@(foreach($path in $paths) { [pscustomobject]@{path=$path;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash} })
$profiles=@(foreach($path in $paths[0..1]) {
 $text=Get-Content -LiteralPath $path -Raw
 $components=@(foreach($block in [regex]::Matches($text,'(?ms)^--- !u!114 .*?(?=^--- !u!|\z)')) {
  $name=[regex]::Match($block.Value,'(?m)^  m_Name: ([^\r\n]*)').Groups[1].Value
  if($name -in @('ColorAdjustments','Bloom','Tonemapping','Vignette','WhiteBalance','FilmGrain','MotionBlur','DepthOfField','ChromaticAberration','LensDistortion','ColorLookup')) {
   $parameters=@(foreach($param in [regex]::Matches($block.Value,'(?m)^  (?<name>\w+):\r?\n    m_OverrideState: (?<override>\d+)\r?\n    m_Value: (?<value>[^\r\n]*)')) {
    [pscustomobject]@{name=$param.Groups['name'].Value;override=$param.Groups['override'].Value;value=$param.Groups['value'].Value}
   })
   [pscustomobject]@{name=$name;parameters=$parameters}
  }
 })
 [pscustomobject]@{path=$path;components=$components}
})
$scenes=@(foreach($path in @('Assets/Scenes/Ruins.unity','Assets/Scenes/Shelter.unity')) {
 $text=Get-Content -LiteralPath $path -Raw
 $names=@{}
 foreach($block in [regex]::Matches($text,'(?ms)^--- !u!1 &(?<id>\d+)\r?\n.*?(?=^--- !u!|\z)')) {
  $names[$block.Groups['id'].Value]=[regex]::Match($block.Value,'(?m)^  m_Name: ([^\r\n]*)').Groups[1].Value
 }
 $canvases=@(foreach($block in [regex]::Matches($text,'(?ms)^--- !u!223 &(?<id>\d+)\r?\n.*?(?=^--- !u!|\z)')) {
  $go=[regex]::Match($block.Value,'m_GameObject: \{fileID: (\d+)\}').Groups[1].Value
  [pscustomobject]@{id=$block.Groups['id'].Value;name=$names[$go];renderMode=[regex]::Match($block.Value,'m_RenderMode: (\d+)').Groups[1].Value}
 })
 [pscustomobject]@{path=$path;directCanvases=$canvases}
})
[pscustomobject]@{utc=[DateTime]::UtcNow.ToString('o');scope='Read-only disk snapshot for collaborator review, not a claim of current Editor state or final visual acceptance.';hashes=$hashes;profiles=$profiles;scenes=$scenes} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $reviewRoot 'CurrentConfigurationSnapshot.json') -Encoding utf8
$scenes | ConvertTo-Json -Depth 5

