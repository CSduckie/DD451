$ErrorActionPreference = 'Stop'
# Read existing Unity/reference assemblies; emit only Docs files. Never launches or calls Unity.
$taskRoot = 'E:/DD451/Docs/Verification/Rendering/ShelterPostProcessing/Preparation'
$outputRoot = Join-Path $taskRoot 'OfflineCompile'
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$template = 'E:/DD451/Docs/Verification/Enemy/TurningVision/Staging/RuntimeCaptureOffline/Compile.rsp'
$arguments = [Collections.Generic.List[string]]::new()
foreach ($line in Get-Content -LiteralPath $template)
{
    if ($line -match '^[-/](out|refout|pdb|errorlog|generatedfilesout):' -or $line -match '^".*\.cs"$') { continue }
    $arguments.Add($line)
}
$extraNames = @('MyPrecious.GameFlow', 'Unity.RenderPipelines.Core.Runtime', 'Unity.RenderPipelines.Core.Runtime.Shared', 'Unity.RenderPipelines.Universal.Runtime')
foreach ($name in $extraNames)
{
    $assembly = Get-ChildItem -LiteralPath 'E:/DD451/Library/Bee/artifacts' -Recurse -Filter ($name + '.ref.dll') | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($null -eq $assembly) { throw ('Missing assembly ' + $name) }
    $arguments.Add('-r:"' + $assembly.FullName + '"')
}
$jsonAssembly = Get-ChildItem -LiteralPath 'E:/DD451/Library/PackageCache' -Recurse -Filter 'Newtonsoft.Json.dll' | Select-Object -First 1
if ($null -eq $jsonAssembly) { throw 'Missing Newtonsoft.Json assembly' }
$arguments.Add('-r:"' + $jsonAssembly.FullName + '"')
$sources = @('01_Begin.cs', '02_Capture.cs', '03_InteractFlowTarget.cs', '04_VerifyReturned.cs', '05_CancelCapture.cs')
foreach ($name in $sources)
{
    $method = [IO.Path]::GetFileNameWithoutExtension($name) -replace '^[0-9]+_', ''
    $body = [IO.File]::ReadAllText((Join-Path $taskRoot $name))
    $wrapper = Join-Path $outputRoot ($method + '.cs')
    [IO.File]::WriteAllText($wrapper, ('public static class ShelterPP' + $method + ' { public static object Run() {' + [Environment]::NewLine + $body + [Environment]::NewLine + '} }'))
    $arguments.Add('"' + $wrapper + '"')
}
$arguments.Add('-out:"' + (Join-Path $outputRoot 'ShelterPPValidation.dll') + '"')
$response = Join-Path $outputRoot 'Compile.rsp'
$arguments | Set-Content -LiteralPath $response -Encoding utf8
$compiler = 'E:/6000.2.12f1/Editor/Data/DotNetSdkRoslyn/csc.dll'
$runtime = 'E:/6000.2.12f1/Editor/Data/NetCoreRuntime/dotnet.exe'
Push-Location -LiteralPath 'E:/DD451'
try
{
    $compileLog = @(& $runtime $compiler ('@' + $response) 2>&1)
    $compileExit = $LASTEXITCODE
    $compileLog | Set-Content -LiteralPath (Join-Path $outputRoot 'Compile.log') -Encoding utf8
    $result = [pscustomobject]@{ method = 'Offline Roslyn C#6 compile against existing local Unity and project reference assemblies'; exitCode = $compileExit; utc = [DateTime]::UtcNow.ToString('o'); sourceFiles = $sources; unityCalled = $false; assetsModified = $false; runtimeExecution = $false; diagnostics = $compileLog }
    $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputRoot 'Result.json') -Encoding utf8
    $result | ConvertTo-Json -Depth 4
}
finally { Pop-Location }
exit $compileExit
