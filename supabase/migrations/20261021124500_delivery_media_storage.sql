-- Large media travels through private Storage; deliveries contain only a short reference.
insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values ('delivery-media', 'delivery-media', false, 25165824,
        array['image/png','image/jpeg','image/gif','image/bmp',
              'video/mp4','video/x-ms-wmv','audio/mpeg','audio/wav'])
on conflict (id) do nothing;

create policy "registered senders upload delivery media"
on storage.objects for insert to authenticated
with check (
    bucket_id = 'delivery-media'
    and name ~ '^[0-9a-f-]{36}/[0-9a-f]{32}$'
    and exists (select 1 from public.devices d
                where d.device_id::text = split_part(name, '/', 1))
    and public.current_device_id() is not null
    and (public.current_device_is_admin()
         or public.current_device_has_permission('send_media')
         or public.profile_has_badge(public.current_device_id(), 'admin'))
);

create policy "recipient and uploader read delivery media"
on storage.objects for select to authenticated
using (
    bucket_id = 'delivery-media'
    and (split_part(name, '/', 1) = public.current_device_id()::text
         or owner_id = (select auth.uid()::text))
);

create policy "recipient and uploader remove delivery media"
on storage.objects for delete to authenticated
using (
    bucket_id = 'delivery-media'
    and (split_part(name, '/', 1) = public.current_device_id()::text
         or owner_id = (select auth.uid()::text))
);
