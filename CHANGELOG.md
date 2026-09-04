# Changelog

## Unreleased

- Translated the GUI to English with a language picker; UI text now loads
  from external `Languages\*.lang` files (English and Polish shipped) so a
  translation can be added without a rebuild.
- Added a CAN bus load indicator and simplified the frame counters to
  `RX:`/`TX:`.
- Added live VT / Task Controller patch-status reporting (not found /
  unpatched / patched / connected) based on the standard install paths.
- Added an application icon.
- Added a Windows installer (Inno Setup) that installs the broker and can
  patch AgISOVirtualTerminal / AOG-TaskController in place; re-running it
  re-patches either app after an update, and uninstalling restores the
  original `PCANBasic.dll`.
- Added a GitHub Actions workflow that builds the broker, the bridge proxy,
  and the installer, and publishes it as a GitHub Release on a version tag.
- `Install-Bridge.ps1` and `Restore-DirectPcan.ps1` now default to the
  standard VT/TC install paths and skip (rather than abort on) a missing
  target, so both can be run with no arguments.
- Patching now swaps `PCANBasic.dll` by renaming rather than overwriting in
  place (the preserved original is `zPCANBasic.dll`, replacing
  `PCANBasicDirect.dll`), so VT/Task Controller no longer need to be closed
  before patching or restoring — verified against a DLL held open by a
  separate process. They still need restarting afterward to load the change.
- Fixed a false "patched" reading: status was previously inferred from
  whether `zPCANBasic.dll` existed, which goes stale if VT/Task Controller is
  reinstalled or updated (its installer restores its own original
  `PCANBasic.dll` but has no idea our backup file is sitting there). The
  bridge proxy DLL now carries an identifiable version marker, and status is
  read from the active `PCANBasic.dll`'s actual identity instead — verified
  by reproducing the stale-backup scenario directly.
- `Install-Bridge.ps1` and `Restore-DirectPcan.ps1` now write a full
  transcript log (`Install-Bridge.log` / `Restore-DirectPcan.log`, next to
  the script) on every run, since the installer runs them hidden.
- The installer can optionally add a Startup-folder shortcut to launch the
  broker automatically (minimized) on Windows sign-in.

## 0.1.1-alpha - 2026-09-03

- Added an installation script that validates a user-supplied official PEAK
  library and deploys the broker and proxy files.
- Added a restoration script for returning VT and Task Controller to direct
  PCAN-Basic operation.
- Added a detailed installation and file-layout guide.
- Bundled the official x64 PCAN-Basic 4.7.1 library already distributed by the
  upstream VT and Task Controller projects, with third-party notices.

## 0.1.0-alpha - 2026-09-03

First experimental release.

### Included

- Windows Forms broker that owns one physical PCAN channel.
- ABI-compatible `PCANBasic.dll` proxy for Classic CAN traffic.
- UDP loopback transport between proxy clients and the broker.
- Transparent direct-library fallback through `PCANBasicDirect.dll`.
- Mock-broker test covering two simultaneous proxy clients and bidirectional
  frame transfer.

### Limitations

- Physical PCAN and real ISOBUS operation have not yet been fully validated.
- CAN FD and CAN XL are not supported.
- No installer or automatic deployment is provided.
- The official PEAK-System DLL is required separately and is not distributed.
