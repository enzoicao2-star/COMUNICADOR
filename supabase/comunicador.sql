-- COMUNICADOR 2.5 - execute todo este arquivo no SQL Editor do Supabase.
-- Depois ative Authentication > Providers > Anonymous Sign-Ins.
create extension if not exists pgcrypto;

create table if not exists public.devices (
    device_id uuid primary key,
    auth_user_id uuid not null unique references auth.users(id) on delete cascade,
    machine_name text not null check (char_length(machine_name) between 1 and 100),
    panel_version text, receiver_version text,
    media_blocked boolean not null default false,
    created_at timestamptz not null default now(), last_seen_at timestamptz not null default now()
);
create table if not exists public.system_settings (
    singleton boolean primary key default true check (singleton),
    admin_device_id uuid references public.devices(device_id) on delete set null,
    admin_password_hash text, updated_at timestamptz not null default now()
);
insert into public.system_settings(singleton) values (true) on conflict do nothing;
create table if not exists public.panel_profiles (
    device_id uuid primary key references public.devices(device_id) on delete cascade,
    display_name text not null check (char_length(display_name) between 1 and 60),
    badges jsonb not null default '[]'::jsonb, updated_at timestamptz not null default now(),
    constraint panel_profiles_badges_array check (jsonb_typeof(badges) = 'array'),
    constraint panel_profiles_badges_limit check (jsonb_array_length(badges) <= 4)
);
create table if not exists public.deliveries (
    id uuid primary key default gen_random_uuid(),
    sender_device_id uuid not null references public.devices(device_id) on delete cascade,
    target_device_id uuid not null references public.devices(device_id) on delete cascade,
    deliver_at timestamptz not null default now(), payload jsonb not null,
    status text not null default 'pending' check (status in ('pending','delivered','responded','failed')),
    response_text text, created_at timestamptz not null default now(),
    delivered_at timestamptz, responded_at timestamptz, sender_notified_at timestamptz
);
alter table public.deliveries add column if not exists sender_notified_at timestamptz;
create index if not exists panel_profiles_updated_at_idx on public.panel_profiles(updated_at desc);
create index if not exists deliveries_target_pending_idx on public.deliveries(target_device_id,status,deliver_at);
create index if not exists deliveries_sender_created_idx on public.deliveries(sender_device_id,created_at desc);
create index if not exists system_settings_admin_device_idx on public.system_settings(admin_device_id);

create or replace function public.current_device_id()
returns uuid language sql stable security definer set search_path=''
as $$ select d.device_id from public.devices d where d.auth_user_id=(select auth.uid()) limit 1 $$;
create or replace function public.current_device_is_admin()
returns boolean language sql stable security definer set search_path=''
as $$ select exists(select 1 from public.system_settings s where s.singleton and s.admin_device_id=public.current_device_id()) $$;

create or replace function public.register_device(p_device_id uuid,p_machine_name text,p_panel_version text,p_receiver_version text)
returns table(device_id uuid,is_admin boolean,admin_device_id uuid)
language plpgsql security definer set search_path=''
as $$
declare existing_owner uuid;
begin
    if (select auth.uid()) is null then raise exception 'authentication required'; end if;
    select d.auth_user_id into existing_owner from public.devices d where d.device_id=p_device_id;
    if existing_owner is not null and existing_owner<>(select auth.uid()) then raise exception 'device id already registered'; end if;
    insert into public.devices(device_id,auth_user_id,machine_name,panel_version,receiver_version)
    values(p_device_id,(select auth.uid()),left(p_machine_name,100),p_panel_version,p_receiver_version)
    on conflict on constraint devices_pkey do update set machine_name=excluded.machine_name,
      panel_version=coalesce(excluded.panel_version,public.devices.panel_version),
      receiver_version=coalesce(excluded.receiver_version,public.devices.receiver_version),last_seen_at=now();
    return query select p_device_id,coalesce(s.admin_device_id=p_device_id,false),s.admin_device_id
      from public.system_settings s where s.singleton;
end $$;

create or replace function public.admin_login_toggle(p_password text)
returns table(status text,is_admin boolean,admin_device_id uuid)
language plpgsql security definer set search_path=''
as $$
declare current_id uuid; settings public.system_settings%rowtype;
begin
    current_id:=public.current_device_id();
    if current_id is null then raise exception 'device not registered'; end if;
    select * into settings from public.system_settings where singleton for update;
    if p_password is null or char_length(p_password)<8 then
      return query select 'password_too_short'::text,false,settings.admin_device_id; return;
    end if;
    if settings.admin_password_hash is null then
      update public.system_settings set admin_password_hash=crypt(p_password,gen_salt('bf',12)),admin_device_id=current_id,updated_at=now() where singleton;
      return query select 'created'::text,true,current_id; return;
    end if;
    if crypt(p_password,settings.admin_password_hash)<>settings.admin_password_hash then
      return query select 'invalid_password'::text,false,settings.admin_device_id; return;
    end if;
    if settings.admin_device_id=current_id then
      update public.system_settings set admin_device_id=null,updated_at=now() where singleton;
      return query select 'disabled'::text,false,null::uuid; return;
    end if;
    update public.system_settings set admin_device_id=current_id,updated_at=now() where singleton;
    return query select 'transferred'::text,true,current_id;
