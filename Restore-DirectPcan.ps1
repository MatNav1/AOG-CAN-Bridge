[CmdletBinding()]
param(
    [string]$VirtualTerminalDirectory = 'C:\Program Files\AgISOVirtualTerminal\bin',
    [string]$TaskControllerDirectory = 'C:\Program Files\AOG-TaskController\bin'
)

# The installer runs this step non-interactively, so a transcript is the
# only record of what it actually did to VT/TC's files.
$logPath = Join-Path $PSScriptRoot 'Restore-DirectPcan.log'
try { Start-Transcript -Path $logPath -Force -ErrorAction Stop | Out-Null } catch { }

try {

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

function Restore-Application([string]$Directory, [string]$ApplicationExe) {
    # Used both for manual restores and as the installer's uninstall step, so
    # a target that is missing or was never patched is skipped, not fatal.
    if (-not (Test-Path -LiteralPath (Join-Path $Directory $ApplicationExe) -PathType Leaf)) {
        Write-Warning "$ApplicationExe was not found in: $Directory. Skipped."
        return
    }

    $activeDll = Join-Path $Directory 'PCANBasic.dll'
    $originalDll = Join-Path $Directory 'zPCANBasic.dll'
    if (-not (Test-Path -LiteralPath $originalDll -PathType Leaf)) {
        Write-Warning "$ApplicationExe has no original library to restore (not patched). Skipped."
        return
    }

    # Clear $activeDll by renaming it aside (works even while
    # $ApplicationExe is still running), then rename the preserved original
    # back into place.
    Set-AsideLockedFile $activeDll
    Move-Item -LiteralPath $originalDll -Destination $activeDll -Force
    Write-Host "Restored direct PCAN-Basic for $ApplicationExe" -ForegroundColor Green
}

Restore-Application $VirtualTerminalDirectory 'AgISOVirtualTerminal.exe'
Restore-Application $TaskControllerDirectory 'AOG-TaskController.exe'

Write-Host 'Direct PCAN-Basic libraries restored where applicable.' -ForegroundColor Green

}
finally {
    try { Stop-Transcript | Out-Null } catch { }
}
