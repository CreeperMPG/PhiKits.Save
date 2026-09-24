using CreeperMPG.PhiKits.Save.CloudStorage;
using CreeperMPG.PhiKits.Save.Taptap;
using CreeperMPG.PhiKits.Save.Taptap.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;
using System.Text.Json;

namespace CreeperMPG.PhiKits.Save.Tests;

/// <summary>
/// TapTap 二维码登录的**离线**测试：MAC 签名、端点拼接、JSON 解析。
/// 不发起任何网络请求。
/// </summary>
[TestClass]
public class TaptapTests
{
    // ── MAC 签名 ──

    [TestMethod]
    public void MacTokenSigner_MatchesKnownVector()
    {
        // 期望值由独立实现（PowerShell 的 HMACSHA1）算出，不是本实现的回显。
        // 签名输入：ts\nnonce\nmethod\nrequestUrl\nhost\n443\n\n（末尾还有一个换行）
        string header = MacTokenSigner.CreateHeader(
            host: "open.tapapis.cn",
            requestUrl: "/account/profile/v1?client_id=rAK3FfdieFob2Nn8Am",
            method: "GET",
            macKey: "test_mac_key_0123456789",
            kid: "test-kid",
            timestamp: "1700000000",
            nonce: "AbCdEfGhIjKlMnOp");

        Assert.AreEqual(
            "MAC id=\"test-kid\",ts=\"1700000000\",nonce=\"AbCdEfGhIjKlMnOp\",mac=\"/OXqL3h3qMHLTfCYu4X1130WAKQ=\"",
            header);
    }

    [TestMethod]
    public void MacTokenSigner_ChangesWithEveryInput()
    {
        const string host = "open.tapapis.cn";
        const string url = "/account/profile/v1?client_id=x";
        const string key = "k";
        const string kid = "id";
        const string ts = "1700000000";
        const string nonce = "nonce";

        string baseline = MacTokenSigner.CreateHeader(host, url, "GET", key, kid, ts, nonce);

        // 任一要素变化都必须改变签名
        Assert.AreNotEqual(baseline, MacTokenSigner.CreateHeader("other.host", url, "GET", key, kid, ts, nonce));
        Assert.AreNotEqual(baseline, MacTokenSigner.CreateHeader(host, "/other", "GET", key, kid, ts, nonce));
        Assert.AreNotEqual(baseline, MacTokenSigner.CreateHeader(host, url, "POST", key, kid, ts, nonce));
        Assert.AreNotEqual(baseline, MacTokenSigner.CreateHeader(host, url, "GET", "other-key", kid, ts, nonce));
        Assert.AreNotEqual(baseline, MacTokenSigner.CreateHeader(host, url, "GET", key, kid, "1700000001", nonce));
        Assert.AreNotEqual(baseline, MacTokenSigner.CreateHeader(host, url, "GET", key, kid, ts, "other-nonce"));
    }

    [TestMethod]
    public void MacTokenSigner_NonceHasRequestedLengthAndLegalCharset()
    {
        string nonce = MacTokenSigner.CreateNonce(24);

        Assert.AreEqual(24, nonce.Length);
        Assert.IsTrue(nonce.All(c => char.IsAsciiLetter(c)), $"nonce 含非法字符: {nonce}");
    }

    [TestMethod]
    public void MacTokenSigner_NoncesDiffer()
    {
        var seen = new HashSet<string>();
        for (int i = 0; i < 50; i++) seen.Add(MacTokenSigner.CreateNonce(16));

        Assert.AreEqual(50, seen.Count, "nonce 出现重复，随机性可疑");
    }

    [TestMethod]
    public void MacTokenSigner_TimestampIsCurrentUnixSeconds()
    {
        long parsed = long.Parse(MacTokenSigner.CreateTimestamp());

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.IsTrue(Math.Abs(now - parsed) < 60, $"时间戳偏差过大: {parsed} vs {now}");
    }

    [TestMethod]
    public void MacTokenSigner_RejectsNullArguments()
    {
        Assert.ThrowsException<ArgumentNullException>(
            () => MacTokenSigner.CreateHeader(null!, "/x", "GET", "k", "id", "1", "n"));
        Assert.ThrowsException<ArgumentNullException>(
            () => MacTokenSigner.CreateHeader("h", null!, "GET", "k", "id", "1", "n"));
        Assert.ThrowsException<ArgumentNullException>(
            () => MacTokenSigner.CreateHeader("h", "/x", null!, "k", "id", "1", "n"));
        Assert.ThrowsException<ArgumentNullException>(
            () => MacTokenSigner.CreateHeader("h", "/x", "GET", null!, "id", "1", "n"));
    }

