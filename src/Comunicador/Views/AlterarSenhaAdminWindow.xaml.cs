using System.Windows;
using System.Windows.Controls;

namespace Comunicador.Views;

public partial class AlterarSenhaAdminWindow : UserControl
{
    public event EventHandler? ConfirmarSolicitado;
    public event EventHandler? VoltarSolicitado;
    public string SenhaAtual => AtualBox.Password;
    public string NovaSenha => NovaBox.Password;

    public AlterarSenhaAdminWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => AtualBox.Focus();
    }

    public void MostrarErro(string mensagem) => Erro.Text = mensagem;

    private void Confirmar_OnClick(object sender, RoutedEventArgs e)
    {
        if (NovaSenha.Length is < 8 or > 256)
        {
            Erro.Text = "A nova senha deve ter de 8 a 256 caracteres.";
            return;
        }
        if (NovaSenha != ConfirmacaoBox.Password)
        {
            Erro.Text = "As novas senhas não coincidem.";
            return;
        }
        if (string.IsNullOrEmpty(SenhaAtual))
        {
            Erro.Text = "Digite a senha atual.";
            return;
        }
        ConfirmarSolicitado?.Invoke(this, EventArgs.Empty);
    }

    private void Cancelar_OnClick(object sender, RoutedEventArgs e) => VoltarSolicitado?.Invoke(this, EventArgs.Empty);
}
