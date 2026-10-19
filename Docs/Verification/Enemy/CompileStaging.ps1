param([string]$ProjectRoot = 'E:\DD451')
$ErrorActionPreference = 'Stop'
# 仅编译009草稿，不启动Editor，不导入Assets，不写Library或其他任务的验证结果。
$stageRoot = Join-Path $ProjectRoot 'Docs\Verification\Enemy\Staging\Assets'
$outputRoot = Join-Path $ProjectRoot 'Temp\Task009OfflineCompile'
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$compiler = 'E:\6000.2.12f1\Editor\Data\DotNetSdkRoslyn\csc.dll'
$runtime = 'E:\6000.2.12f1\Editor\Data\NetCoreRuntime\dotnet.exe'
$definitions = Get-ChildItem -LiteralPath $stageRoot -Recurse -Filter '*.asmdef'
$sources = Get-ChildItem -LiteralPath $stageRoot -Recurse -Filter '*.cs'
$order = @('MyPrecious.Enemy', 'MyPrecious.Enemy.TestSupport', 'MyPrecious.Enemy.EditModeTests', 'MyPrecious.Enemy.PlayModeTests', 'MyPrecious.Enemy.Editor')
$results = @()
Push-Location -LiteralPath $ProjectRoot
try
{
    foreach ($assemblyName in $order)
    {
        $definitionFile = $definitions | Where-Object { (Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).name -eq $assemblyName } | Select-Object -First 1
        if ($null -eq $definitionFile) { throw "Missing assembly: $assemblyName" }
        $definition = Get-Content -LiteralPath $definitionFile.FullName -Raw | ConvertFrom-Json
        $templateName = if ($assemblyName -match 'Tests$') { 'MyPrecious.Player.EditModeTests' } elseif ($assemblyName -match '\.Editor$') { 'MyPrecious.Player.EditorTests' } else { 'MyPrecious.Player' }
        $template = Get-ChildItem -LiteralPath 'Library/Bee/artifacts' -Recurse -Filter ($templateName + '.rsp') | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        if ($null -eq $template) { throw "Missing compiler template: $templateName" }
        $arguments = [Collections.Generic.List[string]]::new()
        foreach ($line in Get-Content -LiteralPath $template.FullName)
        {
            if ($line -match '^[-/](out|refout|pdb|errorlog|generatedfilesout|additionalfile):' -or $line -match '^".*\.cs"$') { continue }
            if ($line -match '^[-/]r:"([^"]+)"$')
            {
                $dependency = $Matches[1]
                $filename = [IO.Path]::GetFileName($dependency) -replace '\.ref\.dll$', '.dll'
                if ($filename.StartsWith('MyPrecious.')) { continue }
                if ($dependency.StartsWith('Library/Bee/'))
                {
                    $imported = Join-Path $ProjectRoot ('Library\ScriptAssemblies\' + $filename)
                    if (Test-Path -LiteralPath $imported) { $line = '-r:"' + $imported + '"' }
                }
            }
            $arguments.Add($line)
        }
        foreach ($reference in $definition.references)
        {
            $filename = if ($reference -eq 'Unity.ugui') { 'UnityEngine.UI.dll' } else { $reference + '.dll' }
            $path = if ($order -contains $reference) { Join-Path $outputRoot $filename } else { Join-Path $ProjectRoot ('Library\ScriptAssemblies\' + $filename) }
            if (-not (Test-Path -LiteralPath $path)) { throw "Missing referenced assembly: $path" }
            $arguments.Add('-r:"' + $path + '"')
        }
        foreach ($source in $sources)
        {
            $owner = $definitions | Where-Object { $source.FullName.StartsWith($_.DirectoryName + '\', [StringComparison]::OrdinalIgnoreCase) } | Sort-Object { $_.DirectoryName.Length } -Descending | Select-Object -First 1
            if ($owner.FullName -eq $definitionFile.FullName) { $arguments.Add('"' + $source.FullName + '"') }
        }
        $arguments.Add('-out:"' + (Join-Path $outputRoot ($assemblyName + '.dll')) + '"')
        $response = Join-Path $outputRoot ($assemblyName + '.rsp')
        $arguments | Set-Content -LiteralPath $response -Encoding utf8
        $log = @(& $runtime $compiler ('@' + $response) 2>&1)
        $code = $LASTEXITCODE
        $log | Set-Content -LiteralPath (Join-Path $outputRoot ($assemblyName + '.log')) -Encoding utf8
        $results += [pscustomobject]@{ assembly=$assemblyName; exitCode=$code; log=($log -join "`n") }
        if ($code -ne 0) { $log | Write-Output; break }
        Write-Output ($assemblyName + ': compiled')
    }
}
finally
{
    $results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'OfflineCompilation.json') -Encoding utf8
    Pop-Location
}
if (@($results | Where-Object exitCode -ne 0).Count -gt 0) { exit 1 }
