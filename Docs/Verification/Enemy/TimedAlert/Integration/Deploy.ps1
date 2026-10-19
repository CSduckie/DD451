$ErrorActionPreference = 'Stop'
$projectRoot = 'E:/DD451'
$integration = Join-Path $projectRoot 'Docs/Verification/Enemy/TimedAlert/Integration'
$before = Get-Content (Join-Path $integration 'BeforeHashes.json') -Raw | ConvertFrom-Json
foreach ($entry in $before)
{
    if ((Get-FileHash -LiteralPath $entry.Path).Hash -ne $entry.Hash)
    {
        throw ('File changed since backup: ' + $entry.Path)
    }
}
$sourceRoot = Join-Path $projectRoot 'Docs/Verification/Enemy/TimedAlert/SourceStaging'
$testRoot = Join-Path $projectRoot 'Docs/Verification/Enemy/TimedAlert/TestStaging'
$integrationTestRoot = Join-Path $projectRoot 'Docs/Verification/Enemy/TimedAlert/IntegrationTestStaging'
$entries = @(
    @{ root=$sourceRoot; path='Assets/Scripts/Enemy/Drone/MonoBehaviour/DroneController.cs' },
    @{ root=$sourceRoot; path='Assets/Scripts/Enemy/Drone/MonoBehaviour/DroneVision.cs' },
    @{ root=$sourceRoot; path='Assets/Scripts/Enemy/Drone/States/DroneAlertState.cs' },
    @{ root=$sourceRoot; path='Assets/Scripts/Enemy/Drone/States/DroneChaseState.cs' },
    @{ root=$sourceRoot; path='Assets/Scripts/Enemy/Drone/ScriptableObject/DroneConfigSO.cs' },
    @{ root=$sourceRoot; path='Assets/Data/Enemy/Drone/DroneConfig.asset' },
    @{ root=$sourceRoot; path='Assets/Tests/Enemy/Editor/DroneVisionEditor.cs' },
    @{ root=$sourceRoot; path='Assets/Tests/Enemy/Editor/EnemyTestSceneBuilder.cs' },
    @{ root=$sourceRoot; path='Assets/Tests/Enemy/PlayMode/Runtime/EnemyTestDriver.cs' },
    @{ root=$sourceRoot; path='Assets/Tests/Enemy/EditMode/DroneVisionViewTests.cs' },
    @{ root=$testRoot; path='Assets/Tests/Enemy/PlayMode/DroneAlertTests.cs' },
    @{ root=$testRoot; path='Assets/Tests/Enemy/PlayMode/DroneBehaviourTests.cs' },
    @{ root=$testRoot; path='Assets/Tests/Enemy/PlayMode/DroneStatusLightTests.cs' },
    @{ root=$integrationTestRoot; path='Assets/Tests/Enemy/PlayMode/EnemySceneIntegrationTests.cs' },
    @{ root=$integrationTestRoot; path='Assets/Tests/GameFlow/PlayMode/RuinsEnemyIntegrationTests.cs' }
)
foreach ($entry in $entries)
{
    if (!(Test-Path -LiteralPath (Join-Path $entry.root $entry.path))) { throw ('Missing stage: '+$entry.path) }
}
$oldTest = Join-Path $projectRoot 'Assets/Tests/Enemy/EditMode/DroneVisionAlertFillTests.cs'
$newTest = Join-Path $projectRoot 'Assets/Tests/Enemy/EditMode/DroneVisionViewTests.cs'
if ((Test-Path -LiteralPath $newTest) -or (Test-Path -LiteralPath ($newTest+'.meta'))) { throw 'Rename target already exists' }
Move-Item -LiteralPath $oldTest -Destination $newTest
Move-Item -LiteralPath ($oldTest+'.meta') -Destination ($newTest+'.meta')
$manifest = @()
foreach ($entry in $entries)
{
    $source = Join-Path $entry.root $entry.path
    $destination = Join-Path $projectRoot $entry.path
    Copy-Item -LiteralPath $source -Destination $destination
    $manifest += [pscustomobject]@{ path=$destination; source=$source; hash=(Get-FileHash -LiteralPath $destination).Hash }
}
$manifest | ConvertTo-Json | Set-Content (Join-Path $integration 'DeployedManifest.json') -Encoding UTF8
'Deployed '+$manifest.Count+' reviewed files; renamed visual test with its existing meta.'

