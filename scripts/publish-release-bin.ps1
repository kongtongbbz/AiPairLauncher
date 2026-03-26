[CmdletBinding()]
param(
    [string]$ProjectPath = "",
    [string]$DotnetPath = "C:\Program Files\dotnet\dotnet.exe",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    return Split-Path -Parent $PSScriptRoot
}

$repoRoot = Resolve-RepoRoot

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $repoRoot "AiPairLauncher.App\AiPairLauncher.App.csproj"
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "AiPairLauncher.App\bin\Release\net8.0-windows"
}

if (-not (Test-Path -LiteralPath $DotnetPath)) {
    throw "dotnet.exe not found: $DotnetPath"
}

if (-not (Test-Path -LiteralPath $ProjectPath)) {
    throw "Project not found: $ProjectPath"
}

if (Test-Path -LiteralPath $OutputDir) {
    cmd /c "rmdir /s /q ""$OutputDir"""
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

Write-Output @"
PublishedApp : $OutputDir
Status       : release-bin-publish-complete
"@
