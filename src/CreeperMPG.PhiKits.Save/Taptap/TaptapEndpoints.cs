namespace CreeperMPG.PhiKits.Save.Taptap
{
    /// <summary>
    /// TapTap 开放平台与 Phigros 云端的端点常量。
    /// <para>
    /// TapTap 分**国际版**与**中国版**两套主机，除设备码/令牌端点外，
    /// 用户信息端点也各有一套；Phigros 云端的用户端点则是独立域名。
    /// </para>
    /// </summary>
    public static class TaptapEndpoints
    {
        /// <summary>TapSDK 版本（请求参数 version）</summary>
        public const string SdkVersion = "2.1";

        /// <summary>TapTap 应用 Client ID</summary>
        public const string ClientId = "rAK3FfdieFob2Nn8Am";

        public const string InternationalWebHost = "accounts.tapapis.com";
        public const string ChinaWebHost = "accounts.tapapis.cn";
        public const string InternationalApiHost = "open.tapapis.com";
        public const string ChinaApiHost = "open.tapapis.cn";

        /// <summary>
        /// 用户信息端点（MAC 签名用的 requestUrl，含查询串）。
        /// <c>client_id</c> 是常量，所以整个串可以做成编译期常量。
        /// </summary>
        public const string ProfilePath = "/account/profile/v1?client_id=" + ClientId;

        /// <summary>Phigros 云端的用户端点：用 TapTap 的 authData 换取 sessionToken</summary>
        public const string PhigrosUserApiUrl = "https://rak3ffdi.cloud.tds1.tapapis.cn/1.1/users";

        /// <summary>Phigros 云端 LeanCloud 的 App Key（用于 X-LC-Sign）</summary>
        internal const string LeanCloudAppKey = "Qr9AEqtuoSVS3zeD6iVbM4ZC0AtkJcQ89tywVyi0";

        internal static string DeviceCodeUrl(bool china)
            => $"https://{(china ? ChinaWebHost : InternationalWebHost)}/oauth2/v1/device/code";

        internal static string TokenUrl(bool china)
            => $"https://{(china ? ChinaWebHost : InternationalWebHost)}/oauth2/v1/token";

        internal static string ApiHost(bool china) => china ? ChinaApiHost : InternationalApiHost;
    }
}
