[CmdletBinding()]
param(
    [string]$OfficialPcanBasicDll = (Join-Path $PSScriptRoot 'Vendor\PCANBasic.dll'),
    [string]$VirtualTerminalDirectory = 'C:\Program Files\AgISOVirtualTerminal\bin',
    [string]$TaskControllerDirectory = 'C:\Program Files\AOG-TaskController\bin',
    [string]$BrokerDirectory,
    [string]$ProxyDll = (Join-Path $PSScriptRoot 'Proxy\PCANBasic.dll')
)

$ErrorActionPreference = 'Stop'

# The installer runs this step non-interactively, so a transcript is the
# only record of what it actually did to VT/TC's files.
$logPath = Join-Path $PSScriptRoot 'Install-Bridge.log'
try { Start-Transcript -Path $logPath -Force -ErrorAction Stop | Out-Null } catch { }

try {

# When this script is deployed next to the broker exe (the installer layout),
# install there instead of a Broker subfolder used by the old manual layout.
if (-not $BrokerDirectory) {
    $BrokerDirectory = if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'AogCanBridge.exe') -PathType Leaf) {
        $PSScriptRoot
    }
    else {
        Join-Path $PSScriptRoot 'Broker'
    }
}

function Resolve-ExistingFile([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description was not found: $Path"
    }
    return (Resolve-Path -LiteralPath $Path).Path
}

function Assert-PeakLibrary([string]$Path) {
    $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    if ($version.CompanyName -notlike '*PEAK-System*' -or
        $version.OriginalFilename -notlike 'PCANBasic*') {
        throw "The selected file is not recognized as an official PEAK-System PCANBasic library: $Path"
    }
    Write-Host "Official PCAN-Basic: $($version.FileVersion) ($($version.CompanyName))"
}

function Set-AsideLockedFile([string]$Path) {
    # A DLL currently loaded by a running process can still be renamed on
    # NTFS, but NOT deleted or overwritten in place (verified: Remove-Item
    # on a loaded DLL fails with access denied, while Move-Item to a fresh
    # name succeeds). So clearing $Path always renames it out of the way;
    # the discarded copy is then deleted best-effort, tolerating failure if
    # it's still locked - it's just disk clutter at that point, not state
    # anything depends on.
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return }
    $discardPath = "$Path.old-$([guid]::NewGuid().ToString('N')).tmp"
    Move-Item -LiteralPath $Path -Destination $discardPath -Force
    Remove-Item -LiteralPath $discardPath -Force -ErrorAction SilentlyContinue
}

function Install-ClientProxy(
    [string]$ApplicationDirectory,
    [string]$ApplicationExe,
    [string]$OfficialDll,
    [string]$BridgeProxy
) {
    # VT and TC are typically installed independently, and this script may
    # run before either exists. Skip a missing target instead of aborting
    # the whole run so re-running after installing the other one still works.
    if (-not (Test-Path -LiteralPath $ApplicationDirectory -PathType Container)) {
        Write-Warning "$ApplicationExe was not found (missing directory: $ApplicationDirectory). Skipped."
        return
    }
    $applicationPath = Join-Path $ApplicationDirectory $ApplicationExe
    if (-not (Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
        Write-Warning "$ApplicationExe was not found in: $ApplicationDirectory. Skipped."
        return
    }

    $activeDll = Join-Path $ApplicationDirectory 'PCANBasic.dll'
    $originalDll = Join-Path $ApplicationDirectory 'zPCANBasic.dll'

    # This never overwrites or deletes $activeDll directly (see
    # Set-AsideLockedFile): it always clears the name by renaming, then
    # copies a fresh file in. That makes it safe to run whether or not
    # VT/TC are currently running; either just needs restarting afterward
    # to pick up the change.
    if (-not (Test-Path -LiteralPath $originalDll -PathType Leaf)) {
        if (Test-Path -LiteralPath $activeDll -PathType Leaf) {
            try {
                Assert-PeakLibrary $activeDll
                Move-Item -LiteralPath $activeDll -Destination $originalDll -Force
            }
            catch {
                # $activeDll didn't look like a genuine PEAK library, so seed
                # the backup from our own known-good copy instead of trusting
                # it - but still clear it out of the way of the fresh file
                # written below.
                Copy-Item -LiteralPath $OfficialDll -Destination $originalDll -Force
                Set-AsideLockedFile $activeDll
            }
        }
        else {
            Copy-Item -LiteralPath $OfficialDll -Destination $originalDll -Force
        }
    }
    else {
        Assert-PeakLibrary $originalDll
        Set-AsideLockedFile $activeDll
    }

    Copy-Item -LiteralPath $BridgeProxy -Destination $activeDll -Force
    Write-Host "Installed bridge proxy for $ApplicationExe" -ForegroundColor Green
}

$officialDll = Resolve-ExistingFile $OfficialPcanBasicDll 'Official PCANBasic.dll'
$proxyDllPath = Resolve-ExistingFile $ProxyDll 'Bridge proxy PCANBasic.dll'

Assert-PeakLibrary $officialDll

if (-not (Test-Path -LiteralPath $BrokerDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $BrokerDirectory -Force | Out-Null
}
$brokerDirectoryPath = (Resolve-Path -LiteralPath $BrokerDirectory).Path
Copy-Item -LiteralPath $officialDll -Destination (Join-Path $brokerDirectoryPath 'PCANBasic.dll') -Force

Install-ClientProxy $VirtualTerminalDirectory 'AgISOVirtualTerminal.exe' $officialDll $proxyDllPath
Install-ClientProxy $TaskControllerDirectory 'AOG-TaskController.exe' $officialDll $proxyDllPath

Write-Host ''
Write-Host 'Installation complete.' -ForegroundColor Green
Write-Host "Start $(Join-Path $brokerDirectoryPath 'AogCanBridge.exe') before starting VT and Task Controller."
Write-Host 'If VT or Task Controller was already running, restart it to load the patched library.'

}
finally {
    try { Stop-Transcript | Out-Null } catch { }
}
