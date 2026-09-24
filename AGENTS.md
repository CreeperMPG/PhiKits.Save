# AGENTS.md — CreeperMPG.PhiKits.Save

Phigros 存档格式、TapTap 二维码登录与云端 API 的 .NET **类库**，是 **PhigrosArchive 的重写版**。本文件是**中性项目说明**。

## 定位

```
CreeperMPG.PhiKits.Save   ← 本仓库：存档格式（Data/）+ 二维码登录（Taptap/）+ 云 API（CloudStorage/）
```

| 项目 | 类型 | 目标框架 | 产出 |
|---|---|---|---|
| **CreeperMPG.PhiKits.Save**（本仓库） | 类库 | `net6.0;net10.0` | `.dll` 引用库 |

> `PhigrosArchive` 是上一代实现（含 `Taptap.cs` 与 `PhigrosPlayerInfo.cs`），位于 `../PhigrosArchive/`。
> **`Taptap` 单向引用 `CloudStorage`**：登录的产物就是 `PlayerObject`（会话令牌 + 玩家信息都在里面），
> 调用方拿到即可直接用。`CloudStorage` **不反向引用** `Taptap`。

## 依赖

> ⚠️ **不要给主项目加 `System.Text.Json` 的 `PackageReference`。**
> 它自 .NET Core 3.0 起就在共享框架内，加了反而会引入不支持 `net6.0` 的版本
> （`NETStandardCompatError_System_Text_Json_net8_0`），**直接让多目标构建失败**。

## 目录结构

```
CreeperMPG.PhiKits.Save/
├── CreeperMPG.PhiKits.Save.slnx
├── src/CreeperMPG.PhiKits.Save/
│   ├── Abstractions/
│   │   └── IDifficultyProvider.cs   — 定数提供者接口（RKS 计算需要，本库不内置数据）
│   ├── Data/                        ← 存档格式
│   │   ├── ISaveEntry.cs            — 条目接口：EntryFileName / EntryVersion / Serialize / Deserialize
│   │   ├── SavePackage.cs           — 五条目容器 + AES 加解密 + zip + GenerateSummary
│   │   ├── SaveSummary.cs           — 云端摘要（Base64 序列化，含 TryFromBase64）
│   │   ├── SongDifficultySet.cs     — EZ/HD/IN/AT/Legacy 容器
│   │   └── SaveEntries/             — PhigrosProgress / User / Settings / Record / Key
│   ├── CloudStorage/
│   │   ├── PlayerObject.cs          — 玩家门面
│   │   ├── SaveInfoObject.cs        — 槽位元信息 + DownloadSave()
│   │   └── Internal/
│   │       ├── PigeonClient.cs      — LeanCloud 端点常量与请求
│   │       └── QiniuUploader.cs     — 七牛分片上传
│   ├── Taptap/                      ← 二维码登录
│   │   ├── TaptapClient.cs          — 登录流程门面（含轮询与 LoginAsync）
│   │   ├── TaptapEndpoints.cs       — 端点常量（国际版 / 中国版）
│   │   ├── QrCodeModels.cs          — QrCodeResponse / QrCodeResult / QrCodeStatus / QrCodePollResult
│   │   ├── TaptapUserProfile.cs     — TapTap 账号资料
│   │   └── Internal/
│   │       ├── MacTokenSigner.cs    — HMAC-SHA1 签名与 nonce
│   │       └── TaptapHttp.cs        — 共用 HttpClient
│   └── Additions/                   ← 可选的现成实现
│       ├── TsvDifficultyProvider.cs — 从 TSV 读定数的 IDifficultyProvider
│       └── BitUtils.cs              — 位操作与 Protobuf VarInt
└── test/CreeperMPG.PhiKits.Save.Tests/   ← MSTest
```

> `InternalsVisibleTo` 已对测试项目开放，所以 `Internal/` 下的协议细节（签名、解析）
> 可以被测试直接覆盖，不必暴露成公开 API。

## 存档格式

`.save` 是 **zip**，五个条目**直接在根**，无目录前缀：`gameProgress` / `user` / `settings` / `gameRecord` / `gameKey`。

每个条目 = **1 字节版本号 + AES-256-CBC 加密的明文**（密钥/IV 硬编码在 `SavePackage` 中）。

**五个条目缺一不可**，缺失即文件损坏 → `SavePackage` 构造函数抛异常。

### `OverflowData` 是有意设计，别删