    // ── 端点 ──

    [TestMethod]
    public void Endpoints_SwitchBetweenChinaAndInternationalHosts()
    {
        Assert.AreEqual("https://accounts.tapapis.cn/oauth2/v1/device/code", TaptapEndpoints.DeviceCodeUrl(true));
        Assert.AreEqual("https://accounts.tapapis.com/oauth2/v1/device/code", TaptapEndpoints.DeviceCodeUrl(false));

        Assert.AreEqual("https://accounts.tapapis.cn/oauth2/v1/token", TaptapEndpoints.TokenUrl(true));
        Assert.AreEqual("https://accounts.tapapis.com/oauth2/v1/token", TaptapEndpoints.TokenUrl(false));

        Assert.AreEqual("open.tapapis.cn", TaptapEndpoints.ApiHost(true));
        Assert.AreEqual("open.tapapis.com", TaptapEndpoints.ApiHost(false));
    }

    [TestMethod]
    public void Endpoints_ProfilePathCarriesClientId()
    {
        Assert.AreEqual("/account/profile/v1?client_id=" + TaptapEndpoints.ClientId, TaptapEndpoints.ProfilePath);
    }

    [TestMethod]
    public void Client_ExposesRequestedRegion()
    {
        Assert.IsTrue(new TaptapClient(china: true).UsesChinaEndpoints);
        Assert.IsFalse(new TaptapClient(china: false).UsesChinaEndpoints);
        Assert.IsTrue(new TaptapClient().UsesChinaEndpoints, "默认应使用中国版端点");
    }

    // ── JSON 解析：设备码 ──

    [TestMethod]
    public void ParseQrCodeResponse_ReadsAllFields()
    {
        using var doc = JsonDocument.Parse("""
            {"success":true,"now":1700000000,
             "data":{"device_code":"dc-1","qrcode_url":"https://tap.cn/qr/abc","expires_in":300,"interval":5}}
            """);

        var qr = TaptapClient.ParseQrCodeResponse(doc.RootElement);

        Assert.AreEqual("dc-1", qr.DeviceCode);
        Assert.AreEqual("https://tap.cn/qr/abc", qr.QrCodeUrl);
        Assert.AreEqual(300, qr.ExpiresIn);
        Assert.AreEqual(5, qr.Interval);
        Assert.AreEqual(1700000000L, qr.RequestTime);
    }

    [TestMethod]
    public void ParseQrCodeResponse_ThrowsWhenNotSuccessful()
    {
        using var doc = JsonDocument.Parse("""{"success":false,"data":{"error":"invalid_client"}}""");

        var ex = Assert.ThrowsException<InvalidOperationException>(
            () => TaptapClient.ParseQrCodeResponse(doc.RootElement));
        StringAssert.Contains(ex.Message, "invalid_client");
    }

    [TestMethod]
    public void ParseQrCodeResponse_ThrowsWhenDataMissing()
    {
        using var doc = JsonDocument.Parse("""{"success":true}""");

        Assert.ThrowsException<InvalidOperationException>(
            () => TaptapClient.ParseQrCodeResponse(doc.RootElement));
    }

    // ── JSON 解析：轮询 ──

    [TestMethod]
    public void ParsePollResponse_MapsBusinessStates()
    {
        Assert.AreEqual(QrCodeStatus.AuthorizationPending,
            ParsePoll("authorization_pending").Status);
        Assert.AreEqual(QrCodeStatus.AuthorizationWaiting,
            ParsePoll("authorization_waiting").Status);
        Assert.AreEqual(QrCodeStatus.InvalidGrantCode,
            ParsePoll("invalid_grant_code").Status);
    }

    [TestMethod]
    public void ParsePollResponse_PendingStatesCarryNoToken()
    {
        Assert.IsNull(ParsePoll("authorization_pending").Token);
        Assert.IsNull(ParsePoll("authorization_waiting").Token);
        Assert.IsNull(ParsePoll("invalid_grant_code").Token);
    }

    [TestMethod]
    public void ParsePollResponse_SuccessCarriesToken()
    {
        using var doc = JsonDocument.Parse("""
            {"success":true,"data":{"access_token":"at","kid":"k1","mac_key":"mk","mac_algorithm":"hmac-sha-1"}}
            """);

        var result = TaptapClient.ParsePollResponse(doc.RootElement);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(QrCodeStatus.Success, result.Status);
        Assert.IsNotNull(result.Token);
        Assert.AreEqual("at", result.Token!.AccessToken);
        Assert.AreEqual("k1", result.Token.Kid);
        Assert.AreEqual("mk", result.Token.MacKey);
        Assert.AreEqual("hmac-sha-1", result.Token.MacAlgorithm);
    }

