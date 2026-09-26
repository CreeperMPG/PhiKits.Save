using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CreeperMPG.PhiKits.Save.Additions
{
    internal static class BitUtils
    {
        internal static bool GetBit(byte value, int position)
        {
            return (value & (1 << position)) != 0;
        }
        public static void SetBit(ref byte value, int bitIndex, bool on)
        {
            if (on)
                value |= (byte)(1 << bitIndex);
            else
                value &= (byte)~(1 << bitIndex);
        }
        // 变长整数（Protobuf VarInt 格式）
        internal static int ReadProtobufVarInt(BinaryReader reader)
        {
            int value = 0;
            int shift = 0;
            while (true)
            {
                byte b = reader.ReadByte();
                value |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    break;
                shift += 7;
            }
            return value;
        }
        internal static byte[] WriteProtobufVarInt(int value)
        {
            using var ms = new MemoryStream();
            while (true)
            {
                byte temp = (byte)(value & 0x7F);
                value >>= 7;
                if (value == 0)
                {
                    ms.WriteByte(temp);
                    break;
                }
                else
                {
                    ms.WriteByte((byte)(temp | 0x80));
                }
            }
            return ms.ToArray();
        }
        // 假设字符串前有长度（VarInt），UTF-8编码
        internal static string ReadString(BinaryReader reader)
        {
            int length = ReadProtobufVarInt(reader);
            if (length == 0) return string.Empty;
            var bytes = reader.ReadBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }
        internal static byte[] WriteString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return WriteProtobufVarInt(0);

            var bytes = Encoding.UTF8.GetBytes(value);
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(WriteProtobufVarInt(bytes.Length));
            writer.Write(bytes);
            return ms.ToArray();
        }

    }
}
