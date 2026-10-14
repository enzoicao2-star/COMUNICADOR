alter table public.devices
  add column if not exists panel_uninstalled_at timestamptz;

create or replace function public.register_device_status(
  p_device_id uuid, p_machine_name text, p_panel_version text,
  p_receiver_version text, p_has_panel boolean, p_media_blocked boolean)
returns table(device_id uuid, is_admin boolean, admin_device_id uuid)
language plpgsql security definer set search_path = ''
as $$
declare existing_owner uuid;
begin
  if (select auth.uid()) is null then raise exception 'authentication required'; end if;
  select d.auth_user_id into existing_owner
    from public.devices d where d.device_id=p_device_id;
  if found and existing_owner is distinct from (select auth.uid()) then
    raise exception 'device id already registered';
  end if;

  insert into public.devices(device_id,auth_user_id,machine_name,panel_version,
                             receiver_version,has_panel,media_blocked)
  values(p_device_id,(select auth.uid()),left(p_machine_name,100),
         case when p_has_panel then p_panel_version else null end,
         p_receiver_version,coalesce(p_has_panel,false),coalesce(p_media_blocked,false))
  on conflict on constraint devices_pkey do update set
    machine_name=excluded.machine_name,
    panel_version=case
      when excluded.has_panel then coalesce(excluded.panel_version,public.devices.panel_version)
      when public.devices.panel_uninstalled_at is not null then null
      else public.devices.panel_version
    end,
    receiver_version=coalesce(excluded.receiver_version,public.devices.receiver_version),
    has_panel=case
      when excluded.has_panel then true
      when public.devices.panel_uninstalled_at is not null then false
      else public.devices.has_panel
    end,
    panel_uninstalled_at=case when excluded.has_panel then null
                              else public.devices.panel_uninstalled_at end,
    media_blocked=excluded.media_blocked,last_seen_at=now();

  return query select p_device_id,coalesce(s.admin_device_id=p_device_id,false),s.admin_device_id
    from public.system_settings s where s.singleton;
end $$;

create or replace function public.unregister_panel(p_device_id uuid)
returns void
language plpgsql security definer set search_path = ''
as $$
declare existing_owner uuid;
begin
  if (select auth.uid()) is null then raise exception 'authentication required'; end if;

  select d.auth_user_id into existing_owner
    from public.devices d where d.device_id=p_device_id for update;
  if not found then raise exception 'device not found'; end if;
  if existing_owner is distinct from (select auth.uid()) then
    raise exception 'device id already registered';
  end if;

  update public.devices
    set has_panel=false,
        panel_version=null,
        panel_uninstalled_at=coalesce(panel_uninstalled_at,now())
    where device_id=p_device_id;
end $$;

revoke all on function public.unregister_panel(uuid) from public,anon;
grant execute on function public.unregister_panel(uuid) to authenticated;
