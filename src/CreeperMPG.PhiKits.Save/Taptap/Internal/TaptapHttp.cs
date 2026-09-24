using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CreeperMPG.PhiKits.Save.Taptap.Internal
{
    /// <summary>
    /// TapTap 请求共用的 <see cref="HttpClient"/>。
    /// <para>
    /// 旧实现在每个方法里 <c>new HttpClient()</c>，会耗尽套接字；这里改为静态单例。
    /// </para>
    /// </summary>
    internal static class TaptapHttp
    {
        private static readonly HttpClient s_client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        /// <summary>
        /// 发送请求并解析 JSON 响应体。
        /// <para>
        /// ⚠️ **不看 HTTP 状态码，只看响应体是不是合法 JSON。**
        /// TapTap 用**非 2xx** 表达业务状态——例如设备码轮询尚未授权时返回
        /// <c>HTTP 400</c> + <c>{"success":false,"data":{"error":"authorization_pending"}}</c>。
        /// 若在这里因为状态码抛异常，就会把业务错误码整个丢掉，轮询永远无法成功。
        /// </para>
        /// <para>
        /// 只有响应体**不是**合法 JSON（网关错误页、空响应等真正的传输层故障）才抛异常。
        /// </para>
        /// </summary>
        internal static async Task<JsonDocument> SendJsonAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var response = await s_client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                return JsonDocument.Parse(body);
            }
            catch (JsonException ex)
            {
                string snippet = body.Length > 200 ? body[..200] + "..." : body;
                throw new HttpRequestException(
                    $"TapTap 请求失败：{request.Method} {request.RequestUri} -> " +
                    $"{(int)response.StatusCode} {response.ReasonPhrase}；响应体不是 JSON：{snippet}", ex);
            }
        }

        /// <summary>以表单方式 POST 并解析 JSON 响应</summary>
        internal static async Task<JsonDocument> PostFormAsync(string url, IDictionary<string, string> form,
                                                              CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent(form),
            };
            return await SendJsonAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
