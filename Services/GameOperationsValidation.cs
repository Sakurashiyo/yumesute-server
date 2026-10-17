static class GameOperationsValidation
{
    static readonly Lazy<AdminSaveCatalog> Inventory = new(() => new AdminSaveCatalog());
    static readonly Lazy<HashSet<long>> Posters = new(() => GachaRules.ReadRows("Shop.PosterMaster.json")
        .Cast<object?[]>().Select(row => GachaRules.Long(row[0])).ToHashSet());

    public static void Validate(GameOperationsConfiguration config)
    {
        if (config is null || config.MissionPass is null || config.Gachas is null || config.LoginBonuses is null)
            Invalid("缺少活动配置");
        if (!config!.Gachas.Keys.Order().SequenceEqual(GachaRules.AllPools.Select(pool => pool.Id).Order())
            || !config.LoginBonuses.Keys.Order().SequenceEqual(LoginBonusRules.Rewards.Select(reward => reward.Id).Order()))
            Invalid("卡池或登录奖励 ID 与服务端目录不符，请刷新页面");
        var pass = config.MissionPass;
        Period(pass.StartsAt, pass.EndsAt);
        if (pass.Stages is not { Length: 50 } || pass.LoopRewards is not { Length: 4 })
            Invalid("通行证必须包含 50 阶和 4 个循环奖励");
        var previous = 0;
        for (var index = 0; index < pass.Stages.Length; index++)
        {
            var stage = pass.Stages[index];
            if (stage is null || stage.Phase != index + 1 || stage.Points <= previous || stage.Points > 20000)
                Invalid("阶段必须连续，积分门槛严格递增且不超过 20000（客户端循环奖励从 20000 开始）");
            if (stage!.Rewards is not { Length: 2 } || stage.Rewards.Count(reward => reward is not null && reward.Sp) != 1)
                Invalid($"第 {index + 1} 阶必须各有一项普通和 SP 奖励");
            foreach (var reward in stage.Rewards) Reward(reward);
            previous = stage.Points;
        }
        foreach (var rewards in pass.LoopRewards)
        {
            if (rewards is not { Length: 1 } || rewards[0] is null || rewards[0].Sp)
                Invalid("每个循环阶段必须有一项普通奖励");
            Reward(rewards[0]);
        }
        foreach (var setting in config.LoginBonuses.Values)
        {
            if (setting is null) Invalid("缺少登录奖励配置");
            Period(setting!.StartsAt, setting.EndsAt);
        }
    }

    static void Period(DateTimeOffset start, DateTimeOffset? end)
    {
        if (start < new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)
            || start >= MissionPassRules.PermanentEnd || end <= start || end > MissionPassRules.PermanentEnd)
            Invalid("开放时间必须在 2020–2099 年之间，结束时间晚于开始时间；留空结束时间表示常驻");
    }
    static void Reward(MissionPassRules.Reward reward)
    {
        if (reward is null || reward.Quantity is < 1 or > 1000000) Invalid("奖励数量必须为 1–1000000");
        if (reward!.Type == 1)
        {
            if (reward.MasterId == MissionPassRules.PointItemId
                || !Inventory.Value.Items.TryGetValue(reward.MasterId, out var item) || reward.Quantity > item.MaxQuantity)
                Invalid("道具 ID/数量无效；通行证不能奖励自身积分");
        }
        else if (reward.Type == 3)
        {
            if (!Posters.Value.Contains(reward.MasterId) || reward.Quantity > 5) Invalid("海报 ID 无效或单项数量超过 5");
        }
        else if (reward.Type is 12 or 13)
        {
            if (reward.MasterId != 0) Invalid("金币和免费宝石的主数据 ID 必须为 0");
        }
        else Invalid("奖励仅支持道具、海报、金币和免费宝石");
    }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    static void Invalid(string message) => throw new GameOperationsException(GameOperationsErrors.Invalid, message);
}
