# 公演协议与结算还原（2026-10-20）

## 本次范围

原先 `GET /localap/api/Auditions/1?auditionId=1` 只有空队伍兼容响应。本次接入公演开演、阶段结算、首次奖励、进度同步、真实通关队伍、本人最高分和编队复制，沿用现有鉴权、MessagePack 五帧、演出计分与事务基础设施，没有新增依赖。业务拆分为 `AuditionRules`、`Auditions`、`AuditionLives`、`AuditionParties` 和追加式迁移 `AuditionSchema`，避免把查询、结算和编队写入堆进通用入口。

## 客户端证据

来源为本地 2.31.1 ARM64 `libil2cpp.so` 与匹配的 Il2CppDumper `dump.cs`、`script.json`、`stringliteral.json`；工作区 `.codex-temp/audition-contracts.txt` 与 `live-formula-research/native-*.txt` 保存检查材料。这里只分析本地安装包和主数据。

| 证据 | 结论 |
| --- | --- |
| `PartyFinalConfirmationModel.StartAuditionAsync` RVA `0xAB8676C` | 使用普通 `StartLivePayload`，key2 设置 `AuditionMasterId`，实际开演入口为 `/Lives/Start` |
| `GetAuditionInformationAsync` RVA `0xA34325C` | GET `/api/Auditions/{id}?auditionId={id}`，结果为单字段 `PartiesPhases`，分组为 `[Parties,ClearedPhase]` |
| `GetAuditionHighScorePartyAsync` RVA `0xA342F0C`、闭包 `0xA40381C` | GET `/api/Auditions/{id}/HighScoreParty?auditionId={id}` |
| `CopyAuditionClearPartyAsync` RVA `0xA342A60`、闭包 `0xA48F89C` | POST `/api/Auditions/{id}/CopyTo/{partyId}?auditionId={id}&uPartyId={partyId}`，返回 `BooleanResult` |
| `AuditionStageInfoDialogBody.InitializeAsync` RVA `0xADE977C` | 按阶段找队伍组，没有记录的阶段可不返回 |
| `AuditionClear` MessagePack 合约 | union90，六字段：`[Id,AuditionMasterId,ClearPhase,AuditionClearPartyId,UserId,SkipClearPhase]`，其中队伍 ID/玩家 ID 为旧兼容字段 |
| `FinishLiveResult` 合约 | key9/10 为结算前/后阶段，key11 为公演奖励，key14 为公演 ID |

`AuditionClearParty` 为 11 字段数组：`[Id,Score,Difficulty,Slots,UserName,RateGrade,nil,nil,nil,PlayerRank,LeaderPosition]`。`PlayerRank` 必须为字符串，不能将稀疏 key9/10 压成连续字段。成员为 15 字段，包含实际角色主数据、等级、潜能/觉醒、海报/饰品主数据和等级、星章、当前属性及卡面显示标志。星章取 CharacterBase，不能误用 Character 的选择类型字段。

主数据共有 250 场公演、550 个阶段和 15 个奖励包。`AuditionMaster` 提供歌曲与 SenseNotation，`AuditionPhaseMaster` 提供分数和可选 Star Act 次数，`AuditionRewardPackageMaster` 当前仅含道具与免费宝石。规则初始化检查阶段连续性、奖励类型和 SenseNotation 引用；主数据损坏直接暴露。

例如公演 1 第一阶段要求 5,000 分，第二阶段要求 1,340,388 分；公演 5 第三阶段额外要求 4 次 Star Act。阶段奖励按真实主数据授予，允许一场跨越多个阶段，各阶段只领取一次。

## 服务端选择与限制

官方服务端实现不可由客户端合约完全确定，以下是本服明确采用的策略：

- 每阶段展示最高分的五支真实队伍，同分按记录 ID 排序；没有记录就返回空组列表，不生成示例玩家。

- 本人最高分保留手动失败场次；公演通关与阶段参考队伍只接受有效手动通关。自动不改变公演成绩、进度和首次奖励。

- 当前完整逐音符和支持的静态技能使用开演数值复算；汇总格式、动态效果及既有未支持的计分仍沿用通用演出规则。本次没有声称已完成全部计分或反作弊。

- 复制恢复角色、装备和领队配置，按当前持有实例解析；不改变当前养成等级。装备已丢失时整体拒绝，不生成替代物品。

- 尚未还原 Skip/SkipClearPhase、逐场解锁链、等级上限联动、官方队伍选样策略。展示期限与歌曲/难度/冲突模式已验证，其他入口限制待进一步证据确认。

- 公演的基础金币、材料、经验和普通谱面成绩沿用既有通用演出流程，特殊差异尚无充分证据。旧普通成绩不含公演 ID，不能可靠补成公演历史。

## 数据一致性与验证

开演冻结 11/15 字段队伍和实际 Sense 时轴。结算的阶段、队伍、首次奖励、两份宝石余额和完成缓存与原演出共用事务、账号锁与提交摘要；失败整体回滚，并发及串行重试重放原结果。复制也使用相同账号锁并以单一事务完成。HTTP 错误沿用稳定错误码和五帧响应，日志记录公演 ID、阶段前后和最终结果。

工作区运行：

```powershell
dotnet run --project tools/ProgressionChecks -- --auditions --database
dotnet run --project tools/ProgressionChecks -- --live-rewards --database
```

公演检查使用独立 PostgreSQL schema、随机端口和临时主数据文件，结束后清理。覆盖全部 550 个分数/Star Act 门槛、真实 HTTP 五帧、开演冻结与重试、并发结算、奖励失败回滚、首次奖励/宝石双余额、重登录 union90、失败/自动隔离、最高分复制、装备缺失事务回滚、账号隔离、重复/非法 Star Act、逐音符复算和有界同分排序。普通演出回归检查原有开演扣费、计分、奖励与缓存兼容。

本次没有重启正在运行的服务。新表在更新服务并启动时自动迁移；测试通过不代表已经完成游戏 UI 实机验收。
