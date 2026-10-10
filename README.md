# World Dai Star: Yume no Stellarium 本地服务端

这是一个非官方的《ワールドダイスター 夢のステラリウム》本地服务端项目，使用 C#、ASP.NET Core 和 PostgreSQL 实现部分客户端所需的 API 与玩家数据持久化。项目面向学习、研究和个人测试，不隶属于游戏开发商或运营方，也不提供官方线上服务。

项目仍在开发中。已有接口并不代表完整游戏功能。
项目大量使用AI开发，您可以会遇到各种各样的神人问题，使用前请先注意，欢迎各位大佬修复这些问题

## 功能实现状态

状态说明：

- ✅ 已实现：所列范围已有实际处理逻辑及必要的数据持久化。
- 🟡 部分实现：已有基础流程，但仍包含固定数据、兼容响应、未补齐的校验或功能。
- 🚧 实现中：正在开发，尚未完成。
- 🔴 未实现：尚未提供对应业务流程。

| 功能 | 状态 | 功能 | 状态 |
| --- | --- | --- | --- |
| 环境信息与连通检查 | ✅ | 账号注册与认证 | ✅ |
| 游戏登录与玩家数据 | ✅ | 确认码 | ✅ |
| MasterData 清单与文件 | ✅ | 密码账号引继 | ✅ |
| 本地资源与剧情资源 | ✅ | 好友邀请活动 | 🟡 |
| 剧情阅读与奖励 | ✅ | 社团 | 🟡 |
| 登录奖励与每日刷新 | ✅ | 海报升级 | 🟡 |
| 玩家资料、主页与教程 | ✅ | 普通演出 | 🟡 |
| 公告 | ✅ | 普通任务与任务通行证 | 🟡 |
| 礼物箱 | ✅ | 随机商店 | ✅ |
| 好友 | ✅ | League 与 Triple Cast | 🟡 |
| 队伍与角色养成 | ✅ | 活动与活动排行 | 🟡 |
| STELLA / OLIVIER 解锁与歌曲兑换 | ✅ | 协力实时大厅 | 🟡 |
| 特殊谱面 | ✅ | 多人演出与 Ghost Live | 🟡 |
| 稽古 | ✅ | 实时公共 / 社团频道 | 🟡 |
| 角色任务与星章 | ✅ | 照片、相册与 MV | 🟡 |
| 统一常驻抽卡池 | ✅ | 序列码与外部支付 | 🟡 |
| 抽卡点数兑换 | ✅ | Google / Apple 账号关联 | 🔴 |
| 每日免费礼包 | ✅ | 账号删除 | 🔴 |
| 常设商店兑换 | ✅ | 社团排行榜 | 🔴 |
| 本机存档管理 | ✅ |  |  |

## 运行要求

- .NET 10 SDK
- PostgreSQL
- 与所用客户端版本匹配、且你有权使用的 MasterData 和本地资源
- 可访问本服务端的测试客户端或模拟器

项目文件 [`SiriusLocalServer.csproj`](SiriusLocalServer.csproj) 会嵌入 `masterdata-export/tables/` 中的部分 JSON。若这些文件未随你的检出内容提供，需要先自行准备对应数据；仅克隆源码不保证能直接构建或运行。请勿把无权再发布的游戏数据提交到公开仓库。

## 本地启动

在包含 `SiriusLocalServer.csproj` 的服务端目录中，将 [`.env.example`](.env.example) 复制为 `.env`：

```powershell
Copy-Item .env.example .env
```

填写数据库凭据、密钥、客户端版本、主数据版本和资源路径后再启动。`Config/LocalConfig.cs` 负责读取和校验配置，不保存机器目录或数据库默认密码；缺少必要配置时启动会明确报错。相对资源路径以实际加载的 `.env` 所在目录为基准。
且需要你将mastermemory.db解包并且命名为：masterdata-export放在服务器根目录

准备好数据库和所需数据文件后，在服务端目录执行：

```powershell
dotnet restore
dotnet run --project SiriusLocalServer.csproj
```

启动时会执行数据库迁移。健康检查地址为 `http://127.0.0.1:8787/health`；本机存档管理页面为 `http://127.0.0.1:8787/admin/save-editor/`。

启动依次读取可执行文件目录、当前工作目录下的 `csharp-server` 子目录和当前工作目录中的 `.env`，后加载的配置覆盖同名项。普通构建将 `.env` 复制到输出目录，`dotnet publish` 不携带本机 `.env`。部署时根据 `.env.example` 单独填写服务器配置。

## 数据

数据库表的概要见 [`Storage/SCHEMA.md`](Storage/SCHEMA.md)。
```powershell
dotnet run --project tools/ProgressionChecks -- --account-authentication --database
dotnet run --project tools/ProgressionChecks -- --account-recovery --database
```
## 统一抽卡池

アクター与ポスター分别使用一个常驻池。

- 角色 ★2／★3／★4：82%／15%／3%；海报 R／SR／SSR：80%／16%／4%。
- 单抽 300、十连 3000 剧ジュエル，优先消耗免费宝石。十连第十抽保证 ★3／SR 以上，最高稀有度概率保持不变。
- 每抽获得本池 1 点，两个池分别累计；200 点可自选本池任意 ★4／SSR，点数不随日期清除。
- 重复角色按 ★2／★3／★4 转换 1／10／100 个ジュゴン像。重复海报先提升突破，满突破后按 R／SR／SSR 转换 1／10／100 个ジュゴン像。

## 当前优先级
- 修复任务无法正常展示
- 补全circle排行榜api
- 解决成绩结算问题(由预设改为实际的计算公式(这个有点难…不确定能不能解决))

## 许可与贡献

### 许可证

本项目采用 PolyForm Noncommercial License 1.0.0，仅允许非商业用途。详细许可条款请参阅 [LICENSE](./LICENSE)。

该许可证仅适用于本项目作者及贡献者原创的代码，不适用于任何第三方游戏资源、商标、角色、音乐、图片、文本、剧情、模型、音频及其他受版权保护的内容。

本项目是由社区维护的非官方项目，与《ワールドダイスター 夢のステラリウム》（World Dai Star: Yume no Stellarium）的开发商、发行商及相关权利方不存在隶属、授权或合作关系。


## 鸣谢
非常感谢以下大佬们对游戏资源进行的备份!
- [Adv-Resource](https://github.com/wds-sirius/Adv-Resource) 
- [asset-of-dreams](https://github.com/Ryota537/asset-of-dreams)
