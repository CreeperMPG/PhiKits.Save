using CreeperMPG.PhiKits.Save.Data;
using CreeperMPG.PhiKits.Save.Data.SaveEntries;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO.Compression;

namespace CreeperMPG.PhiKits.Save.Tests;

/// <summary>
/// <see cref="SavePackage"/> 的 zip 打包 / 解包测试。
/// <para>
/// **主要素材是两份真实存档**（<c>TestData/</c>，已脱敏），
/// 它们覆盖了版本分段（gameProgress v4 与 v5）这条最关键的分支。
/// 合成数据只用于「构造边界」（缺条目、null 参数）这类真实存档无法表达的用例。
/// </para>
/// </summary>
[TestClass]
public class SavePackageZipTests
{
    private static readonly string[] s_entryIds =
    {
        "gameProgress", "user", "settings", "gameRecord", "gameKey"
    };

    private static string TestDataPath(string name)
        => Path.Combine(AppContext.BaseDirectory, "TestData", name);

    // ── 真实存档（脱敏） ──

    [TestMethod]
    public void RealSave_V6_ReadsVersion4Progress()
    {
        var pkg = SavePackage.FromZipFile(TestDataPath("TestSaveV6.save"));

        Assert.AreEqual((byte)4, pkg.GameProgress.EntryVersion, "V6 存档的 gameProgress 是 v4");
        Assert.AreEqual((byte)1, pkg.GameRecord.EntryVersion);
        Assert.AreEqual((byte)1, pkg.User.EntryVersion);
        Assert.AreEqual((byte)1, pkg.Settings.EntryVersion);
        Assert.AreEqual((byte)3, pkg.GameKey.EntryVersion);

        // v4 不含 Chapter9 字段，应保持默认值
        Assert.IsFalse(pkg.GameProgress.Chapter9UnlockBegin);
        Assert.AreEqual("0", pkg.GameProgress.Chapter9SecretPassword);
    }

    [TestMethod]
    public void RealSave_V7_ReadsVersion5ProgressWithChapter9()
    {
        var pkg = SavePackage.FromZipFile(TestDataPath("TestSaveV7.save"));

        Assert.AreEqual((byte)5, pkg.GameProgress.EntryVersion, "V7 存档的 gameProgress 是 v5");

        // v5 新增的 Chapter9 字段应被正确解析
        Assert.IsTrue(pkg.GameProgress.Chapter9UnlockBegin);
        Assert.IsFalse(string.IsNullOrEmpty(pkg.GameProgress.Chapter9SecretPassword));
        Assert.AreNotEqual("0", pkg.GameProgress.Chapter9SecretPassword);
        Assert.AreEqual(8, pkg.GameProgress.Chapter9SongUnlocked.Length);
        Assert.IsTrue(pkg.GameProgress.Chapter9SongUnlocked[0]);
    }

    [TestMethod]
    public void RealSave_V6_RoundTripIsByteIdentical()
    {
        AssertRoundTripIsByteIdentical("TestSaveV6.save");
    }

    [TestMethod]
    public void RealSave_V7_RoundTripIsByteIdentical()
    {
        AssertRoundTripIsByteIdentical("TestSaveV7.save");
    }

    private static void AssertRoundTripIsByteIdentical(string fileName)
    {
        var original = SavePackage.FromZipFile(TestDataPath(fileName));
        var restored = SavePackage.FromZipBytes(original.ToZipBytes());

        // 逐字节比对五个条目 —— 这是"打包无损"的最强证据
        var before = ReadZipEntries(original.ToZipBytes());
        var after = ReadZipEntries(restored.ToZipBytes());

        CollectionAssert.AreEquivalent(before.Keys.ToArray(), after.Keys.ToArray());
        foreach (var key in before.Keys)
            CollectionAssert.AreEqual(before[key], after[key], $"条目 {key} 往返后字节不一致");

        // 版本号也必须保住
        Assert.AreEqual(original.GameProgress.EntryVersion, restored.GameProgress.EntryVersion);
        Assert.AreEqual(original.GameKey.EntryVersion, restored.GameKey.EntryVersion);
    }

    [TestMethod]
    public void RealSave_V7_RoundTripPreservesChapter9Values()
    {
        var original = SavePackage.FromZipFile(TestDataPath("TestSaveV7.save"));
        var restored = SavePackage.FromZipBytes(original.ToZipBytes());

        Assert.AreEqual(original.GameProgress.Chapter9SecretPassword,
                        restored.GameProgress.Chapter9SecretPassword);
        Assert.AreEqual(original.GameProgress.Chapter9SecretChallengeLifeTier,
                        restored.GameProgress.Chapter9SecretChallengeLifeTier);
        Assert.AreEqual(original.GameProgress.Chapter9SecretChallengeSelectedLifeTier,
                        restored.GameProgress.Chapter9SecretChallengeSelectedLifeTier);
        CollectionAssert.AreEqual(original.GameProgress.Chapter9SongUnlocked,
                                  restored.GameProgress.Chapter9SongUnlocked);
    }

