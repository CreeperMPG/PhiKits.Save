using CreeperMPG.PhiKits.Save.Additions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CreeperMPG.PhiKits.Save.Data
{
    public class Achievement
    {
        public ushort Cleared { get; set; }
        public ushort FullCombo { get; set; }
        public ushort Phi { get; set; }

        public Achievement() { }
        public Achievement(ushort cleared, ushort fullCombo, ushort phi)
        {
            Cleared = cleared; FullCombo = fullCombo; Phi = phi;
        }

        public override string ToString() => $"{Cleared}/{FullCombo}/{Phi}";
    }

    public class SaveSummary
    {
        public byte SaveVersion { get; set; }
        public ushort Challenge { get; set; }
        public float RankingScore { get; set; }
        public int GameVersion { get; set; }
        public string Avatar { get; set; } = string.Empty;
        public SongDifficultySet<Achievement> Achievements { get; set; } = default!;

        public SaveSummary() { }

        public SaveSummary(byte saveVersion, ushort challenge, float rankingScore,
                           byte gameVersion, string avatar, SongDifficultySet<Achievement> achievements)
        {
            SaveVersion = saveVersion; Challenge = challenge; RankingScore = rankingScore;
            GameVersion = gameVersion; Avatar = avatar; Achievements = achievements;
        }

        /// <summary>从 Base64 摘要二进制反序列化（解析失败时字段保持默认值）</summary>
        public SaveSummary(string base64Summary)
        {
            TryParse(base64Summary);
        }

        /// <summary>
        /// 尝试从 Base64 摘要二进制反序列化；失败时返回 null，不抛异常。
        /// 与构造函数不同，可以区分"解析失败"和"内容本身即为默认值"。
        /// </summary>
        public static SaveSummary? TryFromBase64(string? base64Summary)
        {
            if (string.IsNullOrEmpty(base64Summary)) return null;

            var summary = new SaveSummary();
            return summary.TryParse(base64Summary) ? summary : null;
        }

        /// <summary>解析 Base64 摘要，成功返回 true</summary>
        private bool TryParse(string base64Summary)
        {
            try
            {
                byte[] data = Convert.FromBase64String(base64Summary);
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms);

                SaveVersion = reader.ReadByte();
                Challenge = reader.ReadUInt16();
                RankingScore = reader.ReadSingle();
                GameVersion = BitUtils.ReadProtobufVarInt(reader);
                Avatar = BitUtils.ReadString(reader);

                var achievements = new Achievement[4];
                for (int i = 0; i < 4; i++)
                {
                    achievements[i] = new Achievement(
                        reader.ReadUInt16(),
                        reader.ReadUInt16(),
                        reader.ReadUInt16()
                    );
                }
                Achievements = new SongDifficultySet<Achievement>(achievements);
                return true;
            }
            catch { return false; }
        }

        /// <summary>序列化为 Base64 字符串</summary>
        public string ToBase64String()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(SaveVersion);
            writer.Write(Challenge);
            writer.Write(RankingScore);
            writer.Write(BitUtils.WriteProtobufVarInt(GameVersion));

            byte[] avatarBytes = Encoding.UTF8.GetBytes(Avatar);
            writer.Write((byte)avatarBytes.Length);
            writer.Write(avatarBytes);

            try
            {
                if (Achievements != null)
                {
                    foreach (var a in Achievements.ToDictionaryWithoutLegacy().Values)
                    {
                        writer.Write(a?.Cleared ?? 0);
                        writer.Write(a?.FullCombo ?? 0);
                        writer.Write(a?.Phi ?? 0);
                    }
                }
            }
            catch { }
            return Convert.ToBase64String(ms.ToArray());
        }

        public override string ToString() => ToBase64String();
    }
}
