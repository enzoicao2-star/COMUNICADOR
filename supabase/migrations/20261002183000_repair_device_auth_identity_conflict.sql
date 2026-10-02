-- The panel and receiver share one Supabase auth session and must represent one
-- physical machine. Older installs can carry a different local device UUID;
-- when the authenticated machine name matches, keep the already registered
-- device UUID so the unique auth_user_id constraint does not reject heartbeats.
create or replace function public.register_device(
  p_device_id uuid, p_machine_name text, p_panel_version text, p_receiver_version text)
returns table(device_id uuid, is_admin boolean, admin_device_id uuid)
language plpgsql security definer set search_path = ''
as $$
declare
  existing_owner uuid;
  existing_device_id uuid;
  existing_machine_name text;
begin
  if (select auth.uid()) is null then raise exception 'authentication required'; end if;

  select d.device_id, d.machine_name
    into existing_device_id, existing_machine_name
    from public.devices d
    where d.auth_user_id=(select auth.uid())
    for update;
  if existing_device_id is not null and existing_device_id<>p_device_id then
    if lower(btrim(existing_machine_name)) is distinct from lower(btrim(left(p_machine_name,100))) then
      raise exception 'auth identity is registered to another machine';
    end if;
    p_device_id := existing_device_id;
  end if;

  select d.auth_user_id into existing_owner from public.devices d where d.device_id=p_device_id;
  if existing_owner is not null and existing_owner<>(select auth.uid()) then
    raise exception 'device id already registered';
  end if;

  insert into public.devices(device_id,auth_user_id,machine_name,panel_version,receiver_version)
  values(p_device_id,(select auth.uid()),left(p_machine_name,100),p_panel_version,p_receiver_version)
  on conflict on constraint devices_pkey do update set
    machine_name=excluded.machine_name,
    panel_version=coalesce(excluded.panel_version,public.devices.panel_version),
    receiver_version=coalesce(excluded.receiver_version,public.devices.receiver_version),
    last_seen_at=now();

  return query select p_device_id,coalesce(s.admin_device_id=p_device_id,false),s.admin_device_id
    from public.system_settings s where s.singleton;
end $$;

create or replace function public.register_device_status(
  p_device_id uuid, p_machine_name text, p_panel_version text,
  p_receiver_version text, p_has_panel boolean, p_media_blocked boolean)
returns table(device_id uuid, is_admin boolean, admin_device_id uuid)
language plpgsql security definer set search_path = ''
as $$
declare
  existing_owner uuid;
  existing_device_id uuid;
  existing_machine_name text;
begin
  if (select auth.uid()) is null then raise exception 'authentication required'; end if;

  select d.device_id, d.machine_name
    into existing_device_id, existing_machine_name
    from public.devices d
    where d.auth_user_id=(select auth.uid())
    for update;
  if existing_device_id is not null and existing_device_id<>p_device_id then
    if lower(btrim(existing_machine_name)) is distinct from lower(btrim(left(p_machine_name,100))) then
      raise exception 'auth identity is registered to another machine';
    end if;
    p_device_id := existing_device_id;
  end if;

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
    media_blocked=excluded.media_blocked,
    last_seen_at=now();

  return query select p_device_id,coalesce(s.admin_device_id=p_device_id,false),s.admin_device_id
    from public.system_settings s where s.singleton;
end $$;

revoke all on function public.register_device(uuid,text,text,text) from public,anon;
grant execute on function public.register_device(uuid,text,text,text) to authenticated;
revoke all on function public.register_device_status(uuid,text,text,text,boolean,boolean) from public,anon;
grant execute on function public.register_device_status(uuid,text,text,text,boolean,boolean) to authenticated;
