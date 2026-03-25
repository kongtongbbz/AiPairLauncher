[CmdletBinding()]
param(
    [string]$SetupProjectPath = "",
    [string]$ProjectPath = "",
    [string]$DotnetPath = "C:\Program Files\dotnet\dotnet.exe",
    [string]$Configuration = "Release",
    [string]$PublishAppDir = "",
    [string]$BundleRoot = "",
    [string]$ProductVersion = ""
)

$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    return Split-Path -Parent $PSScriptRoot
}

function Get-ProjectVersion {
    param([Parameter(Mandatory = $true)][string]$CsprojPath)

    $projectXml = [xml](Get-Content -LiteralPath $CsprojPath -Raw)
    $versionNode = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
    if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
        return "1.2.0"
    }

    return $versionNode.InnerText.Trim()
}

function Assert-PathExists {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Label not found: $Path"
    }
}

$repoRoot = Resolve-RepoRoot

if ([string]::IsNullOrWhiteSpace($SetupProjectPath)) {
    $SetupProjectPath = Join-Path $repoRoot "AiPairLauncher.Setup\AiPairLauncher.Setup.wixproj"
}

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $repoRoot "AiPairLauncher.App\AiPairLauncher.App.csproj"
}

if ([string]::IsNullOrWhiteSpace($PublishAppDir)) {
    $PublishAppDir = Join-Path $repoRoot "artifacts\win-x64\app"
}

if ([string]::IsNullOrWhiteSpace($BundleRoot)) {
    $BundleRoot = Join-Path $repoRoot "artifacts\win-x64"
}

if ([string]::IsNullOrWhiteSpace($ProductVersion)) {
    $ProductVersion = Get-ProjectVersion -CsprojPath $ProjectPath
}

Assert-PathExists -Path $DotnetPath -Label "dotnet.exe"
Assert-PathExists -Path $SetupProjectPath -Label "Setup project"
Assert-PathExists -Path $ProjectPath -Label "Application project"
Assert-PathExists -Path $PublishAppDir -Label "Published app directory"
Assert-PathExists -Path (Join-Path $PublishAppDir "AiPairLauncher.App.exe") -Label "Published executable"
Assert-PathExists -Path (Join-Path $BundleRoot "install\launch.cmd") -Label "Launch wrapper"
Assert-PathExists -Path (Join-Path $BundleRoot "install\preflight-check.ps1") -Label "Preflight script"
Assert-PathExists -Path (Join-Path $BundleRoot "README.md") -Label "README"
Assert-PathExists -Path (Join-Path $BundleRoot "LICENSE") -Label "LICENSE"

$msiPath = Join-Path $BundleRoot "AiPairLauncher.msi"
$symbolsPath = Join-Path $BundleRoot "AiPairLauncher.wixpdb"

if (Test-Path -LiteralPath $msiPath) {
    Remove-Item -LiteralPath $msiPath -Force
}

if (Test-Path -LiteralPath $symbolsPath) {
    Remove-Item -LiteralPath $symbolsPath -Force
}

& $DotnetPath build $SetupProjectPath `
    -c $Configuration `
    -p:PublishAppDir="$PublishAppDir" `
    -p:BundleRoot="$BundleRoot" `
    -p:ProductVersion="$ProductVersion"

if ($LASTEXITCODE -ne 0) {
    throw "dotnet build for MSI failed with exit code $LASTEXITCODE."
}

Assert-PathExists -Path $msiPath -Label "MSI package"

Write-Output @"
BundleRoot     : $BundleRoot
PublishAppDir  : $PublishAppDir
ProductVersion : $ProductVersion
MsiPath        : $msiPath
Status         : msi-built
"@
