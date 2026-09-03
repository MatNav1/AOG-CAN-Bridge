[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$VirtualTerminalDirectory,

    [Parameter(Mandatory = $true)]
    [string]$TaskControllerDirectory
)

$ErrorActionPreference = 'Stop'

function Restore-Application([string]$Directory, [string]$ApplicationExe) {
    if (-not (Test-Path -LiteralPath (Join-Path $Directory $ApplicationExe) -PathType Leaf)) {
        throw "Expected application was not found in: $Directory"
    }

    $directDll = Join-Path $Directory 'PCANBasicDirect.dll'
    if (-not (Test-Path -LiteralPath $directDll -PathType Leaf)) {
        throw "Direct-library backup was not found: $directDll"
    }

    Copy-Item -LiteralPath $directDll -Destination (Join-Path $Directory 'PCANBasic.dll') -Force
    Write-Host "Restored direct PCAN-Basic for $ApplicationExe"
}

Restore-Application $VirtualTerminalDirectory 'AgISOVirtualTerminal.exe'
Restore-Application $TaskControllerDirectory 'AOG-TaskController.exe'

Write-Host 'Direct PCAN-Basic libraries restored.' -ForegroundColor Green
