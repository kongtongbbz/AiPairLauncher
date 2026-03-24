[CmdletBinding()]
param(
    [string]$BundleRoot = "",
    [string]$InstallRoot = (Join-Path $env:TEMP "AiPairLauncher-smoke")
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($BundleRoot)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $BundleRoot = Join-Path $repoRoot "artifacts\win-x64"
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

$installScript = Join-Path $BundleRoot "install\install.ps1"
$publishedAppDir = Join-Path $BundleRoot "app"

Assert-PathExists -Path $installScript -Label "Install script"
Assert-PathExists -Path $publishedAppDir -Label "Published app directory"

if (Test-Path -LiteralPath $InstallRoot) {
    Remove-Item -LiteralPath $InstallRoot -Recurse -Force
}

& $installScript `
    -SourceRoot $BundleRoot `
    -InstallRoot $InstallRoot `
    -PublishedAppDir $publishedAppDir `
    -SkipDesktopShortcut

$expectedPaths = @(
    (Join-Path $InstallRoot "launch.cmd"),
    (Join-Path $InstallRoot "preflight-check.ps1"),
    (Join-Path $InstallRoot "app\AiPairLauncher.App.exe"),
    (Join-Path $InstallRoot "app\config\app.wezterm.lua")
)

foreach ($path in $expectedPaths) {
    Assert-PathExists -Path $path -Label "Installed artifact"
}

Write-Output @"
SmokeInstallRoot : $InstallRoot
Executable       : $(Join-Path $InstallRoot "app\AiPairLauncher.App.exe")
ConfigFile       : $(Join-Path $InstallRoot "app\config\app.wezterm.lua")
ShortcutCreated  : false
Status           : smoke-test-passed
"@
