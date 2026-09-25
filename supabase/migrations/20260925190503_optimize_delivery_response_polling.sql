create index if not exists deliveries_sender_unnotified_idx
    on public.deliveries (sender_device_id, responded_at)
    where status = 'responded' and sender_notified_at is null;

drop index if exists public.panel_profiles_updated_at_idx;
drop index if exists public.system_settings_admin_device_idx;
