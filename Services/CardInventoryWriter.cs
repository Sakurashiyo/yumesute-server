using Npgsql;

// 管理后台和抽卡共用持有卡写入，确保基础角色、默认服装和育成状态一起创建。
static class CardInventoryWriter
{
    public static async Task<bool> GrantAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,long userId,
        AdminSaveCatalog catalog,AdminSaveCatalog.Entry card)
    {
        var character=catalog.Bases[card.BaseId];
        if(!catalog.Costumes.ContainsKey(character.DefaultCostume))throw new InvalidOperationException("默认服装主数据缺失");
        await Execute("""
            insert into user_character_bases(id,"userId",character_base_master_id,costume_master_id)
            values($1,$2,$3,$4) on conflict ("userId",character_base_master_id) do nothing
            """,NewId(),userId,card.BaseId,character.DefaultCostume);
        await using var find=Command("select id from user_character_bases where \"userId\"=$1 and character_base_master_id=$2",userId,card.BaseId);
        var baseId=(long)(await find.ExecuteScalarAsync() ?? throw new InvalidOperationException("创建角色基础状态失败"));
        await using var insert=Command("""
            insert into user_character_cards(id,"userId",character_master_id,character_base_id,rarity,skill_level)
            values($1,$2,$3,$4,$5,1) on conflict ("userId",character_master_id) do nothing returning id
            """,NewId(),userId,card.Id,baseId,card.Rarity);
        var created=await insert.ExecuteScalarAsync() is long;
        await Execute("""
            update user_character_bases set selected_character_id=(select id from user_character_cards where "userId"=$1 and character_master_id=$2)
            where id=$3 and selected_character_id is null
            """,userId,card.Id,baseId);
        await Execute("insert into user_character_costumes(id,\"userId\",costume_master_id) values($1,$2,$3) on conflict (\"userId\",costume_master_id) do nothing",NewId(),userId,character.DefaultCostume);
        foreach(var resource in new[] {(Type:3,Master:10201L),(Type:2,Master:10101L)})
            await Execute("""
                insert into user_character_resource_progress(id,"userId",character_base_master_id,resource_type,resource_master_id)
                values($1,$2,$3,$4,$5) on conflict ("userId",character_base_master_id,resource_type) do nothing
                """,NewId(),userId,card.BaseId,resource.Type,resource.Master);
        return created;
        NpgsqlCommand Command(string sql,params object[] args)
        {
            var command=new NpgsqlCommand(sql,connection,transaction);
            foreach(var value in args)command.Parameters.AddWithValue(value);return command;
        }
        async Task Execute(string sql,params object[] args) {await using var command=Command(sql,args);await command.ExecuteNonQueryAsync();}
    }
    internal static long NewId()=>System.Security.Cryptography.RandomNumberGenerator.GetInt32(1,int.MaxValue)*(long)int.MaxValue
        +System.Security.Cryptography.RandomNumberGenerator.GetInt32(1,int.MaxValue);
}
