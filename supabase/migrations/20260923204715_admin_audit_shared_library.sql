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