    [TestMethod]
    public void RealSave_V7_KeepsMoreRecordsThanV6()
    {
        var v6 = SavePackage.FromZipFile(TestDataPath("TestSaveV6.save"));
        var v7 = SavePackage.FromZipFile(TestDataPath("TestSaveV7.save"));

        // 脱敏时各留了 3 首
        Assert.AreEqual(3, v6.GameRecord.Records.Count);
        Assert.AreEqual(3, v7.GameRecord.Records.Count);
        Assert.AreEqual(3, v6.GameKey.KeyMap.Count);
        Assert.AreEqual(3, v7.GameKey.KeyMap.Count);
    }

    [TestMethod]
    public void RealSave_FromZipFileAndFromZipBytes_Agree()
    {
        string path = TestDataPath("TestSaveV7.save");
        var fromFile = SavePackage.FromZipFile(path);
        var fromBytes = SavePackage.FromZipBytes(File.ReadAllBytes(path));

        Assert.AreEqual(fromFile.GameProgress.EntryVersion, fromBytes.GameProgress.EntryVersion);
        Assert.AreEqual(fromFile.GameProgress.ChallengeModeRank, fromBytes.GameProgress.ChallengeModeRank);
        Assert.AreEqual(fromFile.GameRecord.Records.Count, fromBytes.GameRecord.Records.Count);
        Assert.AreEqual(fromFile.GameKey.KeyMap.Count, fromBytes.GameKey.KeyMap.Count);
    }

    [TestMethod]
    public void ToZipBytes_ContainsExactlyTheFiveEntries_WithBareNames()
    {
        var package = SavePackage.FromZipFile(TestDataPath("TestSaveV7.save"));

        byte[] zip = package.ToZipBytes();

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        CollectionAssert.AreEquivalent(s_entryIds, archive.Entries.Select(e => e.Name).ToArray());

        // 与真实 .save 一致：条目直接位于根，没有目录前缀
        foreach (var entry in archive.Entries)
            Assert.AreEqual(entry.Name, entry.FullName, "条目不应带目录前缀");
    }

    [TestMethod]
    public void ToZipBytes_WritesEachEntryVersionBackToFirstByte()
    {
        var package = SavePackage.FromZipFile(TestDataPath("TestSaveV7.save"));

        // 版本号由条目自己保管
        Assert.AreEqual((byte)5, package.GameProgress.EntryVersion);

        byte[] zip = package.ToZipBytes();

        var bytesByName = ReadZipEntries(zip);
        Assert.AreEqual(package.GameProgress.EntryVersion, bytesByName["gameProgress"][0]);
        Assert.AreEqual(package.User.EntryVersion, bytesByName["user"][0]);
        Assert.AreEqual(package.Settings.EntryVersion, bytesByName["settings"][0]);
        Assert.AreEqual(package.GameRecord.EntryVersion, bytesByName["gameRecord"][0]);
        Assert.AreEqual(package.GameKey.EntryVersion, bytesByName["gameKey"][0]);
    }

    // ── 合成数据：只用于真实存档表达不了的边界 ──

    /// <summary>造一份"结构完整但内容为空"的条目字典，用于构造边界测试</summary>
    private static Dictionary<string, byte[]> BuildEmptyEncryptedFiles()
    {
        ISaveEntry[] entries =
        {
            new PhigrosProgress(), new PhigrosUser(), new PhigrosSettings(),
            new PhigrosRecord(), new PhigrosKey()
        };

        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
            files[entry.EntryFileName] = SavePackage.EncryptData(entry.Serialize());
        return files;
    }

    /// <summary>读出 zip 内每个条目的原始字节</summary>
    private static Dictionary<string, byte[]> ReadZipEntries(byte[] zipBytes)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            using var stream = entry.Open();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            result[entry.Name] = ms.ToArray();
        }
        return result;
    }

    [TestMethod]
    public void FromZipBytes_ThrowsOnNull()
    {
        Assert.ThrowsException<ArgumentNullException>(() => SavePackage.FromZipBytes(null!));
    }

    [TestMethod]
    public void FromZipFile_ThrowsOnNull()
    {
        Assert.ThrowsException<ArgumentNullException>(() => SavePackage.FromZipFile(null!));
    }

    [TestMethod]
    public void FromZipBytes_ThrowsWhenAnEntryIsMissing()
    {
        var files = BuildEmptyEncryptedFiles();
        files.Remove("gameRecord");

        // 正常存档必定含全部五个条目，缺任何一个都说明文件损坏，因此抛异常是预期行为
        Assert.ThrowsException<ArgumentException>(() => new SavePackage(files));
    }
}
