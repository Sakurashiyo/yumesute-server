# World Dai Star: Yume no Stellarium 本地服务端

非官方的《ワールドダイスター 夢のステラリウム》本地服务端，使用 C#、ASP.NET Core 和 PostgreSQL 实现部分客户端 API 与玩家数据持久化。

> [!WARNING]
> - 本项目仅面向学习、研究和个人测试，不隶属于游戏开发商或运营方，也不提供官方线上服务。
> - 项目仍在开发中，已有接口不代表完整游戏功能。
> - 项目大量使用 AI 开发，可能会遇到各种奇怪的问题，使用前请留意，也欢迎各位大佬帮忙修复。

## 目录

- [快速开始](#快速开始)
- [功能状态](#功能状态)
- [已知问题与计划](#已知问题与计划)
- [开发者参考](#开发者参考)
- [本服玩法设定](#本服玩法设定)
- [许可与声明](#许可与声明)
- [鸣谢](#鸣谢)

## 快速开始

### 1. 运行要求

- .NET 10 SDK
- PostgreSQL
- 与所用客户端版本匹配、且你有权使用的 MasterData 和本地资源
- 可访问本服务端的测试客户端或模拟器

### 2. 准备数据

- 将 `mastermemory.db` 解包，输出目录命名为 `masterdata-export`，放在服务端根目录。
- 项目文件 [`SiriusLocalServer.csproj`](SiriusLocalServer.csproj) 会嵌入 `masterdata-export/tables/` 中的部分 JSON，缺少这些文件时无法构建；仅克隆源码不保证能直接构建或运行。
- 准备游戏资源目录（Resources 与 Addressables），路径在下一步的 `.env` 中配置。

### 3. 配置

在服务端目录中复制配置模板：

```powershell
Copy-Item .env.example .env
```

按 [`.env.example`](.env.example) 中的注释填写数据库凭据、密钥、客户端版本、主数据版本和资源路径。缺少必要配置时启动会明确报错。

> [!IMPORTANT]
> 对外提供服务时，`TOKEN_SECRET` 必须替换为独立的随机密钥。

<details>
<summary>配置加载细节</summary>

- 启动时依次读取：可执行文件目录、当前工作目录下的 `csharp-server` 子目录、当前工作目录中的 `.env`，后加载的配置覆盖同名项。
- 相对资源路径以实际加载的 `.env` 所在目录为基准。
- 普通构建会把 `.env` 复制到输出目录；`dotnet publish` 不携带本机 `.env`，部署时需根据 `.env.example` 单独填写服务器配置。
- 读取与校验逻辑见 `Config/LocalConfig.cs`，代码中不保存机器目录或数据库默认密码。

</details>

### 4. 启动

```powershell
dotnet restore
dotnet run --project SiriusLocalServer.csproj
```

- 启动时会自动执行数据库迁移。
- 健康检查：`http://127.0.0.1:8787/health`
- realtime 与 API 共用进程，实时连接、大厅操作和 gRPC 错误单独写入可执行文件目录的 `realtime.log`，查看方法见 [`Realtime/README.md`](Realtime/README.md)。

## 功能状态

✅ 已实现　🟡 部分实现（仍有固定数据、兼容响应或未补齐的功能）　🔴 未实现

| 模块 | ✅ 已实现 | 🟡 / 🔴 未完成 |
| --- | --- | --- |
| 账号 | 注册认证、登录、确认码、密码引继、账号删除、管理后台 | 🔴 Google / Apple 关联 |
| 日常 | 资料主页教程、登录奖励、公告、礼物箱、Mission Pass | 🟡 普通任务、好友邀请活动 |
| 养成 | 剧情阅读、队伍与角色、角色任务与星章、海报升级、稽古 | |
| 演出 | STELLA / OLIVIER 解锁、特殊谱面 | 🟡 普通演出、公演、League / Triple Cast、活动排行 |
| 商店 | 统一抽卡池、点数兑换、随机商店、常设商店、每日免费礼包 | 🟡 序列码与外部支付 |
| 相册 | 照片生成与下载、相册查询与排版 | 🟡 相册扩展与 MV |
| 社交 | 好友、社团管理 / 排行 / 支援 / 体力 | 🟡 社团其余功能 |
| 实时 | | 🟡 协力大厅、多人演出与 Ghost Live、公共 / 社团频道 |

未完成项的具体情况见[已知问题与计划](#已知问题与计划)。

## 已知问题与计划

### 当前优先级

- [ ] 修复任务无法正常展示
- [ ] 成绩结算由预设值改为实际计算公式（难度较高，不确定能否完全解决）

### 演出分数

普通 LIVE 开始时会读取当前编队和实际卡片，按主数据、等级、觉醒与星章计算角色属性，并组装队长效果、基础 Sense、Star Act 和歌曲时轴。

尚未还原：复杂组合技能、海报 / 饰品效果、部分特殊演出模式。因此暂不保证与客户端预期分数完全一致。

### 公演

已接入：实际开演、分数与 Star Act 阶段判定、首次通关奖励、进度持久化、通关队伍展示、本人最高分编队复制。

尚未还原：官方队伍选样、跳过、解锁链及等级上限联动。本服按阶段展示最高分的五支真实队伍。协议依据和范围见 [`devlog/2026-10-20-auditions.md`](devlog/2026-10-20-auditions.md)。

## 开发者参考

| 内容 | 位置 |
| --- | --- |
| 数据库表结构 | [`Storage/SCHEMA.md`](Storage/SCHEMA.md) |
| realtime 日志与调试 | [`Realtime/README.md`](Realtime/README.md) |
| 开发日志 | [`devlog/`](devlog/) |

账号相关的数据库自检：

```powershell
# 校验账号注册与认证流程
dotnet run --project tools/ProgressionChecks -- --account-authentication --database
# 校验确认码与密码引继流程
dotnet run --project tools/ProgressionChecks -- --account-recovery --database
```

## 本项目部分设定

以下为本项目根据先前官服还存在时观察记录的规则，可能会因为记忆问题会些许与官方不同，仅供参考。

### 统一抽卡池

アクター与ポスター各使用一个常驻池。

| 项目 | 角色池 | 海报池 |
| --- | --- | --- |
| 稀有度概率 | ★2 82% ／ ★3 15% ／ ★4 3% | R 80% ／ SR 16% ／ SSR 4% |
| 十连保底 | 第十抽 ★3 以上 | 第十抽 SR 以上 |
| 重复转换 | ★2／★3／★4 → 1／10／100 个ジュゴン像 | 先提升突破，满突破后 R／SR／SSR → 1／10／100 个ジュゴン像 |

- 单抽 300、十连 3000 剧ジュエル，优先消耗免费宝石；保底不改变最高稀有度概率。
- 每抽获得本池 1 点，两个池分别累计，点数不随日期清除；200 点可自选本池任意 ★4／SSR。

## 许可与声明

本项目采用 PolyForm Noncommercial License 1.0.0，仅允许非商业用途。详细条款见 [LICENSE](./LICENSE)。

该许可证仅适用于本项目作者及贡献者原创的代码，不适用于任何第三方游戏资源、商标、角色、音乐、图片、文本、剧情、模型、音频及其他受版权保护的内容。

本项目是由社区维护的非官方项目，与《ワールドダイスター 夢のステラリウム》（World Dai Star: Yume no Stellarium）的开发商、发行商及相关权利方不存在隶属、授权或合作关系。

## 鸣谢

非常感谢以下大佬们对游戏资源进行的备份！

- [Adv-Resource](https://github.com/wds-sirius/Adv-Resource)
- [asset-of-dreams](https://github.com/Ryota537/asset-of-dreams)
