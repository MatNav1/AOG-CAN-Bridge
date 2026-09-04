[CmdletBinding()]
param(
    [string]$OfficialPcanBasicDll = (Join-Path $PSScriptRoot 'Vendor\PCANBasic.dll'),
    [string]$VirtualTerminalDirectory = 'C:\Program Files\AgISOVirtualTerminal\bin',
    [string]$TaskControllerDirectory = 'C:\Program Files\AOG-TaskController\bin',
    [string]$BrokerDirectory,
    [string]$ProxyDll = (Join-Path $PSScriptRoot 'Proxy\PCANBasic.dll')
)

$ErrorActionPreference = 'Stop'

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
    $directDll = Join-Path $ApplicationDirectory 'PCANBasicDirect.dll'

    if (-not (Test-Path -LiteralPath $directDll -PathType Leaf)) {
        if (Test-Path -LiteralPath $activeDll -PathType Leaf) {
            try {
                Assert-PeakLibrary $activeDll
                Copy-Item -LiteralPath $activeDll -Destination $directDll
            }
            catch {
                Copy-Item -LiteralPath $OfficialDll -Destination $directDll
            }
        }
        else {
            Copy-Item -LiteralPath $OfficialDll -Destination $directDll
        }
    }
    else {
        Assert-PeakLibrary $directDll
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
