[CmdletBinding()]
param(
    [string]$TargetRoot = ""
)

$ErrorActionPreference = "Stop"

function Resolve-TargetRoot {
    param([string]$ExplicitRoot)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitRoot)) {
        return $ExplicitRoot
    }

    $scriptDir = Split-Path -Parent $PSCommandPath
    $repoRoot = Split-Path -Parent $scriptDir
    $candidates = @(
        $scriptDir,
        $repoRoot,
        (Join-Path $repoRoot "artifacts\win-x64"),
        (Join-Path $env:LOCALAPPDATA "AiPairLauncher")
    )

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-Path -LiteralPath (Join-Path $candidate "app\AiPairLauncher.App.exe")) {
            return $candidate
        }
    }

    throw "Unable to resolve target root automatically. Please pass -TargetRoot."
}

function Resolve-CommandPath {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [string[]]$PreferredPaths = @()
    )

    foreach ($candidate in $PreferredPaths) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        $expandedPath = [Environment]::ExpandEnvironmentVariables($candidate)
        if (Test-Path -LiteralPath $expandedPath) {
            return $expandedPath
        }
    }

    $command = Get-Command $Name -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $command) {
        return $command.Source
    }

    return $null
}

function Get-VersionLine {
    param([Parameter(Mandatory = $true)][string]$CommandPath)

    try {
        $output = & $CommandPath --version 2>&1 |
            ForEach-Object { $_.ToString().Trim() } |
            Where-Object { $_ } |
            Select-Object -First 1

        if ([string]::IsNullOrWhiteSpace($output)) {
            return "unknown"
        }

        return $output
    }
    catch {
        return "version-check-failed"
    }
}

function Test-Dependency {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [string[]]$PreferredPaths = @()
    )

    $resolvedPath = Resolve-CommandPath -Name $Name -PreferredPaths $PreferredPaths
    if ([string]::IsNullOrWhiteSpace($resolvedPath)) {
        return [PSCustomObject]@{
            Name    = $Name
            Status  = "Missing"
            Path    = ""
            Version = ""
            Message = "command not found"
        }
    }

    return [PSCustomObject]@{
        Name    = $Name
        Status  = "Available"
        Path    = $resolvedPath
        Version = Get-VersionLine -CommandPath $resolvedPath
        Message = ""
    }
}

function Test-Layout {
    param([Parameter(Mandatory = $true)][string]$Root)

    $launchCandidates = @(
        (Join-Path $Root "launch.cmd"),
        (Join-Path $Root "install\launch.cmd")
    )

    $launchPath = $launchCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    $preflightCandidates = @(
        (Join-Path $Root "preflight-check.ps1"),
        (Join-Path $Root "install\preflight-check.ps1")
    )

    $preflightPath = $preflightCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    $exePath = Join-Path $Root "app\AiPairLauncher.App.exe"
    $configPath = Join-Path $Root "app\config\app.wezterm.lua"

    return [PSCustomObject]@{
        LaunchScriptFound = [bool]$launchPath
        LaunchScriptPath  = $launchPath
        PreflightFound    = [bool]$preflightPath
        PreflightPath     = $preflightPath
        ExecutableFound   = Test-Path -LiteralPath $exePath
        ExecutablePath    = $exePath
        ConfigFound       = Test-Path -LiteralPath $configPath
        ConfigPath        = $configPath
    }
}

$resolvedRoot = Resolve-TargetRoot -ExplicitRoot $TargetRoot
$layout = Test-Layout -Root $resolvedRoot
$dependencies = @(
    (Test-Dependency -Name "wezterm" -PreferredPaths @(
        "C:\Program Files\WezTerm\wezterm.exe",
        "%LOCALAPPDATA%\Microsoft\WinGet\Links\wezterm.exe"
    )),
    (Test-Dependency -Name "claude" -PreferredPaths @(
        "%USERPROFILE%\.local\bin\claude.exe"
    )),
    (Test-Dependency -Name "codex" -PreferredPaths @(
        "%APPDATA%\npm\codex.cmd",
        "%APPDATA%\npm\codex.ps1"
    ))
)

$layoutFailures = @()
if (-not $layout.LaunchScriptFound) { $layoutFailures += "launch.cmd missing" }
if (-not $layout.PreflightFound) { $layoutFailures += "preflight-check.ps1 missing" }
if (-not $layout.ExecutableFound) { $layoutFailures += "AiPairLauncher.App.exe missing" }
if (-not $layout.ConfigFound) { $layoutFailures += "app.wezterm.lua missing" }

$dependencyFailures = $dependencies | Where-Object { $_.Status -ne "Available" }
$failed = ($layoutFailures.Count -gt 0) -or ($dependencyFailures.Count -gt 0)

Write-Output "TargetRoot : $resolvedRoot"
Write-Output "LaunchPath : $($layout.LaunchScriptPath)"
Write-Output "ConfigPath : $($layout.ConfigPath)"
Write-Output "Executable : $($layout.ExecutablePath)"
Write-Output ""
Write-Output "Dependencies:"
$dependencies | Format-Table -AutoSize | Out-String | Write-Output

if ($layoutFailures.Count -gt 0) {
    Write-Output "LayoutIssues:"
    $layoutFailures | ForEach-Object { Write-Output " - $_" }
}

if ($failed) {
    throw "preflight-failed"
}

Write-Output "Status     : preflight-passed"
