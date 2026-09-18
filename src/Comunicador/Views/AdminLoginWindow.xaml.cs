using System.Windows;
using System.Windows.Input;

namespace Comunicador.Views;

public partial class AdminLoginWindow : Window
{
    public string Senha => SenhaBox.Password;

    public AdminLoginWindow(bool adminAtivo)
    {
        InitializeComponent();
        Descricao.Text = adminAtivo
            ? "Digite a senha novamente para remover o administrador deste computador."
            : "Na primeira configuração, esta senha define o administrador. Depois ela recupera o acesso em outro computador.";
        Loaded += (_, _) => SenhaBox.Focus();
    }

    private void Continuar_OnClick(object sender, RoutedEventArgs e)
    {
        if (Senha.Length < 8)
        {
            MessageBox.Show("Use uma senha com pelo menos 8 caracteres.", "Administrador",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            SenhaBox.Focus();
            return;
        }
        DialogResult = true;
    }

    private void Cancelar_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void SenhaBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
    }
}
