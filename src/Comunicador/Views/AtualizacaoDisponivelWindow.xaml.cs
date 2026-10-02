using System.Windows;
using System.Windows.Controls;
using Comunicador.Services;

namespace Comunicador.Views;

public partial class AtualizacaoDisponivelWindow : UserControl
{
    public event EventHandler? AtualizarSolicitado;
    public event EventHandler? AdiarSolicitado;

    public AtualizacaoDisponivelWindow(PanelUpdateInfo info)
    {
        InitializeComponent();
        VersaoTexto.Text = $"Versão instalada: {info.CurrentVersion}   ·   Nova versão: {info.LatestVersion}";
        ResumoTexto.Text = info.Summary;
    }

    public void MostrarAdiada()
    {
        StatusTexto.Text = "A atualização continua disponível. Você pode instalá-la depois em Configurações.";
        StatusTexto.Visibility = Visibility.Visible;
    }

    public void MostrarErro(string mensagem)
    {
        StatusTexto.Text = mensagem;
        StatusTexto.Visibility = Visibility.Visible;
    }

    private void Atualizar_Click(object sender, RoutedEventArgs e) =>
        AtualizarSolicitado?.Invoke(this, EventArgs.Empty);

    private void Adiar_Click(object sender, RoutedEventArgs e) =>
        AdiarSolicitado?.Invoke(this, EventArgs.Empty);
}
