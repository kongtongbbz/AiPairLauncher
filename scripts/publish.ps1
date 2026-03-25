[CmdletBinding()]
param(
    [string]$ProjectPath = "",
    [string]$DotnetPath = "C:\Program Files\dotnet\dotnet.exe",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "",
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
        return "1.1.0"
    }

    return $versionNode.InnerText.Trim()
}

$repoRoot = Resolve-RepoRoot

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $repoRoot "AiPairLauncher.App\AiPairLauncher.App.csproj"
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "artifacts\win-x64\app"
}

if ([string]::IsNullOrWhiteSpace($ProductVersion)) {
    $ProductVersion = Get-ProjectVersion -CsprojPath $ProjectPath
}

if (-not (Test-Path -LiteralPath $DotnetPath)) {
    throw "dotnet.exe not found: $DotnetPath"
}

if (-not (Test-Path -LiteralPath $ProjectPath)) {
    throw "Project not found: $ProjectPath"
}

$bundleRoot = Split-Path -Parent $OutputDir
$installDir = Join-Path $bundleRoot "install"
$readyMarker = Join-Path $bundleRoot ".publish-ready"
$msiPath = Join-Path $bundleRoot "AiPairLauncher.msi"
$zipPath = Join-Path $bundleRoot "AiPairLauncher-win-x64.zip"
$buildMsiScript = Join-Path $repoRoot "scripts\build-msi.ps1"

if (Test-Path -LiteralPath $readyMarker) {
    Remove-Item -LiteralPath $readyMarker -Force
}

if (Test-Path -LiteralPath $OutputDir) {
    Remove-Item -LiteralPath $OutputDir -Recurse -Force
}

if (Test-Path -LiteralPath $installDir) {
    Remove-Item -LiteralPath $installDir -Recurse -Force
}

if (Test-Path -LiteralPath $msiPath) {
    Remove-Item -LiteralPath $msiPath -Force
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

& $DotnetPath publish $ProjectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $OutputDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot "install\install.ps1") -Destination (Join-Path $installDir "install.ps1") -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "install\launch.cmd") -Destination (Join-Path $installDir "launch.cmd") -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "install\preflight-check.ps1") -Destination (Join-Path $installDir "preflight-check.ps1") -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination (Join-Path $bundleRoot "README.md") -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination (Join-Path $bundleRoot "LICENSE") -Force

& $buildMsiScript `
    -ProjectPath $ProjectPath `
    -DotnetPath $DotnetPath `
    -Configuration $Configuration `
    -PublishAppDir $OutputDir `
    -BundleRoot $bundleRoot `
    -ProductVersion $ProductVersion

if ($LASTEXITCODE -ne 0) {
    throw "MSI build script failed with exit code $LASTEXITCODE."
}

Compress-Archive `
    -Path @(
        (Join-Path $bundleRoot "app"),
        (Join-Path $bundleRoot "install"),
        (Join-Path $bundleRoot "README.md"),
        (Join-Path $bundleRoot "LICENSE"),
        $msiPath
    ) `
    -DestinationPath $zipPath `
    -CompressionLevel Optimal `
    -Force

if (-not (Test-Path -LiteralPath $zipPath)) {
    throw "ZIP package not found after compress: $zipPath"
}

Set-Content -LiteralPath $readyMarker -Value "ready" -Encoding ASCII

Write-Output @"
BundleRoot     : $bundleRoot
PublishedApp   : $OutputDir
ProductVersion : $ProductVersion
MsiPath        : $msiPath
ZipPath        : $zipPath
Status         : publish-complete
"@
