using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Comunicador.Models;

namespace Comunicador.Services;

public static class ThemeService
{
    public static event Action? ThemeChanged;

    private static readonly Dictionary<string, (string Accent, string Hover, string Pressed)> Palettes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Azul"] = ("#4C8DFF", "#659DFF", "#3377EA"),
            ["Violeta"] = ("#8B7CFF", "#9D91FF", "#7565EA"),
            ["Verde"] = ("#27B07D", "#3DC08F", "#19996A"),
            ["Coral"] = ("#F06E5B", "#F48372", "#DA5947"),
        };

    public static void Apply(AppSettings settings, bool animate = true)
    {
        if (Application.Current is null) return;
        var dark = !settings.Tema.Equals("Claro", StringComparison.OrdinalIgnoreCase);
        var palette = ResolverPaleta(settings);
        var colors = dark
            ? new Dictionary<string, string>
            {
                ["BackgroundBrush"] = "#080B11", ["SurfaceBrush"] = "#121923",
                ["SurfaceAltBrush"] = "#192231", ["SidebarBrush"] = "#121923",
                ["SidebarHoverBrush"] = "#273347", ["SidebarSelectedBrush"] = "#33435C",
                ["TextPrimaryBrush"] = "#F7F9FC", ["TextSecondaryBrush"] = "#B8C3D2",
                ["TextOnDarkBrush"] = "#F7F9FC", ["TextOnDarkDimBrush"] = "#C2CCDA",
                ["BorderBrush"] = "#344156", ["BorderStrongBrush"] = "#52637C",
                ["ControlBorderBrush"] = "#6A7A91", ["NavSurfaceBrush"] = "#171D29",
                ["BackgroundLineBrush"] = "#93B8EA",
            }
            : new Dictionary<string, string>
            {
                ["BackgroundBrush"] = "#F0F4F9", ["SurfaceBrush"] = "#FFFFFF",
                ["SurfaceAltBrush"] = "#F8FAFD", ["SidebarBrush"] = "#FFFFFF",
                ["SidebarHoverBrush"] = "#E3EAF3", ["SidebarSelectedBrush"] = "#D6E3F3",
                ["TextPrimaryBrush"] = "#111923", ["TextSecondaryBrush"] = "#4F6074",
                ["TextOnDarkBrush"] = "#111923", ["TextOnDarkDimBrush"] = "#4F6074",
                ["BorderBrush"] = "#C7D2E0", ["BorderStrongBrush"] = "#9EADC0",
                ["ControlBorderBrush"] = "#8292A7", ["NavSurfaceBrush"] = "#FFFFFF",
                ["BackgroundLineBrush"] = "#294C76",
            };

        colors["AccentBrush"] = palette.Accent;
        colors["AccentHoverBrush"] = palette.Hover;
        colors["AccentPressedBrush"] = palette.Pressed;

        foreach (var pair in colors)
        {
            AnimateBrush(pair.Key, (Color)ColorConverter.ConvertFromString(pair.Value), animate);
        }
        ThemeChanged?.Invoke();
    }

    public static bool TryNormalizeColor(string? value, out string normalized)
    {
        normalized = "#4C8DFF";
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            if (ColorConverter.ConvertFromString(value.Trim()) is not Color color) return false;
            normalized = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            return true;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    private static (string Accent, string Hover, string Pressed) ResolverPaleta(AppSettings settings)
    {
        if (Palettes.TryGetValue(settings.Paleta, out var predefined)) return predefined;
        var custom = settings.PaletasPersonalizadas?.FirstOrDefault(p =>
            string.Equals(p.Id, settings.Paleta, StringComparison.OrdinalIgnoreCase));
        if (custom is null || !TryNormalizeColor(custom.Cor, out var normalized)) return Palettes["Azul"];

        var baseColor = (Color)ColorConverter.ConvertFromString(normalized);
        return (normalized, ToHex(Blend(baseColor, Colors.White, .16)), ToHex(Blend(baseColor, Colors.Black, .18)));
    }

    private static Color Blend(Color source, Color target, double amount) => Color.FromRgb(
        (byte)Math.Round(source.R + (target.R - source.R) * amount),
        (byte)Math.Round(source.G + (target.G - source.G) * amount),
        (byte)Math.Round(source.B + (target.B - source.B) * amount));

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static void AnimateBrush(string key, Color target, bool animate)
    {
        var old = Application.Current.TryFindResource(key) as SolidColorBrush;
        var start = old?.Color ?? target;

        // Recursos Freezable usados por estilos podem ser congelados pelo WPF. Criamos
        // um pincel novo e iniciamos a animação antes de colocá-lo no ResourceDictionary;
        // um Freezable animado não pode ser congelado durante a transição.
        var brush = new SolidColorBrush(target);
        if (animate && start != target)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
            {
                From = start,
                To = target,
                Duration = TimeSpan.FromMilliseconds(280),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop,
            });
        }

        Application.Current.Resources[key] = brush;
    }
}
