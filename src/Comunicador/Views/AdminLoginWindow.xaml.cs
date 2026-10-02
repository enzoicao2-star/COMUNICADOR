using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Comunicador.Views;

public partial class AdminLoginWindow : UserControl
{
    public event EventHandler? ContinuarSolicitado;
    public event EventHandler? VoltarSolicitado;
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
            Erro.Text = "Use uma senha com pelo menos 8 caracteres.";
            SenhaBox.Focus();
            return;
        }
        ContinuarSolicitado?.Invoke(this, EventArgs.Empty);
    }

    public void MostrarErro(string mensagem) => Erro.Text = mensagem;

    private void Cancelar_OnClick(object sender, RoutedEventArgs e) => VoltarSolicitado?.Invoke(this, EventArgs.Empty);

    private void SenhaBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            VoltarSolicitado?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }
}