    [TestMethod]
    public void ParsePollResponse_ThrowsOnUnknownErrorCode()
    {
        // 没见过的错误码是"意外"，应抛出而不是伪装成已知状态（旧实现会压成 Error）
        using var doc = JsonDocument.Parse("""{"success":false,"data":{"error":"brand_new_error"}}""");

        var ex = Assert.ThrowsException<InvalidOperationException>(
            () => TaptapClient.ParsePollResponse(doc.RootElement));
        StringAssert.Contains(ex.Message, "brand_new_error");
    }

    [TestMethod]
    public void ParsePollResponse_ThrowsOnSuccessWithoutData()
    {
        using var doc = JsonDocument.Parse("""{"success":true}""");

        Assert.ThrowsException<InvalidOperationException>(
            () => TaptapClient.ParsePollResponse(doc.RootElement));
    }

    // ── JSON 解析：账号资料 ──

    [TestMethod]
    public void ParseUserProfile_ReadsFields()
    {
        using var doc = JsonDocument.Parse("""
            {"success":true,"data":{"openid":"o1","unionid":"u1","name":"Neko"}}
            """);

        var profile = TaptapClient.ParseUserProfile(doc.RootElement);

        Assert.AreEqual("o1", profile.OpenId);
        Assert.AreEqual("u1", profile.UnionId);
        Assert.AreEqual("Neko", profile.Name);
    }

    [TestMethod]
    public void ParseUserProfile_ThrowsWhenNotSuccessful()
    {
        using var doc = JsonDocument.Parse("""{"success":false,"error":"bad_signature"}""");

        var ex = Assert.ThrowsException<InvalidOperationException>(
            () => TaptapClient.ParseUserProfile(doc.RootElement));
        StringAssert.Contains(ex.Message, "bad_signature", "根上的 error 也要能取到");
    }

    // ── JSON 解析：登录产物（PlayerObject） ──

    [TestMethod]
    public void ParsePlayer_ReadsSessionTokenAndPlayerFields()
    {
        using var doc = JsonDocument.Parse("""
            {"sessionToken":"st-1","nickname":"Creeper","shortId":"abc",
             "objectId":"obj-1","createdAt":"2023-05-10T12:27:09.338Z"}
            """);

        var player = TaptapClient.ParsePlayer(doc.RootElement);

        Assert.AreEqual("st-1", player.SessionToken);
        Assert.AreEqual("Creeper", player.Nickname);
        Assert.AreEqual("abc", player.ShortID);
        Assert.AreEqual("obj-1", player.UserObjectID);
        Assert.AreEqual("2023-05-10T12:27:09.338Z", player.CreateTime);
    }

    [TestMethod]
    public void ParsePlayer_FallsBackToDataNode()
    {
        using var doc = JsonDocument.Parse("""{"data":{"sessionToken":"nested"}}""");

        Assert.AreEqual("nested", TaptapClient.ParsePlayer(doc.RootElement).SessionToken);
    }

    [TestMethod]
    public void ParsePlayer_ThrowsWhenSessionTokenMissing()
    {
        using var doc = JsonDocument.Parse("""{"nickname":"x"}""");

        var ex = Assert.ThrowsException<InvalidOperationException>(
            () => TaptapClient.ParsePlayer(doc.RootElement));
        StringAssert.Contains(ex.Message, "sessionToken");
    }

    // ── LeanCloud 签名 ──

    [TestMethod]
    public void BuildLeanCloudSign_MatchesKnownVector()
    {
        // md5("1700000000" + AppKey).ToLower() + "," + "1700000000"
        Assert.AreEqual("1016a8ee67669d9e5db8b006ba2a9ac5,1700000000",
            TaptapClient.BuildLeanCloudSign("1700000000"));
    }

    // ── 辅助 ──

    private static QrCodePollResult ParsePoll(string errorCode)
    {
        // 不用内插原始字符串：JSON 末尾的 }} 会和 $$ 的插值定界符冲突
        using var doc = JsonDocument.Parse(
            "{\"success\":false,\"data\":{\"error\":\"" + errorCode + "\"}}");
        return TaptapClient.ParsePollResponse(doc.RootElement);
    }
}
