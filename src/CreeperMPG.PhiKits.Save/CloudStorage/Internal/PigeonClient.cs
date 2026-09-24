using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CreeperMPG.PhiKits.Save.CloudStorage.Internal
{
    /// <summary>
    /// LeanCloud（Phigros 云端，代号 "Pigeon"）的接口常量与 HTTP 访问。
    /// 所有云端地址、应用凭据都集中在这里。
    /// </summary>
    internal static class PigeonClient
    {
        internal const string ApiRoot = "https://rak3ffdi.cloud.tds1.tapapis.cn/1.1";
        internal const string AppId = "rAK3FfdieFob2Nn8Am";
        internal const string AppKey = "Qr9AEqtuoSVS3zeD6iVbM4ZC0AtkJcQ89tywVyi0";

        internal const string UserApiUrl = ApiRoot + "/users/me";
        internal const string SaveApiUrl = ApiRoot + "/classes/_GameSave";
        internal const string FileTokenApiUrl = ApiRoot + "/fileTokens";
        internal const string FileCallbackApiUrl = ApiRoot + "/fileCallback";

        /// <summary>单条 _GameSave（槽位）记录的自定义端点</summary>
        internal static string GameSaveUrl(string saveInfoObjectId) => $"{ApiRoot}/gamesaves/{saveInfoObjectId}";

        /// <summary>单个 _File（存档文件）的自定义端点</summary>
        internal static string FileUrl(string fileObjectId) => $"{ApiRoot}/files/{fileObjectId}";

        private static readonly HttpClient s_client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });

        /// <summary>构造带认证头的请求（X-LC-Id / X-LC-Key / X-LC-Session）</summary>
        internal static HttpRequestMessage CreateRequest(HttpMethod method, string url, string sessionToken)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Add("X-LC-Id", AppId);
            request.Headers.Add("X-LC-Key", AppKey);
            request.Headers.Add("X-LC-Session", sessionToken);
            request.Headers.UserAgent.ParseAdd("LeanCloud-CSharp-SDK/1.0.3");
            request.Headers.Accept.ParseAdd("application/json");
            return request;
        }

        /// <summary>发送请求但不检查状态码（由调用方自行判断）</summary>
        internal static Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => s_client.SendAsync(request, cancellationToken);

        /// <summary>发送请求，响应非 2xx 时抛出 <see cref="HttpRequestException"/></summary>
        internal static async Task<HttpResponseMessage> SendCheckedAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await s_client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string message = $"云端请求失败：{request.Method} {request.RequestUri} -> " +
                                 $"{(int)response.StatusCode} {response.ReasonPhrase}";
                response.Dispose();
                throw new HttpRequestException(message);
            }
            return response;
        }

        /// <summary>发送请求并解析 JSON 响应</summary>
        internal static async Task<JsonDocument> SendJsonAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var response = await SendCheckedAsync(request, cancellationToken).ConfigureAwait(false);
            string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonDocument.Parse(content);
        }

        /// <summary>GET 并解析 JSON 响应</summary>
        internal static async Task<JsonDocument> GetJsonAsync(string url, string sessionToken, CancellationToken cancellationToken)
        {
            using var request = CreateRequest(HttpMethod.Get, url, sessionToken);
            return await SendJsonAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
