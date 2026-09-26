# PhiKits.Save

**处理 Phigros 存档与云存档操作的 .NET 类库**

> 本项目为非官方玩家项目，与南京鸽游网络有限公司及《Phigros》官方不存在授权、合作或运营关系。

[![.NET](https://img.shields.io/badge/.NET-net6.0%20%7C%20net10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

**注意：目前 PhiKits.Save 支持的 Phigros 版本范围为 `3.9.0 (111) ~ 4.0.0 (155)`。**

## 🔗 相关项目

- [Phigros Shell](https://github.com/CreeperMPG/PhigrosShell): 此项目的 Shell 版本，使用虚拟文件管理系统实现对存档的读写。Phigros Shell 从 1.3.0 版本开始使用 PhiKits.Save 进行存档读写。
- [PhiShell Studio](https://github.com/CreeperMPG/PhiShellStudio): PhiShell 的 GUI 版本，使用 Avalonia UI 构建，支持 Windows/Linux/iOS/Android。

## ✨ 功能

- **存档格式读写** — 解析 / 生成 Phigros `.save` 存档（五个加密条目）
- **zip 打包解包** — `FromZipBytes` / `FromZipFile` / `ToZipBytes`，全内存，不产生临时文件
- **存档摘要** — 挑战等级、RKS、各难度通关数 / FC / Phi 统计
- **RKS 计算** — 单曲 RKS 与账号总 RKS；定数数据由消费方注入，本库不内置
- **TapTap 二维码登录** — 设备码流程，最终换取 Phigros 云存档玩家信息
- **云端 API** — 列出 / 下载 / 上传 / 删除云存档

## 🎯 目标框架

| 框架 | 说明 |
|---|---|
| `net6.0` | 兼容旧项目引用 |
| `net10.0` | 当前主力 |

## 🚀 快速开始

### 读本地存档

```csharp
using CreeperMPG.PhiKits.Save.Data;

var package = SavePackage.FromZipFile(@"C:\path\to\save");   // 或 FromZipBytes(byte[])
Console.WriteLine($"成绩记录数: {package.GameRecord.Records.Count}");

var summary = package.GenerateSummary();
Console.WriteLine($"课题等级: {summary.Challenge}");

byte[] zip = package.ToZipBytes();                            // 打包回 .save
```

### 计算 RKS

本库**不内置定数数据**，需要自行实现 `IDifficultyProvider`：

```csharp
using CreeperMPG.PhiKits.Save.Abstractions;

public class MyDifficultyProvider : IDifficultyProvider
{
    public bool IsLoaded => /* 定数是否已加载 */;

    // difficultyIndex: 0=EZ 1=HD 2=IN 3=AT 4=Legacy
    public float? GetDifficulty(string songId, int difficultyIndex) => /* 查表，查不到返回 null */;
}
```

另外，本项目附赠 `TsvDifficultyProvider`（位于 `CreeperMPG.PhiKits.Save.Additions`），可以通过其它工具提供的 `.tsv` 导入定数数据。

计算单曲 RKS

```csharp
float songRks = record.GetRankingScore(difficulty: 15.7f);
```

计算存档总 RKS

```csharp
float totalRks = package.GameRecord.CalculateRankingScore(provider);

// 或者在生成摘要时一并算好
var summary = package.GenerateSummary(provider);   // RankingScore 为真实值
```

### TapTap 二维码登录

```csharp
using CreeperMPG.PhiKits.Save.Taptap;

var taptap = new TaptapClient(china: true);

// 拿到二维码时渲染给用户，其余流程内部完成
PlayerObject player = await taptap.LoginAsync(
    onQrCodeReady: qr => { ShowQrCode(qr.QrCodeUrl); return Task.CompletedTask; },
    progress: new Progress<QrCodeStatus>(s => ShowStatus(s)));

// 产物就是 PlayerObject —— 令牌与玩家信息都在里面，直接可用
var saves = await player.GetSaveInfo();
```

也可以自己编排每一步（例如需要自定义轮询节奏或分阶段 UI）：

```csharp
var qr      = await taptap.GetLoginQrCodeAsync();                 // 设备码 + 二维码内容
ShowQrCode(qr.QrCodeUrl);

var poll    = await taptap.WaitForAuthorizationAsync(qr, progress); // 轮询到有结论
var profile = await taptap.FetchUserProfileAsync(poll.Token!);       // TapTap 账号资料
var player  = await taptap.GetPhigrosSessionAsync(poll.Token!, profile);  // → PlayerObject
```

> **失败约定**：可预期的业务状态通过 `QrCodeStatus` 返回
> （`AuthorizationPending` / `AuthorizationWaiting` / `Success` / `InvalidGrantCode`）；
> 网络错误、响应格式异常、取消等**技术失败一律抛异常**。
>
> **登录产物就是 `PlayerObject`**，直接拿去处理云存档即可。

### 云存档

```csharp
using CreeperMPG.PhiKits.Save.CloudStorage;

var player = new PlayerObject(sessionToken);
await player.FetchUserInfo();                         // 上传前必须：补齐 UserObjectID

var saves = await player.GetSaveInfo();               // 列出所有槽位
var package = await saves[0].DownloadSave();          // 下载并解包

var uploaded = await player.UploadSave(package, package.GenerateSummary(provider));  // 新建槽位
await uploaded.DownloadSave();                        // 返回值可直接下载

await player.DeleteSave(uploaded);                    // 删除槽位（记录 + 文件）
```

## ⚠️ 注意事项

- **无参 `GenerateSummary()` 不算 RKS。** 它的 `RankingScore` 是一个**占位值** `11.45`，真实情况务必传入 `IDifficultyProvider`。
  **上传时请用 `GenerateSummary(provider)`**，否则会把占位值写进云端摘要。
- **`SaveVersion` / `GameVersion` 需要手动设置。** 这两个值只存在于云端摘要，不在存档文件里。
  从云端 `DownloadSave()` 时会自动灌入；但**从本地 `.save` 导入时二者为默认值**，上传前请自行赋值：

    ```csharp
    var package = SavePackage.FromZipFile(path);
    package.GameVersion = 154;
    package.SaveVersion  = 6;
    ```

    `SavePackage.FromZipFile()` 后，程序会自动通过存档内容进行推断，但是有可能推断失败。（`SavePackage` 的 `TryInferSaveVersion` 方法可以推断版本）

- `PhiKits.Save` 对 `EntryVersion` 较敏感，导出、导入时会参考设置的版本号。若进行手动存档升级但未导出新版本内容，请检查对应 `EntryVersion` 的设置。另外项目提供了一些 API（`SavePackage.TryInferSaveVersion`, `SavePackage.GetSaveVersionByGameVersion`, `ISaveEntry.GetEntryVersionBySaveVersion`）以进行版本设置。

## ⚡ Vibe-Coding 信息

此项目前身 [PhigrosArchive](https://github.com/CreeperMPG/PhigrosArchive) 均为手写代码。
PhiKits.Save 中的 `CreeperMPG.PhiKits.Save.CloudStorage` 和 `CreeperMPG.PhiKits.Save.Taptap` 在编写时使用 DeepSeek-V4.1-Flash 对 [PhigrosArchive](https://github.com/CreeperMPG/PhigrosArchive) 进行参考性迁移。

## 📄 授权

本项目基于 [MIT License](LICENSE) 开源。

> 本项目为非官方玩家项目，与南京鸽游网络有限公司及《Phigros》官方不存在授权、合作或运营关系。
