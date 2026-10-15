[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.0',

    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\installer\package'),

    [string]$SigningThumbprint = '',

    [uri]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stagingRoot = Join-Path $repositoryRoot 'artifacts\installer'
$appDirectory = Join-Path $stagingRoot 'app'
$actionsDirectory = Join-Path $stagingRoot 'actions'
$installerProject = Join-Path $repositoryRoot 'src\Orchestrator.Installer\Orchestrator.Installer.wixproj'
$installerOutput = Join-Path $repositoryRoot 'src\Orchestrator.Installer\bin\Release\Orchestrator.Installer.msi'
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)

dotnet publish (Join-Path $repositoryRoot 'src\Orchestrator.Api\Orchestrator.Api.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $appDirectory
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish (Join-Path $repositoryRoot 'src\Orchestrator.InstallerActions\Orchestrator.InstallerActions.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $actionsDirectory
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $installerProject `
    --configuration Release `
    -p:ProductVersion=$Version `
    -p:PublishedAppDir=$appDirectory `
    -p:PublishedActionsDir=$actionsDirectory
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
$packagePath = Join-Path $resolvedOutput "Fsp.Orchestrator-$Version-win-x64.msi"
Copy-Item $installerOutput $packagePath -Force

if (-not [string]::IsNullOrWhiteSpace($SigningThumbprint)) {
    $signTool = Get-ChildItem `
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin" `
        -Filter signtool.exe `
        -Recurse |
        Where-Object FullName -Match '\\x64\\signtool\.exe$' |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($null -eq $signTool) {
        throw 'signtool.exe was not found in the Windows SDK.'
    }

    & $signTool.FullName sign `
        /sha1 $SigningThumbprint `
        /fd SHA256 `
        /tr $TimestampUrl.AbsoluteUri `
        /td SHA256 `
        $packagePath
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $signTool.FullName verify /pa /all $packagePath
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Output $packagePath
