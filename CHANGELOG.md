# Changelog

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