end $$;
create or replace function public.get_admin_state()
returns table(is_admin boolean,admin_device_id uuid)
language sql stable security definer set search_path=''
as $$ select coalesce(s.admin_device_id=public.current_device_id(),false),s.admin_device_id from public.system_settings s where s.singleton $$;
create or replace function public.acknowledge_response(p_delivery_id uuid)
returns void language plpgsql security definer set search_path=''
as $$
begin
  update public.deliveries set sender_notified_at=now()
  where id=p_delivery_id and sender_device_id=public.current_device_id() and status='responded';
end $$;
create or replace function public.set_updated_at()
returns trigger language plpgsql security invoker set search_path=''
as $$ begin new.updated_at=now(); return new; end $$;
drop trigger if exists panel_profiles_updated_at on public.panel_profiles;
create trigger panel_profiles_updated_at before update on public.panel_profiles for each row execute function public.set_updated_at();

alter table public.devices enable row level security;
alter table public.system_settings enable row level security;
alter table public.panel_profiles enable row level security;
alter table public.deliveries enable row level security;
revoke all on public.devices,public.system_settings,public.panel_profiles,public.deliveries from anon,authenticated;
grant select on public.devices,public.panel_profiles to authenticated;
grant insert,update,delete on public.panel_profiles to authenticated;
grant select,insert,update on public.deliveries to authenticated;
revoke execute on function public.current_device_id() from public,anon;
revoke execute on function public.current_device_is_admin() from public,anon;
revoke execute on function public.register_device(uuid,text,text,text) from public,anon;
revoke execute on function public.admin_login_toggle(text) from public,anon;
revoke execute on function public.get_admin_state() from public,anon;
revoke execute on function public.acknowledge_response(uuid) from public,anon;
grant execute on function public.current_device_id() to authenticated;
grant execute on function public.current_device_is_admin() to authenticated;
grant execute on function public.register_device(uuid,text,text,text) to authenticated;
grant execute on function public.admin_login_toggle(text) to authenticated;
grant execute on function public.get_admin_state() to authenticated;
grant execute on function public.acknowledge_response(uuid) to authenticated;

drop policy if exists "system settings denied" on public.system_settings;
create policy "system settings denied" on public.system_settings for all to authenticated
using(false) with check(false);

drop policy if exists "devices readable" on public.devices;
create policy "devices readable" on public.devices for select to authenticated using(true);
drop policy if exists "profiles readable" on public.panel_profiles;
create policy "profiles readable" on public.panel_profiles for select to authenticated using(true);
drop policy if exists "profile insert own or admin" on public.panel_profiles;
create policy "profile insert own or admin" on public.panel_profiles for insert to authenticated
with check(device_id=public.current_device_id() or public.current_device_is_admin());
drop policy if exists "profile update own or admin" on public.panel_profiles;
create policy "profile update own or admin" on public.panel_profiles for update to authenticated
using(device_id=public.current_device_id() or public.current_device_is_admin())
with check(device_id=public.current_device_id() or public.current_device_is_admin());
drop policy if exists "profile delete admin" on public.panel_profiles;
create policy "profile delete admin" on public.panel_profiles for delete to authenticated using(public.current_device_is_admin());
drop policy if exists "delivery participants read" on public.deliveries;
create policy "delivery participants read" on public.deliveries for select to authenticated
using(sender_device_id=public.current_device_id() or target_device_id=public.current_device_id() or public.current_device_is_admin());
drop policy if exists "sender creates delivery" on public.deliveries;
create policy "sender creates delivery" on public.deliveries for insert to authenticated with check(sender_device_id=public.current_device_id());
drop policy if exists "target updates delivery" on public.deliveries;
create policy "target updates delivery" on public.deliveries for update to authenticated
using(target_device_id=public.current_device_id() or public.current_device_is_admin())
with check(target_device_id=public.current_device_id() or public.current_device_is_admin());

do $$ begin
 if not exists(select 1 from pg_publication_tables where pubname='supabase_realtime' and schemaname='public' and tablename='panel_profiles') then alter publication supabase_realtime add table public.panel_profiles; end if;
 if not exists(select 1 from pg_publication_tables where pubname='supabase_realtime' and schemaname='public' and tablename='deliveries') then alter publication supabase_realtime add table public.deliveries; end if;
end $$;
