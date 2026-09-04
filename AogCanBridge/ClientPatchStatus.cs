using System;
using System.Diagnostics;
using System.IO;

namespace AogCanBridge
{
    internal enum PatchState
    {
        NotFound,
        Unpatched,
        Patched
    }

    // Detects patch state from the identity of the *active* PCANBasic.dll,
    // not from whether a zPCANBasic.dll backup happens to exist next to it.
    // A backup alone is not reliable: if VT/TC is reinstalled or updated, its
    // own installer restores the original PCANBasic.dll but has no idea our
    // backup file is there, so a stale backup would otherwise read as
    // "patched" even though the active library is back to unpatched.
    internal static class ClientPatchStatus
    {
        internal const string VirtualTerminalDirectory = @"C:\Program Files\AgISOVirtualTerminal\bin";
        internal const string TaskControllerDirectory = @"C:\Program Files\AOG-TaskController\bin";

        // Must match the CompanyName string resource baked into
        // PcanBasicBridgeClient/version.rc.
        private const string ProxyCompanyName = "AOG CAN Bridge";

        internal static PatchState Detect(string directory)
        {
            if (!Directory.Exists(directory)) return PatchState.NotFound;
            string activeDll = Path.Combine(directory, "PCANBasic.dll");
            if (!File.Exists(activeDll)) return PatchState.Unpatched;
            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(activeDll);
                return string.Equals(info.CompanyName, ProxyCompanyName, StringComparison.OrdinalIgnoreCase)
                    ? PatchState.Patched : PatchState.Unpatched;
            }
            catch (FileNotFoundException)
            {
                return PatchState.Unpatched;
            }
        }
    }
}
