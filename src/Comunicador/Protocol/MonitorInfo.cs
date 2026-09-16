using System.Text.Json.Serialization;
using System.Runtime.InteropServices;

namespace Comunicador.Protocol;

public sealed class MonitorInfo
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }

    [JsonIgnore]
    public string Descricao => Width > 0 && Height > 0
        ? $"{Name} — {Width} × {Height}{(Primary ? " (principal)" : string.Empty)}"
        : $"{Name} — resolução desconhecida";

    public static List<MonitorInfo> ListarLocais()
    {
        try
        {
            var resultado = new List<MonitorInfo>();
            MonitorEnumProc callback = (monitor, _, _, _) =>
            {
                var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    resultado.Add(new MonitorInfo
                    {
                        Index = resultado.Count,
                        Name = string.IsNullOrWhiteSpace(info.Device)
                            ? $"Monitor {resultado.Count + 1}"
                            : info.Device,
                        Width = info.Monitor.Right - info.Monitor.Left,
                        Height = info.Monitor.Bottom - info.Monitor.Top,
                        X = info.Monitor.Left,
                        Y = info.Monitor.Top,
                        Primary = (info.Flags & 1) != 0,
                    });
                }
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            return resultado;
        }
        catch
        {
            return new List<MonitorInfo>();
        }
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public RectNative Monitor;
        public RectNative WorkArea;
        public int Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
}
