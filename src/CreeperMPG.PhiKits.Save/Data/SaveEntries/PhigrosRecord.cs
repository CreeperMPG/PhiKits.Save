using CreeperMPG.PhiKits.Save.Abstractions;
using CreeperMPG.PhiKits.Save.Additions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CreeperMPG.PhiKits.Save.Data.SaveEntries
{
    public class PhigrosRecord : ISaveEntry
    {
        public string EntryFileName => "gameRecord";
        public byte EntryVersion { get; set; } = 1;
        public Dictionary<string, SongDifficultySet<LevelRecord?>> Records { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);

        public void Deserialize(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);
            Records = new(StringComparer.OrdinalIgnoreCase);
            int songsNum = BinaryUtils.ReadProtobufVarInt(reader);

            while (ms.Length - ms.Position > 0)
            {
                string songID = BinaryUtils.ReadString(reader);
                reader.ReadByte(); // non-null count byte (ignored on read)
                byte availableDifficulties = reader.ReadByte();
                byte fc = reader.ReadByte();

                var levels = new LevelRecord?[5] { null, null, null, null, null, };

                for (int i = 0; i < 5; i++)
                {
                    if (BinaryUtils.GetBit(availableDifficulties, i))
                    {
                        levels[i] = new LevelRecord()
                        {
                            Score = reader.ReadUInt32(),
                            Acc = reader.ReadSingle(),
                            Fc = BinaryUtils.GetBit(fc, i),
                        };
                    }
                }
                Records[songID] = new SongDifficultySet<LevelRecord?>(levels);
            }
        }

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(BinaryUtils.WriteProtobufVarInt(Records.Count));

            foreach (var (key, diffInfo) in Records)
            {
                writer.Write(BinaryUtils.WriteString(key));

                // 固定5位置: 0=EZ 1=HD 2=IN 3=AT 4=Legacy
                int nonNullCount = 0;
                byte avail = 0;
                byte fc = 0;
                for (int i = 0; i < 5; i++)
                {
                    var r = diffInfo[i];
                    if (r != null)
                    {
                        nonNullCount++;
                        avail |= (byte)(1 << i);
                        if (r.Fc) fc |= (byte)(1 << i);
                    }
                }
                writer.Write(Convert.ToByte(nonNullCount * 8 + 2));
                writer.Write(avail);
                writer.Write(fc);

                // 固定顺序写出数据
                for (int i = 0; i < 5; i++)
                {
                    var r = diffInfo[i];
                    if (r != null)
                    {
                        writer.Write(r.Score);
                        writer.Write(r.Acc);
                    }
                }
            }
            return ms.ToArray();
        }
        byte ISaveEntry.GetEntryVersionBySaveVersion(int saveVersion)
        {
            return 1;
        }

        // ── RKS 计算 ──

        /// <summary>
        /// 单曲 RKS 明细：定数、单曲 RKS、是否满分（Phi）。
        /// </summary>
        private readonly struct SongRks
        {
            internal readonly float Difficulty;
            internal readonly float RankingScore;
            internal readonly bool IsPhi;

            internal SongRks(float difficulty, float rankingScore, bool isPhi)
            {
                Difficulty = difficulty;
                RankingScore = rankingScore;
                IsPhi = isPhi;
            }
        }

        /// <summary>
        /// 计算账号总 RKS（RankingScore）。
        /// </summary>
        /// <param name="provider">定数提供者</param>
        public float CalculateRankingScore(IDifficultyProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));

            // 先把每首有定数的成绩投影出来，避免同一首歌重复查询 provider
            var songs = new List<SongRks>();
            foreach (var (songId, difficulties) in Records)
            {
                for (int index = 0; index < 4; index++)   // 只算 EZ/HD/IN/AT
                {
                    var record = difficulties[index];
                    if (record == null) continue;

                    float? difficulty = provider.GetDifficulty(songId, index);
                    if (difficulty == null) continue;

                    songs.Add(new SongRks(
                        difficulty.Value,
                        record.GetRankingScore(difficulty.Value),
                        record.Rank == RankType.Phi));
                }
            }

            // 定数最高的 3 首满分成绩
            float phiSum = songs
                .Where(s => s.IsPhi)
                .OrderByDescending(s => s.Difficulty)
                .Take(3)
                .Sum(s => s.RankingScore);

            // 单曲 RKS 最高的 27 首
            float bestSum = songs
                .OrderByDescending(s => s.RankingScore)
                .Take(27)
                .Sum(s => s.RankingScore);

            return (phiSum + bestSum) / 30f;
        }
    }
    public enum RankType
    {
        F, C, B, A, S, V, FC, Phi, hyw
    }
    public class LevelRecord
    {
        public uint Score { get; set; }
        public float Acc { get; set; }
        public bool Fc { get; set; }
        public LevelRecord() { }
        public LevelRecord(uint score = 0, float acc = 0f, bool fc = false)
        {
            Score = score; Acc = acc; Fc = fc;
        }
        public RankType Rank
        {
            get
            {
                if (Score > 1000000) return RankType.hyw;
                if (Score == 1000000) return RankType.Phi;
                if (Fc) return RankType.FC;
                if (Score >= 960000) return RankType.V;
                if (Score >= 920000) return RankType.S;
                if (Score >= 880000) return RankType.A;
                if (Score >= 820000) return RankType.B;
                if (Score >= 700000) return RankType.C;
                return RankType.F;
            }
        }


        /// <summary>
        /// 根据 Acc 计算单曲 rks 系数。需要再乘以定数才可得到单曲 rks。
        /// </summary>
        public float GetRankingScoreFactor()
        {
            if (Acc < 70) return 0;
            return (float)Math.Pow((Acc - 55) / 45, 2);
        }

        /// <summary>
        /// 单曲 RKS = 成绩系数 × 定数。
        /// <para>
        /// 定数由外部提供——本类不知道自己是哪首歌、哪个难度，
        /// 因此 RKS 的计算与曲目数据是解耦的。
        /// </para>
        /// </summary>
        /// <param name="difficulty">该曲目该难度的定数</param>
        public float GetRankingScore(float difficulty)
            => GetRankingScoreFactor() * difficulty;
    }
}
