#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)] [string] $ConfigurationPath, [Parameter(Mandatory)] [string] $FoundationPath,
      [Parameter(Mandatory)] [string] $CommitSha, [Parameter(Mandatory)] [string] $AzureCliPath, [Parameter(Mandatory)] [string] $ManifestPath)
. (Join-Path $PSScriptRoot 'Validation.ps1')
$config = Read-TaskManagerConfiguration $ConfigurationPath
$foundation = Get-Content -LiteralPath $FoundationPath -Raw | ConvertFrom-Json -AsHashtable -Depth 100
Assert-TaskManagerFoundationOutputs $foundation
if ($CommitSha -cnotmatch '^[a-f0-9]{40}$') { throw 'A complete reviewed commit SHA is required.' }
$invoker = New-TaskManagerInvoker $AzureCliPath; Assert-TaskManagerContext $invoker $config
$buildx = & docker buildx version
if ($LASTEXITCODE -ne 0 -or $buildx -notmatch 'v0\.38\.0\b') { throw 'Docker Buildx v0.38.0 is required; use the pinned setup action or install that official binary.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$commit = & git -C $repository rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $commit -cne $CommitSha) { throw 'Build checkout does not match the release SHA.' }
$changes = & git -C $repository status --porcelain --untracked-files=normal
if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Immutable release images require a clean checkout.' }
$contract = Get-Content -LiteralPath (Join-Path $repository 'infra/azure/deployment-contract.json') -Raw | ConvertFrom-Json -AsHashtable
$registryName = $foundation.registryResourceId.Split('/')[-1]
$registry = & $invoker @('acr', 'show', '--name', $registryName, '--query', '{id:id,loginServer:loginServer}')
if ($registry.id -ine $foundation.registryResourceId -or $registry.loginServer -cne $foundation.registryLoginServer) { throw 'Actual registry differs from the foundation outputs.' }
$stateDirectory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($ManifestPath))
Save-TaskManagerPrivateJson (Join-Path $stateDirectory 'build-context.json') @{ commitSha = $CommitSha; platform = 'linux/amd64'; buildxVersion = '0.38.0' }
if (-not $env:DOCKER_CONFIG) { $env:DOCKER_CONFIG = Join-Path $stateDirectory 'docker-config' }
$dockerConfigPath = [IO.Path]::GetFullPath($env:DOCKER_CONFIG)
if (-not $dockerConfigPath.StartsWith((Join-Path $repository '.local/azure') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Docker credentials must use the isolated repository-local delivery configuration.' }
$null = [IO.Directory]::CreateDirectory($env:DOCKER_CONFIG)
$null = & $invoker @('acr', 'login', '--name', $registryName)
$images = @{}
foreach ($unit in @('web', 'api', 'notifier', 'migrations', 'sqlBootstrap')) {
    $repo = $contract.repositories[$unit]; $target = $contract.dockerTargets[$unit]
    $metadataPath = Join-Path $stateDirectory "$unit.build-metadata.json"
    # Tags are only the upload handle. Deployment consumes the registry-confirmed digest.
    & docker buildx build --platform linux/amd64 --target $target --tag "$($foundation.registryLoginServer)/${repo}:$CommitSha" --push --provenance=mode=min --metadata-file $metadataPath $repository
    if ($LASTEXITCODE -ne 0) { throw "Immutable image build failed: $unit." }
    $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json -AsHashtable
    $digest = $metadata['containerimage.digest']
    if ($digest -cnotmatch '^sha256:[a-f0-9]{64}$') { throw 'Build did not return a complete immutable image digest.' }
    $actual = & $invoker @('acr', 'repository', 'show', '--name', $registryName, '--image', "$repo@$digest", '--query', '{digest:digest}')
    if ($actual.digest -cne $digest) { throw 'Registry digest verification failed.' }
    $images[$unit] = @{ repository = $repo; digest = $digest; reference = "$($foundation.registryLoginServer)/$repo@$digest" }
}
$manifest = @{ schemaVersion = 1; product = 'TaskManager'; commitSha = $CommitSha; subscriptionId = $config.subscriptionId; tenantId = $config.tenantId;
    resourceGroupName = $config.resourceGroupName; environmentName = $config.foundation.environmentName; resourceNamePrefix = $config.foundation.resourceNamePrefix;
    publicOrigin = $foundation.publicOrigin; registryResourceId = $foundation.registryResourceId; registryLoginServer = $foundation.registryLoginServer; images = $images }
Assert-TaskManagerReleaseManifest $manifest $foundation.registryLoginServer $CommitSha
Save-TaskManagerPrivateJson $ManifestPath $manifest
Write-Host 'Five immutable image references verified against the actual registry. Manifest saved privately.'
