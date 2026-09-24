using CreeperMPG.PhiKits.Save.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CreeperMPG.PhiKits.Save.Tests;

/// <summary>
/// <see cref="SaveSummary"/> 的 Base64 序列化测试，
/// 重点是 <see cref="SaveSummary.TryFromBase64"/> 能在失败时返回 null 而不是抛异常。
/// </summary>
[TestClass]
public class SaveSummaryBase64Tests
{
    [TestMethod]
    public void TryFromBase64_ReturnsNull_OnNullOrEmpty()
    {
        Assert.IsNull(SaveSummary.TryFromBase64(null));
        Assert.IsNull(SaveSummary.TryFromBase64(string.Empty));
    }

    [TestMethod]
    public void TryFromBase64_ReturnsNull_OnInvalidBase64()
    {
        Assert.IsNull(SaveSummary.TryFromBase64("这不是 base64!!"));
    }

    [TestMethod]
    public void TryFromBase64_ReturnsNull_OnTruncatedPayload()
    {
        // 合法 base64，但内容不足以构成一份摘要
        string truncated = Convert.ToBase64String(new byte[] { 1, 2 });
        Assert.IsNull(SaveSummary.TryFromBase64(truncated));
    }

    [TestMethod]
    public void RoundTrip_PreservesAllFields()
    {
        var summary = new SaveSummary
        {
            SaveVersion = 3,
            Challenge = 1234,
            RankingScore = 15.5f,
            GameVersion = 30000,
            Avatar = "avatar-x",
            Achievements = new SongDifficultySet<Achievement>(
                new Achievement(10, 5, 1),
                new Achievement(20, 10, 2),
                new Achievement(30, 15, 3),
                new Achievement(40, 20, 4),
                null)
        };

        var restored = SaveSummary.TryFromBase64(summary.ToBase64String());

        Assert.IsNotNull(restored);
        Assert.AreEqual((byte)3, restored!.SaveVersion);
        Assert.AreEqual((ushort)1234, restored.Challenge);
        Assert.AreEqual(15.5f, restored.RankingScore);
        Assert.AreEqual(30000, restored.GameVersion);
        Assert.AreEqual("avatar-x", restored.Avatar);
        Assert.AreEqual((ushort)10, restored.Achievements.EZ!.Cleared);
        Assert.AreEqual((ushort)5, restored.Achievements.EZ!.FullCombo);
        Assert.AreEqual((ushort)1, restored.Achievements.EZ!.Phi);
        Assert.AreEqual((ushort)4, restored.Achievements.AT!.Phi);
    }

    [TestMethod]
    public void LegacyConstructor_KeepsSwallowingBehaviour()
    {
        // 旧的字符串构造函数在解析失败时不抛异常，字段保持默认值
        var summary = new SaveSummary("这不是 base64!!");

        Assert.AreEqual((byte)0, summary.SaveVersion);
        Assert.AreEqual(string.Empty, summary.Avatar);
    }
}
