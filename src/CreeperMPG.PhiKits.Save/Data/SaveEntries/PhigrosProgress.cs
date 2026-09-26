using CreeperMPG.PhiKits.Save.Additions;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CreeperMPG.PhiKits.Save.Data.SaveEntries
{
    public class PhigrosProgress : ISaveEntry
    {
        public string EntryFileName => "gameProgress";
        public byte EntryVersion { get; set; } = 4;
        public bool IsFirstRun { get; set; }
        public bool LegacyChapterFinished { get; set; }
        public bool AlreadyShowCollectionTip { get; set; }
        public bool AlreadyShowAutoUnlockINTip { get; set; }
        public string Completed { get; set; } = string.Empty;
        public int SongUpdateInfo { get; set; }
        public short ChallengeModeRank { get; set; }
        public PhiData Money { get; set; } = new();
        public byte UnlockFlagOfSpasmodic { get; set; }
        public byte UnlockFlagOfIgallta { get; set; }
        public byte UnlockFlagOfRrharil { get; set; }
        public byte FlagOfSongRecordKey { get; set; }
        public byte RandomVersionUnlocked { get; set; }
        public bool Chapter8UnlockBegin { get; set; }
        public bool Chapter8UnlockSecondPhase { get; set; }
        public bool Chapter8Passed { get; set; }
        public byte Chapter8SongUnlocked { get; set; }
        public byte FlagOfSongRecordKeyTakumi { get; set; }
        public bool Chapter9UnlockBegin { get; set; }
        public bool Chapter9SecretChallengePendingLifeUnlock { get; set; }
        public bool[] Chapter9SongUnlocked { get; set; } = new bool[8];
        public byte Chapter9SecretChallengeLifeTier { get; set; }
        public byte Chapter9SecretChallengeSelectedLifeTier { get; set; }
        public string Chapter9SecretPassword { get; set; } = "0";
        public byte[] OverflowData { get; set; } = Array.Empty<byte>();
        public void Deserialize(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);
            if (EntryVersion >= 1)
            {
                byte flags = reader.ReadByte();
                IsFirstRun = BinaryUtils.GetBit(flags, 0);
                LegacyChapterFinished = BinaryUtils.GetBit(flags, 1);
                AlreadyShowCollectionTip = BinaryUtils.GetBit(flags, 2);
                AlreadyShowAutoUnlockINTip = BinaryUtils.GetBit(flags, 3);
                Completed = BinaryUtils.ReadString(reader);
                SongUpdateInfo = BinaryUtils.ReadProtobufVarInt(reader);
                ChallengeModeRank = reader.ReadInt16();
                int[] components =
                {
                    BinaryUtils.ReadProtobufVarInt(reader),
                    BinaryUtils.ReadProtobufVarInt(reader),
                    BinaryUtils.ReadProtobufVarInt(reader),
                    BinaryUtils.ReadProtobufVarInt(reader),
                    BinaryUtils.ReadProtobufVarInt(reader),
                };
                Money = new PhiData(components);
                UnlockFlagOfSpasmodic = reader.ReadByte();
                UnlockFlagOfIgallta = reader.ReadByte();
                UnlockFlagOfRrharil = reader.ReadByte();
                FlagOfSongRecordKey = reader.ReadByte();
            }
            if (EntryVersion >= 2)
            {
                RandomVersionUnlocked = reader.ReadByte();
            }
            if (EntryVersion >= 3)
            {
                byte chapter8Info = reader.ReadByte();
                Chapter8UnlockBegin = BinaryUtils.GetBit(chapter8Info, 0);
                Chapter8UnlockSecondPhase = BinaryUtils.GetBit(chapter8Info, 1);
                Chapter8Passed = BinaryUtils.GetBit(chapter8Info, 2);
                Chapter8SongUnlocked = reader.ReadByte();
            }
            if (EntryVersion >= 4)
            {
                FlagOfSongRecordKeyTakumi = reader.ReadByte();
            }
            if (EntryVersion >= 5)
            {
                byte chapter9Info = reader.ReadByte();
                Chapter9UnlockBegin = BinaryUtils.GetBit(chapter9Info, 0);
                Chapter9SecretChallengePendingLifeUnlock = BinaryUtils.GetBit(chapter9Info, 1);
                byte songUnlockBits = reader.ReadByte();
                for (int i = 0; i < Chapter9SongUnlocked.Length; i++)
                {
                    Chapter9SongUnlocked[i] = BinaryUtils.GetBit(songUnlockBits, i);
                }
                byte tierByte = reader.ReadByte();
                Chapter9SecretChallengeLifeTier = (byte)(tierByte & 0x0F);          // 低 4 位
                Chapter9SecretChallengeSelectedLifeTier = (byte)((tierByte >> 4) & 0x0F); // 高 4 位
                Chapter9SecretPassword = BinaryUtils.ReadString(reader);
            }
            OverflowData = reader.ReadBytes((int)(ms.Length - ms.Position));
        }
        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            if (EntryVersion >= 1)
            {
                byte flags = 0;
                if (IsFirstRun) flags |= 1 << 0;
                if (LegacyChapterFinished) flags |= 1 << 1;
                if (AlreadyShowCollectionTip) flags |= 1 << 2;
                if (AlreadyShowAutoUnlockINTip) flags |= 1 << 3;
                writer.Write(flags);
                writer.Write(BinaryUtils.WriteString(Completed));
                writer.Write(BinaryUtils.WriteProtobufVarInt(SongUpdateInfo));
                writer.Write(ChallengeModeRank);

                foreach (int m in Money.GetComponents())
                    writer.Write(BinaryUtils.WriteProtobufVarInt(m));

                writer.Write(UnlockFlagOfSpasmodic);
                writer.Write(UnlockFlagOfIgallta);
                writer.Write(UnlockFlagOfRrharil);
                writer.Write(FlagOfSongRecordKey);
            }
            if (EntryVersion >= 2)
            {
                writer.Write(RandomVersionUnlocked);
            }
            if (EntryVersion >= 3)
            {
                byte chapter8Info = 0;
                if (Chapter8UnlockBegin) chapter8Info |= 1 << 0;
                if (Chapter8UnlockSecondPhase) chapter8Info |= 1 << 1;
                if (Chapter8Passed) chapter8Info |= 1 << 2;
                writer.Write(chapter8Info);
                writer.Write(Chapter8SongUnlocked);
            }
            if (EntryVersion >= 4)
            {
                writer.Write(FlagOfSongRecordKeyTakumi);
            }
            if (EntryVersion >= 5)
            {
                byte flags = 0;
                BinaryUtils.SetBit(ref flags, 0, Chapter9UnlockBegin);
                BinaryUtils.SetBit(ref flags, 1, Chapter9SecretChallengePendingLifeUnlock);
                writer.Write(flags);
                byte songUnlockBits = 0;
                for (int i = 0; i < Chapter9SongUnlocked.Length && i < 8; i++)
                {
                    BinaryUtils.SetBit(ref songUnlockBits, i, Chapter9SongUnlocked[i]);
                }
                writer.Write(songUnlockBits);
                byte tierByte = (byte)(
                    (Chapter9SecretChallengeLifeTier & 0x0F) |
                    ((Chapter9SecretChallengeSelectedLifeTier & 0x0F) << 4)
                );
                writer.Write(tierByte);
                writer.Write(BinaryUtils.WriteString(Chapter9SecretPassword));
            }
            writer.Write(OverflowData);
            return ms.ToArray();
        }
        byte ISaveEntry.GetEntryVersionBySaveVersion(int saveVersion)
        {
            if (saveVersion < 2)
            {
                return 1;
            }
            else if (saveVersion < 3)
            {
                return 2; // Random 单曲
            }
            else if (saveVersion < 5)
            {
                return 3; // 凌日潮汐
            }
            else if (saveVersion < 7)
            {
                return 4; // TAKUMI3 精选集
            }
            else
            {
                return 5; // 穹顶孤舟
            }
        }
    }
    public class PhiData
    {
        public long TotalKiB;
        public int KiB { get => (int)(TotalKiB % 1024); set => TotalKiB = TotalKiB / 1024 * 1024 + value; }
        public int MiB { get => (int)(TotalKiB / 1024 % 1024); set => TotalKiB = TotalKiB - (long)MiB * 1024 + (long)value * 1024; }
        public int GiB
        {
            get => (int)(TotalKiB / (1024L * 1024) % 1024);
            set => TotalKiB = TotalKiB - (long)GiB * 1024L * 1024 + (long)value * 1024L * 1024;
        }
        public int TiB
        {
            get => (int)(TotalKiB / (1024L * 1024 * 1024) % 1024);
            set => TotalKiB = TotalKiB - (long)TiB * 1024L * 1024 * 1024 + (long)value * 1024L * 1024 * 1024;
        }
        public int PiB
        {
            get => (int)(TotalKiB / (1024L * 1024 * 1024 * 1024));
            set => TotalKiB = TotalKiB - (long)PiB * 1024L * 1024 * 1024 * 1024 + (long)value * 1024L * 1024 * 1024 * 1024;
        }
        public PhiData() { }
        public PhiData(int[] components)
        {
            if (components.Length < 5)
                throw new ArgumentException("Money array must have exactly 5 elements.", nameof(components));
            TotalKiB = components[0]
                    + (long)components[1] * 1024
                    + (long)components[2] * 1024 * 1024
                    + (long)components[3] * 1024L * 1024 * 1024
                    + (long)components[4] * 1024L * 1024 * 1024 * 1024;
        }
        public PhiData(long totalKB) { TotalKiB = totalKB; }
        public int[] GetComponents() => new[] { KiB, MiB, GiB, TiB, PiB };
        public override string ToString() => $"{PiB}PiB {TiB}TiB {GiB}GiB {MiB}MiB {KiB}KiB";
    }
}
