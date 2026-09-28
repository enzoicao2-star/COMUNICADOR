-- A tag manage_badges permits badges; only OWNER/manager of profiles can rename another PC.
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

  if new.device_id<>public.current_device_id()
     and not public.current_device_is_delegated_admin() then
    if tg_op='UPDATE' then
      new.display_name:=old.display_name;
    else
      select machine_name into new.display_name from public.devices where device_id=new.device_id;
    end if;
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
