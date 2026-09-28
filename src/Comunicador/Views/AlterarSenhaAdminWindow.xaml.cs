using System.Windows;

namespace Comunicador.Views;

public partial class AlterarSenhaAdminWindow : Window
{
    public string SenhaAtual => AtualBox.Password;
    public string NovaSenha => NovaBox.Password;

    public AlterarSenhaAdminWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => AtualBox.Focus();
    }

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
        DialogResult = true;
    }

    private void Cancelar_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
