using CreeperMPG.PhiKits.Save.Abstractions;
using CreeperMPG.PhiKits.Save.Data;
using CreeperMPG.PhiKits.Save.Data.SaveEntries;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CreeperMPG.PhiKits.Save.Tests;

/// <summary>
/// RKS 计算测试：单曲 RKS（<see cref="LevelRecord.GetRankingScore"/>）
/// 与账号总 RKS（<see cref="PhigrosRecord.CalculateRankingScore"/>）。
/// </summary>
[TestClass]
public class RankingScoreTests
{
    private sealed class TestProvider : IDifficultyProvider
    {
        private readonly Dictionary<string, float> _map;

        public TestProvider(Dictionary<string, float> map) => _map = map;

        public bool IsLoaded => true;

        public float? GetDifficulty(string songId, int difficultyIndex)
            => _map.TryGetValue($"{songId}:{difficultyIndex}", out float value) ? value : null;
    }

    private static PhigrosRecord BuildRecord(
        params (string SongId, int DiffIndex, uint Score, float Acc)[] entries)
    {
        var record = new PhigrosRecord();
        foreach (var (songId, diffIndex, score, acc) in entries)
        {
            if (!record.Records.TryGetValue(songId, out var set))
            {
                set = new SongDifficultySet<LevelRecord?>();
                record.Records[songId] = set;
            }
            set[diffIndex] = new LevelRecord(score, acc);
        }
        return record;
    }

    // ── 单曲 RKS ──

    [TestMethod]
    public void GetRankingScore_MultipliesFactorByDifficulty()
    {
        // Acc=100 -> factor = ((100-55)/45)^2 = 1
        var record = new LevelRecord(1000000, 100f);
        Assert.AreEqual(1f, record.GetRankingScoreFactor(), 1e-6f);

        Assert.AreEqual(16.5f, record.GetRankingScore(16.5f), 1e-4f);
    }

    [TestMethod]
    public void GetRankingScore_IsZero_WhenAccBelow70()
    {
        var record = new LevelRecord(500000, 69.9f);

        Assert.AreEqual(0f, record.GetRankingScoreFactor(), 1e-6f);
        Assert.AreEqual(0f, record.GetRankingScore(16f), 1e-6f);
    }

    [TestMethod]
    public void GetRankingScore_MatchesFormula()
    {
        // Acc=90 -> ((90-55)/45)^2 = 0.604938...
        var record = new LevelRecord(900000, 90f);
        float expected = (float)Math.Pow((90 - 55) / 45.0, 2) * 15f;

        Assert.AreEqual(expected, record.GetRankingScore(15f), 1e-4f);
    }

    // ── 总 RKS ──

    [TestMethod]
    public void CalculateRankingScore_ThrowsOnNullProvider()
    {
        var record = new PhigrosRecord();

        Assert.ThrowsException<ArgumentNullException>(() => record.CalculateRankingScore(null!));
    }

    [TestMethod]
    public void CalculateRankingScore_ReturnsZero_WhenNoRecords()
    {
        var record = new PhigrosRecord();

        Assert.AreEqual(0f, record.CalculateRankingScore(new TestProvider(new())), 1e-6f);
    }

    [TestMethod]
    public void CalculateRankingScore_SkipsSongsWithoutDifficulty()
    {
        var record = BuildRecord(("song.a", 2, 1000000, 100f));
        var provider = new TestProvider(new());   // 查不到任何定数

        Assert.AreEqual(0f, record.CalculateRankingScore(provider), 1e-6f);
    }

    [TestMethod]
    public void CalculateRankingScore_IgnoresLegacyDifficulty()
    {
        // Legacy（index 4）即使有成绩也不能计入——它没有定数
        var record = BuildRecord(("song.a", 4, 1000000, 100f));
        var provider = new TestProvider(new() { ["song.a:4"] = 16f });

        Assert.AreEqual(0f, record.CalculateRankingScore(provider), 1e-6f);
    }

    [TestMethod]
    public void CalculateRankingScore_SinglePhi_CountsTwice()
    {
        // 单曲：满分 + 定数 16 -> 单曲 RKS = 16
        // 前 3 首 Phi 取到它（16），前 27 首也取到它（16）
        // 总 = (16 + 16) / 30
        var record = BuildRecord(("song.a", 2, 1000000, 100f));
        var provider = new TestProvider(new() { ["song.a:2"] = 16f });

        Assert.AreEqual(32f / 30f, record.CalculateRankingScore(provider), 1e-4f);
    }

    [TestMethod]
    public void CalculateRankingScore_PhiBucketPicksHighestDifficulty_NotHighestRks()
    {
        // 两首 Phi：a 定数低但…两者 Acc 都是 100，所以单曲 RKS == 定数。
        // 用「定数高者」与「RKS 高者」在 Phi 之间是同一首，故另造非 Phi 的高 RKS 来区分：
        //   phi.low  : Phi, 定数 10        -> 单曲 RKS 10
        //   phi.high : Phi, 定数 16        -> 单曲 RKS 16
        //   other    : 非 Phi, Acc 100, 定数 15 -> 单曲 RKS 15（不进 Phi 桶）
        var record = BuildRecord(
            ("phi.low", 2, 1000000, 100f),
            ("phi.high", 2, 1000000, 100f),
            ("other", 2, 999000, 100f));
        var provider = new TestProvider(new()
        {
            ["phi.low:2"] = 10f,
            ["phi.high:2"] = 16f,
            ["other:2"] = 15f,
        });

        // Phi 桶（按定数降序取 3）：16 + 10 = 26
        // 最佳 27 桶：16 + 15 + 10 = 41
        // 总 = (26 + 41) / 30
        Assert.AreEqual(67f / 30f, record.CalculateRankingScore(provider), 1e-4f);
    }

