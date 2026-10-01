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
        await CardInventoryWriter.GrantAsync(connection, transaction, userId, catalog, card);
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
