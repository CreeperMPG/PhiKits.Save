using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CreeperMPG.PhiKits.Save.Taptap.Internal
{
    /// <summary>
    /// TapTap 开放平台的 MAC Token 签名（HMAC-SHA1）。
    /// </summary>
    internal static class MacTokenSigner
    {
        private const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

        /// <summary>当前 Unix 时间戳（秒）</summary>
        internal static string CreateTimestamp()
            => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// 生成随机 nonce。
        /// 用 <see cref="RandomNumberGenerator"/> 而非 <c>System.Random</c>——
        /// 旧实现用了后者，那是可预测的伪随机。
        /// </summary>
        internal static string CreateNonce(int length = 16)
        {
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), "nonce 长度必须为正数。");

            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++)
                sb.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
            return sb.ToString();
        }

        /// <summary>用当前时间戳与随机 nonce 生成 Authorization 头的值</summary>
        internal static string CreateHeader(string host, string requestUrl, string method, string macKey, string kid)
            => CreateHeader(host, requestUrl, method, macKey, kid, CreateTimestamp(), CreateNonce());

        /// <summary>
        /// 生成 Authorization 头的值。
        /// <para>
        /// 签名输入为 <c>timestamp\nnonce\nmethod\nrequestUrl\nhost\n443\n\n</c>
        /// （注意末尾**还有一个换行**），签名结果再 Base64 编码。
        /// </para>
        /// </summary>
        internal static string CreateHeader(string host, string requestUrl, string method,
                                            string macKey, string kid, string timestamp, string nonce)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (requestUrl == null) throw new ArgumentNullException(nameof(requestUrl));
            if (method == null) throw new ArgumentNullException(nameof(method));
            if (macKey == null) throw new ArgumentNullException(nameof(macKey));
            if (kid == null) throw new ArgumentNullException(nameof(kid));
            if (timestamp == null) throw new ArgumentNullException(nameof(timestamp));
            if (nonce == null) throw new ArgumentNullException(nameof(nonce));

            string signInput = string.Join("\n", timestamp, nonce, method, requestUrl, host, "443", "") + "\n";

            using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(macKey));
            string mac = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(signInput)));

            return $"MAC id=\"{kid}\",ts=\"{timestamp}\",nonce=\"{nonce}\",mac=\"{mac}\"";
        }
    }
}
