using Npgsql;

static class SocialRealtimeSchema
{
    public static async Task MigrateAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            create table if not exists circle_realtime_chats (
              id bigint generated always as identity primary key,
              circle_id text not null references system_circles(id) on delete cascade,
              sender_id bigint not null references user_accounts(id) on delete cascade,
              payload bytea not null, sent_at timestamptz not null default now(), deleted boolean not null default false
            );
            create index if not exists circle_realtime_chats_history on circle_realtime_chats(circle_id,id desc);
            create index if not exists circle_realtime_chats_sender on circle_realtime_chats(sender_id,sent_at desc);
            create table if not exists circle_realtime_reads (
              circle_id text not null references system_circles(id) on delete cascade,
              user_id bigint not null references user_accounts(id) on delete cascade,
              last_read_id bigint not null check(last_read_id>=0), primary key(circle_id,user_id)
            );
            create table if not exists circle_realtime_activities (
              id bigint generated always as identity primary key,
              circle_id text not null references system_circles(id) on delete cascade,
              user_id bigint not null references user_accounts(id) on delete cascade,
              public_id text not null, user_name text not null, character_id bigint not null,
              log_type integer not null, log_value text, logged_at timestamptz not null default now()
            );
            alter table circle_realtime_activities add column if not exists display_awakening boolean not null default false;
            create index if not exists circle_realtime_activities_history on circle_realtime_activities(circle_id,id desc);
            create table if not exists realtime_friend_invitations (
              recipient_id bigint not null references user_accounts(id) on delete cascade,
              sender_id bigint not null references user_accounts(id) on delete cascade,
              hall_id text not null, payload bytea not null, invited_at timestamptz not null default now(),
              primary key(recipient_id,sender_id,hall_id)
            );
            create or replace function circle_record_membership_activity() returns trigger language plpgsql as $fn$
            declare member record; kind integer;
            begin
              if TG_OP='DELETE' then member:=OLD; kind:=2;
              elsif TG_OP='UPDATE' then
                if NEW.authority=OLD.authority then return NEW; end if;
                member:=NEW; kind:=3;
              else member:=NEW; kind:=1; end if;
              insert into circle_realtime_activities(circle_id,user_id,public_id,user_name,character_id,log_type,log_value,display_awakening)
              select member.circle_id,a.id,a.public_id,coalesce(p.display_name,a.name),
                coalesce(p.favorite_character_master_id,110010),kind,
                case when kind=3 then member.authority::text else null end,coalesce(b.portal_display_awakening_status,false)
              from user_accounts a left join user_profiles p on p."userId"=a.id
              left join user_character_cards c on c.id=p.favorite_character_id and c."userId"=a.id
              left join user_character_bases b on b.id=c.character_base_id and b."userId"=a.id
              where a.id=member."userId" and exists(select 1 from system_circles where id=member.circle_id);
              if TG_OP='DELETE' then
                delete from circle_realtime_reads where circle_id=OLD.circle_id and user_id=OLD."userId";
                return OLD;
              end if;
              return NEW;
            end; $fn$;
            create or replace trigger circle_membership_activity after insert or update of authority or delete
              on user_circle_memberships for each row execute function circle_record_membership_activity();
            """, connection);
        await command.ExecuteNonQueryAsync();
    }
}
