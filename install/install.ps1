[CmdletBinding()]
param(
    [string]$SourceRoot = "",
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA "AiPairLauncher"),
    [string]$PublishedAppDir = "",
    [switch]$SkipDesktopShortcut,
    [int]$WaitTimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"

function Resolve-ScriptSourceRoot {
    $scriptPath = $null

    if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        $scriptPath = Join-Path $PSScriptRoot "install.ps1"
    }
    elseif (-not [string]::IsNullOrWhiteSpace($PSCommandPath)) {
        $scriptPath = $PSCommandPath
    }
    elseif ($null -ne $MyInvocation.MyCommand.Path -and -not [string]::IsNullOrWhiteSpace($MyInvocation.MyCommand.Path)) {
        $scriptPath = $MyInvocation.MyCommand.Path
    }

    if ([string]::IsNullOrWhiteSpace($scriptPath)) {
        throw "Unable to resolve install.ps1 location automatically. Please pass -SourceRoot explicitly."
    }

    $installDir = Split-Path -Parent $scriptPath
    return Split-Path -Parent $installDir
}

function Ensure-Directory {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Wait-ForPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $Path) {
            return
        }

        Start-Sleep -Milliseconds 300
    }

    throw "$Label not found within timeout: $Path"
}

function Resolve-AbsolutePath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$BasePath = ""
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Path must not be empty."
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    if (-not [string]::IsNullOrWhiteSpace($BasePath)) {
        return [System.IO.Path]::GetFullPath((Join-Path $BasePath $Path))
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Invoke-WithRetry {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [Parameter(Mandatory = $true)][string]$Label,
        [int]$MaxAttempts = 3,
        [int]$DelayMilliseconds = 500
    )

    $lastError = $null
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            & $Action
            return
        }
        catch {
            $lastError = $_
            if ($attempt -eq $MaxAttempts) {
                break
            }

            Start-Sleep -Milliseconds $DelayMilliseconds
        }
    }

    throw "$Label failed after $MaxAttempts attempts. $($lastError.Exception.Message)"
}

if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    $SourceRoot = Resolve-ScriptSourceRoot
}

$SourceRoot = Resolve-AbsolutePath -Path $SourceRoot

if ($WaitTimeoutSeconds -lt 1) {
    throw "WaitTimeoutSeconds must be greater than 0."
}

if (-not [System.IO.Path]::IsPathRooted($InstallRoot)) {
    $InstallRoot = Resolve-AbsolutePath -Path $InstallRoot
}

Ensure-Directory -Path $InstallRoot
$appDir = Join-Path $InstallRoot "app"
$stagingDir = Join-Path $InstallRoot "app.staging"
$backupDir = Join-Path $InstallRoot "app.previous"

if ([string]::IsNullOrWhiteSpace($PublishedAppDir)) {
    $candidateDirs = @(
        (Join-Path $SourceRoot "app"),
        (Join-Path $SourceRoot "artifacts\win-x64\app")
    )

    $PublishedAppDir = $candidateDirs | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($PublishedAppDir)) {
    $PublishedAppDir = Join-Path $SourceRoot "app"
}

$PublishedAppDir = Resolve-AbsolutePath -Path $PublishedAppDir -BasePath $SourceRoot

$readyMarker = Join-Path $SourceRoot ".publish-ready"
if (Test-Path -LiteralPath $readyMarker) {
    Wait-ForPath -Path $readyMarker -Label "Publish ready marker" -TimeoutSeconds $WaitTimeoutSeconds
}

Wait-ForPath -Path $PublishedAppDir -Label "Published app directory" -TimeoutSeconds $WaitTimeoutSeconds
Wait-ForPath -Path (Join-Path $PublishedAppDir "AiPairLauncher.App.exe") -Label "Published executable" -TimeoutSeconds $WaitTimeoutSeconds
Wait-ForPath -Path (Join-Path $SourceRoot "install\launch.cmd") -Label "Launch wrapper" -TimeoutSeconds $WaitTimeoutSeconds
Wait-ForPath -Path (Join-Path $SourceRoot "install\preflight-check.ps1") -Label "Preflight script" -TimeoutSeconds $WaitTimeoutSeconds

if (-not (Test-Path -LiteralPath $PublishedAppDir)) {
    throw "Published app directory not found. Expected one of: '$SourceRoot\\app' or '$SourceRoot\\artifacts\\win-x64\\app'. Please run scripts\\publish.ps1 first."
}

Remove-Item -LiteralPath $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $backupDir -Recurse -Force -ErrorAction SilentlyContinue
Ensure-Directory -Path $stagingDir

Invoke-WithRetry -Label "Copy published app to staging" -Action {
    Remove-Item -LiteralPath $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
    Ensure-Directory -Path $stagingDir
    Get-ChildItem -LiteralPath $PublishedAppDir -Force | Copy-Item -Destination $stagingDir -Recurse -Force
    if (-not (Test-Path -LiteralPath (Join-Path $stagingDir "AiPairLauncher.App.exe"))) {
        throw "Staging executable missing after copy."
    }
}

Invoke-WithRetry -Label "Swap staged app into install root" -Action {
    Remove-Item -LiteralPath $backupDir -Recurse -Force -ErrorAction SilentlyContinue

    try {
        if (Test-Path -LiteralPath $appDir) {
            Move-Item -LiteralPath $appDir -Destination $backupDir -Force
        }

        Move-Item -LiteralPath $stagingDir -Destination $appDir -Force
        Remove-Item -LiteralPath $backupDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    catch {
        if ((-not (Test-Path -LiteralPath $appDir)) -and (Test-Path -LiteralPath $backupDir)) {
            Move-Item -LiteralPath $backupDir -Destination $appDir -Force
        }

        throw
    }
}

Copy-Item -LiteralPath (Join-Path $SourceRoot "install\launch.cmd") -Destination (Join-Path $InstallRoot "launch.cmd") -Force
Copy-Item -LiteralPath (Join-Path $SourceRoot "install\preflight-check.ps1") -Destination (Join-Path $InstallRoot "preflight-check.ps1") -Force

$shortcutPath = ""

if (-not $SkipDesktopShortcut) {
    $shortcutPath = Join-Path ([Environment]::GetFolderPath("Desktop")) "AiPairLauncher.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = (Join-Path $InstallRoot "launch.cmd")
    $shortcut.WorkingDirectory = $InstallRoot
    $shortcut.IconLocation = "$(Join-Path $InstallRoot "app\AiPairLauncher.App.exe"),0"
    $shortcut.Save()
}

Write-Output @"
InstallRoot : $InstallRoot
PublishedApp: $PublishedAppDir
Shortcut    : $shortcutPath
SkipShortcut: $SkipDesktopShortcut
Status      : installed
"@