每个条目都保留「读不完的尾部字节」，序列化时原样写回。
这让低版本库改动存档**不会抹掉高版本游戏新增的字段**。改动各条目的 `Deserialize`/`Serialize` 时请保持这个模式。

### 版本号由条目自己保管

`EncryptData` 会**空出第 0 字节**（版本号位），由调用方填充。
**版本号存在条目自己的 `ISaveEntry.EntryVersion` 里**，`SavePackage` 不做集中保管：

- 反序列化：`entry.EntryVersion = content[0]`，然后才 `entry.Deserialize(...)`
- 序列化：`encrypted[0] = entry.EntryVersion`

> 早先 `SavePackage` 用一个 `IReadOnlyDictionary<string, byte> EntryVersions` 集中存版本号，
> 已**删除**——版本号是条目自身的一部分，由容器代管既别扭（那个字典对外只读，没有写入途径），
> 也不符合「数据自己管自己」。
>
> ⚠️ 各条目的**默认版本号目前统一是 `1`**（新建对象时用），真实值由项目所有者后续填写
> （实测真实存档为 `gameProgress:4, user:1, settings:1, gameRecord:1, gameKey:3`）。

## 云 API 要点

### 端点（注意自定义路径）

| 用途 | 端点 |
|---|---|
| 玩家信息 | `GET /1.1/users/me` |
| 槽位列表 | `GET /1.1/classes/_GameSave` |
| **单条槽位** | `/1.1/gamesaves/{SaveInfoObjectID}` ← **自定义路径，不是 `/classes/_GameSave`** |
| **存档文件** | `/1.1/files/{FileObjectID}` ← 自定义路径 |
| 上传 Token | `POST /1.1/fileTokens` |
| 上传回调 | `POST /1.1/fileCallback` |

### 上传流程（六步）

申请 fileToken → 七牛 init → 上传第 1 片 → merge → fileCallback → 新建 `_GameSave` 记录（写入摘要）。

**新建记录的响应里 `gameFile` 只是指针（只有 `objectId`，没有 `url`）**，
所以 `UploadSave` 会在新建后**单独 GET `/1.1/gamesaves/{id}`** 补全，返回值才能直接 `DownloadSave()`。

### 删除必须删两个

```
DELETE /1.1/gamesaves/{SaveInfoObjectID}    ← 槽位记录（先）
DELETE /1.1/files/{FileObjectID}            ← 存档文件（后）
```

**只删文件会留下「僵尸槽位」**——记录还在、`gameFile` 变成悬空指针，扫描时仍会被列出但下载必失败。
顺序上**先删记录再删文件**，这样中途失败不会留下孤儿文件。

### 版本号闭环

`SaveVersion` / `GameVersion` **不在存档文件里**，只存在于云端摘要。所以：

```
DownloadSave()  从 CloudSummary 灌进 SavePackage
GenerateSummary()  从 SavePackage 原样带出
UploadSave()  写回云端
```

**这样「下载 → 改 → 上传」不会把版本号抹成 0。**
但从本地 `.save` 导入时没有 `CloudSummary` 可灌，**使用方需要自行赋值**（README 有说明）。

### 容错约定

- **摘要解析失败** → `CloudSummary = null`，**槽位保留、不抛异常**
- **`FileUrl` 不做校验** → 空值照收，由后续下载时自然失败
- **`DownloadSave()` 失败抛异常**（不返回 null）

## TapTap 二维码登录

设备码流程，五步：

```
GetLoginQrCodeAsync      → QrCodeResponse（device_code + qrcode_url + interval + expires_in）
   ↓ 用户扫码
PollQrCodeAsync          → QrCodeStatus（Pending / Waiting / Success / InvalidGrantCode）
   ↓ 或用 WaitForAuthorizationAsync 一次等到结论
FetchUserProfileAsync    → openid / unionid / name（HMAC-SHA1 的 MAC 签名）
   ↓
GetPhigrosSessionAsync   → PlayerObject（X-LC-Sign = md5(ts+AppKey) + "," + ts）
```

`LoginAsync(onQrCodeReady, progress, ct)` 把五步串成一个调用。

### 产物就是 `PlayerObject`

**没有单独的"会话"类型**——`GetPhigrosSessionAsync` 的响应本身就是 Phigros `/users/me` 的格式
（`sessionToken` / `nickname` / `shortId` / `objectId` / `createdAt`），
所以直接构造成 `PlayerObject` 返回。

