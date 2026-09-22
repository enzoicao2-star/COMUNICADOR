drop policy if exists "sender creates delivery" on public.deliveries;
create policy "sender creates delivery" on public.deliveries for insert to authenticated
with check(sender_device_id=public.current_device_id()
  and ((payload->>'kind') is distinct from 'admin_command' or public.current_device_is_admin()));
