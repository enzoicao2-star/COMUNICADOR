using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Comunicador.Models;

namespace Comunicador.Converters;

public sealed class StatusComputadorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        StatusComputador.Online => new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
        StatusComputador.Offline => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
        _ => new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StatusComputadorToTextoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        StatusComputador.Online => "Online",
        StatusComputador.Offline => "Offline",
        _ => "Desconhecido",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class PareadoToTextoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "Parear novamente" : "Parear";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StatusEnvioToTextoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        StatusEnvio.Enviando => "Enviando…",
        StatusEnvio.Entregue => "Entregue",
        StatusEnvio.Exibido => "Exibido",
        StatusEnvio.Respondido => "Respondido",
        StatusEnvio.SemResposta => "Sem resposta",
        StatusEnvio.Erro => "Erro",
        _ => value?.ToString() ?? string.Empty,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StatusEnvioToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        StatusEnvio.Respondido => new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
        StatusEnvio.Entregue or StatusEnvio.Exibido => new SolidColorBrush(Color.FromRgb(0x3A, 0x7A, 0xFE)),
        StatusEnvio.Erro => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
        StatusEnvio.SemResposta => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
        _ => new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class DirecaoHistoricoToTextoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DirecaoHistorico.Recebida => "Recebida de ",
        _ => "Enviada para ",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ContagemParaVisibilidadeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count > 0
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class TextoParaVisibilidadeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string)
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
