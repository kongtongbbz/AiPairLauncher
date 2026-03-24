[CmdletBinding()]
param(
    [string]$SolutionPath = "",
    [string]$DotnetPath = "C:\Program Files\dotnet\dotnet.exe",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($SolutionPath)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $SolutionPath = Join-Path $repoRoot "AiPairLauncher.sln"
}

if (-not (Test-Path -LiteralPath $DotnetPath)) {
    throw "dotnet.exe not found: $DotnetPath"
}

if (-not (Test-Path -LiteralPath $SolutionPath)) {
    throw "Solution not found: $SolutionPath"
}

& $DotnetPath build $SolutionPath -c $Configuration

if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}
