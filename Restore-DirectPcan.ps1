[CmdletBinding()]
param(
    [string]$VirtualTerminalDirectory = 'C:\Program Files\AgISOVirtualTerminal\bin',
    [string]$TaskControllerDirectory = 'C:\Program Files\AOG-TaskController\bin'
)

function Restore-Application([string]$Directory, [string]$ApplicationExe) {
    # Used both for manual restores and as the installer's uninstall step, so
    # a target that is missing or was never patched is skipped, not fatal.
    if (-not (Test-Path -LiteralPath (Join-Path $Directory $ApplicationExe) -PathType Leaf)) {
        Write-Warning "$ApplicationExe was not found in: $Directory. Skipped."
        return
    }

    $directDll = Join-Path $Directory 'PCANBasicDirect.dll'
    if (-not (Test-Path -LiteralPath $directDll -PathType Leaf)) {
        Write-Warning "$ApplicationExe has no direct-library backup to restore (not patched). Skipped."
        return
    }

    Copy-Item -LiteralPath $directDll -Destination (Join-Path $Directory 'PCANBasic.dll') -Force
    Write-Host "Restored direct PCAN-Basic for $ApplicationExe" -ForegroundColor Green
}

Restore-Application $VirtualTerminalDirectory 'AgISOVirtualTerminal.exe'
Restore-Application $TaskControllerDirectory 'AOG-TaskController.exe'

Write-Host 'Direct PCAN-Basic libraries restored where applicable.' -ForegroundColor Green
