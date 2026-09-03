[CmdletBinding()]
param(
    [string]$OfficialPcanBasicDll = (Join-Path $PSScriptRoot 'Vendor\PCANBasic.dll'),

    [Parameter(Mandatory = $true)]
    [string]$VirtualTerminalDirectory,

    [Parameter(Mandatory = $true)]
    [string]$TaskControllerDirectory,

    [string]$BrokerDirectory = (Join-Path $PSScriptRoot 'Broker'),
    [string]$ProxyDll = (Join-Path $PSScriptRoot 'Proxy\PCANBasic.dll')
)

$ErrorActionPreference = 'Stop'

function Resolve-ExistingFile([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description was not found: $Path"
    }
    return (Resolve-Path -LiteralPath $Path).Path
}

function Resolve-ExistingDirectory([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
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
    $applicationPath = Join-Path $ApplicationDirectory $ApplicationExe
    if (-not (Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
        throw "Expected application was not found: $applicationPath"
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
    Write-Host "Installed bridge proxy for $ApplicationExe"
}

$officialDll = Resolve-ExistingFile $OfficialPcanBasicDll 'Official PCANBasic.dll'
$proxyDllPath = Resolve-ExistingFile $ProxyDll 'Bridge proxy PCANBasic.dll'
$vtDirectory = Resolve-ExistingDirectory $VirtualTerminalDirectory 'Virtual Terminal directory'
$tcDirectory = Resolve-ExistingDirectory $TaskControllerDirectory 'Task Controller directory'

Assert-PeakLibrary $officialDll

if (-not (Test-Path -LiteralPath $BrokerDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $BrokerDirectory -Force | Out-Null
}
$brokerDirectoryPath = (Resolve-Path -LiteralPath $BrokerDirectory).Path
Copy-Item -LiteralPath $officialDll -Destination (Join-Path $brokerDirectoryPath 'PCANBasic.dll') -Force

Install-ClientProxy $vtDirectory 'AgISOVirtualTerminal.exe' $officialDll $proxyDllPath
Install-ClientProxy $tcDirectory 'AOG-TaskController.exe' $officialDll $proxyDllPath

Write-Host ''
Write-Host 'Installation complete.' -ForegroundColor Green
Write-Host 'Start Broker\AogCanBridge.exe before starting VT and Task Controller.'
