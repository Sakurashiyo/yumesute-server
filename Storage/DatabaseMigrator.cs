using Npgsql;

using NpgsqlTypes;
using System.Text.Json;
using System.Text.RegularExpressions;

static class DatabaseMigrator
{
    enum StaticColumnKind
    {
        Bigint,
        Integer,
        Real,
        Boolean,
        Text,
        Timestamp,
        Jsonb
    }

    sealed record StaticColumn(string Name, string SqlType, NpgsqlDbType DbType, StaticColumnKind Kind, int Index, bool IsPrimaryKey = false);

    sealed record StaticTableSchema(string MasterName, string TableName, StaticColumn[] Columns);

    static readonly HashSet<string> StaticMasterDatabaseTables = new(StringComparer.Ordinal)
    {
        "TrophyMaster",
        "MusicMaster",
        "MusicCourseMaster",
        "JewelShopItemMaster",
        "IconFrameMaster",
        "GachaMaster",
        "EventMaster",
        "StoryMaster",
        "DugongRunCourseMaster",
        "ComicMaster",
        "AccessoryMaster",
        "LiveMaster",
        "AdditionalRewardPackageMaster",
        "AuditionRewardPackageMaster",
        "EpisodeRewardPackageMaster",
        "LeaguePlayRewardPackageMaster",
        "LeagueRewardPackageMaster"
    };

    static readonly Dictionary<string, StaticTableSchema> DedicatedStaticMasterSchemas = new(StringComparer.Ordinal)
    {
        ["AccessoryMaster"] = new("AccessoryMaster", "wds_static_accessory", new[]
        {
            Bigint("id", 0, true), Text("name", 1), Text("description", 2), Integer("rarity", 3),
            Bigint("accessory_level_pattern_group_id", 4), Jsonb("fixed_accessory_effects", 5),
            Jsonb("random_effect_groups", 6), Text("pronounce_name", 7), Integer("series", 8), Integer("max_level", 9)
        }),
        ["ComicMaster"] = new("ComicMaster", "wds_static_comic", new[]
        {
            Bigint("id", 0, true), Text("group_name", 1), Jsonb("episodes", 2)
        }),
        ["DugongRunCourseMaster"] = new("DugongRunCourseMaster", "wds_static_dugong_run_course", new[]
        {
            Bigint("id", 0, true), Bigint("event_master_id", 1), Integer("difficulty", 2),
            Bigint("music_master_id", 3), Text("notation_path", 4), Integer("scroll_speed", 5),
            Bigint("dugong_run_course_group_id", 6)
        }),
        ["EventMaster"] = new("EventMaster", "wds_static_event", new[]
        {
            Bigint("id", 0, true), Integer("event_type", 1), Timestamp("start_date", 2), Timestamp("end_date", 3),
            Timestamp("force_end_date", 4), Text("name", 5), Boolean("is_aside_live_button", 6),
            Bigint("event_point_item_master_id", 7), Integer("bonus_attribute", 8),
            Bigint("bonus_category_master_id10", 9), Bigint("bonus_category_master_id20", 10),
            Jsonb("bonuses", 11), Jsonb("point_rewards", 12), Jsonb("ranking_rewards", 13),
            Bigint("exchange_shop_master_id", 14), Integer("aside_live_button_type", 15),
            Integer("secondary_bonus_attribute", 16)
        }),
        ["GachaMaster"] = new("GachaMaster", "wds_static_gacha", new[]
        {
            Bigint("id", 0, true), Text("name", 1), Integer("card_type", 2), Integer("gacha_type", 3),
            Bigint("gacha_text_template_master_id", 4), Timestamp("start_date", 5), Timestamp("end_date", 6),
            Jsonb("bonus_things", 7), Jsonb("gacha_details", 8), Jsonb("things", 9), Boolean("has_movie", 10),
            Bigint("story_event_master_id", 11), Bigint("attention_gacha_text_template_master_id", 12),
            Boolean("is_force_display", 13), Boolean("is_hide_end_date", 14), Integer("unlock_type", 15),
            Bigint("unlock_value", 16), Text("exchange_shop_banner_path", 17), Bigint("order", 18),
            Integer("display_footer_condition", 19), Integer("group_type", 20), Jsonb("roll_bonuses", 21),
            Integer("re_roll_limit", 22), Text("replace_asset_name", 23)
        }),
        ["IconFrameMaster"] = new("IconFrameMaster", "wds_static_icon_frame", new[]
        {
            Bigint("id", 0, true), Text("name", 1), Integer("order", 2), Text("unlock_condition_text", 3),
            Boolean("hidden", 4), Boolean("is_default", 5)
        }),
        ["AdditionalRewardPackageMaster"] = new("AdditionalRewardPackageMaster", "wds_static_additional_reward_package", new[]
        {
            Bigint("id", 0, true), Text("condition_text", 1), Integer("condition_type", 2), Jsonb("rewards", 3),
            Boolean("is_hidden", 4), Bigint("condition_value_1", 5), Bigint("condition_value_2", 6),
            Integer("display_order", 7)
        }),
        ["AuditionRewardPackageMaster"] = new("AuditionRewardPackageMaster", "wds_static_audition_reward_package", new[]
        {
            Bigint("id", 0, true), Jsonb("rewards", 1)
        }),
        ["EpisodeRewardPackageMaster"] = new("EpisodeRewardPackageMaster", "wds_static_episode_reward_package", new[]
        {
            Bigint("id", 0, true), Jsonb("rewards", 1)
        }),
        ["JewelShopItemMaster"] = new("JewelShopItemMaster", "wds_static_jewel_shop_item", new[]
        {
            Bigint("id", 0, true), Integer("order", 1), Text("app_store_product_id", 2),
            Text("google_play_product_id", 3), Text("name", 4), Text("pack_text", 5),
            Bigint("m_jewel_shop_category_id", 6), Jsonb("m_jewel_shop_category_master", 7),
            Integer("dialog_type", 8), Integer("purchase_type", 9), Integer("purchase_value", 10),
            Boolean("subscription_flag", 11), Integer("give_paid_jewel", 12), Integer("replace_types", 13),
            Integer("replace_value", 14), Integer("purchase_limit", 15), Integer("expired_day", 16),
            Text("can_purchase_day_of_week", 17), Boolean("is_buy_not_display", 18), Integer("sale_type", 19),
            Integer("sale_value", 20), Boolean("is_display_locking", 21), Integer("unlock_type", 22),
            Bigint("unlock_value", 23), Timestamp("start_date", 24), Timestamp("end_date", 25),
            Jsonb("lineup", 26), Integer("shop_item_type", 27), Bigint("shop_item_value", 28),
            Bigint("comeback_campaign_master_id", 29), Text("item_icon_body_image_path", 30),
            Text("item_icon_detail_image_path", 31), Text("item_icon_badge_image_path", 32)
        }),
        ["MusicCourseMaster"] = new("MusicCourseMaster", "wds_static_music_course", new[]
        {
            Bigint("id", 0, true), Text("name", 1), Integer("music_course_type", 2), Bigint("order", 3),
            Integer("initial_life", 4), Bigint("required_item_master_id", 5), Integer("required_amount", 6),
            Timestamp("start_date", 7), Timestamp("end_date", 8), Integer("unlock_condition", 9),
            Bigint("unlock_condition_value", 10), Bigint("tournament_qualifying_master_id", 11), Jsonb("details", 12)
        }),
        ["MusicMaster"] = new("MusicMaster", "wds_static_music", new[]
        {
            Bigint("id", 0, true), Text("name", 1), Text("description", 2), Bigint("reward_rule_master_id", 3),
            Text("pronounce_name", 4), Text("lyric_writer", 5), Text("composer", 6), Text("arranger", 7),
            Text("unlock_text", 8), Boolean("is_long_version", 9), Timestamp("released_at", 10),
            Integer("stamina_consumption", 11), Integer("music_time_second", 12), Boolean("invisible", 13),
            Real("sample_start_seconds", 14), Real("sample_end_seconds", 15), Real("delay_seconds", 16),
            Jsonb("vocal_versions", 17), Integer("unlock_condition_type", 18), Bigint("unlock_condition_value", 19),
            Integer("music_video_type", 20), Integer("music_cover_type", 21), Bigint("story_event_master_id", 22),
            Bigint("story_master_id", 23), Bigint("event_master_id", 24)
        }),
        ["LiveMaster"] = new("LiveMaster", "wds_static_live", new[]
        {
            Bigint("id", 0, true), Bigint("music_master_id", 1), Integer("difficulty", 2), Integer("level", 3),
            Integer("live_type", 4), Integer("reward_group", 5), Jsonb("extra_condition", 6),
            Timestamp("start_at", 7), Timestamp("end_at", 8)
        }),
        ["LeaguePlayRewardPackageMaster"] = new("LeaguePlayRewardPackageMaster", "wds_static_league_play_reward_package", new[]
        {
            Bigint("id", 0, true), Jsonb("rewards", 1)
        }),
        ["LeagueRewardPackageMaster"] = new("LeagueRewardPackageMaster", "wds_static_league_reward_package", new[]
        {
            Bigint("id", 0, true), Jsonb("rewards", 1)
        }),
        ["StoryMaster"] = new("StoryMaster", "wds_static_story", new[]
        {
            Bigint("id", 0, true), Integer("type", 1), Bigint("company_master_id", 2),
            Bigint("event_master_id", 3), Integer("chapter_order", 4), Timestamp("display_start_at", 5),
            Timestamp("display_end_at", 6)
        }),
        ["TrophyMaster"] = new("TrophyMaster", "wds_static_trophy", new[]
        {
            Bigint("id", 0, true), Text("name", 1), Text("description", 2), Integer("rarity", 3),
            Integer("order", 4), Bigint("trophy_group_master_id", 5), Boolean("hidden", 6), Text("unlock_text", 7)
        })
    };

