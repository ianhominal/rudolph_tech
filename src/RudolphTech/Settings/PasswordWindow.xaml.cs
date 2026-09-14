using System.Windows;
using System.Windows.Input;

namespace RudolphTech.Settings;

/// <summary>
/// Asks for the web password before the settings window opens. The password is checked against the
/// app itself (POST /api/ingresar, through AgentService.LoginAsync) and then forgotten by this
/// window: whoever opens it keeps the verified password in memory for as long as the settings
/// window it feeds stays open, but this prompt never writes it anywhere.
///
/// The prompt text comes from RudolphTech.Core.Settings.LinkFlow.PromptText: it already knows
/// whether this PC is linked, so it is the one that decides whether the person is linking or just
/// looking.
/// </summary>
public partial class PasswordWindow : Window
{
    private readonly Func<string, CancellationToken, Task<(bool Ok, string Error)>> _verify;

    /// <summary> The password that was verified, once ShowDialog() returns true. Never set otherwise. </summary>
    public string? VerifiedPassword { get; private set; }

    public PasswordWindow(string appUrl, string promptText, Func<string, CancellationToken, Task<(bool Ok, string Error)>> verify)
    {
        _verify = verify;

        InitializeComponent();
        Subtitle.Text = $"{promptText} Se verifica contra {appUrl}.";
        Loaded += (_, _) => PasswordInput.Focus();
    }

    private async void OnPasswordKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await AcceptAsync();
    }

    private async void OnAccept(object sender, RoutedEventArgs e) => await AcceptAsync();

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async Task AcceptAsync()
    {
        var password = PasswordInput.Password;
        if (string.IsNullOrEmpty(password))
        {
            ErrorText.Text = "Escribí la contraseña.";
            return;
        }

        SetBusy(true);
        try
        {
            var (ok, error) = await _verify(password, CancellationToken.None);
            if (ok)
            {
                VerifiedPassword = password;
                DialogResult = true;
                Close();
                return;
            }

            ErrorText.Text = error;
            PasswordInput.Clear();
            PasswordInput.Focus();
        }
        catch (Exception exception)
        {
            ErrorText.Text = $"No se pudo verificar: {exception.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        AcceptButton.IsEnabled = !busy;
        CancelButton.IsEnabled = !busy;
        PasswordInput.IsEnabled = !busy;
        AcceptButton.Content = busy ? "Verificando" : "Entrar";
        if (busy) ErrorText.Text = "";
    }
}
