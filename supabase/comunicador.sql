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
    admin_password_hash text, global_config jsonb not null default '{}'::jsonb,
    updated_at timestamptz not null default now()
);
alter table public.system_settings add column if not exists global_config jsonb not null default '{}'::jsonb;
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
create or replace function public.profile_has_badge(p_device_id uuid,p_badge_id text)
returns boolean language sql stable security definer set search_path=''
as $$
  select exists(
    select 1 from public.panel_profiles p,
      jsonb_array_elements(p.badges) badge
    where p.device_id=p_device_id and badge->>'id'=p_badge_id
  )
$$;
create or replace function public.current_device_has_permission(p_permission text)
returns boolean language sql stable security definer set search_path=''
as $$
  select exists(
    select 1 from public.system_settings s, public.panel_profiles p,
      jsonb_array_elements(p.badges) b,
      jsonb_array_elements(coalesce(s.global_config->'modelos_badge','[]'::jsonb)) role
    where p.device_id=public.current_device_id()
      and b->>'role_id'=role->>'id'
      and coalesce(role->'permissoes','[]'::jsonb) ? p_permission
      and p_permission in ('manage_profiles','manage_badges','send_media','remote_install','remote_panel_access','remote_receiver')
  )
$$;
create or replace function public.current_device_is_delegated_admin()
returns boolean language sql stable security definer set search_path=''
as $$ select public.profile_has_badge(public.current_device_id(),'admin') or public.current_device_has_permission('manage_profiles') $$;
create or replace function public.can_manage_profile(p_device_id uuid)
returns boolean language sql stable security definer set search_path=''
as $$
  select p_device_id=public.current_device_id()
    or public.current_device_is_admin()
    or (
      (public.current_device_is_delegated_admin() or public.current_device_has_permission('manage_profiles') or public.current_device_has_permission('manage_badges'))
      and not public.profile_has_badge(p_device_id,'owner')
      and not public.profile_has_badge(p_device_id,'admin')
    )
