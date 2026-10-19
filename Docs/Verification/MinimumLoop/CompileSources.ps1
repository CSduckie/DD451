param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$UnityEditorRoot = 'E:\6000.2.12f1\Editor'
)

# 只使用Unity现有编译器与依赖；不启动Editor，不刷新Assets，不写Library。
# 此检查覆盖C#编译，不能代替Unity导入、EditMode/PlayMode测试或真实试玩。
$ErrorActionPreference = 'Stop'
$projectPath = [IO.Path]::GetFullPath($ProjectRoot)
$outputPath = [IO.Path]::GetFullPath((Join-Path $projectPath 'Temp\Task007OfflineCompile'))
$allowedOutputParent = [IO.Path]::GetFullPath((Join-Path $projectPath 'Temp')).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if (-not $outputPath.StartsWith($allowedOutputParent, [StringComparison]::OrdinalIgnoreCase))
{
    throw '离线编译中间件输出必须位于项目Temp目录内。'
}
$dotnetPath = Join-Path $UnityEditorRoot 'Data\NetCoreRuntime\dotnet.exe'
$compilerPath = Join-Path $UnityEditorRoot 'Data\DotNetSdkRoslyn\csc.dll'
foreach ($required in @($dotnetPath, $compilerPath))
{
    if (-not (Test-Path -LiteralPath $required)) { throw "缺少Unity编译器：$required" }
}
[IO.Directory]::CreateDirectory($outputPath) | Out-Null

$assemblies = @{}
$assemblyGuids = @{}
$sourceOwners = @{}
$sourceRoots = @((Join-Path $projectPath 'Assets\Scripts'), (Join-Path $projectPath 'Assets\Tests'))
foreach ($sourceRoot in $sourceRoots)
{
    foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -Filter '*.asmdef' -File)
    {
        $definition = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        if ($assemblies.ContainsKey($definition.name)) { throw "重复程序集：$($definition.name)" }
        if (@($definition.defineConstraints).Count -gt 0 -and $null -ne $definition.defineConstraints)
        {
            throw "当前离线检查未处理有条件程序集：$($definition.name)"
        }
        $assemblies[$definition.name] = [pscustomobject]@{
            Name = $definition.name
            Definition = $definition
            Path = $file.FullName
            References = @()
            Sources = [Collections.Generic.List[string]]::new()
        }
        $sourceOwners[$file.DirectoryName] = $definition.name
        $metaPath = $file.FullName + '.meta'
        if (Test-Path -LiteralPath $metaPath)
        {
            $match = [regex]::Match((Get-Content -LiteralPath $metaPath -Raw), '(?m)^guid:\s*(\w+)')
            if ($match.Success) { $assemblyGuids[$match.Groups[1].Value] = $definition.name }
        }
    }
}

function Resolve-AssemblyReference([string]$Reference)
{
    if ($Reference.StartsWith('GUID:', [StringComparison]::OrdinalIgnoreCase))
    {
        $guid = $Reference.Substring(5)
        if (-not $assemblyGuids.ContainsKey($guid)) { throw "不能解析程序集GUID：$Reference" }
        return $assemblyGuids[$guid]
    }
    return $Reference
}

foreach ($assembly in $assemblies.Values)
{
    $assembly.References = @($assembly.Definition.references | ForEach-Object { Resolve-AssemblyReference $_ })
}
# 06转移代码位于Items/Transfers，但通过asmref归Loot程序集，必须按真实边界编译。
foreach ($sourceRoot in $sourceRoots)
{
    foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -Filter '*.asmref' -File)
    {
        $reference = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        $owner = Resolve-AssemblyReference $reference.reference
        if (-not $assemblies.ContainsKey($owner)) { throw "asmref引用未知自有程序集：$owner" }
        $sourceOwners[$file.DirectoryName] = $owner
    }
    foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -Filter '*.cs' -File)
    {
        $directory = $file.Directory
        while ($null -ne $directory -and -not $sourceOwners.ContainsKey($directory.FullName))
        {
            $directory = $directory.Parent
        }
        if ($null -eq $directory) { throw "源码没有明确的asmdef归属：$($file.FullName)" }
        $assemblies[$sourceOwners[$directory.FullName]].Sources.Add($file.FullName)
    }
}

$buildOrder = [Collections.Generic.List[string]]::new()
$visitStates = @{}
function Visit-Assembly([string]$Name)
{
    if ($visitStates[$Name] -eq 2) { return }
    if ($visitStates[$Name] -eq 1) { throw "发现自有程序集循环引用：$Name" }
    $visitStates[$Name] = 1
    foreach ($reference in $assemblies[$Name].References)
    {
        if ($assemblies.ContainsKey($reference)) { Visit-Assembly $reference }
        elseif ($reference.StartsWith('MyPrecious.')) { throw "缺失自有程序集：$reference" }
    }
    $visitStates[$Name] = 2
    $buildOrder.Add($Name)
}
foreach ($name in @($assemblies.Keys | Sort-Object)) { Visit-Assembly $name }

# 从已存在的Bee响应文件读取Unity宏、系统引用与分析器参数。
$responseFiles = @{}
$beePath = Join-Path $projectPath 'Library\Bee\artifacts'
foreach ($file in Get-ChildItem -LiteralPath $beePath -Recurse -Filter 'MyPrecious.*.rsp' -File)
{
    if ($file.Name.EndsWith('.dll.mvfrm.rsp')) { continue }
    $name = $file.BaseName
    if (-not $responseFiles.ContainsKey($name) -or $file.LastWriteTimeUtc -gt $responseFiles[$name].LastWriteTimeUtc)
    {
        $responseFiles[$name] = $file
    }
}