这也意味着 `Taptap` **单向引用 `CloudStorage`**。早先曾有一个只搬这五个字段的 `TaptapSession`，
纯属重复定义（字段名还不一致：`ObjectId` vs `UserObjectID`、`CreatedAt` vs `CreateTime`），已删除。

### 失败约定（重要）

**可预期的业务状态走返回值**（`QrCodeStatus`），**技术失败一律抛异常**：

| 情形 | 处理 |
|---|---|
| 未扫码 / 已扫码待确认 | 返回 `AuthorizationPending` / `AuthorizationWaiting` |
| 设备码失效 | 返回 `InvalidGrantCode` |
| 本地等待超时 | 抛 `TimeoutException` |
| 响应体不是合法 JSON（真正的传输层故障） | 抛 `HttpRequestException` |
| 响应结构异常、没见过的错误码 | 抛 `InvalidOperationException` |
| 用户取消 | 抛 `OperationCanceledException` |

> ⚠️ **旧实现把所有异常统一 `catch` 成 `Error` 状态**，那样会把**代码 bug 伪装成网络问题**，
> 也让调用方分不清"用户取消"和"网络失败"。**新实现刻意不这么做，别改回去。**
> 也正因为如此，`QrCodeStatus` 里**没有** `Error` 这个值。

### 签名要点

- **MAC 签名输入**：`timestamp\nnonce\nmethod\nrequestUrl\nhost\n443\n\n`（**末尾还有一个换行**），
  HMAC-SHA1 后 Base64。`host` 不带 scheme。
- **nonce 用 `RandomNumberGenerator`**——旧实现用 `System.Random`，那是可预测的。
- **中国版 / 国际版两套 host**：`china: true` 走 `accounts.tapapis.cn` + `open.tapapis.cn`，
  否则走 `.com`。**用户信息端点也要跟着切**（旧实现写死了中国版，是个不一致）。

### ⚠️ 绝不能按 HTTP 状态码判断成败

**TapTap 用非 2xx 表达业务状态。** 实测：设备码尚未授权时，令牌端点返回

```
HTTP 400 Bad Request
{"data":{"code":-1,"msg":"请求错误","error":"authorization_pending", ...},"success":false}
```

所以 `TaptapHttp.SendJsonAsync` **刻意不看 `IsSuccessStatusCode`，只看响应体是不是合法 JSON**。
若在那里因状态码抛异常，就会把 `authorization_pending` 这类业务码整个丢掉，**轮询永远无法成功**
（这个回归真实发生过：曾加过状态码检查，导致未扫码时直接抛 `HttpRequestException`）。

只有响应体**不是合法 JSON**（网关错误页等真正的传输层故障）才抛异常。

### 超时基准

`WaitForAuthorizationAsync` 的本地超时用 **`ExpiresIn`（服务端下发，实测 300 秒）**。

> `Interval` 实测返回 **1 秒**（不是常见的 5 秒），所以一次完整的未扫码等待可能产生上百次请求。
> 调用方若在意请求量，可自行用 `PollQrCodeAsync` 控制节奏。

## 游戏规则常数

- **通关判据是 `Score >= 820000`（B 级及以上）**，不是 700000（C 级）。
  已用真实存档在四个难度上验证：`EZ=64 HD=97 IN=209 AT=47` 全部与云端摘要精确吻合。

## RKS 计算

**分三层，刻意解耦**——存档数据不认识定数，定数来源由消费方注入：

```
LevelRecord.GetRankingScore(float difficulty)      ← 单曲 RKS = 系数 × 定数
PhigrosRecord.CalculateRankingScore(provider)      ← 账号总 RKS
SavePackage.GenerateSummary(provider)              ← 摘要 + 真 RKS
```

- `IDifficultyProvider` 由**本库自己定义**（`Abstractions/`），形状与 `PhigrosArchive` 的同名接口一致，
  同一份实现可同时供两代库使用。本库**不内置任何定数数据**。
- `LevelRecord` 只接受一个 `float difficulty`，**不知道曲目 ID、不知道难度序号**——这是解耦的关键。
- **总 RKS 规则**：`(定数最高的 3 首满分成绩之和 + 单曲 RKS 最高的 27 首之和) / 30`。
  - 「满分成绩」= `Score == 1000000 && Acc == 100`（`LevelRecord.IsPhi`）
  - 前 3 首按**定数**降序取，不是按 RKS 降序——「最高 Phi」指的就是难度最高的那几个 Phi
  - **两批允许重叠**：同一首歌可能既进前 3 也进前 27，这是游戏本身的规则
  - **不含 Legacy**（Legacy 曲目没有定数），只算 EZ/HD/IN/AT
