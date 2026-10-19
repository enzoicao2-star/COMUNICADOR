-- Restore an existing admin panel after its anonymous Supabase session changes.
-- The password proves authority; the RPC can only rebind the current admin device.
create or replace function public.recover_admin_device(p_device_id uuid, p_password text)
returns table(status text, is_admin boolean, admin_device_id uuid)
language plpgsql security definer set search_path = ''
as $$
declare
  settings public.system_settings%rowtype;
  current_auth_id uuid := (select auth.uid());
begin
  if current_auth_id is null then raise exception 'authentication required'; end if;

  select * into settings from public.system_settings where singleton for update;
  if settings.admin_password_hash is null
     or p_password is null
     or char_length(p_password) < 8
     or extensions.crypt(p_password, settings.admin_password_hash) <> settings.admin_password_hash then
    return query select 'invalid_password'::text, false, settings.admin_device_id;
    return;
  end if;

  if settings.admin_device_id is distinct from p_device_id then
    return query select 'device_not_admin'::text, false, settings.admin_device_id;
    return;
  end if;

  if exists (
    select 1 from public.devices d
    where d.auth_user_id = current_auth_id and d.device_id <> p_device_id
  ) then
    return query select 'session_has_another_device'::text, false, settings.admin_device_id;
    return;
  end if;

  update public.devices
     set auth_user_id = current_auth_id, last_seen_at = now()
   where device_id = p_device_id;
  if not found then
    return query select 'device_not_admin'::text, false, settings.admin_device_id;
    return;
  end if;

  return query select 'recovered'::text, true, p_device_id;
end;
$$;

revoke all on function public.recover_admin_device(uuid, text) from public, anon;
grant execute on function public.recover_admin_device(uuid, text) to authenticated;