    [TestMethod]
    public void CalculateRankingScore_PhiBucketTakesOnlyTop3ByDifficulty()
    {
        // 4 首 Phi，定数 10/11/12/13 -> Phi 桶只取 13+12+11 = 36
        var record = BuildRecord(
            ("p10", 2, 1000000, 100f),
            ("p11", 2, 1000000, 100f),
            ("p12", 2, 1000000, 100f),
            ("p13", 2, 1000000, 100f));
        var provider = new TestProvider(new()
        {
            ["p10:2"] = 10f, ["p11:2"] = 11f, ["p12:2"] = 12f, ["p13:2"] = 13f,
        });

        // Phi 桶 = 13+12+11 = 36
        // 最佳 27 桶 = 13+12+11+10 = 46
        Assert.AreEqual(82f / 30f, record.CalculateRankingScore(provider), 1e-4f);
    }

    [TestMethod]
    public void CalculateRankingScore_BestBucketTakesOnlyTop27()
    {
        // 30 首非 Phi，定数=单曲 RKS=1..30
        var entries = new List<(string, int, uint, float)>();
        var map = new Dictionary<string, float>();
        for (int i = 1; i <= 30; i++)
        {
            entries.Add(($"s{i:D2}", 2, 900000, 100f));
            map[$"s{i:D2}:2"] = i;
        }
        var record = BuildRecord(entries.ToArray());
        var provider = new TestProvider(map);

        // 无 Phi，所以 Phi 桶为 0
        // 最佳 27 桶 = 30+29+...+4 = (30+4)*27/2 = 459
        Assert.AreEqual(459f / 30f, record.CalculateRankingScore(provider), 1e-3f);
    }

    [TestMethod]
    public void CalculateRankingScore_UsesPerDifficultyValue()
    {
        // 同一首歌不同难度分别查出定数，各算各的
        var record = BuildRecord(
            ("song.a", 0, 1000000, 100f),   // EZ 定数 5
            ("song.a", 3, 1000000, 100f));  // AT 定数 16
        var provider = new TestProvider(new()
        {
            ["song.a:0"] = 5f,
            ["song.a:3"] = 16f,
        });

        // Phi 桶：按定数降序 -> 16 + 5 = 21
        // 最佳 27 桶：16 + 5 = 21
        Assert.AreEqual(42f / 30f, record.CalculateRankingScore(provider), 1e-4f);
    }

    // ── GenerateSummary 集成 ──

    /// <summary>造一个"空但合法"的存档包（不能用空字典，构造函数会因缺条目抛异常）</summary>
    private static SavePackage BuildEmptyPackage()
    {
        ISaveEntry[] entries =
        {
            new PhigrosProgress(), new PhigrosUser(), new PhigrosSettings(),
            new PhigrosRecord(), new PhigrosKey()
        };
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
            files[entry.EntryFileName] = SavePackage.EncryptData(entry.Serialize());

        return new SavePackage(files);
    }

    [TestMethod]
    public void GenerateSummary_WithProvider_UsesRealRks_LeavingOtherFieldsIntact()
    {
        var provider = new TestProvider(new() { ["song.a:2"] = 16f });
        var package = BuildEmptyPackage();
        package.User.Avatar = "avatar-z";
        package.GameProgress.ChallengeModeRank = 1234;
        package.GameRecord = BuildRecord(("song.a", 2, 1000000, 100f));

        var summary = package.GenerateSummary(provider);

        Assert.AreEqual(32f / 30f, summary.RankingScore, 1e-4f);
        Assert.AreEqual("avatar-z", summary.Avatar);
        Assert.AreEqual((ushort)1234, summary.Challenge);
    }

    [TestMethod]
    public void GenerateSummary_WithoutProvider_IgnoresRecordsAndReturnsPlaceholder()
    {
        // 无参重载**不计算** RKS，只填占位值，所以成绩内容不影响 RankingScore。
        // 这里刻意断言「与成绩无关」，而不是断言某个具体数字——
        // 占位值是可以被项目所有者调整的，测试不该锁死它。
        var provider = new TestProvider(new() { ["song.a:2"] = 16f });

        var withRecords = BuildEmptyPackage();
        withRecords.GameRecord = BuildRecord(("song.a", 2, 1000000, 100f));
        float rksWithRecords = withRecords.GenerateSummary().RankingScore;

        var empty = BuildEmptyPackage();
        float rksEmpty = empty.GenerateSummary().RankingScore;

        Assert.AreEqual(rksEmpty, rksWithRecords, 1e-6f, "无参重载的 RKS 不应受成绩影响");

        // 与带 provider 的重载确实不同 —— 证明它是占位值而非真实计算
        float realRks = withRecords.GenerateSummary(provider).RankingScore;
        Assert.AreNotEqual(realRks, rksWithRecords, "无参重载不应等于真实 RKS");
    }

    [TestMethod]
    public void GenerateSummary_WithoutProvider_KeepsSignatureAndOtherFields()
    {
        var package = BuildEmptyPackage();
        package.User.Avatar = "avatar-p";
        package.GameProgress.ChallengeModeRank = 999;
        package.GameRecord = BuildRecord(("song.a", 2, 1000000, 100f));

        var summary = package.GenerateSummary();

        // 除 RankingScore 外，其余字段与带 provider 的重载一致
        Assert.AreEqual("avatar-p", summary.Avatar);
        Assert.AreEqual((ushort)999, summary.Challenge);
        Assert.IsNotNull(summary.Achievements);
    }
}
