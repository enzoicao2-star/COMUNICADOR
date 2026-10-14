create or replace function public.current_device_has_permission(p_permission text)
returns boolean language sql stable security definer set search_path=''
as $$
  select (p_permission in ('customize_panel','manage_shared_content')
      and public.profile_has_badge(public.current_device_id(),'admin'))
    or exists(
      select 1
      from public.panel_profiles p,
        jsonb_array_elements(p.badges) as badge(value)
      where p.device_id=public.current_device_id()
        and badge.value->>'id'='__individual_permissions'
        and coalesce(badge.value->'permissoes_individuais','[]'::jsonb) ? p_permission
        and p_permission in ('manage_profiles','manage_badges','send_media','change_wallpaper',
                             'remote_install','remote_panel_access','remote_receiver','remote_command',
                             'customize_panel','manage_shared_content')
    )
    or exists(
      select 1
      from public.system_settings s,
        public.panel_profiles p,
        jsonb_array_elements(p.badges) as badge(value),
        jsonb_array_elements(coalesce(s.global_config->'modelos_badge','[]'::jsonb)) as role(value)
      where p.device_id=public.current_device_id()
        and badge.value->>'role_id'=role.value->>'id'
        and coalesce(role.value->'permissoes','[]'::jsonb) ? p_permission
        and p_permission in ('manage_profiles','manage_badges','send_media','change_wallpaper',
                             'remote_install','remote_panel_access','remote_receiver','remote_command',
                             'customize_panel','manage_shared_content')
    )
$$;

create or replace function public.save_global_config(p_config jsonb)
returns boolean language plpgsql security definer set search_path=''
as $$
declare
  is_owner boolean := public.current_device_is_admin();
  can_customize boolean;
  can_manage_shared boolean;
  merged_config jsonb;
begin
  can_customize := is_owner
    or public.profile_has_badge(public.current_device_id(),'admin')
    or public.current_device_has_permission('customize_panel');
  can_manage_shared := is_owner
    or public.profile_has_badge(public.current_device_id(),'admin')
    or public.current_device_has_permission('manage_shared_content');

  if not can_customize and not can_manage_shared then
    raise exception 'shared settings permission required';
  end if;
  if jsonb_typeof(p_config) is distinct from 'object' or octet_length(p_config::text)>262144 then
    raise exception 'invalid global config';
  end if;

  if not is_owner then
    select coalesce(s.global_config,'{}'::jsonb) into merged_config
    from public.system_settings s where s.singleton;

    if can_customize then
      if coalesce(p_config->>'tema','') not in ('Escuro','Claro')
        or coalesce(p_config->>'paleta','') not in ('Azul','Violeta','Verde','Coral')
        or coalesce(p_config->>'fundo_painel','') not in ('Sem fundo','Ondas interativas','Topográfico','Caminhos flutuantes',
          'Vórtice','Ondas luminosas','Constelação','Grade fluida','Partículas fluidas','Onda de partículas')
        or jsonb_typeof(p_config->'reduzir_movimento') is distinct from 'boolean'
        or jsonb_typeof(p_config->'transparencia_cards') is distinct from 'number'
        or jsonb_typeof(p_config->'blur_cards') is distinct from 'number'
        or jsonb_typeof(p_config->'velocidade_fundo') is distinct from 'number'
        or (p_config->>'transparencia_cards')::numeric not between 0 and 90
        or (p_config->>'blur_cards')::numeric not between 0 and 40
        or (p_config->>'velocidade_fundo')::numeric not between 5 and 100 then
        raise exception 'invalid panel appearance';
      end if;
      merged_config := merged_config||jsonb_build_object(
        'tema',p_config->'tema','paleta',p_config->'paleta','fundo_painel',p_config->'fundo_painel',
        'reduzir_movimento',p_config->'reduzir_movimento','transparencia_cards',p_config->'transparencia_cards',
        'blur_cards',p_config->'blur_cards','velocidade_fundo',p_config->'velocidade_fundo');
    end if;

    if can_manage_shared then
      if jsonb_typeof(coalesce(p_config->'grupos_computadores','[]'::jsonb)) is distinct from 'array'
        or jsonb_typeof(coalesce(p_config->'modelos_mensagem','[]'::jsonb)) is distinct from 'array'
        or jsonb_array_length(coalesce(p_config->'grupos_computadores','[]'::jsonb))>30
        or jsonb_array_length(coalesce(p_config->'modelos_mensagem','[]'::jsonb))>50 then
        raise exception 'invalid shared library';
      end if;
      if p_config ? 'grupos_computadores' or p_config ? 'modelos_mensagem' then
        merged_config := merged_config||jsonb_strip_nulls(jsonb_build_object(
          'grupos_computadores',case when p_config ? 'grupos_computadores'
            then coalesce(p_config->'grupos_computadores','[]'::jsonb) end,
          'modelos_mensagem',case when p_config ? 'modelos_mensagem'
            then coalesce(p_config->'modelos_mensagem','[]'::jsonb) end));
      end if;
    end if;

    -- Delegated users can update only the categories they were granted.
    update public.system_settings s
    set global_config=merged_config, updated_at=now()
    where s.singleton;
    return true;
  end if;

  if jsonb_typeof(coalesce(p_config->'modelos_badge','[]'::jsonb))<>'array'
    or jsonb_array_length(coalesce(p_config->'modelos_badge','[]'::jsonb))>20 then
    raise exception 'invalid global config';
  end if;
  if exists(
    select 1
    from jsonb_array_elements(coalesce(p_config->'modelos_badge','[]'::jsonb)) as model(value),
      jsonb_array_elements(coalesce(model.value->'permissoes','[]'::jsonb)) as permission(value)
    where permission.value#>>'{}' not in ('manage_profiles','manage_badges','send_media','change_wallpaper',
      'remote_install','remote_panel_access','remote_receiver','remote_command',
      'customize_panel','manage_shared_content')
  ) then raise exception 'invalid permission'; end if;

  update public.system_settings set global_config=p_config,updated_at=now() where singleton;
  return true;
end $$;
