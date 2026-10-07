using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows;
using System.Windows.Input;
using Comunicador.ViewModels;

namespace Comunicador.Views;

public partial class LembretesView : UserControl
{
    private ListCollectionView? _destinatariosFiltrados;

    public LembretesView()
    {
        InitializeComponent();
        FormularioLayout.Children.Remove(FormularioLembrete);
        DataContextChanged += (_, _) => PrepararListaDestinatarios();
        Loaded += (_, _) => PrepararListaDestinatarios();
    }

    private void PrepararListaDestinatarios()
    {
        if (DataContext is not LembretesViewModel vm) return;
        _destinatariosFiltrados = new ListCollectionView(vm.Destinatarios)
        {
            Filter = FiltrarDestinatario,
        };
        ListaDestinatarios.ItemsSource = _destinatariosFiltrados;
    }

    private bool FiltrarDestinatario(object item)
    {
        if (item is not ComputadorSelecionavel recipient) return false;
        var filter = FiltroDestinatarios.Text.Trim();
        return filter.Length == 0
            || recipient.Computador.NomeExibicao.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
            || recipient.Computador.Nome.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
            || recipient.Computador.EnderecoIpExibicao.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
    }

    private void FiltroDestinatarios_TextChanged(object sender, TextChangedEventArgs e) =>
        _destinatariosFiltrados?.Refresh();

    private void OpenRecipients_Click(object sender, RoutedEventArgs e)
    {
        FiltroDestinatarios.Clear();
        RecipientsOverlay.Visibility = Visibility.Visible;
    }

    private void CloseRecipients_Click(object sender, RoutedEventArgs e) =>
        RecipientsOverlay.Visibility = Visibility.Collapsed;

    private void AbrirEditorLembrete_Click(object sender, RoutedEventArgs e)
    {
        EditorLembreteConteudo.Content = FormularioLembrete;
        EditorLembreteOverlay.Visibility = Visibility.Visible;
    }

    private void FecharEditorLembrete_Click(object sender, RoutedEventArgs e) => FecharEditorLembrete();

    private void EditorLembreteOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(sender, e.OriginalSource)) FecharEditorLembrete();
    }

    private void FecharEditorLembrete()
    {
        DatePopup.IsOpen = false;
        EditorLembreteConteudo.Content = null;
        EditorLembreteOverlay.Visibility = Visibility.Collapsed;
    }

    private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(sender, e.OriginalSource) && sender is Grid overlay)
            overlay.Visibility = Visibility.Collapsed;
    }

    private void SelectAllRecipients_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LembretesViewModel vm) return;
        foreach (var recipient in vm.Destinatarios) recipient.Selecionado = true;
    }

    private void ClearRecipients_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LembretesViewModel vm) return;
        foreach (var recipient in vm.Destinatarios) recipient.Selecionado = false;
    }

    private void OpenCalendar_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is LembretesViewModel vm)
        {
            var culture = CultureInfo.GetCultureInfo("pt-BR");
            var date = DateTime.TryParseExact(vm.DataTexto, "dd/MM/yyyy", culture,
                DateTimeStyles.None, out var parsed) ? parsed : vm.DataHora.Date;
            DateCalendar.DisplayDate = date;
            DateCalendar.SelectedDate = date;
        }
        DatePopup.IsOpen = true;
    }

    private void CalendarDate_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!DatePopup.IsOpen || DateCalendar.SelectedDate is not { } date
            || DataContext is not LembretesViewModel vm) return;
        vm.DataTexto = date.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR"));
        DatePopup.IsOpen = false;
    }
}
