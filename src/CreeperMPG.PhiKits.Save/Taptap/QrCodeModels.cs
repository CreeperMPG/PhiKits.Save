using System;

namespace CreeperMPG.PhiKits.Save.Taptap
{
    /// <summary>二维码登录的设备码响应（第一步的产物，交给调用方渲染成二维码）</summary>
    public sealed class QrCodeResponse
    {
        /// <summary>设备码，轮询时回传</summary>
        public string DeviceCode { get; set; } = string.Empty;

        /// <summary>二维码内容（调用方自行生成图片）</summary>
        public string QrCodeUrl { get; set; } = string.Empty;

        /// <summary>服务端下发时刻（Unix 秒）</summary>
        public long RequestTime { get; set; }

        /// <summary>有效期（秒）</summary>
        public int ExpiresIn { get; set; }

        /// <summary>建议的轮询间隔（秒）</summary>
        public int Interval { get; set; }
    }

    /// <summary>授权成功后的令牌集合</summary>
    public sealed class QrCodeResult
    {
        public string AccessToken { get; set; } = string.Empty;
        public string Kid { get; set; } = string.Empty;
        public string MacKey { get; set; } = string.Empty;
        public string MacAlgorithm { get; set; } = string.Empty;
    }

    /// <summary>
    /// 轮询结果的状态。
    /// <para>
    /// 这里只表达**业务状态**——网络错误、响应格式异常、用户取消等**技术失败一律抛异常**，
    /// 不再像旧实现那样统一压成 <c>Error</c>（那会把代码 bug 和网络问题混为一谈）。
    /// </para>
    /// </summary>
    public enum QrCodeStatus
    {
        /// <summary>尚未扫码</summary>
        AuthorizationPending,

        /// <summary>已扫码，等待用户确认授权</summary>
        AuthorizationWaiting,

        /// <summary>授权成功，<see cref="QrCodePollResult.Token"/> 可用</summary>
        Success,

        /// <summary>设备码已失效（过期或已被使用）——需要重新获取二维码</summary>
        InvalidGrantCode,
    }

    /// <summary>一次轮询的结果：状态 +（成功时）令牌</summary>
    public sealed class QrCodePollResult
    {
        public QrCodeStatus Status { get; }

        /// <summary>仅当 <see cref="Status"/> 为 <see cref="QrCodeStatus.Success"/> 时非 null</summary>
        public QrCodeResult? Token { get; }

        public bool IsSuccess => Status == QrCodeStatus.Success;

        private QrCodePollResult(QrCodeStatus status, QrCodeResult? token)
        {
            Status = status;
            Token = token;
        }

        internal static QrCodePollResult FromToken(QrCodeResult token)
            => new QrCodePollResult(QrCodeStatus.Success, token ?? throw new ArgumentNullException(nameof(token)));

        internal static QrCodePollResult Pending()
            => new QrCodePollResult(QrCodeStatus.AuthorizationPending, null);

        internal static QrCodePollResult WaitingAuthorization()
            => new QrCodePollResult(QrCodeStatus.AuthorizationWaiting, null);

        internal static QrCodePollResult InvalidGrantCode()
            => new QrCodePollResult(QrCodeStatus.InvalidGrantCode, null);
    }
}
