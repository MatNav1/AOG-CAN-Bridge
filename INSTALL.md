# Installation

## Requirements

- Windows x64.
- AgIsoVirtualTerminal and AOG-TaskController installed in separate folders.
- A compatible PEAK-System Windows driver.
- PowerShell 5.1 or newer.

The release includes the same official x64 PCAN-Basic 4.7.1 library distributed
by the upstream AgIsoVirtualTerminal and AOG-TaskController projects. It remains
copyrighted by PEAK-System and is subject to PEAK-System's terms. See
`THIRD_PARTY_NOTICES.md`.

## Recommended: Windows installer

Download `AogCanBridge-Setup-<version>.exe` from the
[latest release](https://github.com/gunicsba/AOG-CAN-Bridge/releases/latest)
and run it as Administrator. It installs the broker to
`C:\Program Files\AOG CAN Bridge`, and — if the "Patch AgOpenGPS Virtual
Terminal / Task Controller now" task is left checked — patches both apps in
their standard locations (`C:\Program Files\AgISOVirtualTerminal\bin` and
`C:\Program Files\AOG-TaskController\bin`). Re-run the same installer after
updating either app to re-patch it. Uninstalling restores each app's original
`PCANBasic.dll`.

If VT or Task Controller is installed somewhere else, or you want more
control over the patch step, use the manual PowerShell installation below
instead.

## Manual PowerShell installation

Extract the release ZIP. Open PowerShell in the extracted folder and run:

```powershell
.\Install-Bridge.ps1
```

With no arguments this patches whichever of VT and Task Controller it finds
at their standard install locations, and installs the broker next to the
script. To target non-standard locations instead:

```powershell
.\Install-Bridge.ps1 `
  -VirtualTerminalDirectory 'C:\path\to\VT' `
  -TaskControllerDirectory 'C:\path\to\TaskController'
```

A target that isn't found is skipped with a warning rather than aborting the
whole run, so it's safe to re-run after installing VT or Task Controller
later.

To use another official x64 release, add
`-OfficialPcanBasicDll 'C:\path\to\official\PCANBasic.dll'`.

The installer:

1. verifies that the selected library identifies itself as PEAK-System
   `PCANBasic.dll`;
2. copies the official DLL next to `Broker\AogCanBridge.exe`;
3. preserves the direct library as `PCANBasicDirect.dll` next to VT and Task
   Controller;
4. installs the small bridge proxy as `PCANBasic.dll` next to both applications.

The installer does not modify the application executables or the Windows
driver. Close VT and Task Controller before running it.

## Starting the applications

1. Start `Broker\AogCanBridge.exe`.
2. Select the physical PCAN channel and start the bridge.
3. Start AgIsoVirtualTerminal.
4. Start AOG-TaskController.

All applications must use Classic CAN at 250 kbit/s.

The broker window shows each client's status as "not found", "unpatched",
"patched", or "patched, connected" — a quick way to tell whether VT or Task
Controller still needs patching (for example after an update overwrote its
`PCANBasic.dll`).

## Restore direct PCAN operation

Close VT and Task Controller, then run:

```powershell
.\Restore-DirectPcan.ps1
```

This defaults to the standard VT/TC install locations and skips whichever
one isn't found or wasn't patched. To target non-standard locations, pass
`-VirtualTerminalDirectory` and/or `-TaskControllerDirectory` as above.

This copies each preserved `PCANBasicDirect.dll` back to `PCANBasic.dll`. The
backup is intentionally retained so bridge mode can be installed again later.

## Manual layout

After installation, the relevant files should look like this:

```text
Broker/
  AogCanBridge.exe
  PCANBasic.dll          official PEAK library
VT/
  AgISOVirtualTerminal.exe
  PCANBasic.dll          AOG CAN Bridge proxy (small file)
  PCANBasicDirect.dll    official PEAK library
TaskController/
  AOG-TaskController.exe
  PCANBasic.dll          AOG CAN Bridge proxy (small file)
  PCANBasicDirect.dll    official PEAK library
```
