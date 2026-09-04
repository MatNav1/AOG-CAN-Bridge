using System.IO;

namespace AogCanBridge
{
    internal enum PatchState
    {
        NotFound,
        Unpatched,
        Patched
    }

    // Mirrors the on-disk state that Install-Bridge.ps1 / Restore-DirectPcan.ps1
    // leave behind: patching backs the original PCANBasic.dll up as
    // PCANBasicDirect.dll before overwriting PCANBasic.dll with our proxy, so
    // that backup file's presence is what tells "patched" apart from "unpatched".
    internal static class ClientPatchStatus
    {
        internal const string VirtualTerminalDirectory = @"C:\Program Files\AgISOVirtualTerminal\bin";
        internal const string TaskControllerDirectory = @"C:\Program Files\AOG-TaskController\bin";

        internal static PatchState Detect(string directory)
        {
            if (!Directory.Exists(directory)) return PatchState.NotFound;
            return File.Exists(Path.Combine(directory, "PCANBasicDirect.dll"))
                ? PatchState.Patched : PatchState.Unpatched;
        }
    }
}
