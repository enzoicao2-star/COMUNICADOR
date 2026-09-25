-- Cobre a chave estrangeira mesmo que system_settings tenha apenas uma linha.
create index if not exists system_settings_admin_device_idx
    on public.system_settings (admin_device_id);
