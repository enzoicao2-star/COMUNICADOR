# Shared by the panel and the standalone receiver. Runs in the current user session.
# Endpoint enumeration/volume use the documented Core Audio APIs. Windows does not
# publish an API for changing the system default endpoint; PolicyConfig may differ
# between Windows releases, so failures are reported to the sender.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class ComunicadorAudio
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class DeviceEnumeratorClass { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceEnumerator
    {
        void EnumAudioEndpoints(int flow, int state, out IDeviceCollection devices);
        void GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
        void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IDevice device);
        void RegisterEndpointNotificationCallback(IntPtr client);
        void UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceCollection
    {
        void GetCount(out int count);
        void Item(int index, out IDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice
    {
        void Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        void OpenPropertyStore(int access, out IPropertyStore properties);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetState(out int state);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out int count);
        void GetAt(int index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid fmtid; public int pid; }
    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant { [FieldOffset(0)] public short vt; [FieldOffset(8)] public IntPtr text; }
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEndpointVolume
    {
        void RegisterControlChangeNotify(IntPtr callback);
        void UnregisterControlChangeNotify(IntPtr callback);
        void GetChannelCount(out int count);
        void SetMasterVolumeLevel(float level, IntPtr context);
        void SetMasterVolumeLevelScalar(float level, IntPtr context);
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClass { }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        void GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr format);
        void GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, bool def, IntPtr format);
        void ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        void SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpoint, IntPtr mix);
        void GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, bool def, IntPtr standard, IntPtr minimum);
        void SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr period);
        void GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        void SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        void GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, bool store, ref PropertyKey key, out PropVariant value);
        void SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, bool store, ref PropertyKey key, ref PropVariant value);
        void SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        void SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, bool visible);
    }

    private static string FriendlyName(IDevice device)
    {
        IPropertyStore store;
        device.OpenPropertyStore(0, out store);
        try
        {
            PropertyKey key = new PropertyKey { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
            PropVariant value;
            store.GetValue(ref key, out value);
            try { return Marshal.PtrToStringUni(value.text) ?? "(sem nome)"; }
            finally { PropVariantClear(ref value); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    public static string List()
    {
        IDeviceEnumerator enumerator = (IDeviceEnumerator)new DeviceEnumeratorClass();
        try
        {
            IDeviceCollection collection;
            enumerator.EnumAudioEndpoints(0, 1, out collection);
            try
            {
                IDevice current = null;
                string currentId = null;
                try { enumerator.GetDefaultAudioEndpoint(0, 1, out current); current.GetId(out currentId); }
                catch (COMException) { }
                finally { if (current != null) Marshal.ReleaseComObject(current); }
                int count; collection.GetCount(out count);
                List<string> lines = new List<string>();
                for (int i = 0; i < count; i++)
                {
                    IDevice device; collection.Item(i, out device);
                    try
                    {
                        string id; device.GetId(out id);
                        lines.Add((i + 1) + ". " + FriendlyName(device) + (id == currentId ? " [PADRÃO]" : ""));
                    }
                    finally { Marshal.ReleaseComObject(device); }
                }
                return lines.Count == 0 ? "Nenhum dispositivo de saída ativo." : string.Join("\n", lines.ToArray());
            }
            finally { Marshal.ReleaseComObject(collection); }
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    public static string Select(int number)
    {
        IDeviceEnumerator enumerator = (IDeviceEnumerator)new DeviceEnumeratorClass();
        try
        {
            IDeviceCollection collection;
            enumerator.EnumAudioEndpoints(0, 1, out collection);
            try
            {
                int count; collection.GetCount(out count);
                if (number < 1 || number > count) throw new ArgumentException("Número do dispositivo inválido. Use audio devices.");
                IDevice device; collection.Item(number - 1, out device);
                try
                {
                    string id; device.GetId(out id);
                    IPolicyConfig policy = (IPolicyConfig)new PolicyConfigClass();
                    try { for (int role = 0; role < 3; role++) policy.SetDefaultEndpoint(id, role); }
                    finally { Marshal.ReleaseComObject(policy); }
                    return "Saída de áudio selecionada: " + FriendlyName(device);
                }
                finally { Marshal.ReleaseComObject(device); }
            }
            finally { Marshal.ReleaseComObject(collection); }
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    public static string Volume(int percent)
    {
        if (percent < 0 || percent > 100) throw new ArgumentException("Volume deve estar entre 0 e 100.");
        IDeviceEnumerator enumerator = (IDeviceEnumerator)new DeviceEnumeratorClass();
        try
        {
            IDevice device;
            enumerator.GetDefaultAudioEndpoint(0, 1, out device);
            try
            {
                Guid iid = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
                object volumeObject;
                device.Activate(ref iid, 23, IntPtr.Zero, out volumeObject);
                IEndpointVolume volume = (IEndpointVolume)volumeObject;
                try { volume.SetMasterVolumeLevelScalar(percent / 100f, IntPtr.Zero); }
                finally { Marshal.ReleaseComObject(volume); }
                return "Volume da saída padrão: " + percent + "%";
            }
            finally { Marshal.ReleaseComObject(device); }
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }
}
'@

try {
    switch ($env:COMUNICADOR_AUDIO_ACTION) {
        'list' { [ComunicadorAudio]::List(); break }
        'select' { [ComunicadorAudio]::Select([int]$env:COMUNICADOR_AUDIO_VALUE); break }
        'volume' { [ComunicadorAudio]::Volume([int]$env:COMUNICADOR_AUDIO_VALUE); break }
        default { throw 'Ação de áudio desconhecida.' }
    }
} catch {
    Write-Output ('Falha ao controlar áudio: ' + $_.Exception.Message)
    exit 1
}
