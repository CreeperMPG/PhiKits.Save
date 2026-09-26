using CreeperMPG.PhiKits.Save.Additions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CreeperMPG.PhiKits.Save.Data.SaveEntries
{
    public class PhigrosKey : ISaveEntry
    {
        public string EntryFileName => "gameKey";
        public byte EntryVersion { get; set; } = 3;
        public Dictionary<string, double[]> KeyMap { get; set; } = new();
        public byte LanotaReadKeys { get; set; }
        public bool CamelliaReadKey { get; set; }
        public byte SideStory4BeginReadKey { get; set; }
        public byte OldScoreClearedV390 { get; set; }
        public byte[] OverflowData { get; set; } = Array.Empty<byte>();
        public void Deserialize(byte[] data)
        {
            if (data.Length == 0) return;

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            // deserializationMap
            KeyMap = new Dictionary<string, double[]>();
            int count = BitUtils.ReadProtobufVarInt(reader);

            for (int i = 0; i < count; i++)
            {
                string key = BitUtils.ReadString(reader);
                long entryStart = reader.BaseStream.Position;

                byte entryOffset = reader.ReadByte();
                byte len = reader.ReadByte();

                double[] values = new double[5];
                for (int ii = 0; ii < 5; ii++)
                {
                    if ((len >> ii & 1) != 0)
                        values[ii] = reader.ReadByte();
                }

                KeyMap[key] = values;
                reader.BaseStream.Position = entryStart + entryOffset + 1;
            }

            // deserializationNodes
            LanotaReadKeys = reader.ReadByte();
            CamelliaReadKey = reader.ReadByte() != 0;
            SideStory4BeginReadKey = reader.ReadByte();
            OldScoreClearedV390 = reader.ReadByte();

            // overflow
            OverflowData = reader.ReadBytes((int)(ms.Length - ms.Position));
        }
        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            // serializationMap
            writer.Write(BitUtils.WriteProtobufVarInt(KeyMap.Count));

            foreach (var kvp in KeyMap)
            {
                writer.Write(BitUtils.WriteString(kvp.Key));
                long entryStart = writer.BaseStream.Position;
                writer.Write((byte)0);

                byte len = 0;
                for (int i = 0; i < 5; i++)
                {
                    if (kvp.Value.Length > i && kvp.Value[i] != 0)
                        len |= (byte)(1 << i);
                }

                writer.Write(len);

                for (int i = 0; i < 5 && i < kvp.Value.Length; i++)
                {
                    if ((len >> i & 1) != 0)
                        writer.Write((byte)kvp.Value[i]);
                }

                long endPos = writer.BaseStream.Position;
                writer.BaseStream.Position = entryStart;
                writer.Write((byte)(endPos - entryStart - 1));
                writer.BaseStream.Position = endPos;
            }

            writer.Write(LanotaReadKeys);
            writer.Write((byte)(CamelliaReadKey ? 1 : 0));
            writer.Write(SideStory4BeginReadKey);
            writer.Write(OldScoreClearedV390);

            // overflow
            writer.Write(OverflowData);

            return ms.ToArray();
        }
    }
}
