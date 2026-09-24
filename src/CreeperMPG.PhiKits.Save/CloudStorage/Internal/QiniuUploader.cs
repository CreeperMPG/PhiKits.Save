using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CreeperMPG.PhiKits.Save.CloudStorage.Internal
{
    /// <summary>
    /// 七牛云分片上传。Phigros 的 .save 只上传一片，但仍走完整的分片流程。
    /// </summary>
    internal static class QiniuUploader
    {
        private const string Bucket = "rAK3Ffdi";
        private const string UploadHost = "upload.qiniup.com";

        private static readonly HttpClient s_client = new HttpClient();

        /// <summary>初始化分片 → 上传数据 → 合并分片</summary>
        /// <param name="data">.save 的 zip 字节</param>
        /// <param name="fileKey">fileTokens 返回的 key</param>
        /// <param name="uploadToken">fileTokens 返回的 token</param>
        internal static async Task UploadAsync(byte[] data, string fileKey, string uploadToken, CancellationToken cancellationToken)
        {
            string b64Key = Convert.ToBase64String(Encoding.UTF8.GetBytes(fileKey));
            string objectUrl = $"http://{UploadHost}/buckets/{Bucket}/objects/{b64Key}/uploads";

            // 1. 初始化分片，取得 uploadId
            string uploadId;
            using (var request = CreateRequest(HttpMethod.Post, objectUrl, uploadToken))
            {
                request.Content = new StringContent(string.Empty, Encoding.UTF8, "application/octet-stream");
                using var doc = await SendJsonAsync(request, cancellationToken).ConfigureAwait(false);
                uploadId = doc.RootElement.GetProperty("uploadId").GetString()!;
            }

            // 2. 上传第 1 片，取得 etag
            string etag;
            using (var request = CreateRequest(HttpMethod.Put, $"{objectUrl}/{uploadId}/1", uploadToken))
            {
                request.Content = new ByteArrayContent(data);
                using var doc = await SendJsonAsync(request, cancellationToken).ConfigureAwait(false);
                etag = doc.RootElement.GetProperty("etag").GetString()!;
            }

            // 3. 合并分片
            using (var request = CreateRequest(HttpMethod.Post, $"{objectUrl}/{uploadId}", uploadToken))
            {
                request.Content = new StringContent(
                    JsonSerializer.Serialize(new { parts = new[] { new { partNumber = 1, etag } } }),
                    Encoding.UTF8, "application/json");
                using var response = await SendCheckedAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }

        private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string uploadToken)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Host = UploadHost;
            request.Headers.Add("Authorization", $"UpToken {uploadToken}");
            return request;
        }

        private static async Task<JsonDocument> SendJsonAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var response = await SendCheckedAsync(request, cancellationToken).ConfigureAwait(false);
            string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonDocument.Parse(content);
        }

        private static async Task<HttpResponseMessage> SendCheckedAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await s_client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string message = $"七牛上传失败：{request.Method} {request.RequestUri} -> " +
                                 $"{(int)response.StatusCode} {response.ReasonPhrase}";
                response.Dispose();
                throw new HttpRequestException(message);
            }
            return response;
        }
    }
}