- **容错**：定数查不到的曲目会被**跳过**（总 RKS 偏低但不抛异常）。
  `IsLoaded == false` 时同理——由调用方自行判断是否要提示。

`GenerateSummary()` 的**无参重载签名与行为完全不变**，但它**不计算 RKS**——
`RankingScore` 是一个占位值（具体数值由项目所有者维护，别把测试写死到它上面）。
只有传入 provider 的重载才会算出真 RKS。

## 构建与测试

```bash
dotnet build
dotnet test
```

### ⚠️ 沙箱 / CI 环境注意

**`dotnet build` 可能「0 错误却生成失败」**：.NET 10 的 MSBuild server 在某些受限环境下会死掉，
只报 `MSB5021 终止 csc`，**把真实的编译错误完全掩盖**。遇到这种情况先关掉它：

```powershell
$env:DOTNET_CLI_USE_MSBUILD_SERVER='0'
$env:MSBUILDDISABLENODEREUSE='1'
dotnet build -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

**`dotnet test` 需要能 `OpenProcess` 父进程**（VSTest testhost 监视退出），
权限受限的环境会报 `Win32Exception (5) 拒绝访问`。普通开发机和 CI 不受影响。

## 测试项目

MSTest 3.1.1（`Microsoft.NET.Test.Sdk` 17.10.0-preview），`net10.0`。
只覆盖**不依赖网络**的部分：存档 zip 往返、版本号回填、摘要 Base64 解析、云对象解析，
以及 TapTap 的 **MAC 签名已知向量**、端点拼接、响应解析（含各种错误码分支）。

**云 API 与二维码登录没有自动化测试**（需要真实账号扫码）。
如需手工验证，注意**上传会真在云端新建槽位**，务必用测试账号，并在验证后 `DeleteSave` 清理。

## 版本控制

**本仓库目前没有 git**（未 `git init`）。`.gitignore` 已经写好，供将来初始化时使用。

> ⚠️ **`.gitignore` 里 `*.save` 那几条否定规则不能删。**
> Windows / macOS 上 git 路径匹配**不区分大小写**，`*.save` 会连目录
> `CreeperMPG.PhiKits.Save` 一起匹配，**导致整个 `src/CreeperMPG.PhiKits.Save/` 被忽略**
> （曾实测复现）。必须保留：
> ```gitignore
> *.save
> !CreeperMPG.PhiKits.Save/
> !**/CreeperMPG.PhiKits.Save/
> ```


## 尚待完成

- **无参 `GenerateSummary()` 不计算 RKS**，`RankingScore` 只是个占位值（有意保留旧行为）。
  要拿真 RKS 必须用 `GenerateSummary(IDifficultyProvider)` 重载，
  或调用方自己用 `GameRecord.CalculateRankingScore(provider)` 覆盖。
  **上传时若用无参重载，会把占位值写进云端摘要。**
  ⚠️ 占位值的具体数值由项目所有者维护，**测试不要写死到它上面**（曾经因此失效过一次）。
- `SaveSummary.ToBase64String` 写头像长度用单字节、读用 VarInt（短头像下等价，未验证长头像）
- `PhigrosRecord.Serialize` 里的 `nonNullCount * 8 + 2` 可能是冗余字段
- 仓库**尚未 `git init`**（`.gitignore` 已备好，但项目所有者目前**不要 git**）
- 尚未发布到 NuGet（README 里按项目引用写）

## 已验证

- **RKS 计算已用真实定数端到端验证**：某槽位云端 `16.180485` 与本地计算 **逐位相等**。
  （验证用的定数表来自使用方，未纳入本仓库。）
- **存档 zip 往返**：真实 `.save` 五个条目逐字节一致。
- **云 API 全链路**（列出 / 下载 / 上传 / 删除）已在真实账号上手工跑通。
- **TapTap 二维码登录已端到端实测通过**（真实扫码，2026-09-21）：
  设备码 → 轮询 `Pending`/`Waiting`/`Success` → MAC 签名取账号资料 → X-LC-Sign 换 `sessionToken`，
  全程无错。**产出的 `sessionToken` 与云存档用的是同一个**（已交叉验证可喂给 `CloudStorage.PlayerObject`）。