$results = [Collections.Generic.List[object]]::new()
$combinedLog = [Collections.Generic.List[string]]::new()
$logPath = Join-Path $PSScriptRoot 'OfflineCompilation.log'
$startedAt = [DateTime]::UtcNow
$previousDirectory = Get-Location
try
{
    Set-Location -LiteralPath $projectPath
    foreach ($name in $buildOrder)
    {
        $assembly = $assemblies[$name]
        $templateName = $name
        if (-not $responseFiles.ContainsKey($templateName))
        {
            if (@($assembly.Definition.optionalUnityReferences) -contains 'TestAssemblies')
            {
                $templateName = 'MyPrecious.Player.EditModeTests'
            }
            elseif (@($assembly.Definition.includePlatforms) -contains 'Editor')
            {
                $templateName = 'MyPrecious.Player.EditorTests'
            }
            else { $templateName = 'MyPrecious.Player' }
        }
        if (-not $responseFiles.ContainsKey($templateName)) { throw "缺少可复用的Unity响应文件：$name" }

        $arguments = [Collections.Generic.List[string]]::new()
        $externalReferences = @{}
        foreach ($line in Get-Content -LiteralPath $responseFiles[$templateName].FullName)
        {
            # 原输出全部丢弃；源码按当前磁盘重新收集，不沿用过期导入记录。
            if ($line -match '^[-/](out|refout|pdb|errorlog|generatedfilesout|additionalfile):' -or $line -match '^".*\.cs"$') { continue }
            if ($line -match '^[-/]r:"([^"]+)"$')
            {
                $referencePath = $Matches[1]
                $referenceName = [IO.Path]::GetFileName($referencePath) -replace '\.ref\.dll$', '.dll'
                if ($referenceName.StartsWith('MyPrecious.')) { continue }
                if ($referencePath.StartsWith('Library/Bee/'))
                {
                    $compiledDependency = Join-Path $projectPath ("Library\ScriptAssemblies\" + $referenceName)
                    if (Test-Path -LiteralPath $compiledDependency) { $referencePath = $compiledDependency }
                }
                $externalReferences[$referenceName] = $referencePath
                continue
            }
            $arguments.Add($line)
        }

        foreach ($reference in $assembly.References)
        {
            if ($assemblies.ContainsKey($reference))
            {
                $arguments.Add('-r:"' + (Join-Path $outputPath ($reference + '.dll')) + '"')
            }
            else
            {
                $fileName = if ($reference -eq 'Unity.ugui') { 'UnityEngine.UI.dll' } else { $reference + '.dll' }
                if (-not $externalReferences.ContainsKey($fileName))
                {
                    $dependencyPath = Join-Path $projectPath ("Library\ScriptAssemblies\" + $fileName)
                    if (-not (Test-Path -LiteralPath $dependencyPath)) { throw "缺少已导入的外部依赖：$reference" }
                    $externalReferences[$fileName] = $dependencyPath
                }
            }
        }
        foreach ($fileName in @($externalReferences.Keys | Sort-Object))
        {
            $arguments.Add('-r:"' + $externalReferences[$fileName] + '"')
        }
        foreach ($source in @($assembly.Sources | Sort-Object)) { $arguments.Add('"' + $source + '"') }
        $arguments.Add('-out:"' + (Join-Path $outputPath ($name + '.dll')) + '"')
        $arguments.Add('-refout:"' + (Join-Path $outputPath ($name + '.ref.dll')) + '"')
        $responsePath = Join-Path $outputPath ($name + '.rsp')
        [IO.File]::WriteAllLines($responsePath, $arguments, [Text.UTF8Encoding]::new($false))

        $compileOutput = @(& $dotnetPath $compilerPath ('@' + $responsePath) 2>&1)
        $exitCode = $LASTEXITCODE
        $summary = "{0}: exit={1}, sources={2}" -f $name, $exitCode, $assembly.Sources.Count
        $combinedLog.Add($summary)
        foreach ($line in $compileOutput) { $combinedLog.Add([string]$line) }
        $results.Add([pscustomobject]@{
            Assembly = $name
            SourceCount = $assembly.Sources.Count
            TemplateAssembly = $templateName
            ExitCode = $exitCode
            Log = $logPath
        })
        Write-Output $summary
        if ($exitCode -ne 0)
        {
            $compileOutput | ForEach-Object { Write-Output $_ }
            throw "离线编译失败：$name；详见$logPath"
        }
    }
}
finally
{
    Set-Location -LiteralPath $previousDirectory.Path
    $report = [pscustomobject]@{
        Check = 'Offline C# compilation only; Unity import, tests and gameplay are not executed.'
        StartedUtc = $startedAt.ToString('o')
        FinishedUtc = [DateTime]::UtcNow.ToString('o')
        ProjectRoot = $projectPath
        OutputDirectory = $outputPath
        ExpectedAssemblyCount = $buildOrder.Count
        CompiledAssemblyCount = $results.Count
        Passed = ($results.Count -eq $buildOrder.Count -and @($results | Where-Object ExitCode -ne 0).Count -eq 0)
        Assemblies = $results.ToArray()
    }
    [IO.File]::WriteAllLines($logPath, $combinedLog, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'OfflineCompilationResults.json'),
        ($report | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
}
