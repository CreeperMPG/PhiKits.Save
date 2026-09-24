using CreeperMPG.PhiKits.Save.CloudStorage.Internal;
using CreeperMPG.PhiKits.Save.Data;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CreeperMPG.PhiKits.Save.CloudStorage
{
    /// <summary>
    /// 玩家门面：持有会话 Token，提供云存档的列出 / 上传 / 下载 / 删除 / Token 刷新。
    /// <para>
    /// 本类不处理登录——会话 Token 由 <c>Taptap.TaptapClient</c> 产出
    /// （它的 <c>LoginAsync</c> 直接返回本类的实例），也可以由调用方自行提供。
    /// </para>
    /// </summary>
    public class PlayerObject
    {
        public string Nickname { get; private set; } = string.Empty;
        public string ShortID { get; private set; } = string.Empty;
        public string UserObjectID { get; private set; } = string.Empty;
        public string CreateTime { get; private set; } = string.Empty;

        /// <summary>会话 Token；<see cref="RefreshToken"/> 成功后会就地更新</summary>
        public string SessionToken { get; internal set; } = string.Empty;


        /// <summary>
        /// 仅凭会话 Token 构造。用户信息（昵称、ShortID 等）此时为空，
        /// 需要时调用 <see cref="FetchUserInfo"/> 补齐。
        /// </summary>
        public PlayerObject(string nickname, string shortId, string objectId, string createTime, string sessionToken = "")
        {
            Nickname = nickname;
            ShortID = shortId;
            UserObjectID = objectId;
            CreateTime = createTime;
            SessionToken = sessionToken;
        }

        /// <summary>从 /users/me 返回的用户 JSON 构造</summary>
        public static PlayerObject FromJson(JsonElement userInfo, string sessionToken = "")
        {
            if (userInfo.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("用户信息不是 JSON 对象。", nameof(userInfo));

            return new PlayerObject(
                GetStringOrEmpty(userInfo, "nickname"),
                GetStringOrEmpty(userInfo, "shortId"),
                GetStringOrEmpty(userInfo, "objectId"),
                GetStringOrEmpty(userInfo, "createdAt"),
                sessionToken);
        }

        /// <summary>拉取当前 Token 对应的玩家信息（静态工厂）</summary>
        public static async Task<PlayerObject> FetchAsync(string sessionToken, CancellationToken cancellationToken = default)
        {
            using var doc = await PigeonClient.GetJsonAsync(PigeonClient.UserApiUrl, sessionToken, cancellationToken)
                .ConfigureAwait(false);
            return FromJson(doc.RootElement, sessionToken);
        }

        /// <summary>
        /// 用当前 Token 拉取玩家信息，填充本实例的昵称、ShortID、UserObjectID 与创建时间。
        /// 上传存档需要 <see cref="UserObjectID"/>（ACL 与 user 指针都用它），
        /// 所以用仅 Token 构造之后、上传之前必须调用一次。
        /// </summary>
        public async Task FetchUserInfo(CancellationToken cancellationToken = default)
        {
            using var doc = await PigeonClient.GetJsonAsync(PigeonClient.UserApiUrl, SessionToken, cancellationToken)
                .ConfigureAwait(false);
            var root = doc.RootElement;

            Nickname = GetStringOrEmpty(root, "nickname");
            ShortID = GetStringOrEmpty(root, "shortId");
            UserObjectID = GetStringOrEmpty(root, "objectId");
            CreateTime = GetStringOrEmpty(root, "createdAt");
        }

        // ── 存档列表 ──

        /// <summary>
        /// 列出云端所有存档槽位。
        /// 摘要解析失败时该槽位的 <see cref="SaveInfoObject.CloudSummary"/> 为 null，但槽位仍会返回。
        /// </summary>
        public async Task<SaveInfoObject[]> GetSaveInfo(CancellationToken cancellationToken = default)
        {
            using var doc = await PigeonClient.GetJsonAsync(PigeonClient.SaveApiUrl, SessionToken, cancellationToken)
                .ConfigureAwait(false);

            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                return Array.Empty<SaveInfoObject>();

            var list = new List<SaveInfoObject>();
            foreach (var info in results.EnumerateArray())
                list.Add(ParseSaveInfo(info));

            return list.ToArray();
        }

        /// <summary>
        /// 把一条云端 _GameSave 的 JSON 解析为 <see cref="SaveInfoObject"/>。
        /// 宽容处理：缺字段不抛异常、不丢弃槽位，摘要解析失败则 <c>CloudSummary</c> 为 null。
        /// </summary>
        private static SaveInfoObject ParseSaveInfo(JsonElement info)
        {
            // gameFile 可能缺失（文件被删、指针悬空），此处不校验链接、不丢弃槽位
            JsonElement gameFile = info.TryGetProperty("gameFile", out var gf) && gf.ValueKind == JsonValueKind.Object
                ? gf
                : default;

            return new SaveInfoObject(
                fileUrl: GetStringOrEmpty(gameFile, "url"),
                fileObjectId: GetStringOrEmpty(gameFile, "objectId"),
                saveInfoObjectId: GetStringOrEmpty(info, "objectId"),
                saveUpdateTime: GetStringOrEmpty(gameFile, "updatedAt"),
                cloudSummary: SaveSummary.TryFromBase64(GetStringOrNull(info, "summary")));
        }

        // ── 上传（新建槽位） ──

        /// <summary>
        /// 上传存档并新建一个云槽位。
        /// 摘要请用 <see cref="SavePackage.GenerateSummary"/> 生成后传入（本类不解析存档内容）。
        /// </summary>
        public Task<SaveInfoObject> UploadSave(SavePackage package, SaveSummary summary, CancellationToken cancellationToken = default)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (summary == null) throw new ArgumentNullException(nameof(summary));

            return UploadSave(package.ToZipBytes(), summary.ToBase64String(), cancellationToken);
        }

        /// <summary>上传 .save zip 字节并新建一个云槽位</summary>
        public Task<SaveInfoObject> UploadSave(byte[] zipFile, SaveSummary summary, CancellationToken cancellationToken = default)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));

            return UploadSave(zipFile, summary.ToBase64String(), cancellationToken);
        }

        /// <summary>上传存档并新建一个云槽位（摘要为已序列化的 Base64 字符串）</summary>
        public Task<SaveInfoObject> UploadSave(SavePackage package, string summary, CancellationToken cancellationToken = default)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            return UploadSave(package.ToZipBytes(), summary, cancellationToken);
        }

        /// <summary>
        /// 上传 .save zip 字节并新建一个云槽位（摘要为已序列化的 Base64 字符串）。
        /// 流程：申请上传 Token → 七牛分片上传 → 回调 → 新建 _GameSave 记录。
        /// </summary>
        public async Task<SaveInfoObject> UploadSave(byte[] zipFile, string summary, CancellationToken cancellationToken = default)
        {
            if (zipFile == null) throw new ArgumentNullException(nameof(zipFile));
            if (string.IsNullOrEmpty(summary)) throw new ArgumentNullException(nameof(summary));
            // UserObjectID 是 ACL 与 user 指针的必需项；缺失会写出无人可读的存档
            if (string.IsNullOrEmpty(UserObjectID))
                throw new InvalidOperationException(
                    "UserObjectID 为空，无法上传。请先用 FetchUserInfo() 补齐玩家信息。");

            string modifiedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff") + "Z";

            // 1. 申请上传 Token
            var fileMeta = new
            {
                name = ".save",
                __type = "File",
                ACL = new Dictionary<string, object> { [UserObjectID] = new { read = true, write = true } },
                prefix = "gamesaves",
                metaData = new { size = zipFile.Length, _checksum = GetMd5String(zipFile), prefix = "gamesaves" }
            };

            string fileKey, fileObjectId, uploadToken;
            using (var request = PigeonClient.CreateRequest(HttpMethod.Post, PigeonClient.FileTokenApiUrl, SessionToken))
            {
                request.Content = new StringContent(JsonSerializer.Serialize(fileMeta), Encoding.UTF8, "application/json");
                using var doc = await PigeonClient.SendJsonAsync(request, cancellationToken).ConfigureAwait(false);
                fileKey = doc.RootElement.GetProperty("key").GetString()!;
                fileObjectId = doc.RootElement.GetProperty("objectId").GetString()!;
                uploadToken = doc.RootElement.GetProperty("token").GetString()!;
            }

            // 2. 七牛分片上传
            await QiniuUploader.UploadAsync(zipFile, fileKey, uploadToken, cancellationToken).ConfigureAwait(false);

            // 3. 通知云端上传完成
            using (var request = PigeonClient.CreateRequest(HttpMethod.Post, PigeonClient.FileCallbackApiUrl, SessionToken))
            {
                request.Content = new StringContent(
                    JsonSerializer.Serialize(new { result = true, token = uploadToken }),
                    Encoding.UTF8, "application/json");
                using var response = await PigeonClient.SendCheckedAsync(request, cancellationToken).ConfigureAwait(false);
            }

            // 4. 新建 _GameSave 记录（写入摘要）
            var saveData = new
            {
                summary,
                modifiedAt = new { __type = "Date", iso = modifiedAt },
                gameFile = new { __type = "Pointer", className = "_File", objectId = fileObjectId },
                ACL = new Dictionary<string, object> { [UserObjectID] = new { read = true, write = true } },
                user = new { __type = "Pointer", className = "_User", objectId = UserObjectID },
                name = ".save"
            };

            using (var request = PigeonClient.CreateRequest(HttpMethod.Post, PigeonClient.SaveApiUrl, SessionToken))
            {
                request.Content = new StringContent(JsonSerializer.Serialize(saveData), Encoding.UTF8, "application/json");
                using var doc = await PigeonClient.SendJsonAsync(request, cancellationToken).ConfigureAwait(false);

                var root = doc.RootElement;
                string saveInfoObjectId = GetStringOrEmpty(root, "objectId");

                // 新建记录的响应里 gameFile 只是指针（不含 url），
                // 因此再单独取一次该记录，才能拿到可下载的完整元信息。
                var created = await GetSaveInfoById(saveInfoObjectId, cancellationToken).ConfigureAwait(false);
                if (created != null)
                    return created;

                // 单独取失败时退回响应里的残缺信息（FileUrl 为空，但 ID 可用）
                JsonElement gameFile = root.TryGetProperty("gameFile", out var gf) && gf.ValueKind == JsonValueKind.Object
                    ? gf
                    : default;

                return new SaveInfoObject(
                    fileUrl: GetStringOrEmpty(gameFile, "url"),
                    fileObjectId: fileObjectId,
                    saveInfoObjectId: saveInfoObjectId,
                    saveUpdateTime: modifiedAt,
                    cloudSummary: SaveSummary.TryFromBase64(summary));
            }
        }

        /// <summary>
        /// 单独取一条槽位记录（GET /1.1/gamesaves/{id}）。
        /// 上传后用它把只含指针的响应补全成可下载的 <see cref="SaveInfoObject"/>；失败返回 null。
        /// </summary>
        private async Task<SaveInfoObject?> GetSaveInfoById(string saveInfoObjectId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(saveInfoObjectId)) return null;

            try
            {
                using var request = PigeonClient.CreateRequest(
                    HttpMethod.Get, PigeonClient.GameSaveUrl(saveInfoObjectId), SessionToken);
                using var doc = await PigeonClient.SendJsonAsync(request, cancellationToken).ConfigureAwait(false);

                return ParseSaveInfo(doc.RootElement);
            }
            catch
            {
                return null;
            }
        }

        // ── 删除 ──

        /// <summary>
        /// 从云端彻底删除一个存档槽位：先删 <c>_GameSave</c> 记录，再删它指向的 <c>_File</c> 文件。
        /// <para>只删文件会留下指向空洞的「僵尸槽位」——它仍出现在列表里，但下载必然失败。</para>
        /// </summary>
        public async Task DeleteSave(SaveInfoObject save, CancellationToken cancellationToken = default)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));

            await DeleteSave(save.FileObjectID, save.SaveInfoObjectID, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 从云端彻底删除一个存档槽位：先删 <c>_GameSave</c> 记录，再删 <c>_File</c> 文件。
        /// </summary>
        public async Task DeleteSave(string fileObjectId, string saveInfoObjectId,
                                     CancellationToken cancellationToken = default)
        {
            // 1. 先删槽位记录 —— 这样即使后续删文件失败，也不会留下指向已消失记录的孤儿文件
            if (!string.IsNullOrEmpty(saveInfoObjectId))
            {
                using var infoRequest = PigeonClient.CreateRequest(
                    HttpMethod.Delete, PigeonClient.GameSaveUrl(saveInfoObjectId), SessionToken);
                infoRequest.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");
                using var infoResponse = await PigeonClient.SendCheckedAsync(infoRequest, cancellationToken)
                    .ConfigureAwait(false);
            }

            // 2. 再删存档文件
            if (!string.IsNullOrEmpty(fileObjectId))
            {
                using var fileRequest = PigeonClient.CreateRequest(
                    HttpMethod.Delete, PigeonClient.FileUrl(fileObjectId), SessionToken);
                fileRequest.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");
                using var fileResponse = await PigeonClient.SendCheckedAsync(fileRequest, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        // ── Token 刷新 ──

        /// <summary>
        /// 刷新会话 Token 并就地更新 <see cref="SessionToken"/>；失败返回 false。
        /// <para>
        /// ⚠️ Phigros 云存档的 sessionToken 是**固定**的，正常使用中不会过期，
        /// 因此本方法**几乎不应被调用**。它会让旧 Token 失效，仅在 Token 确实失效时才用。
        /// </para>
        /// </summary>
        public async Task<bool> RefreshToken(CancellationToken cancellationToken = default)
        {
            try
            {
                using var request = PigeonClient.CreateRequest(
                    HttpMethod.Put, $"{PigeonClient.ApiRoot}/users/{UserObjectID}/refreshSessionToken", SessionToken);
                using var response = await PigeonClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode) return false;

                string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(content);
                SessionToken = doc.RootElement.GetProperty("sessionToken").GetString()
                    ?? throw new InvalidOperationException("响应缺少 sessionToken。");
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ── 工具 ──

        private static string GetMd5String(byte[] data)
        {
            using var md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(data);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static string GetStringOrEmpty(JsonElement element, string propertyName)
            => GetStringOrNull(element, propertyName) ?? string.Empty;

        /// <summary>宽容地读取字符串属性；缺失、null 或非对象都返回 null</summary>
        private static string? GetStringOrNull(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;
            if (!element.TryGetProperty(propertyName, out var value)) return null;

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => value.ToString()
            };
        }
    }
}
