# AOG CAN Bridge

Experimental Windows bridge intended to let AgIsoVirtualTerminal and
AOG-TaskController exchange Classic CAN traffic through one physical PEAK PCAN
adapter. The project is under development and has not yet been validated as a
complete replacement for two independently connected applications on real
ISOBUS hardware.

The solution consists of two parts:

- `AogCanBridge.exe` is the only process that loads the official PEAK DLL and
  opens the physical adapter.
- `PcanBasicBridgeClient/PCANBasic.dll` is an ABI-compatible proxy placed next
  to VT and TC. It forwards classic CAN frames to the broker over UDP loopback.

The proxy exports the 17 PCAN-Basic entry points needed for ABI compatibility.
Classic CAN functions used by AgIsoStack are implemented by the bridge. FD and
XL calls return `PCAN_ERROR_ILLMODE` because this prototype targets Classic CAN
at 250 kbit/s.

> [!WARNING]
> This is experimental software. Test it on a bench setup before connecting it
> to agricultural machinery. Do not rely on it for safety-critical control.

## Runtime file layout

```text
Broker/
  AogCanBridge.exe
  PCANBasic.dll          official PEAK DLL
VT/
  AgISOVirtualTerminal.exe
  PCANBasic.dll          bridge proxy
  PCANBasicDirect.dll    official PEAK DLL, used when broker is off
TaskController/
  AOG-TaskController.exe
  PCANBasic.dll          bridge proxy
  PCANBasicDirect.dll    official PEAK DLL, used when broker is off
```

See [INSTALL.md](INSTALL.md) for the automated installation and restoration
commands. The small `PCANBasic.dll` included in the release is the bridge proxy,
not the official PEAK library.

When the broker is running before VT/TC start, the proxy connects to it. If the
broker is not running, the proxy loads `PCANBasicDirect.dll`, allowing either VT
or TC to operate alone with the physical adapter.

The broker disables Windows UDP connection-reset notifications and also handles
transient socket errors. Closing or restarting a local client therefore cannot
terminate the broker process.

## Updating VT or TC

No source patches are required. Replace the upstream executable and its normal
dependencies, then restore the proxy as `PCANBasic.dll` and retain
`PCANBasicDirect.dll` in that application's directory. The proxy recognizes VT
and TC by executable name, independently of the selected logical PCAN channel.

## Verification

`PcanBasicBridgeClientTest` loads two independent copies of the proxy and uses a
mock broker to verify initialization and bidirectional frame transfer without
CAN hardware. Passing this test verifies the local proxy protocol only; it does
not prove correct behavior with a physical PCAN adapter or an ISOBUS implement.

## Building

Build the broker with a Visual Studio developer shell:

```powershell
dotnet build AogCanBridge/AogCanBridge.csproj -c Release
```

Build and test the proxy:

```powershell
cmake -S PcanBasicBridgeClient -B build-proxy
cmake --build build-proxy --config Release
./build-proxy/Release/PcanBasicBridgeClientTest.exe ./build-proxy/Release/PCANBasic.dll
```

The official PEAK-System `PCANBasic.dll` is required at runtime next to the
broker (and as `PCANBasicDirect.dll` for direct fallback). The repository and
Windows release contain the same x64 PCAN-Basic 4.7.1 library currently bundled
by the upstream VT and Task Controller projects. See `THIRD_PARTY_NOTICES.md`.

## Project status

- Mock two-client proxy test: passing on Windows x64.
- Physical PCAN communication: requires bench verification.
- Simultaneous VT and Task Controller session: requires bench verification.
- PowerShell installation and restoration scripts: implemented.

## License and trademarks

The original code in this repository is licensed under the MIT License. PEAK,
PCAN, and PCAN-Basic are trademarks or product names of PEAK-System Technik GmbH.
This project is not affiliated with or endorsed by PEAK-System,
Open-Agriculture, or AgOpenGPS.
