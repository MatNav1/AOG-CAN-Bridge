using System;

namespace AogCanBridge
{
    internal enum BridgeMessageType : byte
    {
        Hello = 1,
        FrameFromClient = 2,
        FrameToClient = 3,
        Heartbeat = 4
    }

    internal sealed class BridgePacket
    {
        internal const uint Magic = 0x43474F41; // "AOGC" in little endian
        internal const byte Version = 1;
        internal const int Size = 30;

        internal BridgeMessageType Type;
        internal ushort ClientId;
        internal uint Identifier;
        internal ulong TimestampUs;
        internal byte DataLength;
        internal byte Flags;
        internal readonly byte[] Data = new byte[8];

        internal static bool TryParse(byte[] bytes, int count, out BridgePacket packet)
        {
            packet = null;
            if (count != Size || BitConverter.ToUInt32(bytes, 0) != Magic ||
                bytes[4] != Version) return false;

            packet = new BridgePacket
            {
                Type = (BridgeMessageType)bytes[5],
                ClientId = BitConverter.ToUInt16(bytes, 6),
                Identifier = BitConverter.ToUInt32(bytes, 8),
                TimestampUs = BitConverter.ToUInt64(bytes, 12),
                DataLength = bytes[20],
                Flags = bytes[21]
            };
            if (packet.DataLength > 8) return false;
            Buffer.BlockCopy(bytes, 22, packet.Data, 0, 8);
            return true;
        }

        internal byte[] Serialize()
        {
            byte[] bytes = new byte[Size];
            Buffer.BlockCopy(BitConverter.GetBytes(Magic), 0, bytes, 0, 4);
            bytes[4] = Version;
            bytes[5] = (byte)Type;
            Buffer.BlockCopy(BitConverter.GetBytes(ClientId), 0, bytes, 6, 2);
            Buffer.BlockCopy(BitConverter.GetBytes(Identifier), 0, bytes, 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(TimestampUs), 0, bytes, 12, 8);
            bytes[20] = DataLength;
            bytes[21] = Flags;
            Buffer.BlockCopy(Data, 0, bytes, 22, 8);
            return bytes;
        }
    }
}
