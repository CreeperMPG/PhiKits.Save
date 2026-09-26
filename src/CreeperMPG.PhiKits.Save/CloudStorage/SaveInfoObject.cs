using CreeperMPG.PhiKits.Save.Data;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CreeperMPG.PhiKits.Save.CloudStorage
{
    /// <summary>
    /// 云端一个存档槽位的元信息。
    /// </summary>
    public class SaveInfoObject
    {
        private static readonly HttpClient s_client = new HttpClient();

        /// <summary>存档文件（.save zip）的下载地址</summary>
        public string FileUrl { get; set; } = string.Empty;

        /// <summary>云端 _File 记录的 objectId，删除文件时使用</summary>
        public string FileObjectID { get; set; } = string.Empty;

        /// <summary>云端 _GameSave 记录的 objectId</summary>
        public string SaveInfoObjectID { get; set; } = string.Empty;

        /// <summary>云端记录的最后更新时间</summary>
        public string SaveUpdateTime { get; set; } = string.Empty;

        /// <summary>云端摘要；解析失败时为 null（槽位本身仍然有效）</summary>
        public SaveSummary? CloudSummary { get; set; }

        public SaveInfoObject() { }

        public SaveInfoObject(string fileUrl, string fileObjectId, string saveInfoObjectId,
                              string saveUpdateTime, SaveSummary? cloudSummary = null)
        {
            FileUrl = fileUrl;
            FileObjectID = fileObjectId;
            SaveInfoObjectID = saveInfoObjectId;
            SaveUpdateTime = saveUpdateTime;
            CloudSummary = cloudSummary;
        }

        /// <summary>
        /// 下载该槽位的 .save 并解包为 <see cref="SavePackage"/>（全内存，不产生临时文件）。
        /// 下载失败或存档格式损坏都会抛出异常。
        /// </summary>
        public async Task<SavePackage> DownloadSave(CancellationToken cancellationToken = default)
        {
            byte[] zipBytes = await s_client.GetByteArrayAsync(FileUrl, cancellationToken).ConfigureAwait(false);
            var save = SavePackage.FromZipBytes(zipBytes);
            save.GameVersion = CloudSummary?.GameVersion ?? 0;
            save.SaveVersion = CloudSummary?.SaveVersion ?? 7;
            return save;
        }
    }
}
