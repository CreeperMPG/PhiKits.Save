using CreeperMPG.PhiKits.Save.Additions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CreeperMPG.PhiKits.Save.Data.SaveEntries
{
    public class PhigrosUser : ISaveEntry
    {
        public string EntryFileName => "user";
        public byte EntryVersion { get; set; } = 1;
        public bool ShowPlayerId { get; set; }
        public string SelfIntro { get; set; } = string.Empty;
        public string Avatar { get; set; } = string.Empty;
        public string Background { get; set; } = string.Empty;
        public byte[] OverflowData { get; set; } = Array.Empty<byte>();
        public void Deserialize(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);
            ShowPlayerId = BitUtils.GetBit(reader.ReadByte(), 0);
            SelfIntro = BitUtils.ReadString(reader);
            Avatar = BitUtils.ReadString(reader);
            Background = BitUtils.ReadString(reader);
            OverflowData = reader.ReadBytes((int)(ms.Length - ms.Position));
        }

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            byte flags = 0;
            if (ShowPlayerId) flags |= 1 << 0;
            writer.Write(flags);
            writer.Write(BitUtils.WriteString(SelfIntro));
            writer.Write(BitUtils.WriteString(Avatar));
            writer.Write(BitUtils.WriteString(Background));
            writer.Write(OverflowData);
            return ms.ToArray();
        }
        byte ISaveEntry.GetEntryVersionBySaveVersion(int saveVersion)
        {
            return 1;
        }
    }
}
