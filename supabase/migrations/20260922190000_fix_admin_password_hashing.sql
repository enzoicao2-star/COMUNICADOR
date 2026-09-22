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
      update public.system_settings
        set admin_password_hash=extensions.crypt(p_password,extensions.gen_salt('bf'::text,12)),
            admin_device_id=current_id,updated_at=now()
        where singleton;
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