$$;

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
      update public.system_settings set admin_password_hash=extensions.crypt(p_password,extensions.gen_salt('bf'::text,12)),admin_device_id=current_id,updated_at=now() where singleton;
      return query select 'created'::text,true,current_id; return;
    end if;
    if extensions.crypt(p_password,settings.admin_password_hash)<>settings.admin_password_hash then
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
create or replace function public.get_global_config()
returns table(config jsonb) language sql stable security definer set search_path=''
as $$ select s.global_config from public.system_settings s where s.singleton $$;
create or replace function public.save_global_config(p_config jsonb)
returns boolean language plpgsql security definer set search_path=''
as $$
begin
  if not public.current_device_is_admin() then raise exception 'owner only'; end if;
  if jsonb_typeof(p_config)<>'object' or jsonb_array_length(coalesce(p_config->'modelos_badge','[]'::jsonb))>20 then raise exception 'invalid global config'; end if;
  if exists(select 1 from jsonb_array_elements(coalesce(p_config->'modelos_badge','[]'::jsonb)) model,
      jsonb_array_elements(coalesce(model->'permissoes','[]'::jsonb)) permission
      where permission#>>'{}' not in ('manage_profiles','manage_badges','send_media','remote_install','remote_panel_access','remote_receiver'))
  then raise exception 'invalid permission'; end if;
  update public.system_settings set global_config=p_config,updated_at=now() where singleton;
  return true;
end $$;
create or replace function public.acknowledge_response(p_delivery_id uuid)
returns void language plpgsql security definer set search_path=''
as $$
begin
  update public.deliveries set sender_notified_at=now()
  where id=p_delivery_id and sender_device_id=public.current_device_id() and status='responded';
end $$;
create or replace function public.claim_due_deliveries()
returns table(id uuid,sender_device_id uuid,target_device_id uuid,payload jsonb,status text)
language sql volatile security definer set search_path=''
as $$
  with claimed as (
    select d.id from public.deliveries d
    where d.target_device_id=public.current_device_id()
      and d.status='pending' and d.deliver_at<=now()
    order by d.deliver_at asc limit 20 for update skip locked
  ), updated as (
    update public.deliveries d set status='delivered',delivered_at=now()
    from claimed c where d.id=c.id
    returning d.id,d.sender_device_id,d.target_device_id,d.payload,d.status
  )
  select u.id,u.sender_device_id,u.target_device_id,u.payload,u.status from updated u
$$;
create or replace function public.respond_to_delivery(p_delivery_id uuid,p_response text)
returns void language plpgsql security definer set search_path=''
as $$
begin
  update public.deliveries set status='responded',response_text=left(p_response,1000),responded_at=now()
  where id=p_delivery_id and target_device_id=public.current_device_id()
    and status in ('delivered','responded');
end $$;
create or replace function public.set_updated_at()
returns trigger language plpgsql security invoker set search_path=''
as $$ begin new.updated_at=now(); return new; end $$;
create or replace function public.protect_reserved_badges()
returns trigger language plpgsql security definer set search_path=''
as $$
declare preserved_admin jsonb; clean_badges jsonb; old_badges jsonb:='[]'::jsonb; regular_limit integer:=4;
begin
  if public.current_device_is_admin() then
    if new.device_id<>public.current_device_id() then
      select coalesce(jsonb_agg(item order by ordinal),'[]'::jsonb) into clean_badges
      from (select value item,ordinality ordinal from jsonb_array_elements(new.badges) with ordinality
            where value->>'id'<>'owner' order by ordinality limit 4) allowed;
      new.badges:=clean_badges;
    end if;
    return new;
  end if;
  if tg_op='UPDATE' then
    old_badges:=old.badges;
    select badge.value into preserved_admin from jsonb_array_elements(old.badges) as badge(value)
      where badge.value->>'id'='admin' limit 1;
  end if;
  if preserved_admin is not null then regular_limit:=3; end if;
  select coalesce(jsonb_agg(item order by ordinal),'[]'::jsonb) into clean_badges
  from (
    select case when previous_badge.value->>'role_id' is not null
      then badge.value||jsonb_build_object('role_id',previous_badge.value->>'role_id')
      else badge.value-'role_id' end item, badge.ordinality ordinal
    from jsonb_array_elements(new.badges) with ordinality as badge(value,ordinality)
    left join lateral (select old_badge.value from jsonb_array_elements(old_badges) as old_badge(value)
      where old_badge.value->>'id'=badge.value->>'id' limit 1) previous_badge on true
    where badge.value->>'id' not in ('owner','admin') order by badge.ordinality limit regular_limit
  ) allowed;
  new.badges:=case when preserved_admin is null then clean_badges
    else jsonb_build_array(preserved_admin)||clean_badges end;
  return new;
end $$;
drop trigger if exists panel_profiles_updated_at on public.panel_profiles;
create trigger panel_profiles_updated_at before update on public.panel_profiles for each row execute function public.set_updated_at();
drop trigger if exists panel_profiles_protect_reserved_badges on public.panel_profiles;
create trigger panel_profiles_protect_reserved_badges before insert or update on public.panel_profiles
for each row execute function public.protect_reserved_badges();

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
revoke execute on function public.profile_has_badge(uuid,text) from public,anon;
revoke execute on function public.current_device_is_delegated_admin() from public,anon;
revoke execute on function public.current_device_has_permission(text) from public,anon;
revoke execute on function public.can_manage_profile(uuid) from public,anon;
revoke execute on function public.protect_reserved_badges() from public,anon,authenticated;
revoke execute on function public.register_device(uuid,text,text,text) from public,anon;
revoke execute on function public.admin_login_toggle(text) from public,anon;
revoke execute on function public.get_admin_state() from public,anon;
revoke execute on function public.get_global_config() from public,anon;
revoke execute on function public.save_global_config(jsonb) from public,anon;
revoke execute on function public.acknowledge_response(uuid) from public,anon;
revoke execute on function public.claim_due_deliveries() from public,anon;
revoke execute on function public.respond_to_delivery(uuid,text) from public,anon;
grant execute on function public.current_device_id() to authenticated;
grant execute on function public.current_device_is_admin() to authenticated;
grant execute on function public.profile_has_badge(uuid,text) to authenticated;
grant execute on function public.current_device_is_delegated_admin() to authenticated;
grant execute on function public.current_device_has_permission(text) to authenticated;
grant execute on function public.can_manage_profile(uuid) to authenticated;
grant execute on function public.register_device(uuid,text,text,text) to authenticated;
grant execute on function public.admin_login_toggle(text) to authenticated;
grant execute on function public.get_admin_state() to authenticated;
grant execute on function public.get_global_config() to authenticated;
grant execute on function public.save_global_config(jsonb) to authenticated;
grant execute on function public.acknowledge_response(uuid) to authenticated;
grant execute on function public.claim_due_deliveries() to authenticated;
grant execute on function public.respond_to_delivery(uuid,text) to authenticated;

drop policy if exists "system settings denied" on public.system_settings;
create policy "system settings denied" on public.system_settings for all to authenticated
using(false) with check(false);

drop policy if exists "devices readable" on public.devices;
create policy "devices readable" on public.devices for select to authenticated using(true);
drop policy if exists "profiles readable" on public.panel_profiles;
create policy "profiles readable" on public.panel_profiles for select to authenticated using(true);
drop policy if exists "profile insert own or admin" on public.panel_profiles;
create policy "profile insert own or admin" on public.panel_profiles for insert to authenticated
with check(public.can_manage_profile(device_id));
drop policy if exists "profile update own or admin" on public.panel_profiles;
create policy "profile update own or admin" on public.panel_profiles for update to authenticated
using(public.can_manage_profile(device_id))
with check(public.can_manage_profile(device_id));
drop policy if exists "profile delete admin" on public.panel_profiles;
create policy "profile delete admin" on public.panel_profiles for delete to authenticated using(public.current_device_is_admin());
drop policy if exists "delivery participants read" on public.deliveries;
create policy "delivery participants read" on public.deliveries for select to authenticated
using(sender_device_id=public.current_device_id() or target_device_id=public.current_device_id() or public.current_device_is_admin());
drop policy if exists "sender creates delivery" on public.deliveries;
create policy "sender creates delivery" on public.deliveries for insert to authenticated
with check(sender_device_id=public.current_device_id()
  and ((payload->>'kind') is distinct from 'admin_command' or public.current_device_is_admin()
    or case payload->>'command'
      when 'install_panel' then public.current_device_has_permission('remote_install')
      when 'reinstall_panel' then public.current_device_has_permission('remote_install')
      when 'disable_panel' then public.current_device_has_permission('remote_panel_access')
      when 'enable_panel' then public.current_device_has_permission('remote_panel_access')
      when 'reinstall_receiver' then public.current_device_has_permission('remote_receiver')
      else false end));
drop policy if exists "target updates delivery" on public.deliveries;
create policy "target updates delivery" on public.deliveries for update to authenticated
using(target_device_id=public.current_device_id() or public.current_device_is_admin())
with check(target_device_id=public.current_device_id() or public.current_device_is_admin());

do $$ begin
 if not exists(select 1 from pg_publication_tables where pubname='supabase_realtime' and schemaname='public' and tablename='panel_profiles') then alter publication supabase_realtime add table public.panel_profiles; end if;
 if not exists(select 1 from pg_publication_tables where pubname='supabase_realtime' and schemaname='public' and tablename='deliveries') then alter publication supabase_realtime add table public.deliveries; end if;
end $$;

-- admin_audit_shared_library (20260923204715): historico administrativo e biblioteca compartilhada
-- Registro gerado no banco: clientes podem ler apenas quando sao OWNER.
create table if not exists public.admin_audit (
  id uuid primary key default gen_random_uuid(),
  created_at timestamptz not null default now(),
  actor_device_id uuid,
  actor_name text not null,
  action text not null,
  target_device_id uuid,
  target_name text,
  summary text not null
);
create index if not exists admin_audit_created_at_idx on public.admin_audit (created_at desc);
alter table public.admin_audit enable row level security;
revoke all on public.admin_audit from anon, authenticated;
grant select on public.admin_audit to authenticated;
drop policy if exists "owner reads admin audit" on public.admin_audit;
create policy "owner reads admin audit" on public.admin_audit
  for select to authenticated using ((select public.current_device_is_admin()));

create or replace function public.record_admin_audit()
returns trigger language plpgsql security definer set search_path=''
as $$
declare
  actor_id uuid := public.current_device_id();
  actor_label text;
  target_id uuid;
  target_label text;
  command_name text;
begin
  select coalesce(p.display_name, d.machine_name) into actor_label
  from public.devices d left join public.panel_profiles p on p.device_id=d.device_id
  where d.device_id=actor_id;
  actor_label := coalesce(actor_label, 'Sistema');

  if tg_table_name='system_settings' then
    if old.admin_device_id is distinct from new.admin_device_id then
      target_id := new.admin_device_id;
      select coalesce(p.display_name,d.machine_name) into target_label
        from public.devices d left join public.panel_profiles p on p.device_id=d.device_id
        where d.device_id=target_id;
      insert into public.admin_audit(actor_device_id,actor_name,action,target_device_id,target_name,summary)
      values(actor_id,actor_label,'owner_changed',target_id,target_label,
        case when target_id is null then 'Acesso OWNER desativado.'
          else 'Acesso OWNER atribuído a '||coalesce(target_label,'painel desconhecido')||'.' end);
    end if;
    if old.global_config is distinct from new.global_config then
      insert into public.admin_audit(actor_device_id,actor_name,action,summary)
      values(actor_id,actor_label,'global_config_changed','Configurações globais, grupos ou modelos atualizados.');
    end if;
    return new;
  end if;

  if tg_table_name='panel_profiles' then
    if tg_op='DELETE' then return old; end if;
    if tg_op='INSERT' then
      insert into public.admin_audit(actor_device_id,actor_name,action,target_device_id,target_name,summary)
      values(actor_id,actor_label,'profile_changed',new.device_id,new.display_name,
        'Nome ou badges de '||left(new.display_name,60)||' atualizados.');
    elsif old.display_name is distinct from new.display_name
      or old.badges is distinct from new.badges then
      insert into public.admin_audit(actor_device_id,actor_name,action,target_device_id,target_name,summary)
      values(actor_id,actor_label,'profile_changed',new.device_id,new.display_name,
        'Nome ou badges de '||left(new.display_name,60)||' atualizados.');
    end if;
    return new;
  end if;

  if tg_table_name='deliveries' then
    if new.payload->>'kind' is distinct from 'admin_command' then return new; end if;
    command_name := left(coalesce(new.payload->>'command','comando'),40);
    select coalesce(p.display_name,d.machine_name) into target_label
      from public.devices d left join public.panel_profiles p on p.device_id=d.device_id
      where d.device_id=new.target_device_id;
    if tg_op='INSERT' then
      insert into public.admin_audit(actor_device_id,actor_name,action,target_device_id,target_name,summary)
      values(actor_id,actor_label,'remote_command_sent',new.target_device_id,target_label,
        'Comando '||command_name||' enviado a '||coalesce(target_label,'painel desconhecido')||'.');
    elsif old.status is distinct from new.status then
      insert into public.admin_audit(actor_device_id,actor_name,action,target_device_id,target_name,summary)
      values(actor_id,actor_label,'remote_command_status',new.target_device_id,target_label,
        'Comando '||command_name||': '||left(new.status,30)||'.');
    end if;
    return new;
  end if;
  return new;
end $$;
revoke execute on function public.record_admin_audit() from public, anon, authenticated;

drop trigger if exists system_settings_audit on public.system_settings;
create trigger system_settings_audit after update on public.system_settings
  for each row execute function public.record_admin_audit();
drop trigger if exists panel_profiles_audit on public.panel_profiles;
create trigger panel_profiles_audit after insert or update on public.panel_profiles
  for each row execute function public.record_admin_audit();
drop trigger if exists deliveries_admin_audit on public.deliveries;
create trigger deliveries_admin_audit after insert or update on public.deliveries
  for each row execute function public.record_admin_audit();

create or replace function public.save_global_config(p_config jsonb)
returns boolean language plpgsql security definer set search_path=''
as $$
begin
  if not public.current_device_is_admin() then raise exception 'owner only'; end if;
  if jsonb_typeof(p_config) is distinct from 'object' or octet_length(p_config::text)>262144 then
    raise exception 'invalid global config';
  end if;
  if jsonb_typeof(coalesce(p_config->'modelos_badge','[]'::jsonb))<>'array'
    or jsonb_typeof(coalesce(p_config->'grupos_computadores','[]'::jsonb))<>'array'
    or jsonb_typeof(coalesce(p_config->'modelos_mensagem','[]'::jsonb))<>'array' then
    raise exception 'invalid global config';
  end if;
  if jsonb_array_length(coalesce(p_config->'modelos_badge','[]'::jsonb))>20
    or jsonb_array_length(coalesce(p_config->'grupos_computadores','[]'::jsonb))>30
    or jsonb_array_length(coalesce(p_config->'modelos_mensagem','[]'::jsonb))>50 then
    raise exception 'shared library limit exceeded';
  end if;
  if exists(
    select 1 from jsonb_array_elements(coalesce(p_config->'modelos_badge','[]'::jsonb)) as model(value),
      jsonb_array_elements(coalesce(model.value->'permissoes','[]'::jsonb)) as permission(value)
    where permission.value#>>'{}' not in
      ('manage_profiles','manage_badges','send_media','remote_install','remote_panel_access','remote_receiver')
  ) then raise exception 'invalid permission'; end if;
  update public.system_settings set global_config=p_config,updated_at=now() where singleton;
  return true;
end $$;
revoke execute on function public.save_global_config(jsonb) from public,anon;
grant execute on function public.save_global_config(jsonb) to authenticated;
