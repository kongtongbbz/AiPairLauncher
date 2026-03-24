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

if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    $SourceRoot = Resolve-ScriptSourceRoot
}

if ($WaitTimeoutSeconds -lt 1) {
    throw "WaitTimeoutSeconds must be greater than 0."
}

Ensure-Directory -Path $InstallRoot
Ensure-Directory -Path (Join-Path $InstallRoot "app")

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

Remove-Item -LiteralPath (Join-Path $InstallRoot "app") -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath $PublishedAppDir -Destination (Join-Path $InstallRoot "app") -Recurse -Force
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
