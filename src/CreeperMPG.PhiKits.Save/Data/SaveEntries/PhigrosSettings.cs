using CreeperMPG.PhiKits.Save.Additions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CreeperMPG.PhiKits.Save.Data.SaveEntries
{
    public class PhigrosSettings : ISaveEntry
    {
        public string EntryFileName => "settings";
        public byte EntryVersion { get; set; } = 1;
        public bool ChordSupport { get; set; }
        public bool FcAPIndicator { get; set; }
        public bool EnableHitSound { get; set; }
        public bool LowResolutionMode { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public float Bright { get; set; }
        public float MusicVolume { get; set; }
        public float EffectVolume { get; set; }
        public float HitSoundVolume { get; set; }
        public float SoundOffset { get; set; }
        public float NoteScale { get; set; }
        public byte[] OverflowData { get; set; } = Array.Empty<byte>();

        public void Deserialize(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);
            byte flags = reader.ReadByte();
            ChordSupport = BitUtils.GetBit(flags, 0);
            FcAPIndicator = BitUtils.GetBit(flags, 1);
            EnableHitSound = BitUtils.GetBit(flags, 2);
            LowResolutionMode = BitUtils.GetBit(flags, 3);
            DeviceName = BitUtils.ReadString(reader);
            Bright = reader.ReadSingle();
            MusicVolume = reader.ReadSingle();
            EffectVolume = reader.ReadSingle();
            HitSoundVolume = reader.ReadSingle();
            SoundOffset = reader.ReadSingle();
            NoteScale = reader.ReadSingle();
            OverflowData = reader.ReadBytes((int)(ms.Length - ms.Position));
        }

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            byte flags = 0;
            if (ChordSupport) flags |= 1 << 0;
            if (FcAPIndicator) flags |= 1 << 1;
            if (EnableHitSound) flags |= 1 << 2;
            if (LowResolutionMode) flags |= 1 << 3;
            writer.Write(flags);
            writer.Write(BitUtils.WriteString(DeviceName));
            writer.Write(Bright);
            writer.Write(MusicVolume);
            writer.Write(EffectVolume);
            writer.Write(HitSoundVolume);
            writer.Write(SoundOffset);
            writer.Write(NoteScale);
            writer.Write(OverflowData);
            return ms.ToArray();
        }
    }
}
