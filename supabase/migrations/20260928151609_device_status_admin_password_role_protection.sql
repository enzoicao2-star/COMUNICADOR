-- Keep device visibility and local media preference synchronized even when a panel is closed.
alter table public.devices add column if not exists has_panel boolean not null default false;
update public.devices set has_panel = panel_version is not null where has_panel = false;

create or replace function public.register_device_status(
  p_device_id uuid, p_machine_name text, p_panel_version text,
  p_receiver_version text, p_has_panel boolean, p_media_blocked boolean)
returns table(device_id uuid, is_admin boolean, admin_device_id uuid)
language plpgsql security definer set search_path = ''
as $$
declare existing_owner uuid;
begin
  if (select auth.uid()) is null then raise exception 'authentication required'; end if;
  select d.auth_user_id into existing_owner from public.devices d where d.device_id=p_device_id;
  if existing_owner is not null and existing_owner<>(select auth.uid()) then
    raise exception 'device id already registered';
  end if;
  insert into public.devices(device_id,auth_user_id,machine_name,panel_version,
                             receiver_version,has_panel,media_blocked)
  values(p_device_id,(select auth.uid()),left(p_machine_name,100),
         case when p_has_panel then p_panel_version else null end,
         p_receiver_version,coalesce(p_has_panel,false),coalesce(p_media_blocked,false))
  on conflict on constraint devices_pkey do update set
    machine_name=excluded.machine_name,
    panel_version=case when excluded.has_panel then coalesce(excluded.panel_version,public.devices.panel_version) else null end,
    receiver_version=coalesce(excluded.receiver_version,public.devices.receiver_version),
    has_panel=excluded.has_panel,media_blocked=excluded.media_blocked,last_seen_at=now();
  return query select p_device_id,coalesce(s.admin_device_id=p_device_id,false),s.admin_device_id
    from public.system_settings s where s.singleton;
end $$;
revoke all on function public.register_device_status(uuid,text,text,text,boolean,boolean) from public,anon;
grant execute on function public.register_device_status(uuid,text,text,text,boolean,boolean) to authenticated;

create or replace function public.change_admin_password(p_current_password text,p_new_password text)
returns text language plpgsql security definer set search_path = ''
as $$
declare settings public.system_settings%rowtype;
begin
  if not public.current_device_is_admin() then raise exception 'owner only'; end if;
  if p_new_password is null or char_length(p_new_password)<8 or char_length(p_new_password)>256 then
    return 'password_too_short';
  end if;
  select * into settings from public.system_settings where singleton for update;
  if settings.admin_password_hash is null or p_current_password is null
    or extensions.crypt(p_current_password,settings.admin_password_hash)<>settings.admin_password_hash then
    return 'invalid_password';
  end if;
  update public.system_settings
    set admin_password_hash=extensions.crypt(p_new_password,extensions.gen_salt('bf'::text,12)),
        updated_at=now() where singleton;
  insert into public.admin_audit(actor_device_id,actor_name,action,summary)
  select public.current_device_id(),coalesce(p.display_name,d.machine_name),
    'password_changed','Senha administrativa alterada pelo OWNER.'
  from public.devices d left join public.panel_profiles p on p.device_id=d.device_id
  where d.device_id=public.current_device_id();
  return 'changed';
end $$;
revoke all on function public.change_admin_password(text,text) from public,anon;
grant execute on function public.change_admin_password(text,text) to authenticated;

-- Role IDs grant permissions, so only OWNER may add/remove/modify those badges.
create or replace function public.protect_reserved_badges()
returns trigger language plpgsql security definer set search_path=''
as $$
declare old_badges jsonb := '[]'::jsonb;
declare preserved jsonb := '[]'::jsonb;
declare ordinary jsonb := '[]'::jsonb;
declare free_slots integer;
begin
  if public.current_device_is_admin() then
    if new.device_id<>public.current_device_id() then
      select coalesce(jsonb_agg(item order by ordinal),'[]'::jsonb) into new.badges
      from (select value item, ordinality ordinal from jsonb_array_elements(new.badges) with ordinality
        where value->>'id'<>'owner' order by ordinality limit 4) allowed;
    end if;
    return new;
  end if;
  if tg_op='UPDATE' then old_badges:=old.badges; end if;
  select coalesce(jsonb_agg(item order by ordinal),'[]'::jsonb) into preserved
  from (select value item, ordinality ordinal from jsonb_array_elements(old_badges) with ordinality
    where value->>'id'='admin' or value->>'role_id' is not null
    order by ordinality limit 4) protected;
  free_slots:=greatest(0,4-jsonb_array_length(preserved));
  select coalesce(jsonb_agg(item order by ordinal),'[]'::jsonb) into ordinary
  from (select value item, ordinality ordinal from jsonb_array_elements(new.badges) with ordinality
    where value->>'id' not in ('owner','admin') and value->>'role_id' is null
      and not exists(select 1 from jsonb_array_elements(preserved) as saved(badge)
        where saved.badge->>'id'=value->>'id')
    order by ordinality limit free_slots) allowed;
  new.badges:=preserved||ordinary;
  return new;
end $$;
revoke execute on function public.protect_reserved_badges() from public,anon,authenticated;
