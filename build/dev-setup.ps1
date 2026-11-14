<#
.SYNOPSIS
  Sets up a Windows machine for developing bpp-wsl: Git, .NET SDK 10, GitHub CLI,
  Claude Code and a clone of the repository.

.DESCRIPTION
  Idempotent: every step first checks whether the tool is already there. Each tool is
  installed with winget when that works; otherwise the script falls back to a per-user
  install that needs neither administrator rights nor a UAC prompt, so it also runs in
  a non-interactive SSH session.

  Works in Windows PowerShell 5.1 and PowerShell 7.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File dev-setup.ps1

.EXAMPLE
  # From another machine:
  scp build/dev-setup.ps1 user@winbox:dev-setup.ps1
  ssh user@winbox "powershell -NoProfile -ExecutionPolicy Bypass -File dev-setup.ps1 -Verify"
#>
[CmdletBinding()]
param(
    # Where the repository is cloned.
    [string]$RepoDir = (Join-Path $HOME 'Programowanie\bpp-wsl'),
    [string]$RepoUrl = 'https://github.com/iplweb/bpp-wsl.git',
    # Do not clone the repository.
    [switch]$SkipClone,
    # Run "dotnet test installer" at the end to prove the toolchain works.
    [switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest is many times slower with the progress bar
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$DotnetChannel = '10.0'   # keep in sync with installer/global.json
$UserPrograms = Join-Path $env:LOCALAPPDATA 'Programs'
$TempDir = Join-Path ([IO.Path]::GetTempPath()) "bpp-dev-setup-$PID"
New-Item -ItemType Directory -Force -Path $TempDir | Out-Null

function Write-Step([string]$Text) { Write-Host "`n==> $Text" -ForegroundColor Cyan }
function Write-Info([string]$Text) { Write-Host "    $Text" }

function Test-Command([string]$Name) {
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

# Re-reads PATH from the registry, so tools installed a moment ago are visible in this session.
function Update-SessionPath {
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = (@($machine, $user) | Where-Object { $_ }) -join ';'
}

# Adds a directory to the user's PATH (persistently) and to the current session.
function Add-UserPath([string]$Dir) {
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $entries = @()
    if ($user) { $entries = @($user -split ';' | Where-Object { $_ }) }
    if ($entries -notcontains $Dir) {
        $newPath = (@($entries) + $Dir) -join ';'
        [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
        Write-Info "Added to user PATH: $Dir"
    }
    if (($env:Path -split ';') -notcontains $Dir) { $env:Path = "$Dir;$env:Path" }
}

# Returns $true when winget installed the package. $false means "use the fallback":
# winget is often missing or unusable in SSH sessions and on older Windows 10 images.
function Install-WithWinget([string]$Id) {
    if (-not (Test-Command 'winget')) {
        Write-Info 'winget is not available here, using the direct installer.'
        return $false
    }
    Write-Info "winget install $Id"
    & winget install --id $Id --exact --silent --disable-interactivity `
        --accept-source-agreements --accept-package-agreements | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Write-Info "winget failed (exit code $LASTEXITCODE), using the direct installer."
        return $false
    }
    Update-SessionPath
    return $true
}

# Downloads the asset of the latest GitHub release whose name matches $Pattern.
function Get-LatestReleaseAsset([string]$Repo, [string]$Pattern) {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" `
        -Headers @{ 'User-Agent' = 'dev-setup-script' }
    $asset = @($release.assets | Where-Object { $_.name -match $Pattern }) | Select-Object -First 1
    if (-not $asset) { throw "No asset matching '$Pattern' in the latest release of $Repo." }
    $target = Join-Path $TempDir $asset.name
    Write-Info "Downloading $($asset.name)"
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $target -UseBasicParsing
    return $target
}

# Runs a downloaded script in a separate PowerShell process: it must not inherit this
# script's strict mode, and it must run whatever the machine's execution policy is.
function Invoke-DownloadedScript([string]$Url, [string]$Name, [string[]]$Arguments = @()) {
    $script = Join-Path $TempDir $Name
    Invoke-WebRequest -Uri $Url -OutFile $script -UseBasicParsing
    $shell = (Get-Process -Id $PID).Path
    & $shell -NoProfile -ExecutionPolicy Bypass -File $script @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$Name failed with exit code $LASTEXITCODE." }
}

function Assert-Command([string]$Name, [string]$What) {
    if (-not (Test-Command $Name)) {
        throw "$What was installed, but '$Name' is still not on PATH. Open a new terminal and run the script again."
    }
}

# --- 1. Git ---------------------------------------------------------------------------

function Install-Git {
    Write-Step '1/5 Git for Windows'
    if (Test-Command 'git') { Write-Info "Already installed: $(git --version)"; return }

    if (-not ((Install-WithWinget 'Git.Git') -and (Test-Command 'git'))) {
        # Without administrator rights the installer puts Git in %LOCALAPPDATA%\Programs\Git.
        $installer = Get-LatestReleaseAsset 'git-for-windows/git' '^Git-[\d.]+-64-bit\.exe$'
        $proc = Start-Process -FilePath $installer -Wait -PassThru `
            -ArgumentList '/VERYSILENT', '/NORESTART', '/NOCANCEL', '/SP-', '/SUPPRESSMSGBOXES'
        if ($proc.ExitCode -ne 0) { throw "The Git installer failed with exit code $($proc.ExitCode)." }
        Update-SessionPath
        foreach ($dir in @((Join-Path $env:ProgramFiles 'Git\cmd'), (Join-Path $UserPrograms 'Git\cmd'))) {
            if ((Test-Path (Join-Path $dir 'git.exe')) -and -not (Test-Command 'git')) { Add-UserPath $dir }
        }
    }
    Assert-Command 'git' 'Git'
    Write-Info "Installed: $(git --version)"
}

# Claude Code needs Git Bash. It finds it by itself in the default location; anywhere
# else it has to be told through CLAUDE_CODE_GIT_BASH_PATH.
function Set-GitBashPath {
    $gitRoot = Split-Path (Split-Path (Get-Command git).Source)
    $bash = Join-Path $gitRoot 'bin\bash.exe'
    $default = Join-Path $env:ProgramFiles 'Git\bin\bash.exe'
    if ((Test-Path $bash) -and ($bash -ne $default)) {
        [Environment]::SetEnvironmentVariable('CLAUDE_CODE_GIT_BASH_PATH', $bash, 'User')
        $env:CLAUDE_CODE_GIT_BASH_PATH = $bash
        Write-Info "CLAUDE_CODE_GIT_BASH_PATH = $bash"
    }
}

# --- 2. .NET SDK ----------------------------------------------------------------------

function Test-DotnetSdk {
    if (-not (Test-Command 'dotnet')) { return $false }
    $sdks = @(& dotnet --list-sdks)
    return [bool]($sdks | Where-Object { $_ -like "$DotnetChannel.*" })
}

function Install-Dotnet {
    Write-Step "2/5 .NET SDK $DotnetChannel"
    if (Test-DotnetSdk) { Write-Info 'Already installed.'; return }

    $major = $DotnetChannel.Split('.')[0]
    if (-not ((Install-WithWinget "Microsoft.DotNet.SDK.$major") -and (Test-DotnetSdk))) {
        # Microsoft's own script: a per-user install, no administrator rights needed.
        $dotnetDir = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
        Invoke-DownloadedScript 'https://dot.net/v1/dotnet-install.ps1' 'dotnet-install.ps1' `
            @('-Channel', $DotnetChannel, '-InstallDir', $dotnetDir)
        [Environment]::SetEnvironmentVariable('DOTNET_ROOT', $dotnetDir, 'User')
        $env:DOTNET_ROOT = $dotnetDir
        Add-UserPath $dotnetDir
        if ((Get-Command dotnet).Source -ne (Join-Path $dotnetDir 'dotnet.exe')) {
            Write-Warning ("Another dotnet.exe comes first on PATH ($((Get-Command dotnet).Source)). " +
                "The machine PATH wins over the user PATH, so new terminals may not see SDK $DotnetChannel; " +
                "install it machine-wide in that case.")
        }
    }
    if (-not (Test-DotnetSdk)) { throw ".NET SDK $DotnetChannel is still not visible to 'dotnet --list-sdks'." }
    Write-Info "Installed: $(& dotnet --version)"
}

# --- 3. GitHub CLI --------------------------------------------------------------------

function Install-GitHubCli {
    Write-Step '3/5 GitHub CLI'
    if (Test-Command 'gh') { Write-Info "Already installed: $(@(& gh --version)[0])"; return }

    if (-not ((Install-WithWinget 'GitHub.cli') -and (Test-Command 'gh'))) {
        $zip = Get-LatestReleaseAsset 'cli/cli' '^gh_[\d.]+_windows_amd64\.zip$'
        $ghDir = Join-Path $UserPrograms 'gh'
        if (Test-Path $ghDir) { Remove-Item -Recurse -Force $ghDir }
        Expand-Archive -Path $zip -DestinationPath $ghDir
        Add-UserPath (Join-Path $ghDir 'bin')
    }
    Assert-Command 'gh' 'GitHub CLI'
    Write-Info "Installed: $(@(& gh --version)[0])"
}

# --- 4. Claude Code -------------------------------------------------------------------

function Install-ClaudeCode {
    Write-Step '4/5 Claude Code'
    Set-GitBashPath
    $claudeBin = Join-Path $HOME '.local\bin'
    if (Test-Path (Join-Path $claudeBin 'claude.exe')) { Add-UserPath $claudeBin }
    if (Test-Command 'claude') { Write-Info "Already installed: $(& claude --version)"; return }

    Invoke-DownloadedScript 'https://claude.ai/install.ps1' 'claude-install.ps1'
    Add-UserPath $claudeBin
    Assert-Command 'claude' 'Claude Code'
    Write-Info "Installed: $(& claude --version)"
}

# --- 5. Repository --------------------------------------------------------------------

function Get-Repository {
    Write-Step '5/5 Repository'
    if ($SkipClone) { Write-Info 'Skipped (-SkipClone).'; return }
    if (Test-Path (Join-Path $RepoDir '.git')) { Write-Info "Already cloned: $RepoDir"; return }
    if (Test-Path $RepoDir) { throw "$RepoDir exists but is not a git repository. Move it away or pass -RepoDir." }

    New-Item -ItemType Directory -Force -Path (Split-Path $RepoDir) | Out-Null
    & git clone $RepoUrl $RepoDir
    if ($LASTEXITCODE -ne 0) { throw "git clone failed with exit code $LASTEXITCODE." }
    Write-Info "Cloned to $RepoDir"
}

function Test-Toolchain {
    Write-Step 'Verification: dotnet test installer'
    $installer = Join-Path $RepoDir 'installer'
    if (-not (Test-Path $installer)) { throw "Nothing to verify: $installer does not exist." }
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    & dotnet test $installer -c Release
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE." }
}

# --- main -----------------------------------------------------------------------------

try {
    $memoryGb = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
    Write-Host "Windows $([Environment]::OSVersion.Version), $memoryGb GB RAM, user $env:USERNAME"

    Update-SessionPath
    Install-Git
    Install-Dotnet
    Install-GitHubCli
    Install-ClaudeCode
    Get-Repository
    if ($Verify) { Test-Toolchain }
}
finally {
    Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue
}

Write-Host "`nDone. What is left needs you in person:" -ForegroundColor Green
Write-Host '  1. Open a NEW terminal (so it picks up the changed PATH).'
Write-Host '  2. gh auth login          - sign in to GitHub (needed to push and open PRs)'
Write-Host "  3. cd `"$RepoDir`""
Write-Host '  4. claude                 - the first start asks you to sign in'
