using System.Globalization;
using System.Windows.Controls;
using System.Windows;
using Comunicador.ViewModels;

namespace Comunicador.Views;

public partial class LembretesView : UserControl
{
    public LembretesView()
    {
        InitializeComponent();
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
