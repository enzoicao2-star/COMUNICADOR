alter table public.system_settings
  add column if not exists global_config jsonb not null default '{}'::jsonb;

create or replace function public.current_device_has_permission(p_permission text)
returns boolean language sql stable security definer set search_path=''
as $$
  select exists(
    select 1
    from public.system_settings s,
      public.panel_profiles p,
      jsonb_array_elements(p.badges) as badge(value),
      jsonb_array_elements(coalesce(s.global_config->'modelos_badge','[]'::jsonb)) as role(value)
    where p.device_id=public.current_device_id()
      and badge.value->>'role_id'=role.value->>'id'
      and coalesce(role.value->'permissoes','[]'::jsonb) ? p_permission
      and p_permission in ('manage_profiles','manage_badges','send_media','remote_install','remote_panel_access','remote_receiver')
  )
$$;

create or replace function public.current_device_is_delegated_admin()
returns boolean language sql stable security definer set search_path=''
as $$
  select public.profile_has_badge(public.current_device_id(),'admin')
    or public.current_device_has_permission('manage_profiles')
$$;

create or replace function public.can_manage_profile(p_device_id uuid)
returns boolean language sql stable security definer set search_path=''
as $$
  select p_device_id=public.current_device_id()
    or public.current_device_is_admin()
    or ((public.current_device_is_delegated_admin()
      or public.current_device_has_permission('manage_profiles')
      or public.current_device_has_permission('manage_badges'))
      and not public.profile_has_badge(p_device_id,'owner')
      and not public.profile_has_badge(p_device_id,'admin'))
$$;

create or replace function public.protect_reserved_badges()
returns trigger language plpgsql security definer set search_path=''
as $$
declare
  preserved_admin jsonb;
  clean_badges jsonb;
  old_badges jsonb:='[]'::jsonb;
  regular_limit integer:=4;
begin
  if public.current_device_is_admin() then
    if new.device_id<>public.current_device_id() then
      select coalesce(jsonb_agg(item order by ordinal),'[]'::jsonb) into clean_badges
      from (
        select value item, ordinality ordinal
        from jsonb_array_elements(new.badges) with ordinality
        where value->>'id'<>'owner'
        order by ordinality limit 4
      ) allowed;
      new.badges:=clean_badges;
    end if;
    return new;
  end if;

  if tg_op='UPDATE' then
    old_badges:=old.badges;
    select badge.value into preserved_admin
    from jsonb_array_elements(old.badges) as badge(value)
    where badge.value->>'id'='admin' limit 1;
  end if;
  if preserved_admin is not null then regular_limit:=3; end if;

  select coalesce(jsonb_agg(item order by ordinal),'[]'::jsonb) into clean_badges
  from (
    select case
      when previous_badge.value->>'role_id' is not null
        then badge.value||jsonb_build_object('role_id',previous_badge.value->>'role_id')
      else badge.value-'role_id'
    end item, badge.ordinality ordinal
    from jsonb_array_elements(new.badges) with ordinality as badge(value,ordinality)
    left join lateral (
      select old_badge.value
      from jsonb_array_elements(old_badges) as old_badge(value)
      where old_badge.value->>'id'=badge.value->>'id'
      limit 1
    ) previous_badge on true
    where badge.value->>'id' not in ('owner','admin')
    order by badge.ordinality limit regular_limit
  ) allowed;

  new.badges:=case when preserved_admin is null then clean_badges
    else jsonb_build_array(preserved_admin)||clean_badges end;
  return new;
end $$;

create or replace function public.get_global_config()
returns table(config jsonb) language sql stable security definer set search_path=''
as $$ select settings.global_config from public.system_settings settings where settings.singleton $$;

create or replace function public.save_global_config(p_config jsonb)
returns boolean language plpgsql security definer set search_path=''
as $$
begin
  if not public.current_device_is_admin() then raise exception 'owner only'; end if;
  if jsonb_typeof(p_config)<>'object'
    or jsonb_typeof(coalesce(p_config->'modelos_badge','[]'::jsonb))<>'array'
    or jsonb_array_length(coalesce(p_config->'modelos_badge','[]'::jsonb))>20 then
    raise exception 'invalid global config';
  end if;
  if exists(
    select 1
    from jsonb_array_elements(coalesce(p_config->'modelos_badge','[]'::jsonb)) as model(value),
      jsonb_array_elements(coalesce(model.value->'permissoes','[]'::jsonb)) as permission(value)
    where permission.value#>>'{}' not in ('manage_profiles','manage_badges','send_media','remote_install','remote_panel_access','remote_receiver')
  ) then raise exception 'invalid permission'; end if;
  update public.system_settings set global_config=p_config,updated_at=now() where singleton;
  return true;
end $$;

revoke execute on function public.current_device_has_permission(text) from public,anon;
revoke execute on function public.get_global_config() from public,anon;
revoke execute on function public.save_global_config(jsonb) from public,anon;
revoke execute on function public.protect_reserved_badges() from public,anon,authenticated;
grant execute on function public.current_device_has_permission(text) to authenticated;
grant execute on function public.get_global_config() to authenticated;
grant execute on function public.save_global_config(jsonb) to authenticated;

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
      else false end));
