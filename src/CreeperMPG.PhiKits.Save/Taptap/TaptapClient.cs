using CreeperMPG.PhiKits.Save.CloudStorage;
using CreeperMPG.PhiKits.Save.Taptap.Internal;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CreeperMPG.PhiKits.Save.Taptap
{
    /// <summary>
    /// TapTap 二维码（设备码）登录。
    /// <para>
    /// 完整流程：
    /// <list type="number">
    ///   <item><see cref="GetLoginQrCodeAsync"/> 取设备码，把 <c>QrCodeUrl</c> 渲染成二维码</item>
    ///   <item>用户用 TapTap App 扫码并确认</item>
    ///   <item><see cref="PollQrCodeAsync"/> 轮询状态（或用 <see cref="WaitForAuthorizationAsync"/> 一次性等到结果）</item>
    ///   <item><see cref="FetchUserProfileAsync"/> 取 TapTap 账号资料</item>
    ///   <item><see cref="GetPhigrosSessionAsync"/> 换取 Phigros 的 <c>sessionToken</c></item>
    /// </list>
    /// </para>
    /// <para>
    /// <see cref="LoginAsync"/> 把以上流程串成一个调用。
    /// </para>
    /// <para>
    /// <b>失败约定</b>：可预期的业务状态通过返回值表达（见 <see cref="QrCodeStatus"/>）；
    /// 网络错误、响应格式异常、取消等**技术失败一律抛异常**。
    /// </para>
    /// <para>
    /// 产物是 <see cref="PlayerObject"/>——它同时携带会话令牌与玩家信息，
    /// 可直接用于云存档操作。
    /// </para>
    /// </summary>
    public sealed class TaptapClient
    {
        /// <summary>服务端未下发 expires_in 时的兜底有效期（秒）</summary>
        private const int DefaultExpiresInSeconds = 300;

        private readonly bool _china;

        /// <summary>用中国版端点（默认）还是国际版端点</summary>
        public bool UsesChinaEndpoints => _china;

        /// <param name="china">true 用中国版主机，false 用国际版</param>
        public TaptapClient(bool china = true) => _china = china;

        // ── 第一步：设备码 ──

        /// <summary>
        /// 获取登录用的设备码与二维码内容。
        /// </summary>
        public async Task<QrCodeResponse> GetLoginQrCodeAsync(CancellationToken cancellationToken = default)
        {
            var form = new Dictionary<string, string>
            {
                ["client_id"] = TaptapEndpoints.ClientId,
                ["response_type"] = "device_code",
                ["scope"] = "public_profile",
                ["version"] = TaptapEndpoints.SdkVersion,
                ["platform"] = "unity",
            };

            using var doc = await TaptapHttp.PostFormAsync(TaptapEndpoints.DeviceCodeUrl(_china), form, cancellationToken)
                .ConfigureAwait(false);
            return ParseQrCodeResponse(doc.RootElement);
        }

        // ── 第三步：轮询授权状态 ──

        /// <summary>
        /// 轮询一次授权状态。调用方按 <see cref="QrCodeResponse.Interval"/> 自行决定节奏，
        /// 或用 <see cref="WaitForAuthorizationAsync"/> 让它代劳。
        /// </summary>
        public async Task<QrCodePollResult> PollQrCodeAsync(string deviceCode, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(deviceCode)) throw new ArgumentNullException(nameof(deviceCode));

            var form = new Dictionary<string, string>
            {
                ["client_id"] = TaptapEndpoints.ClientId,
                ["grant_type"] = "device_token",
                ["code"] = deviceCode,
                ["version"] = TaptapEndpoints.SdkVersion,
                ["platform"] = "unity",
                ["secret_type"] = "hmac-sha-1",
            };

            using var doc = await TaptapHttp.PostFormAsync(TaptapEndpoints.TokenUrl(_china), form, cancellationToken)
                .ConfigureAwait(false);
            return ParsePollResponse(doc.RootElement);
        }

        /// <summary>
        /// 按服务端建议的间隔持续轮询，直到**有了明确结论**。
        /// <para>
        /// 返回 <see cref="QrCodeStatus.Success"/>（拿到令牌）或
        /// <see cref="QrCodeStatus.InvalidGrantCode"/>（设备码失效，需重新获取二维码）。
        /// </para>
        /// </summary>
        /// <param name="qrCode">第一步拿到的设备码响应（提供 Interval / ExpiresIn / DeviceCode）</param>
        /// <param name="progress">可选：每次轮询后回报一次状态</param>
        /// <exception cref="TimeoutException">本地等待超过了 <see cref="QrCodeResponse.ExpiresIn"/></exception>
        /// <exception cref="OperationCanceledException">调用方取消</exception>
        public async Task<QrCodePollResult> WaitForAuthorizationAsync(
            QrCodeResponse qrCode,
            IProgress<QrCodeStatus>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (qrCode == null) throw new ArgumentNullException(nameof(qrCode));

            var interval = TimeSpan.FromSeconds(Math.Max(1, qrCode.Interval));
            int expiresIn = qrCode.ExpiresIn > 0 ? qrCode.ExpiresIn : DefaultExpiresInSeconds;
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                QrCodePollResult result = await PollQrCodeAsync(qrCode.DeviceCode, cancellationToken)
                    .ConfigureAwait(false);
                progress?.Report(result.Status);

                if (result.Status == QrCodeStatus.Success || result.Status == QrCodeStatus.InvalidGrantCode)
                    return result;

                if (DateTimeOffset.UtcNow.Add(interval) >= deadline)
                    throw new TimeoutException($"TapTap 二维码未在 {expiresIn} 秒内完成授权。");

                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
        }

        // ── 第四步：TapTap 账号资料 ──

        /// <summary>用授权令牌取 TapTap 账号资料（openid / unionid / name）</summary>
        public async Task<TaptapUserProfile> FetchUserProfileAsync(QrCodeResult token,
                                                                   CancellationToken cancellationToken = default)
        {
            if (token == null) throw new ArgumentNullException(nameof(token));

            string host = TaptapEndpoints.ApiHost(_china);
            string signature = MacTokenSigner.CreateHeader(host, TaptapEndpoints.ProfilePath, "GET",
                                                          token.MacKey, token.Kid);

            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{host}{TaptapEndpoints.ProfilePath}");
            request.Headers.TryAddWithoutValidation("Authorization", signature);

            using var doc = await TaptapHttp.SendJsonAsync(request, cancellationToken).ConfigureAwait(false);
            return ParseUserProfile(doc.RootElement);
        }

        // ── 第五步：换取 Phigros 会话 ──

        /// <summary>
        /// 用 TapTap 授权信息换取 Phigros 云端的会话，直接返回可用的
        /// <see cref="PlayerObject"/>（会话令牌与玩家信息都在里面）。
        /// <para>
        /// 这一步的响应本身就是 Phigros <c>/users/me</c> 的格式，
        /// 所以玩家信息不需要再发一次请求——也正因如此**不再需要单独的会话类型**。
        /// </para>
        /// </summary>
        public async Task<PlayerObject> GetPhigrosSessionAsync(QrCodeResult token, TaptapUserProfile profile,
                                                               CancellationToken cancellationToken = default)
        {
            if (token == null) throw new ArgumentNullException(nameof(token));
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            string timestamp = MacTokenSigner.CreateTimestamp();

            var payload = new
            {
                authData = new
                {
                    taptap = new Dictionary<string, string>
                    {
                        ["access_token"] = token.AccessToken,
                        ["kid"] = token.Kid,
                        ["mac_key"] = token.MacKey,
                        ["mac_algorithm"] = token.MacAlgorithm,
                        ["openid"] = profile.OpenId,
                        ["unionid"] = profile.UnionId,
                        ["name"] = profile.Name,
                    },
                },
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, TaptapEndpoints.PhigrosUserApiUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("X-LC-Id", TaptapEndpoints.ClientId);
            request.Headers.TryAddWithoutValidation("X-LC-Sign", BuildLeanCloudSign(timestamp));

            using var doc = await TaptapHttp.SendJsonAsync(request, cancellationToken).ConfigureAwait(false);
            return ParsePlayer(doc.RootElement);
        }

        // ── 一把梭 ──

        /// <summary>
        /// 走完整个二维码登录流程。
        /// </summary>
        /// <param name="onQrCodeReady">
        /// 拿到二维码后回调——调用方在这里把 <see cref="QrCodeResponse.QrCodeUrl"/> 渲染给用户。
        /// 允许异步（例如写入界面）。
        /// </param>
        /// <param name="progress">可选：轮询进度</param>
        /// <returns>可直接用于云存档操作的 <see cref="PlayerObject"/></returns>
        /// <exception cref="InvalidOperationException">设备码失效（二维码过期或已被使用）</exception>
        public async Task<PlayerObject> LoginAsync(Func<QrCodeResponse, Task> onQrCodeReady,
                                                   IProgress<QrCodeStatus>? progress = null,
                                                   CancellationToken cancellationToken = default)
        {
            if (onQrCodeReady == null) throw new ArgumentNullException(nameof(onQrCodeReady));

            QrCodeResponse qrCode = await GetLoginQrCodeAsync(cancellationToken).ConfigureAwait(false);
            await onQrCodeReady(qrCode).ConfigureAwait(false);

            QrCodePollResult poll = await WaitForAuthorizationAsync(qrCode, progress, cancellationToken)
                .ConfigureAwait(false);

            if (!poll.IsSuccess || poll.Token == null)
                throw new InvalidOperationException("TapTap 设备码已失效，请重新获取二维码。");

            TaptapUserProfile profile = await FetchUserProfileAsync(poll.Token, cancellationToken).ConfigureAwait(false);
            return await GetPhigrosSessionAsync(poll.Token, profile, cancellationToken).ConfigureAwait(false);
        }

        // ── 解析（internal 以便测试覆盖） ──

        internal static QrCodeResponse ParseQrCodeResponse(JsonElement root)
        {
            if (!IsSuccess(root))
                throw new InvalidOperationException($"TapTap 拒绝下发设备码：{DescribeError(root)}");

            JsonElement data = RequireData(root, "设备码");

            return new QrCodeResponse
            {
                DeviceCode = GetStringOrEmpty(data, "device_code"),
                QrCodeUrl = GetStringOrEmpty(data, "qrcode_url"),
                ExpiresIn = GetInt32OrDefault(data, "expires_in"),
                Interval = GetInt32OrDefault(data, "interval"),
                RequestTime = GetInt64OrDefault(root, "now"),
            };
        }

        internal static QrCodePollResult ParsePollResponse(JsonElement root)
        {
            if (IsSuccess(root))
            {
                JsonElement data = RequireData(root, "授权");

                return QrCodePollResult.FromToken(new QrCodeResult
                {
                    AccessToken = GetStringOrEmpty(data, "access_token"),
                    Kid = GetStringOrEmpty(data, "kid"),
                    MacKey = GetStringOrEmpty(data, "mac_key"),
                    MacAlgorithm = GetStringOrEmpty(data, "mac_algorithm"),
                });
            }

            return GetErrorCode(root) switch
            {
                "authorization_pending" => QrCodePollResult.Pending(),
                "authorization_waiting" => QrCodePollResult.WaitingAuthorization(),
                "invalid_grant_code" => QrCodePollResult.InvalidGrantCode(),
                // 没见过的错误码属于"意外"，抛出去而不是伪装成已知状态
                _ => throw new InvalidOperationException($"TapTap 授权失败：{DescribeError(root)}"),
            };
        }

        internal static TaptapUserProfile ParseUserProfile(JsonElement root)
        {
            if (!IsSuccess(root))
                throw new InvalidOperationException($"TapTap 账号资料获取失败：{DescribeError(root)}");

            JsonElement data = RequireData(root, "账号资料");

            return new TaptapUserProfile
            {
                OpenId = GetStringOrEmpty(data, "openid"),
                UnionId = GetStringOrEmpty(data, "unionid"),
                Name = GetStringOrEmpty(data, "name"),
            };
        }

        /// <summary>
        /// 把 Phigros 登录响应解析成 <see cref="PlayerObject"/>。
        /// 该响应与 <c>/users/me</c> 同构，所以字段名与 <see cref="PlayerObject.FromJson"/> 保持一致。
        /// </summary>
        internal static PlayerObject ParsePlayer(JsonElement root)
        {
            // LeanCloud 把用户文档放在根上（旧实现也是从根读 nickname/shortId/...）
            string sessionToken = GetStringOrEmpty(root, "sessionToken");
            if (string.IsNullOrEmpty(sessionToken) && root.TryGetProperty("data", out JsonElement data)
                && data.ValueKind == JsonValueKind.Object)
            {
                sessionToken = GetStringOrEmpty(data, "sessionToken");
            }

            if (string.IsNullOrEmpty(sessionToken))
                throw new InvalidOperationException("Phigros 登录响应缺少 sessionToken。");

            return new PlayerObject(
                GetStringOrEmpty(root, "nickname"),
                GetStringOrEmpty(root, "shortId"),
                GetStringOrEmpty(root, "objectId"),
                GetStringOrEmpty(root, "createdAt"),
                sessionToken);
        }

        // ── 工具 ──

        /// <summary>LeanCloud 的 X-LC-Sign = <c>md5(timestamp + AppKey).ToLower() + "," + timestamp</c></summary>
        internal static string BuildLeanCloudSign(string timestamp)
        {
            using var md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(timestamp + TaptapEndpoints.LeanCloudAppKey));
            return Convert.ToHexString(hash).ToLowerInvariant() + "," + timestamp;
        }

        private static bool IsSuccess(JsonElement root)
            => root.ValueKind == JsonValueKind.Object
               && root.TryGetProperty("success", out JsonElement success)
               && success.ValueKind == JsonValueKind.True;

        private static JsonElement RequireData(JsonElement root, string what)
        {
            if (!root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"TapTap {what}响应缺少 data 字段。");
            return data;
        }

        /// <summary>取错误码：优先 <c>data.error</c>，其次根上的 <c>error</c></summary>
        private static string GetErrorCode(JsonElement root)
        {
            if (root.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("error", out JsonElement inner) && inner.ValueKind == JsonValueKind.String)
            {
                return inner.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("error", out JsonElement outer) && outer.ValueKind == JsonValueKind.String)
                return outer.GetString() ?? string.Empty;

            return string.Empty;
        }

        private static string DescribeError(JsonElement root)
        {
            string code = GetErrorCode(root);
            return string.IsNullOrEmpty(code) ? "响应中没有 success/error 字段" : code;
        }

        private static string GetStringOrEmpty(JsonElement element, string propertyName)
            => GetStringOrNull(element, propertyName) ?? string.Empty;

        private static string? GetStringOrNull(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;
            if (!element.TryGetProperty(propertyName, out JsonElement value)) return null;

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => value.ToString(),
            };
        }

        private static int GetInt32OrDefault(JsonElement element, string propertyName)
            => GetInt64OrDefault(element, propertyName) is long value && value >= int.MinValue && value <= int.MaxValue
                ? (int)value
                : 0;

        private static long GetInt64OrDefault(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object) return 0;
            if (!element.TryGetProperty(propertyName, out JsonElement value)) return 0;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number)) return number;
            if (value.ValueKind == JsonValueKind.String
                && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
            {
                return parsed;
            }
            return 0;
        }
    }
}
