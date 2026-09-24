using CreeperMPG.PhiKits.Save.CloudStorage;
using CreeperMPG.PhiKits.Save.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace CreeperMPG.PhiKits.Save.Tests;

/// <summary>
/// <see cref="PlayerObject"/> / <see cref="SaveInfoObject"/> 中不依赖网络的部分。
/// </summary>
[TestClass]
public class CloudStorageObjectTests
{
    [TestMethod]
    public void PlayerObject_FromJson_ReadsUserFields()
    {
        using var doc = JsonDocument.Parse(
            "{\"nickname\":\"cmwx\",\"shortId\":\"abc123\",\"objectId\":\"obj-1\"," +
            "\"createdAt\":\"2026-05-17T00:00:00.000Z\"}");

        var player = PlayerObject.FromJson(doc.RootElement, "token-1");

        Assert.AreEqual("cmwx", player.Nickname);
        Assert.AreEqual("abc123", player.ShortID);
        Assert.AreEqual("obj-1", player.UserObjectID);
        Assert.AreEqual("2026-05-17T00:00:00.000Z", player.CreateTime);
        Assert.AreEqual("token-1", player.SessionToken);
    }

    [TestMethod]
    public void PlayerObject_FromJson_ToleratesMissingFields()
    {
        using var doc = JsonDocument.Parse("{}");

        var player = PlayerObject.FromJson(doc.RootElement);

        Assert.AreEqual(string.Empty, player.Nickname);
        Assert.AreEqual(string.Empty, player.UserObjectID);
    }

    [TestMethod]
    public void PlayerObject_FromJson_ThrowsWhenNotAnObject()
    {
        using var doc = JsonDocument.Parse("[]");

        Assert.ThrowsException<ArgumentException>(() => PlayerObject.FromJson(doc.RootElement));
    }

    [TestMethod]
    public void SaveInfoObject_DefaultsAreEmpty()
    {
        var info = new SaveInfoObject();

        Assert.AreEqual(string.Empty, info.FileUrl);
        Assert.AreEqual(string.Empty, info.FileObjectID);
        Assert.IsNull(info.CloudSummary);
    }

    [TestMethod]
    public async Task SaveInfoObject_DownloadSave_Throws_WhenFileUrlIsEmpty()
    {
        // 契约：下载失败必须抛异常，而不是返回 null
        var info = new SaveInfoObject { FileUrl = string.Empty };

        bool threw = false;
        try
        {
            await info.DownloadSave();
        }
        catch
        {
            threw = true;
        }

        Assert.IsTrue(threw, "FileUrl 无效时 DownloadSave 应当抛出异常");
    }
}
