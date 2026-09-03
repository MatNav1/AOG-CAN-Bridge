using System;
using System.Runtime.InteropServices;

namespace AogCanBridge
{
    internal static class PcanBasic
    {
        internal const ushort PcanUsbBus1 = 0x51;
        internal const ushort Baud250K = 0x011C;
        internal const uint ErrorOk = 0;
        internal const uint ErrorReceiveQueueEmpty = 0x20;
        internal const byte MessageExtended = 0x02;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct CanMessage
        {
            internal uint Id;
            internal byte MessageType;
            internal byte Length;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            internal byte[] Data;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct CanTimestamp
        {
            internal uint Millis;
            internal ushort MillisOverflow;
            internal ushort Micros;
        }

        [DllImport("PCANBasic.dll", EntryPoint = "CAN_Initialize")]
        internal static extern uint Initialize(ushort channel, ushort baudrate,
            uint hardwareType = 0, uint ioPort = 0, ushort interrupt = 0);

        [DllImport("PCANBasic.dll", EntryPoint = "CAN_Uninitialize")]
        internal static extern uint Uninitialize(ushort channel);

        [DllImport("PCANBasic.dll", EntryPoint = "CAN_Read")]
        internal static extern uint Read(ushort channel, out CanMessage message,
            out CanTimestamp timestamp);

        [DllImport("PCANBasic.dll", EntryPoint = "CAN_Write")]
        internal static extern uint Write(ushort channel, ref CanMessage message);

        [DllImport("PCANBasic.dll", EntryPoint = "CAN_GetErrorText",
            CharSet = CharSet.Ansi)]
        internal static extern uint GetErrorText(uint error, ushort language,
            [Out] System.Text.StringBuilder text);
    }
}
