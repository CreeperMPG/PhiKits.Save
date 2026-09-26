using CreeperMPG.PhiKits.Save.Abstractions;
using CreeperMPG.PhiKits.Save.Data.SaveEntries;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CreeperMPG.PhiKits.Save.Data
{
    public class SavePackage
    {
        public PhigrosProgress GameProgress { get; set; } = new();
        public PhigrosUser User { get; set; } = new();
        public PhigrosSettings Settings { get; set; } = new();
        public PhigrosRecord GameRecord { get; set; } = new();
        public PhigrosKey GameKey { get; set; } = new();
        public byte SaveVersion { get; set; }
        public int GameVersion { get; set; }
        private static readonly byte[] AESKey =
        {
            232, 150, 154, 210, 165, 64, 37, 155, 151, 145,
            144, 139, 136, 230, 191, 3, 30, 109, 33, 149,
            110, 250, 214, 138, 80, 221, 85, 214, 122, 176, 146, 75
        };
        private static readonly byte[] AESIV =
        {
            42, 79, 240, 138, 200, 13, 99, 7, 0, 87, 197, 149, 24, 200, 50, 83
        };

        /// <summary>从内存文件字典构造（key: 文件名, value: 加密字节）</summary>
        public SavePackage(Dictionary<string, byte[]> encryptedFiles)
        {
            foreach (var entry in CreateEntryList())
            {
                if (encryptedFiles.TryGetValue(entry.EntryFileName, out var content) && content.Length > 0)
                {
                    // 第 0 字节是条目自带的版本号，由条目自己保管
                    entry.EntryVersion = content[0];
                    entry.Deserialize(DecryptData(content));
                    // 自动推断存档版本
                    if (SaveVersion == 0 && TryInferSaveVersion(out byte sv))
                    {
                        SaveVersion = sv;
                    }
                }
                else
                {
                    throw new ArgumentException($"存档条目 '{entry.EntryFileName}' 的数据缺失或为空。", nameof(encryptedFiles));
                }
            }
        }

        /// <summary>
        /// 存档条目清单（按 .save 内的写出顺序）。
        /// 每次调用都重新读取属性，因为条目实例可被替换。
        /// </summary>
        private List<ISaveEntry> CreateEntryList()
            => new List<ISaveEntry>(5) { GameKey, GameProgress, GameRecord, Settings, User };

        // ── zip 打包 / 解包 ──

        /// <summary>从 .save zip 的字节内容构造（全内存，不产生临时文件）</summary>
        /// <param name="zipFile">.save 文件的原始字节，内含已加密的五个条目</param>
        public static SavePackage FromZipBytes(byte[] zipFile)
        {
            if (zipFile == null) throw new ArgumentNullException(nameof(zipFile));
            return new SavePackage(ExtractZipEntries(zipFile));
        }

        /// <summary>从 .save zip 文件路径构造</summary>
        public static SavePackage FromZipFile(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            return FromZipBytes(File.ReadAllBytes(path));
        }

        /// <summary>
        /// 打包为 .save zip 的字节内容（全内存，不产生临时文件）。
        /// 每个条目的版本号取自条目自身（<see cref="ISaveEntry.EntryVersion"/>），写回加密数据的第 0 字节。
        /// </summary>
        public byte[] ToZipBytes()
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var entry in CreateEntryList())
                {
                    byte[] encrypted = EncryptData(entry.Serialize());
                    // EncryptData 会空出第 0 字节，此处回填条目自己的版本号
                    encrypted[0] = entry.EntryVersion;

                    var zipEntry = zip.CreateEntry(entry.EntryFileName, CompressionLevel.Optimal);
                    using var stream = zipEntry.Open();
                    stream.Write(encrypted, 0, encrypted.Length);
                }
            }
            return ms.ToArray();
        }

        /// <summary>解压 zip 为条目字典（key: 条目名，value: 加密字节）</summary>
        private static Dictionary<string, byte[]> ExtractZipEntries(byte[] zipFile)
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using var zip = new ZipArchive(new MemoryStream(zipFile), ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                // 目录条目没有内容；用 Name 而非 FullName，兼容带目录前缀的 .save
                if (string.IsNullOrEmpty(entry.Name)) continue;

                using var stream = entry.Open();
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                files[entry.Name] = ms.ToArray();
            }
            return files;
        }

        // 将会删除第 0 字节（版本号）
        public static byte[] DecryptData(byte[] data)
        {
            if (data.Length < 2) throw new ArgumentException("Invalid data length");
            byte[] trimmed = new byte[data.Length - 1];
            Array.Copy(data, 1, trimmed, 0, trimmed.Length);

            using var aes = System.Security.Cryptography.Aes.Create();
            aes.Key = AESKey;
            aes.IV = AESIV;
            aes.Mode = System.Security.Cryptography.CipherMode.CBC;
            aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(trimmed, 0, trimmed.Length);
        }
        // 将会空出第 0 字节，需要自行填充版本号
        public static byte[] EncryptData(byte[] data)
        {
            using var aes = System.Security.Cryptography.Aes.Create();
            aes.Key = AESKey;
            aes.IV = AESIV;
            aes.Mode = System.Security.Cryptography.CipherMode.CBC;
            aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;

            using var encryptor = aes.CreateEncryptor();
            byte[] encrypted = encryptor.TransformFinalBlock(data, 0, data.Length);
            byte[] result = new byte[encrypted.Length + 1];
            Array.Copy(encrypted, 0, result, 1, encrypted.Length);
            return result;
        }

        /// <summary>
        /// 根据当前存档数据生成完整的 <see cref="SaveSummary"/> 对象。
        /// 逻辑与 SyncSaveToSummary 相同，但直接从存档数据生成新对象而非合并到已有对象。
        /// </summary>
        public SaveSummary GenerateSummary()
        {
            var summary = new SaveSummary
            {
                Challenge = (ushort)GameProgress.ChallengeModeRank,
                RankingScore = 11.45f, // 默认占位值，若要计算真实 RKS 请使用带定数提供者的重载
                Avatar = User.Avatar,
                SaveVersion = SaveVersion,
                GameVersion = GameVersion
            };

            int ezClr = 0, ezFC = 0, ezPhi = 0;
            int hdClr = 0, hdFC = 0, hdPhi = 0;
            int inClr = 0, inFC = 0, inPhi = 0;
            int atClr = 0, atFC = 0, atPhi = 0;

            foreach (var record in GameRecord.Records.Values)
            {
                CountStats(record.EZ, ref ezClr, ref ezFC, ref ezPhi);
                CountStats(record.HD, ref hdClr, ref hdFC, ref hdPhi);
                CountStats(record.IN, ref inClr, ref inFC, ref inPhi);
                CountStats(record.AT, ref atClr, ref atFC, ref atPhi);
            }

            summary.Achievements = new SongDifficultySet<Achievement>(
                new Achievement((ushort)ezClr, (ushort)ezFC, (ushort)ezPhi),
                new Achievement((ushort)hdClr, (ushort)hdFC, (ushort)hdPhi),
                new Achievement((ushort)inClr, (ushort)inFC, (ushort)inPhi),
                new Achievement((ushort)atClr, (ushort)atFC, (ushort)atPhi),
                null);

            return summary;
        }

        private static void CountStats(LevelRecord? record, ref int cleared, ref int fc, ref int phi)
        {
            if (record == null) return;
            if (record.Score >= 820000) cleared++;
            if (record.Rank == RankType.FC || record.Rank == RankType.Phi) fc++;
            if (record.Rank == RankType.Phi) phi++;
        }

        /// <summary>
        /// 生成摘要，并用给定的定数提供者计算真实的 RKS。
        /// <para>
        /// 与无参重载的区别只有 <see cref="SaveSummary.RankingScore"/>：
        /// 这里交给 <see cref="PhigrosRecord.CalculateRankingScore"/> 计算，而非占位值。
        /// </para>
        /// </summary>
        /// <param name="difficultyProvider">定数提供者</param>
        public SaveSummary GenerateSummary(IDifficultyProvider difficultyProvider)
        {
            if (difficultyProvider == null) throw new ArgumentNullException(nameof(difficultyProvider));

            var summary = GenerateSummary();
            summary.RankingScore = GameRecord.CalculateRankingScore(difficultyProvider);
            return summary;
        }

        // [SaveVersion] GameVersion UpdateContent => EntryFileName EntryVersion
        // [1] (INITIAL VERSION)
        // [2] 77  RANDOM   => GameProgress V2
        // [3] 78  CHAP8    => GameProgress V3
        // [4] 87  CAMELLIA => GameKey      V2
        // [5] 108 TAKUMI3  => GameProgress V4
        // [6] 111 Side4    => GameKey      V3
        // [7] 155 CHAP9    => GameProgress V5

        /// <summary>
        /// 尝试通过存档 Entries 的状态反推 SaveVersion
        /// </summary>
        /// <param name="saveVersion">存档版本输出</param>
        /// <returns>是否推断成功</returns>
        public bool TryInferSaveVersion(out byte saveVersion)
        {
            if (GameProgress.EntryVersion <= 2)
            {
                saveVersion = GameProgress.EntryVersion; // 1, 2
                return GameKey.EntryVersion == 1;
            }
            if (GameProgress.EntryVersion == 3)
            {
                saveVersion = (byte)(GameKey.EntryVersion + 2); // 3, 4
                return GameKey.EntryVersion == 1 || GameKey.EntryVersion == 2;
            }
            if (GameProgress.EntryVersion == 4)
            {
                saveVersion = (byte)(GameKey.EntryVersion + 3); // 5, 6
                return GameKey.EntryVersion == 2 || GameKey.EntryVersion == 3;
            }
            if (GameProgress.EntryVersion == 5)
            {
                saveVersion = 7; // 7
                return GameKey.EntryVersion == 3;
            }
            saveVersion = 7;
            return false;
        }
        public static int GetSaveVersionByGameVersion(int gameVersion)
        {
            if (gameVersion < 77)
            {
                return 1;
            }
            else if (gameVersion < 78)
            {
                return 2;
            }
            else if (gameVersion < 87)
            {
                return 3;
            }
            else if (gameVersion < 108)
            {
                return 4;
            }
            else if (gameVersion < 111)
            {
                return 5;
            }
            else if (gameVersion < 155)
            {
                return 6;
            }
            else
            {
                return 7;
            }
        }
    }
}
