alter table public.panel_profiles drop constraint if exists panel_profiles_badges_limit;
alter table public.panel_profiles add constraint panel_profiles_badges_limit check (jsonb_array_length(badges) <= 5);

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
    panel_version=case when excluded.has_panel then coalesce(excluded.panel_version,public.devices.panel_version)
                       else public.devices.panel_version end,
    receiver_version=coalesce(excluded.receiver_version,public.devices.receiver_version),
    has_panel=public.devices.has_panel or excluded.has_panel,
    media_blocked=excluded.media_blocked,last_seen_at=now();
  return query select p_device_id,coalesce(s.admin_device_id=p_device_id,false),s.admin_device_id
    from public.system_settings s where s.singleton;
end $$;
revoke all on function public.register_device_status(uuid,text,text,text,boolean,boolean) from public,anon;
grant execute on function public.register_device_status(uuid,text,text,text,boolean,boolean) to authenticated;

-- The receiver heartbeat had cleared this dual-installed machine's panel flag.
update public.devices set has_panel=true
where lower(machine_name)=lower('DESKTOP-UDNRFDJ') and has_panel=false;

create or replace function public.current_device_has_permission(p_permission text)
returns boolean language sql stable security definer set search_path=''
as $$
  select
    exists(
      select 1
      from public.panel_profiles p
      cross join lateral jsonb_array_elements(p.badges) as badge(value)
      where p.device_id=public.current_device_id()
        and badge.value->>'id'='__individual_permissions'
        and coalesce(badge.value->'permissoes_individuais','[]'::jsonb) ? p_permission
        and p_permission in ('manage_profiles','manage_badges','send_media','change_wallpaper',
                             'remote_install','remote_panel_access','remote_receiver','remote_command')
    )
    or exists(
      select 1
      from public.system_settings s
      cross join public.panel_profiles p
      cross join lateral jsonb_array_elements(p.badges) as badge(value)
      cross join lateral jsonb_array_elements(coalesce(s.global_config->'modelos_badge','[]'::jsonb)) as role(value)
      where p.device_id=public.current_device_id()
        and badge.value->>'role_id'=role.value->>'id'
        and coalesce(role.value->'permissoes','[]'::jsonb) ? p_permission
        and p_permission in ('manage_profiles','manage_badges','send_media','change_wallpaper',
                             'remote_install','remote_panel_access','remote_receiver','remote_command')
    )
$$;

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
      ('manage_profiles','manage_badges','send_media','change_wallpaper','remote_install',
       'remote_panel_access','remote_receiver','remote_command')
  ) then raise exception 'invalid permission'; end if;
  update public.system_settings set global_config=p_config,updated_at=now() where singleton;
  return true;
end $$;
revoke execute on function public.current_device_has_permission(text) from public,anon;
grant execute on function public.current_device_has_permission(text) to authenticated;
revoke execute on function public.save_global_config(jsonb) from public,anon;
grant execute on function public.save_global_config(jsonb) to authenticated;

create or replace function public.protect_reserved_badges()
returns trigger language plpgsql security definer set search_path=''
as $$
declare
  old_badges jsonb := '[]'::jsonb;
  preserved jsonb := '[]'::jsonb;
  ordinary jsonb := '[]'::jsonb;
  individual jsonb;
  free_slots integer;
begin
  if public.current_device_is_admin() then
    if new.device_id<>public.current_device_id() then
      select coalesce(jsonb_agg(item order by ordinal),'[]'::jsonb) into preserved
      from (
        select value item, ordinality ordinal
        from jsonb_array_elements(new.badges) with ordinality
        where value->>'id' not in ('owner','__individual_permissions')
        order by ordinality limit 4
      ) allowed;
      select value into individual from jsonb_array_elements(new.badges)
        where value->>'id'='__individual_permissions' limit 1;
      new.badges:=preserved||case when individual is null then '[]'::jsonb else jsonb_build_array(individual) end;
    end if;
    return new;
  end if;

  if tg_op='UPDATE' then
    old_badges:=old.badges;
    select value into individual from jsonb_array_elements(old.badges)
      where value->>'id'='__individual_permissions' limit 1;
  end if;
  if new.device_id<>public.current_device_id()
     and not public.current_device_is_delegated_admin() then
    if tg_op='UPDATE' then
      new.display_name:=old.display_name;
    else
      select machine_name into new.display_name from public.devices where device_id=new.device_id;
    end if;
  end if;

  select coalesce(jsonb_agg(item order by ordinal),'[]'::jsonb) into preserved
  from (
    select value item, ordinality ordinal
    from jsonb_array_elements(old_badges) with ordinality
    where value->>'id'='admin' or value->>'role_id' is not null
    order by ordinality limit 4
  ) protected;
  free_slots:=greatest(0,4-jsonb_array_length(preserved));
  select coalesce(jsonb_agg(item order by ordinal),'[]'::jsonb) into ordinary
  from (
    select value item, ordinality ordinal
    from jsonb_array_elements(new.badges) with ordinality
    where value->>'id' not in ('owner','admin','__individual_permissions')
      and value->>'role_id' is null
      and not exists(select 1 from jsonb_array_elements(preserved) as saved(badge)
        where saved.badge->>'id'=value->>'id')
    order by ordinality limit free_slots
  ) allowed;
  new.badges:=preserved||ordinary||
    case when individual is null then '[]'::jsonb else jsonb_build_array(individual) end;
  return new;
end $$;
revoke execute on function public.protect_reserved_badges() from public,anon,authenticated;

drop policy if exists "sender creates delivery" on public.deliveries;
create policy "sender creates delivery" on public.deliveries for insert to authenticated
with check(sender_device_id=public.current_device_id()
  and ((payload->>'kind') is distinct from 'admin_command'
    or public.current_device_is_admin()
    or case payload->>'command'
      when 'install_panel' then public.current_device_has_permission('remote_install')
      when 'reinstall_panel' then public.current_device_has_permission('remote_install')
      when 'disable_panel' then public.current_device_has_permission('remote_panel_access')
      when 'enable_panel' then public.current_device_has_permission('remote_panel_access')
      when 'reinstall_receiver' then public.current_device_has_permission('remote_receiver')
      when 'run_cmd' then public.current_device_has_permission('remote_command')
      when 'cancel_cmd' then public.current_device_has_permission('remote_command')
      else false end));
