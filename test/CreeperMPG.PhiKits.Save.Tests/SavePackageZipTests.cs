using CreeperMPG.PhiKits.Save.Data;
using CreeperMPG.PhiKits.Save.Data.SaveEntries;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO.Compression;

namespace CreeperMPG.PhiKits.Save.Tests;

/// <summary>
/// <see cref="SavePackage"/> 的 zip 打包 / 解包测试。
/// 全部使用内存中的合成存档，不依赖真实 .save 文件。
/// </summary>
[TestClass]
public class SavePackageZipTests
{
    private static readonly string[] s_entryIds =
    {
        "gameProgress", "user", "settings", "gameRecord", "gameKey"
    };

    /// <summary>
    /// 用公开 API 合成一份"已加密的条目字典"，等价于 .save 解压后的内容。
    /// </summary>
    private static Dictionary<string, byte[]> BuildEncryptedFiles(byte version = 0)
    {
        ISaveEntry[] entries =
        {
            new PhigrosProgress(), new PhigrosUser(), new PhigrosSettings(),
            new PhigrosRecord(), new PhigrosKey()
        };

        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            byte[] encrypted = SavePackage.EncryptData(entry.Serialize());
            if (version != 0) encrypted[0] = version;
            files[entry.EntryFileName] = encrypted;
        }
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
    public void ToZipBytes_ContainsExactlyTheFiveEntries_WithBareNames()
    {
        var package = new SavePackage(BuildEncryptedFiles());

        byte[] zip = package.ToZipBytes();

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        CollectionAssert.AreEquivalent(s_entryIds, archive.Entries.Select(e => e.Name).ToArray());

        // 与真实 .save 一致：条目直接位于根，没有目录前缀
        foreach (var entry in archive.Entries)
            Assert.AreEqual(entry.Name, entry.FullName, "条目不应带目录前缀");
    }

    [TestMethod]
    public void RoundTrip_PreservesEntryValues()
    {
        var original = new SavePackage(BuildEncryptedFiles());
        original.GameProgress.ChallengeModeRank = 1234;
        original.GameProgress.IsFirstRun = true;
        original.GameProgress.Chapter8Passed = true;
        original.User.Avatar = "avatar-42";
        original.User.SelfIntro = "neko 的存档喵";
        original.Settings.MusicVolume = 0.75f;
        original.Settings.NoteScale = 1.25f;
        original.GameRecord.Records["song.1"] = new SongDifficultySet<LevelRecord?>(
            new LevelRecord(1000000, 100f, true), null, null, null, null);
        original.GameKey.KeyMap["song.1"] = new double[] { 1, 0, 2, 0, 0 };

        var restored = SavePackage.FromZipBytes(original.ToZipBytes());

        Assert.AreEqual((short)1234, restored.GameProgress.ChallengeModeRank);
        Assert.IsTrue(restored.GameProgress.IsFirstRun);
        Assert.IsTrue(restored.GameProgress.Chapter8Passed);
        Assert.AreEqual("avatar-42", restored.User.Avatar);
        Assert.AreEqual("neko 的存档喵", restored.User.SelfIntro);
        Assert.AreEqual(0.75f, restored.Settings.MusicVolume);
        Assert.AreEqual(1.25f, restored.Settings.NoteScale);
        Assert.AreEqual(2d, restored.GameKey.KeyMap["song.1"][2]);

        var record = restored.GameRecord.Records["song.1"].EZ;
        Assert.IsNotNull(record);
        Assert.AreEqual(1000000u, record!.Score);
        Assert.IsTrue(record.Fc);
    }

    [TestMethod]
    public void ToZipBytes_WritesEntryVersionsBackToFirstByte()
    {
        var package = new SavePackage(BuildEncryptedFiles(version: 7));
        // 版本号由条目自己保管
        Assert.AreEqual((byte)7, package.User.EntryVersion);

        byte[] zip = package.ToZipBytes();

        foreach (var (name, bytes) in ReadZipEntries(zip))
            Assert.AreEqual((byte)7, bytes[0], $"条目 {name} 的第 0 字节应为版本号");

        var restored = SavePackage.FromZipBytes(zip);
        Assert.AreEqual((byte)7, restored.GameProgress.EntryVersion);
        Assert.AreEqual((byte)7, restored.User.EntryVersion);
        Assert.AreEqual((byte)7, restored.Settings.EntryVersion);
        Assert.AreEqual((byte)7, restored.GameRecord.EntryVersion);
        Assert.AreEqual((byte)7, restored.GameKey.EntryVersion);
    }

    [TestMethod]
    public void RoundTrip_LosesNothing_EntryBytesAreStable()
    {
        var package = new SavePackage(BuildEncryptedFiles(version: 5));
        package.User.Avatar = "stable";

        var first = ReadZipEntries(package.ToZipBytes());
        var second = ReadZipEntries(SavePackage.FromZipBytes(package.ToZipBytes()).ToZipBytes());

        CollectionAssert.AreEquivalent(first.Keys.ToArray(), second.Keys.ToArray());
        foreach (var key in first.Keys)
            CollectionAssert.AreEqual(first[key], second[key], $"条目 {key} 的字节不稳定");
    }

    [TestMethod]
    public void FromZipFile_ReadsSamePackageAsFromZipBytes()
    {
        var original = new SavePackage(BuildEncryptedFiles(version: 3));
        original.User.Avatar = "from-file";

        string path = Path.Combine(Path.GetTempPath(), $"phikits-{Guid.NewGuid():N}.save");
        try
        {
            File.WriteAllBytes(path, original.ToZipBytes());

            var restored = SavePackage.FromZipFile(path);

            Assert.AreEqual("from-file", restored.User.Avatar);
            Assert.AreEqual((byte)3, restored.User.EntryVersion);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
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
        var files = BuildEncryptedFiles();
        files.Remove("gameRecord");

        // 正常存档必定含全部五个条目，缺任何一个都说明文件损坏，因此抛异常是预期行为
        Assert.ThrowsException<ArgumentException>(() => new SavePackage(files));
    }
}