    static StaticColumn Bigint(string name, int index, bool primary = false) =>
        new(name, "bigint", NpgsqlDbType.Bigint, StaticColumnKind.Bigint, index, primary);

    static StaticColumn Integer(string name, int index, bool primary = false) =>
        new(name, "integer", NpgsqlDbType.Integer, StaticColumnKind.Integer, index, primary);

    static StaticColumn Real(string name, int index, bool primary = false) =>
        new(name, "real", NpgsqlDbType.Real, StaticColumnKind.Real, index, primary);

    static StaticColumn Boolean(string name, int index, bool primary = false) =>
        new(name, "boolean", NpgsqlDbType.Boolean, StaticColumnKind.Boolean, index, primary);

    static StaticColumn Text(string name, int index, bool primary = false) =>
        new(name, "text", NpgsqlDbType.Text, StaticColumnKind.Text, index, primary);

    static StaticColumn Timestamp(string name, int index, bool primary = false) =>
        new(name, "timestamptz", NpgsqlDbType.TimestampTz, StaticColumnKind.Timestamp, index, primary);

    static StaticColumn Jsonb(string name, int index, bool primary = false) =>
        new(name, "jsonb", NpgsqlDbType.Jsonb, StaticColumnKind.Jsonb, index, primary);

    public static async Task MigrateAsync(PostgresDatabase database, LocalConfig? config = null)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(Sql, connection);
        await command.ExecuteNonQueryAsync();
        await CircleSchema.MigrateAsync(connection);
        await CharacterMissionSchema.MigrateAsync(connection);
        await AnotherNotationSchema.MigrateAsync(connection);
        await MigrateCharacterSenseLevelsAsync(connection);
        if (config is not null)
        {
            await SeedActiveMasterDataVersionAsync(connection, config);
        }
        await SeedStaticMasterDataAsync(connection, config);
        await SeedStaticCharactersAsync(connection);
        await SeedStaticItemsAsync(connection);
    }

    public static async Task MigrateCharacterSenseLevelsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction = null)
    {
        // Sense 按精确等级查找效果；历史默认值 0 会使客户端计算编队时抛出异常。
        await using var command = new NpgsqlCommand("""
            alter table user_character_cards alter column skill_level set default 1;
            update user_character_cards set skill_level = 1 where skill_level = 0;
            do $$ begin
              if not exists (select 1 from pg_constraint where conrelid = 'user_character_cards'::regclass
                and conname = 'user_character_cards_sense_level_positive') then
                alter table user_character_cards add constraint user_character_cards_sense_level_positive
                  check (skill_level >= 1);
              end if;
            end $$;
            """, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

    static async Task SeedActiveMasterDataVersionAsync(NpgsqlConnection connection, LocalConfig config)
    {
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using (var deactivateCommand = new NpgsqlCommand(
                """
                update system_master_data_versions
                set is_active = false,
                    updated_at = now()
                where is_active
                  and version <> $1
                """,
                connection,
                transaction))
            {
                deactivateCommand.Parameters.AddWithValue(config.MasterDataVersion);
                await deactivateCommand.ExecuteNonQueryAsync();
            }

            await using (var command = new NpgsqlCommand(
                """
                insert into system_master_data_versions (
                  version, publish_timestamp, remote_path, local_file_path, sas_token, is_active
                )
                values ($1, $2, $3, $4, '', true)
                on conflict (version) do update set
                  publish_timestamp = excluded.publish_timestamp,
                  remote_path = excluded.remote_path,
                  local_file_path = excluded.local_file_path,
                  is_active = true,
                  updated_at = now()
                """,
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(config.MasterDataVersion);
                command.Parameters.AddWithValue(config.MasterDataPublishTimestamp);
                command.Parameters.AddWithValue(config.MasterDataRemotePath);
                command.Parameters.AddWithValue(config.MasterDataFile);
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync();
            throw;
        }
    }

    static async Task SeedStaticCharactersAsync(NpgsqlConnection connection)
    {
        var path = FindMasterDataFile("CharacterMaster.json");
        if (path is null) return;

        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return;

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using var command = new NpgsqlCommand(
                """
                insert into wds_static_character (
                  character_master_id,
                  character_base_master_id,
                  costume_master_id,
                  display_order,
                  payload
                )
                values ($1, $2, null, $3, $4::jsonb)
                on conflict (character_master_id)
                do update set
                  character_base_master_id = excluded.character_base_master_id,
                  payload = excluded.payload,
                  updated_at = now()
                """,
                connection,
                transaction);

            var characterMasterIdParameter = command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint });
            var characterBaseMasterIdParameter = command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint });
            var displayOrderParameter = command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer });
            var payloadParameter = command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb });

            var displayOrder = 0;
            foreach (var row in document.RootElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 2) continue;
                if (!row[0].TryGetInt64(out var characterMasterId)) continue;
                if (!row[1].TryGetInt64(out var characterBaseMasterId)) continue;

                characterMasterIdParameter.Value = characterMasterId;
                characterBaseMasterIdParameter.Value = characterBaseMasterId;
                displayOrderParameter.Value = ++displayOrder;
                payloadParameter.Value = row.GetRawText();
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            if (transaction.Connection is not null)
            {
                await transaction.RollbackAsync();
            }
            throw;
        }
    }

    static string? FindMasterDataFile(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "masterdata-export", fileName),
            Path.Combine(AppContext.BaseDirectory, "masterdata-export", "tables", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "masterdata-export", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "masterdata-export", "tables", fileName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    static string? FindMasterDataManifest()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "masterdata-export", "manifest.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "masterdata-export", "manifest.json")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    static async Task SeedStaticMasterDataAsync(NpgsqlConnection connection, LocalConfig? config)
    {
        var manifestPath = FindMasterDataManifest();
        if (manifestPath is null) return;

        await using var stream = File.OpenRead(manifestPath);
        using var document = await JsonDocument.ParseAsync(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return;
        if (!document.RootElement.TryGetProperty("tables", out var tablesElement) ||
            tablesElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var source = document.RootElement.TryGetProperty("source", out var sourceElement) &&
                     sourceElement.ValueKind == JsonValueKind.String
            ? sourceElement.GetString() ?? ""
            : "";
        var version = config?.MasterDataVersion ?? TryReadMasterDataVersion(source) ?? "unknown";
        var manifestDirectory = Path.GetDirectoryName(manifestPath) ?? Directory.GetCurrentDirectory();

        foreach (var tableElement in tablesElement.EnumerateArray())
        {
            if (!tableElement.TryGetProperty("name", out var nameElement) ||
                nameElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var tableName = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(tableName) || !StaticMasterDatabaseTables.Contains(tableName))
            {
                continue;
            }

            await SeedStaticMasterTableAsync(connection, manifestDirectory, version, tableName, tableElement);
        }
    }

    static async Task SeedStaticMasterTableAsync(
        NpgsqlConnection connection,
        string manifestDirectory,
        string version,
        string tableName,
        JsonElement tableElement)
    {
        var tableFile = ResolveMasterTableFile(manifestDirectory, tableName, tableElement);
        if (tableFile is null || !File.Exists(tableFile)) return;

        await using var stream = File.OpenRead(tableFile);
        using var tableDocument = await JsonDocument.ParseAsync(stream);
        if (tableDocument.RootElement.ValueKind != JsonValueKind.Array) return;

        var rowCount = tableDocument.RootElement.GetArrayLength();
        var blockType = tableElement.TryGetProperty("block_type", out var blockTypeElement) &&
                        blockTypeElement.ValueKind == JsonValueKind.String
            ? blockTypeElement.GetString() ?? ""
            : "";
        var (indexOffset, indexLength) = ReadStaticMasterIndex(tableElement);

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using (var tableCommand = new NpgsqlCommand(
                """
                insert into wds_static_master_tables (
                  table_name,
                  source_version,
                  source_file,
                  block_type,
                  index_offset,
                  index_length,
                  row_count,
                  manifest
                )
                values ($1, $2, $3, $4, $5, $6, $7, $8::jsonb)
                on conflict (table_name) do update set
                  source_version = excluded.source_version,
                  source_file = excluded.source_file,
                  block_type = excluded.block_type,
                  index_offset = excluded.index_offset,
                  index_length = excluded.index_length,
                  row_count = excluded.row_count,
                  manifest = excluded.manifest,
                  imported_at = now(),
                  updated_at = now()
                """,
                connection,
                transaction))
            {
                tableCommand.Parameters.AddWithValue(tableName);
                tableCommand.Parameters.AddWithValue(version);
                tableCommand.Parameters.AddWithValue(tableFile);
                tableCommand.Parameters.AddWithValue(blockType);
                tableCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint, Value = indexOffset.HasValue ? indexOffset.Value : DBNull.Value });
                tableCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint, Value = indexLength.HasValue ? indexLength.Value : DBNull.Value });
                tableCommand.Parameters.AddWithValue(rowCount);
                tableCommand.Parameters.AddWithValue(NpgsqlDbType.Jsonb, tableElement.GetRawText());
                await tableCommand.ExecuteNonQueryAsync();
            }

            await using (var deleteCommand = new NpgsqlCommand(
                "delete from wds_static_master_rows where table_name = $1",
                connection,
                transaction))
            {
                deleteCommand.Parameters.AddWithValue(tableName);
                await deleteCommand.ExecuteNonQueryAsync();
            }

            await using (var rowCommand = new NpgsqlCommand(
                """
                insert into wds_static_master_rows (
                  table_name,
                  row_index,
                  master_id,
                  payload
                )
                values ($1, $2, $3, $4::jsonb)
                """,
                connection,
                transaction))
            {
                var tableNameParameter = rowCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text });
                var rowIndexParameter = rowCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer });
                var masterIdParameter = rowCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint });
                var payloadParameter = rowCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb });

                var rowIndex = 0;
                foreach (var row in tableDocument.RootElement.EnumerateArray())
                {
                    tableNameParameter.Value = tableName;
                    rowIndexParameter.Value = rowIndex++;
                    masterIdParameter.Value = TryReadMasterId(row) is { } masterId ? masterId : DBNull.Value;
                    payloadParameter.Value = row.GetRawText();
                    await rowCommand.ExecuteNonQueryAsync();
                }
            }

            await SeedDedicatedStaticMasterTableAsync(connection, transaction, tableName, tableDocument.RootElement);

            await transaction.CommitAsync();
        }
        catch
        {
            if (transaction.Connection is not null)
            {
                await transaction.RollbackAsync();
            }
            throw;
        }
    }

    static async Task SeedDedicatedStaticMasterTableAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string masterName,
        JsonElement rows)
    {
        if (!DedicatedStaticMasterSchemas.TryGetValue(masterName, out var schema)) return;

        await EnsureDedicatedStaticTableAsync(connection, transaction, schema);

        await using (var deleteCommand = new NpgsqlCommand(
            $"delete from {QuoteIdentifier(schema.TableName)}",
            connection,
            transaction))
        {
            await deleteCommand.ExecuteNonQueryAsync();
        }

        var columnNames = schema.Columns.Select(column => QuoteIdentifier(column.Name)).Append("payload").ToArray();
        var parameterNames = Enumerable.Range(1, schema.Columns.Length + 1).Select(index => $"${index}").ToArray();
        var insertSql =
            $"insert into {QuoteIdentifier(schema.TableName)} ({string.Join(", ", columnNames.Select(name => name == "payload" ? QuoteIdentifier("payload") : name))}) " +
            $"values ({string.Join(", ", parameterNames.Take(schema.Columns.Length).Concat(new[] { $"${schema.Columns.Length + 1}::jsonb" }))})";

        await using var insertCommand = new NpgsqlCommand(insertSql, connection, transaction);
        foreach (var column in schema.Columns)
        {
            insertCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = column.DbType });
        }
        insertCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb });

        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array) continue;

            for (var i = 0; i < schema.Columns.Length; i++)
            {
                var column = schema.Columns[i];
                insertCommand.Parameters[i].Value = ReadStaticColumnValue(row, column);
            }

            insertCommand.Parameters[schema.Columns.Length].Value = row.GetRawText();
            await insertCommand.ExecuteNonQueryAsync();
        }
    }

    static async Task EnsureDedicatedStaticTableAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StaticTableSchema schema)
    {
        var columnDefinitions = schema.Columns.Select(column =>
        {
            var definition = $"{QuoteIdentifier(column.Name)} {column.SqlType}";
            if (column.IsPrimaryKey)
            {
                definition += " primary key";
            }

            return definition;
        });

        var sql =
            $"""
            create table if not exists {QuoteIdentifier(schema.TableName)} (
              {string.Join(",\n              ", columnDefinitions)},
              payload jsonb not null,
              imported_at timestamptz not null default now(),
              updated_at timestamptz not null default now()
            )
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

    static object ReadStaticColumnValue(JsonElement row, StaticColumn column)
    {
        if (column.Index >= row.GetArrayLength()) return DBNull.Value;

        var value = row[column.Index];
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return DBNull.Value;

        return column.Kind switch
        {
            StaticColumnKind.Bigint => TryGetInt64(value) is { } int64Value ? int64Value : DBNull.Value,
            StaticColumnKind.Integer => TryGetInt32(value) is { } int32Value ? int32Value : DBNull.Value,
            StaticColumnKind.Real => TryGetSingle(value) is { } singleValue ? singleValue : DBNull.Value,
            StaticColumnKind.Boolean => value.ValueKind == JsonValueKind.True ? true :
                value.ValueKind == JsonValueKind.False ? false : DBNull.Value,
            StaticColumnKind.Text => value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText(),
            StaticColumnKind.Timestamp => TryGetDateTimeOffset(value) is { } dateTimeOffset ? dateTimeOffset : DBNull.Value,
            StaticColumnKind.Jsonb => value.GetRawText(),
            _ => DBNull.Value
        };
    }

    static long? TryGetInt64(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result)) return result;
        return null;
    }

    static int? TryGetInt32(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)) return result;
        return null;
    }

    static float? TryGetSingle(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var result)) return result;
        return null;
    }

    static DateTimeOffset? TryGetDateTimeOffset(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(value.GetString(), out var stringResult))
        {
            return stringResult;
        }

        if (value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty("value", out var timestampValue) &&
            timestampValue.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(timestampValue.GetString(), out var objectResult))
        {
            return objectResult;
        }

        return null;
    }

    static string QuoteIdentifier(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    static string? ResolveMasterTableFile(string manifestDirectory, string tableName, JsonElement tableElement)
    {
        if (tableElement.TryGetProperty("file", out var fileElement) &&
            fileElement.ValueKind == JsonValueKind.String)
        {
            var file = fileElement.GetString();
            if (!string.IsNullOrWhiteSpace(file))
            {
                return Path.IsPathRooted(file)
                    ? file
                    : Path.GetFullPath(Path.Combine(manifestDirectory, file));
            }
        }

        return Path.Combine(manifestDirectory, "tables", $"{tableName}.json");
    }

    static (long? Offset, long? Length) ReadStaticMasterIndex(JsonElement tableElement)
    {
        if (!tableElement.TryGetProperty("index", out var indexElement) ||
            indexElement.ValueKind != JsonValueKind.Array ||
            indexElement.GetArrayLength() < 2)
        {
            return (null, null);
        }

        var values = indexElement.EnumerateArray().Take(2).ToArray();
        var offset = values[0].ValueKind == JsonValueKind.Number && values[0].TryGetInt64(out var offsetValue)
            ? offsetValue
            : (long?)null;
        var length = values[1].ValueKind == JsonValueKind.Number && values[1].TryGetInt64(out var lengthValue)
            ? lengthValue
            : (long?)null;
        return (offset, length);
    }

    static long? TryReadMasterId(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() == 0) return null;
        return row[0].ValueKind == JsonValueKind.Number && row[0].TryGetInt64(out var masterId)
            ? masterId
            : null;
    }

    static string? TryReadMasterDataVersion(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        var match = Regex.Match(
            Path.GetFileName(source),
            @"mastermemory_(?<version>[^.]+)\.db$",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["version"].Value : null;
    }

    static async Task SeedStaticItemsAsync(NpgsqlConnection connection)
    {
        var path = FindMasterDataFile("ItemMaster.json");
        if (path is null) return;

        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return;

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using var command = new NpgsqlCommand(
                """
                insert into wds_static_item (
                  item_master_id,
                  item_type,
                  name,
                  payload
                )
                values ($1, $2, $3, $4::jsonb)
                on conflict (item_master_id)
                do update set
                  item_type = excluded.item_type,
                  name = excluded.name,
                  payload = excluded.payload,
                  updated_at = now()
                """,
                connection,
                transaction);

            var itemMasterIdParameter = command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint });
            var itemTypeParameter = command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer });
            var nameParameter = command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text });
            var payloadParameter = command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb });

            foreach (var row in document.RootElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 2) continue;
                if (!row[0].TryGetInt64(out var itemMasterId)) continue;

                itemMasterIdParameter.Value = itemMasterId;
                itemTypeParameter.Value = row.GetArrayLength() > 6 && row[6].TryGetInt32(out var itemType) ? itemType : 0;
                nameParameter.Value = row[1].ValueKind == JsonValueKind.String ? row[1].GetString() ?? "" : "";
                payloadParameter.Value = row.GetRawText();
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            if (transaction.Connection is not null)
            {
                await transaction.RollbackAsync();
            }
            throw;
        }
    }

    const string Sql = """
    do $$
    begin
      if to_regclass('public.user_accounts') is null and to_regclass('public.users') is not null then
        alter table users rename to user_accounts;
      end if;
      if to_regclass('public.system_invite_codes') is null and to_regclass('public.invites') is not null then
        alter table invites rename to system_invite_codes;
      end if;
      if to_regclass('public.user_auth_sessions') is null and to_regclass('public.auth_sessions') is not null then
        alter table auth_sessions rename to user_auth_sessions;
      end if;
      if to_regclass('public.user_login_identities') is null and to_regclass('public.local_login_identities') is not null then
        alter table local_login_identities rename to user_login_identities;
      end if;
      if to_regclass('public.system_live_sessions') is null and to_regclass('public.live_sessions') is not null then
        alter table live_sessions rename to system_live_sessions;
      end if;
      if to_regclass('public.system_multi_rooms') is null and to_regclass('public.multi_rooms') is not null then
        alter table multi_rooms rename to system_multi_rooms;
      end if;
      if to_regclass('public.wds_static_master_tables') is null and to_regclass('public.static_master_tables') is not null then
        alter table static_master_tables rename to wds_static_master_tables;
      end if;
      if to_regclass('public.wds_static_master_rows') is null and to_regclass('public.static_master_rows') is not null then
        alter table static_master_rows rename to wds_static_master_rows;
      end if;
      if to_regclass('public.user_character_cards') is null and to_regclass('public.user_characters') is not null then
        alter table user_characters rename to user_character_cards;
      end if;
      if to_regclass('public.user_character_costumes') is null and to_regclass('public.user_costumes') is not null then
        alter table user_costumes rename to user_character_costumes;
      end if;
      if to_regclass('public.user_item_currencies') is null and to_regclass('public.user_currencies') is not null then
        alter table user_currencies rename to user_item_currencies;
      end if;
      if to_regclass('public.user_item_inbox_packages') is null and to_regclass('public.user_inbox_packages') is not null then
        alter table user_inbox_packages rename to user_item_inbox_packages;
      end if;
    end $$;

    create table if not exists user_accounts (
      id bigserial primary key,
      numeric_id integer,
      public_id text not null unique,
      name text not null,
      password_hash text not null,
      role text not null default 'player',
      banned_until timestamptz,
      created_at timestamptz not null default now(),
      updated_at timestamptz not null default now()
    );

    alter table user_accounts add column if not exists numeric_id integer;

    update user_accounts
    set numeric_id = (100000 + (id % 900000))::integer
    where numeric_id is null;

    create unique index if not exists users_numeric_id_key on user_accounts(numeric_id);

    create table if not exists wds_static_character (
      character_master_id bigint primary key,
      character_base_master_id bigint not null,
      costume_master_id bigint,
      display_order integer not null default 0,
      payload jsonb not null default '{}'::jsonb,
      created_at timestamptz not null default now(),
      updated_at timestamptz not null default now()
    );

    create table if not exists wds_static_item (
      item_master_id bigint primary key,
      item_type integer not null default 0,
      name text not null default '',
      payload jsonb not null default '{}'::jsonb,
      created_at timestamptz not null default now(),
      updated_at timestamptz not null default now()
    );

    create table if not exists wds_static_master_tables (
      table_name text primary key,
      source_version text not null,
      source_file text not null default '',
      block_type text not null default '',
      index_offset bigint,
      index_length bigint,
      row_count integer not null default 0,
      manifest jsonb not null default '{}'::jsonb,
      imported_at timestamptz not null default now(),
      created_at timestamptz not null default now(),
      updated_at timestamptz not null default now()
    );

    create table if not exists wds_static_master_rows (
      table_name text not null references wds_static_master_tables(table_name) on delete cascade,
      row_index integer not null,
      master_id bigint,
      payload jsonb not null,
      created_at timestamptz not null default now(),
      primary key (table_name, row_index)
    );

    create index if not exists wds_static_master_rows_table_master_id_idx
      on wds_static_master_rows(table_name, master_id);

    create table if not exists system_master_data_versions (
      id bigserial primary key,
      version text not null unique,
      publish_timestamp bigint not null,
      remote_path text not null,
      local_file_path text not null,
      sas_token text not null default '',
      is_active boolean not null default false,
      created_at timestamptz not null default now(),
      updated_at timestamptz not null default now()
    );

    create unique index if not exists system_master_data_versions_active_key
      on system_master_data_versions(is_active)
      where is_active;

    update user_accounts
    set public_id = (floor(random() * 9000000000) + 1000000000)::bigint::text
    where public_id !~ '^[0-9]{10}$';

    create table if not exists system_invite_codes (
      code text primary key,
      max_uses integer not null default 1,
      used_count integer not null default 0,
      "createdBy" bigint references user_accounts(id),
      expires_at timestamptz,
      created_at timestamptz not null default now()
    );

    create table if not exists user_states (
      "userId" bigint primary key references user_accounts(id) on delete cascade,
      state jsonb not null,
      updated_at timestamptz not null default now()
    );

    update user_states
    set state = jsonb_set(state, '{publicId}', to_jsonb(user_accounts.public_id), true)
    from user_accounts
    where user_states."userId" = user_accounts.id
      and user_states.state ->> 'publicId' is distinct from user_accounts.public_id;

    create table if not exists user_game_states (
      "userId" bigint primary key references user_accounts(id) on delete cascade,
      rank integer not null default 1,
      exp integer not null default 0,
      stamina integer not null default 100,
      max_stamina integer not null default 100,
      stamina_recovered_at timestamptz not null default now(),
      free_jewel integer not null default 0,
      paid_jewel integer not null default 0,
      coin integer not null default 0,
      last_login_at timestamptz not null default now(),
      splash_last_displayed_at timestamptz not null default now(),
      tutorial_status integer not null default 99,
      game_start_at timestamptz not null default now(),
      home_last_transition_at timestamptz not null default now(),
      notification_read_at timestamptz not null default now(),
      is_tutorial_finished boolean not null default true,
      has_unread_game_hint boolean not null default false,
      updated_at timestamptz not null default now()
    );

    alter table user_game_states
      add column if not exists stamina_jewel_recovery_count integer not null default 0;

    alter table user_game_states
      add column if not exists rank_limit integer not null default 50;

    create table if not exists user_live_sessions (
      "userId" bigint primary key references user_accounts(id) on delete cascade,
      live_master_id bigint not null,
      is_auto boolean not null,
      use_stamina boolean not null,
      completion bytea,
      finish_hash bytea,
      started_at timestamptz not null default now()
    );

    create table if not exists user_profiles (
      "userId" bigint primary key references user_accounts(id) on delete cascade,
      display_name text not null,
      comment text not null default '',
      favorite_character_id bigint,
      favorite_character_master_id bigint,
      title_master_id bigint,
      nameplate_master_id bigint,
      icon_frame_master_id bigint,
      birthday double precision,
      support_character_id bigint,
      is_public boolean not null default true,
      total_power integer not null default 0,
      honor_point integer not null default 0,
      accepts_friend_requests boolean not null default true,
      selected_poster_id bigint,
      home_character_master_id bigint,
      is_home_character_illust boolean not null default false,
      show_home_character boolean not null default true,
      name_base_color_master_id bigint not null default 0,
      name_color_master_id bigint not null default 0,
      home_skin_master_id bigint,
      updated_at timestamptz not null default now()
    );

    alter table user_profiles
      add column if not exists updated_at timestamptz not null default now();

    create table if not exists user_friend_relationships (
      id bigserial primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      "friendUserId" bigint not null references user_accounts(id) on delete cascade,
      is_favorite boolean not null default false,
      created_at timestamptz not null default now(),
      updated_at timestamptz not null default now(),
      unique("userId", "friendUserId"),
      check ("userId" <> "friendUserId")
    );

    create index if not exists user_friend_relationships_friend_idx
      on user_friend_relationships("friendUserId");

    create table if not exists user_friend_requests (
      id bigserial primary key,
      "fromUserId" bigint not null references user_accounts(id) on delete cascade,
      "toUserId" bigint not null references user_accounts(id) on delete cascade,
      status text not null default 'pending',
      message text not null default '',
      requested_at timestamptz not null default now(),
      responded_at timestamptz,
      created_at timestamptz not null default now(),
      updated_at timestamptz not null default now(),
      check ("fromUserId" <> "toUserId"),
      check (status in ('pending', 'accepted', 'denied', 'cancelled'))
    );

    create unique index if not exists user_friend_requests_pending_pair_key
      on user_friend_requests("fromUserId", "toUserId")
      where status = 'pending';

    create index if not exists user_friend_requests_to_status_idx
      on user_friend_requests("toUserId", status, requested_at desc);

    create index if not exists user_friend_requests_from_status_idx
      on user_friend_requests("fromUserId", status, requested_at desc);

    create table if not exists user_friend_blocks (
      id bigserial primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      "blockedUserId" bigint not null references user_accounts(id) on delete cascade,
      reason text not null default '',
      created_at timestamptz not null default now(),
      unique("userId", "blockedUserId"),
      check ("userId" <> "blockedUserId")
    );

    create index if not exists user_friend_blocks_blocked_idx
      on user_friend_blocks("blockedUserId");

    create table if not exists user_home_display_preferences (
      "userId" bigint primary key references user_accounts(id) on delete cascade,
      character_base_master_id bigint not null,
      illustration_character_base_master_id bigint,
      sub_character_base_master_id_1 bigint not null,
      sub_character_base_master_id_2 bigint not null,
      sub_character_base_master_id_3 bigint not null,
      costume_master_id bigint not null,
      illustration_costume_master_id bigint,
      sub_costume_master_id_1 bigint not null,
      sub_costume_master_id_2 bigint not null,
      sub_costume_master_id_3 bigint not null,
      character_master_id bigint not null,
      is_illust boolean not null default false,
      display_type integer not null default 0,
      selected_character_base_master_id bigint not null,
      selected_costume_master_id bigint not null
    );

    create table if not exists user_player_preferences (
      "userId" bigint primary key references user_accounts(id) on delete cascade,
      selected_party_id bigint,
      payload jsonb not null default '{}'::jsonb,
      updated_at timestamptz not null default now()
    );

    create table if not exists user_party_groups (
      id bigint primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      display_index integer not null,
      name text not null,
      party_type integer not null default 1,
      created_at timestamptz not null default now(),
      updated_at timestamptz not null default now(),
      unique("userId", display_index)
    );

    create table if not exists user_party_slots (
      id bigint primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      party_id bigint not null,
      slot_number integer not null,
      character_card_id bigint,
      accessory_id bigint,
      poster_id bigint,
      position integer not null default 0,
      created_at timestamptz not null default now(),
      updated_at timestamptz not null default now(),
      unique(party_id, slot_number)
    );

    create table if not exists user_character_resource_progress (
      id bigint primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      character_base_master_id bigint not null,
      resource_type integer not null,
      resource_master_id bigint not null,
      level integer not null default 1,
      exp integer not null default 0,
      rank integer not null default 0,
      progress integer not null default 0,
      created_at timestamptz not null default now(),
      updated_at timestamptz not null default now(),
      unique("userId", character_base_master_id, resource_type)
    );

    create table if not exists user_mission_statuses (
      id bigint primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      is_cleared boolean not null default false,
      is_received boolean not null default false,
      progress integer not null default 0,
      completed_at timestamptz,
      mission_from integer not null,
      mission_to integer not null,
      created_at timestamptz not null default now(),
      updated_at timestamptz not null default now(),
      unique("userId", mission_from, mission_to)
    );

    create table if not exists user_live_user_stats (
      "userId" bigint primary key references user_accounts(id) on delete cascade,
      score_rating double precision not null default 0,
      technical_rating double precision not null default 0,
      updated_at timestamptz not null default now()
    );

    create table if not exists user_music_bookmarks (
      id bigint primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      music_master_id bigint not null,
      bookmark_state integer not null default 0,
      updated_at timestamptz not null default now(),
      unique("userId", music_master_id)
    );

    create table if not exists user_player_settings (
      "userId" bigint primary key references user_accounts(id) on delete cascade,
      id bigint not null unique,
      display_quality integer not null default 0,
      download_category integer not null default 36,
      is_push_enabled boolean not null default false,
      push_start_at timestamptz,
      push_end_at timestamptz,
      is_live_push_enabled boolean not null default false,
      payload jsonb not null default '{}'::jsonb,
      updated_at timestamptz not null default now()
    );

    create table if not exists user_restriction_states (
      "userId" bigint primary key references user_accounts(id) on delete cascade,
      restricted_until timestamptz,
      reason text,
      updated_at timestamptz not null default now()
    );

    create table if not exists user_album_groups (
      "userId" bigint not null references user_accounts(id) on delete cascade,
      album_index integer not null,
      name text not null,
      display_order integer not null,
      updated_at timestamptz not null default now(),
      primary key ("userId", album_index)
    );

    create table if not exists user_invite_states (
      "userId" bigint primary key references user_accounts(id) on delete cascade,
      invited_count integer not null default 0,
      invite_code text not null,
      is_reward_received boolean not null default false,
      updated_at timestamptz not null default now()
    );

    create table if not exists user_login_bonus_states (
      id bigint primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      current_count integer not null default 0,
      total_count integer not null default 0,
      shown_at timestamptz not null default now(),
      status integer not null default 0,
      created_at timestamptz not null default now(),
      updated_at timestamptz not null default now(),
      unique("userId")
    );

    create table if not exists user_daily_limits (
      id bigint primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      auto_play_times integer not null default 0 check (auto_play_times >= 0),
      daily_lesson_times integer not null default 1 check (daily_lesson_times >= 0),
      last_refreshed_at timestamptz not null,
      music_course_free_challenge_times integer not null default 0 check (music_course_free_challenge_times >= 0),
      updated_at timestamptz not null default now(),
      unique("userId")
    );

    create table if not exists user_notification_states (
      "userId" bigint primary key references user_accounts(id) on delete cascade,
      notification_read_at timestamptz,
      notice_read_at timestamptz,
      present_read_at timestamptz,
      updated_at timestamptz not null default now()
    );

    create table if not exists user_game_hint_reads (
      id bigint primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      category integer not null,
      is_read boolean not null default true,
      read_at timestamptz not null default now(),
      updated_at timestamptz not null default now(),
      unique("userId", category)
    );

    create table if not exists user_shop_last_views (
      id bigserial primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      shop_category integer not null default 0,
      shop_master_id bigint,
      view_key text not null,
      last_viewed_at timestamptz not null default now(),
      updated_at timestamptz not null default now(),
      unique("userId", view_key)
    );

    create table if not exists user_live_play_results (
      id bigint primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      live_master_id bigint not null,
      difficulty integer not null default 0,
      best_score double precision,
      best_technical_score double precision,
      clear_count integer not null default 0,
      full_combo_count integer not null default 0,
      all_perfect_count integer not null default 0,
      payload jsonb not null default '{}'::jsonb,
      played_at timestamptz not null default now(),
      updated_at timestamptz not null default now(),
      unique("userId", live_master_id, difficulty)
    );

    alter table user_live_play_results
      add column if not exists achievement_rate double precision not null default 0,
      add column if not exists notation_rate double precision not null default 0,
      add column if not exists clear_lamp integer not null default 0,
      add column if not exists rate_grade integer not null default 0;

    create table if not exists user_live_music_states (
      id bigint primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      music_master_id bigint not null,
      unlocked_at timestamptz,
      is_unlocked boolean not null default true,
      payload jsonb not null default '{}'::jsonb,
      updated_at timestamptz not null default now(),
      unique("userId", music_master_id)
    );

    create table if not exists user_live_lesson_party_states (
      "userId" bigint not null references user_accounts(id) on delete cascade,
      lesson_master_id bigint not null,
      slots jsonb not null default '[]'::jsonb,
      payload jsonb not null default '{}'::jsonb,
      updated_at timestamptz not null default now(),
      primary key ("userId", lesson_master_id)
    );

    create table if not exists user_live_course_states (
      id bigint primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      course_master_id bigint not null,
      status integer not null default 0,
      progress integer not null default 0,
      payload jsonb not null default '{}'::jsonb,
      updated_at timestamptz not null default now(),
      unique("userId", course_master_id)
    );

    create table if not exists user_story_event_states (
      id bigint primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      event_master_id bigint not null,
      point bigint not null default 0,
      rank integer not null default 0,
      is_tips_read boolean not null default false,
      payload jsonb not null default '{}'::jsonb,
      updated_at timestamptz not null default now(),
      unique("userId", event_master_id)
    );

    create table if not exists user_dugong_run_states (
      id bigint primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      event_master_id bigint not null default 50000,
      cleared_course_ids integer[] not null default '{}'::integer[],
      no_mistake_course_ids integer[] not null default '{}'::integer[],
      status integer not null default 1,
      updated_at timestamptz not null default now(),
      unique("userId", event_master_id)
    );

    create table if not exists user_event_exchange_shop_states (
      id bigint primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      exchange_shop_thing_id bigint not null,
      exchanged_count integer not null default 0,
      reset_count integer not null default 0,
      payload jsonb not null default '{}'::jsonb,
      updated_at timestamptz not null default now(),
      unique("userId", exchange_shop_thing_id)
    );

    create table if not exists user_shop_purchase_states (
      id bigint primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      shop_item_id bigint not null,
      purchased_count integer not null default 0,
      purchase_limit integer,
      reset_at timestamptz,
      payload jsonb not null default '{}'::jsonb,
      updated_at timestamptz not null default now(),
      unique("userId", shop_item_id)
    );

    create table if not exists user_episode_read_states (
      "userId" bigint not null references user_accounts(id) on delete cascade,
      episode_master_id bigint not null,
      is_new boolean not null default false,
      read_at timestamptz not null default now(),
      payload jsonb not null default '{}'::jsonb,
      updated_at timestamptz not null default now(),
      primary key ("userId", episode_master_id)
    );

    create table if not exists user_photo_records (
      id bigint primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      file_name text not null,
      read_sas_token text,
      write_sas_token text,
      is_favorite boolean not null default false,
      album_group_id bigint,
      source_type integer not null default 0,
      source_master_id bigint,
      payload jsonb not null default '{}'::jsonb,
      created_at timestamptz not null default now(),
      updated_at timestamptz not null default now()
    );

    create table if not exists user_photo_states (
      id bigint primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      photo_category integer not null default 0,
      current_count integer not null default 0,
      max_count integer not null default 0,
      payload jsonb not null default '{}'::jsonb,
      updated_at timestamptz not null default now(),
      unique("userId", photo_category)
    );

    create table if not exists user_photo_album_arrangements (
      id bigint primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      album_group_id bigint not null,
      arrangement_type integer not null,
      is_public boolean not null default true,
      layout_payload bytea not null,
      extra_payload jsonb not null default '{}'::jsonb,
      updated_at timestamptz not null default now(),
      unique("userId", album_group_id, arrangement_type)
    );

    create table if not exists user_music_video_watch_states (
      id bigint primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      music_video_id bigint not null,
      watched_at timestamptz not null default now(),
      payload jsonb not null default '{}'::jsonb,
      updated_at timestamptz not null default now(),
      unique("userId", music_video_id)
    );

    create table if not exists user_raw_union_states (
      "userId" bigint not null references user_accounts(id) on delete cascade,
      union_key integer not null,
      object_key text not null,
      payload jsonb not null,
      updated_at timestamptz not null default now(),
      primary key ("userId", union_key, object_key)
    );

    alter table user_home_display_preferences
      alter column sub_costume_master_id_1 drop not null,
      alter column sub_costume_master_id_2 drop not null,
      alter column sub_costume_master_id_3 drop not null;

    create table if not exists user_character_cards (
      id bigint primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      character_master_id bigint not null,
      rarity integer not null default 1,
      level integer not null default 1,
      exp integer not null default 0,
      skill_level integer not null default 1,
      character_base_id bigint not null,
      rank integer not null default 1,
      awakening integer not null default 0,
      friendship_level integer not null default 0,
      is_locked boolean not null default false,
      duplicated_at timestamptz,
      extra_1 integer not null default 0,
      extra_2 integer not null default 0,
      is_new boolean not null default false,
      unique("userId", character_master_id)
    );

    create table if not exists user_character_bases (
      id bigint primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      character_base_master_id bigint not null,
      level integer not null default 1,
      exp integer not null default 0,
      costume_master_id bigint not null,
      rank integer not null default 0,
      selected_character_id bigint,
      is_new boolean not null default false,
      unique("userId", character_base_master_id)
    );

    create table if not exists user_character_costumes (
      id bigint primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      costume_master_id bigint not null,
      unique("userId", costume_master_id)
    );

    create table if not exists user_item_currencies (
      "userId" bigint primary key references user_accounts(id) on delete cascade,
      free_jewel integer not null default 10000,
      paid_jewel integer not null default 0,
      coin integer not null default 0
    );

    create table if not exists user_gacha_histories (
      id bigserial primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      gacha_detail_master_id bigint not null,
      thing_type integer not null,
      thing_master_id bigint not null,
      quantity integer not null default 1,
      payload jsonb not null default '{}'::jsonb,
      rolled_at timestamptz not null default now()
    );

    create index if not exists user_gacha_histories_user_rolled_idx
      on user_gacha_histories("userId", rolled_at desc);

    create index if not exists user_gacha_histories_user_thing_type_idx
      on user_gacha_histories("userId", thing_type, rolled_at desc);

    create table if not exists user_home_bgms (
      "userId" bigint primary key references user_accounts(id) on delete cascade,
      home_bgm_master_id bigint not null,
      selection_type integer not null default 1,
      home_bgm_detail_master_id bigint not null
    );

    create table if not exists user_home_skins (
      "userId" bigint primary key references user_accounts(id) on delete cascade,
      selected_home_skin_master_id bigint not null
    );

    create table if not exists user_home_skin_possessions (
      "userId" bigint not null references user_accounts(id) on delete cascade,
      home_skin_master_id bigint not null,
      primary key ("userId", home_skin_master_id)
    );

    create table if not exists user_item_inbox_packages (
      id bigserial primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      payload jsonb not null default '{}'::jsonb,
      received_at timestamptz,
      expires_at timestamptz,
      created_at timestamptz not null default now(),
      updated_at timestamptz not null default now()
    );

    alter table user_item_inbox_packages
      add column if not exists updated_at timestamptz not null default now();

    create table if not exists user_notifications (
      id bigserial primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      payload jsonb not null default '{}'::jsonb,
      read_at timestamptz,
      created_at timestamptz not null default now()
    );

    create table if not exists system_notifications (
      id bigserial primary key,
      title text not null,
      body text not null default '[]',
      banner_path text not null default '',
      posting_at timestamptz not null default now(),
      last_updated_at timestamptz not null default now(),
      starts_at timestamptz not null default now(),
      ends_at timestamptz,
      notification_tab_category integer not null default 1,
      notification_category integer not null default 1,
      is_confirmation boolean not null default false,
      display_order integer not null default 0,
      created_at timestamptz not null default now(),
      updated_at timestamptz not null default now()
    );

    update system_notifications
    set body = '[]'
    where trim(body) = '';

    update system_notifications
    set body = jsonb_build_array(jsonb_build_object('Element', 4, 'Data', body))::text
    where trim(body) <> ''
      and trim(body) !~ '^[\[\{]';

    create table if not exists user_notification_reads (
      "userId" bigint not null references user_accounts(id) on delete cascade,
      notification_id bigint not null references system_notifications(id) on delete cascade,
      read_at timestamptz not null default now(),
      primary key ("userId", notification_id)
    );

    create table if not exists user_auth_sessions (
      id bigserial primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      token_hash text not null,
      expires_at timestamptz not null,
      created_at timestamptz not null default now()
    );

    create table if not exists user_login_identities (
      login_token_hash text primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      game_version text not null,
      apk_hash text,
      apk_application_signature text,
      application_version text,
      first_seen_at timestamptz not null default now(),
      last_seen_at timestamptz not null default now()
    );

    create table if not exists system_live_sessions (
      id bigserial primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      live_master_id bigint not null,
      party_id bigint,
      status text not null,
      payload jsonb not null,
      result jsonb,
      created_at timestamptz not null default now(),
      finished_at timestamptz
    );

    create table if not exists system_multi_rooms (
      id bigserial primary key,
      hashed_room_id text not null unique,
      "ownerUserId" bigint not null references user_accounts(id) on delete cascade,
      name text not null,
      password_hash text,
      live_master_id bigint not null,
      play_mode integer not null default 0,
      max_members integer not null default 5,
      status text not null default 'open',
      state jsonb not null,
      created_at timestamptz not null default now(),
      updated_at timestamptz not null default now()
    );

    create table if not exists user_item_possessions (
      id bigint primary key,
      "userId" bigint not null references user_accounts(id) on delete cascade,
      item_master_id bigint not null,
      quantity integer not null default 0,
      created_at timestamptz not null default now(),
      updated_at timestamptz not null default now(),
      unique("userId", item_master_id)
    );

    do $$
    begin
      if not exists (select 1 from pg_constraint where conname = 'user_character_cards_character_base_fk') then
        alter table user_character_cards
          add constraint user_character_cards_character_base_fk
          foreign key (character_base_id)
          references user_character_bases(id)
          on delete cascade
          not valid;
      end if;

      if not exists (select 1 from pg_constraint where conname = 'user_profiles_favorite_character_fk') then
        alter table user_profiles
          add constraint user_profiles_favorite_character_fk
          foreign key (favorite_character_id)
          references user_character_cards(id)
          on delete set null
          not valid;
      end if;

      if not exists (select 1 from pg_constraint where conname = 'user_profiles_selected_poster_fk') then
        alter table user_profiles
          add constraint user_profiles_selected_poster_fk
          foreign key (selected_poster_id)
          references user_character_cards(id)
          on delete set null
          not valid;
      end if;
    end $$;

    insert into user_game_states ("userId", free_jewel, coin)
    select id, 999, 10000 from user_accounts
    on conflict ("userId") do nothing;

    update user_game_states
    set tutorial_status = 99,
        is_tutorial_finished = true,
        updated_at = now()
    where tutorial_status <> 99
       or not is_tutorial_finished;

    insert into user_profiles (
      "userId",
      display_name,
      favorite_character_master_id,
      home_character_master_id,
      home_skin_master_id
    )
    select id, name, 110010, 110010, 200101 from user_accounts
    on conflict ("userId") do nothing;

    insert into user_home_display_preferences (
      "userId",
      character_base_master_id,
      sub_character_base_master_id_1,
      sub_character_base_master_id_2,
      sub_character_base_master_id_3,
      costume_master_id,
      sub_costume_master_id_1,
      sub_costume_master_id_2,
      sub_costume_master_id_3,
      character_master_id,
      selected_character_base_master_id,
      selected_costume_master_id
    )
    select id, 101, 101, 101, 101, 11, 11, 11, 11, 110010, 101, 11 from user_accounts
    on conflict ("userId") do nothing;

    insert into user_character_bases (
      id,
      "userId",
      character_base_master_id,
      costume_master_id,
      selected_character_id
    )
    select id * 1000000 + 1, id, 101, 11, id * 1000000 + 1 from user_accounts
    on conflict (id) do nothing;

    insert into user_character_cards (
      id,
      "userId",
      character_master_id,
      character_base_id
    )
    select id * 1000000 + 1, id, 110010, id * 1000000 + 1 from user_accounts
    on conflict (id) do nothing;

    insert into user_character_costumes (id, "userId", costume_master_id)
    select id * 1000000 + 1, id, 11 from user_accounts
    on conflict (id) do nothing;

    insert into user_item_currencies ("userId", free_jewel, paid_jewel, coin)
    select id, 10000, 0, 0 from user_accounts
    on conflict ("userId") do nothing;

    insert into user_home_bgms ("userId", home_bgm_master_id, selection_type, home_bgm_detail_master_id)
    select id, 1, 1, 1001 from user_accounts
    on conflict ("userId") do nothing;

    insert into user_home_skins ("userId", selected_home_skin_master_id)
    select id, 200101 from user_accounts
    on conflict ("userId") do nothing;

    insert into user_home_skin_possessions ("userId", home_skin_master_id)
    select id, 200101 from user_accounts
    on conflict ("userId", home_skin_master_id) do nothing;

    insert into user_item_possessions (id, "userId", item_master_id, quantity)
    select user_accounts.id * 1000000 + bootstrap_items.item_master_id,
           user_accounts.id,
           bootstrap_items.item_master_id,
           bootstrap_items.quantity
    from user_accounts
    cross join (
      values
        (111103::bigint, 5),
        (120002::bigint, 500),
        (130001::bigint, 50),
        (130041::bigint, 100),
        (130042::bigint, 100),
        (130043::bigint, 100),
        (130051::bigint, 10),
        (130061::bigint, 100),
        (130062::bigint, 100),
        (130063::bigint, 100),
        (130064::bigint, 100),
        (150000::bigint, 250),
        (151001::bigint, 10),
        (151002::bigint, 10),
        (151003::bigint, 10),
        (151004::bigint, 10),
        (151005::bigint, 10),
        (151006::bigint, 10),
        (151007::bigint, 10),
        (151008::bigint, 10),
        (151009::bigint, 10),
        (151010::bigint, 10),
        (151011::bigint, 10),
        (151012::bigint, 10),
        (151013::bigint, 10),
        (151014::bigint, 10),
        (151015::bigint, 10),
        (151016::bigint, 10),
        (151017::bigint, 10),
        (151018::bigint, 10),
        (151019::bigint, 10),
        (151020::bigint, 10),
        (151021::bigint, 10),
        (210001::bigint, 12),
        (210424::bigint, 1),
        (210446::bigint, 1),
        (211432::bigint, 1),
        (220101::bigint, 5),
        (221101::bigint, 5)
    ) as bootstrap_items(item_master_id, quantity)
    on conflict ("userId", item_master_id) do nothing;
    """;
}


