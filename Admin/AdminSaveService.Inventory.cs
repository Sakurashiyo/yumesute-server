using Npgsql;

sealed partial class AdminSaveService
{
    async Task SaveCardAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId, AdminSaveCommand request)
    {
        if (!catalog.Cards.TryGetValue(request.MasterId, out var card))
            throw new AdminSaveException(AdminSaveErrors.MasterMissing, "不存在该卡片主数据");
        if (request.Remove)
        {
            await using var used = Command(connection, transaction,
                """
                select exists(select 1 from user_character_cards c where c."userId" = $1 and c.character_master_id = $2 and (
                  exists(select 1 from user_party_slots s where s.character_card_id = c.id) or
                  exists(select 1 from user_character_bases b where b.selected_character_id = c.id) or
                  exists(select 1 from user_profiles p where p."userId" = $1 and (p.favorite_character_id = c.id or p.home_character_master_id = $2))))
                """, userId, card.Id);
            if (card.Id == 110010 || (bool)(await used.ExecuteScalarAsync())!)
                throw new AdminSaveException(AdminSaveErrors.InUse, "初始卡片或正在使用的卡片不能移除，请先在游戏中切换角色", 409);
            await ExecuteAsync(connection, transaction, "delete from user_character_cards where \"userId\" = $1 and character_master_id = $2", userId, card.Id);
            return;
        }
        var character = catalog.Bases[card.BaseId];
        if (!catalog.Costumes.ContainsKey(character.DefaultCostume)) throw new InvalidOperationException("默认服装主数据缺失");
        await ExecuteAsync(connection, transaction,
            """
            insert into user_character_bases (id, "userId", character_base_master_id, costume_master_id)
            values ($1,$2,$3,$4) on conflict ("userId", character_base_master_id) do nothing
            """, NewId(), userId, card.BaseId, character.DefaultCostume);
        await using var findBase = Command(connection, transaction, "select id from user_character_bases where \"userId\" = $1 and character_base_master_id = $2", userId, card.BaseId);
        var baseId = (long)(await findBase.ExecuteScalarAsync() ?? throw new InvalidOperationException("创建角色基础状态失败"));
        await ExecuteAsync(connection, transaction,
            """
            insert into user_character_cards (id,"userId",character_master_id,character_base_id,rarity,skill_level)
            values ($1,$2,$3,$4,$5,1) on conflict ("userId",character_master_id) do nothing
            """, NewId(), userId, card.Id, baseId, card.Rarity);
        await ExecuteAsync(connection, transaction,
            """
            update user_character_bases set selected_character_id = (
              select id from user_character_cards where "userId" = $1 and character_master_id = $2)
            where id = $3 and selected_character_id is null
            """, userId, card.Id, baseId);
        await GrantCostumeAsync(connection, transaction, userId, character.DefaultCostume);
        // 与现有新用户初始化约定一致，补齐该角色的基础育成状态。
        foreach (var resource in new[] { (Type: 3, Master: 10201L), (Type: 2, Master: 10101L) })
            await ExecuteAsync(connection, transaction,
                """
                insert into user_character_resource_progress (id,"userId",character_base_master_id,resource_type,resource_master_id)
                values ($1,$2,$3,$4,$5) on conflict ("userId",character_base_master_id,resource_type) do nothing
                """, NewId(), userId, card.BaseId, resource.Type, resource.Master);
    }

    async Task SaveCostumeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId, AdminSaveCommand request)
    {
        if (!catalog.Costumes.ContainsKey(request.MasterId)) throw new AdminSaveException(AdminSaveErrors.MasterMissing, "不存在该服装主数据");
        if (!request.Remove) { await GrantCostumeAsync(connection, transaction, userId, request.MasterId); return; }
        await using var used = Command(connection, transaction,
            """
            select exists(select 1 from user_character_bases where "userId" = $1 and costume_master_id = $2)
              or exists(select 1 from user_home_display_preferences where "userId" = $1 and
              (costume_master_id = $2 or illustration_costume_master_id = $2 or sub_costume_master_id_1 = $2
               or sub_costume_master_id_2 = $2 or sub_costume_master_id_3 = $2 or selected_costume_master_id = $2))
            """, userId, request.MasterId);
        if (request.MasterId == 11 || (bool)(await used.ExecuteScalarAsync())!)
            throw new AdminSaveException(AdminSaveErrors.InUse, "正在使用的服装不能移除，请先切换服装", 409);
        await ExecuteAsync(connection, transaction, "delete from user_character_costumes where \"userId\" = $1 and costume_master_id = $2", userId, request.MasterId);
    }

    static Task GrantCostumeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId, long masterId) =>
        ExecuteAsync(connection, transaction,
            "insert into user_character_costumes (id,\"userId\",costume_master_id) values ($1,$2,$3) on conflict (\"userId\",costume_master_id) do nothing", NewId(), userId, masterId);

    async Task SaveItemAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId, AdminSaveCommand request)
    {
        if (!catalog.Items.TryGetValue(request.MasterId, out var item)) throw new AdminSaveException(AdminSaveErrors.MasterMissing, "不存在该道具主数据");
        var quantity = request.Remove ? 0 : request.Quantity;
        if (quantity < 0 || quantity > item.MaxQuantity)
            throw new AdminSaveException(AdminSaveErrors.Invalid, $"该道具数量范围为 0–{item.MaxQuantity}");
        await ExecuteAsync(connection, transaction,
            """
            insert into user_item_possessions (id,"userId",item_master_id,quantity) values ($1,$2,$3,$4)
            on conflict ("userId",item_master_id) do update set quantity = excluded.quantity, updated_at = now()
            """, NewId(), userId, request.MasterId, quantity);
    }
}
