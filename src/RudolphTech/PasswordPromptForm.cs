namespace RudolphTech;

/// <summary>
/// Asks for the web password before the settings window opens. The password is checked against the
/// app itself (POST /api/ingresar) and then forgotten: it is never written anywhere.
/// </summary>
public sealed class PasswordPromptForm : Form
{
    private readonly TextBox _password = new() { UseSystemPasswordChar = true, Width = 300 };
    private readonly Label _message = new() { AutoSize = false, Width = 300, Height = 34, ForeColor = Color.Firebrick };
    private readonly Button _accept = new() { Text = "Entrar", DialogResult = DialogResult.None, Width = 100 };
    private readonly Button _cancel = new() { Text = "Cancelar", DialogResult = DialogResult.Cancel, Width = 100 };
    private readonly Func<string, CancellationToken, Task<(bool Ok, string Error)>> _verify;

    public PasswordPromptForm(string appUrl, Func<string, CancellationToken, Task<(bool Ok, string Error)>> verify)
    {
        _verify = verify;

        Text = "Rudolph Tech";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(340, 190);
        Font = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;

        var title = new Label { Text = "Contraseña de la aplicación", AutoSize = true, Location = new Point(16, 16) };
        var subtitle = new Label
        {
            Text = $"Se verifica contra {appUrl}.",
            AutoSize = false,
            Width = 300,
            Height = 32,
            Location = new Point(16, 38),
            ForeColor = SystemColors.GrayText,
        };
        _password.Location = new Point(16, 74);
        _message.Location = new Point(16, 104);
        _accept.Location = new Point(120, 146);
        _cancel.Location = new Point(228, 146);

        Controls.AddRange([title, subtitle, _password, _message, _accept, _cancel]);
        AcceptButton = _accept;
        CancelButton = _cancel;
        _accept.Click += OnAccept;
    }

    private async void OnAccept(object? sender, EventArgs e)
    {
        var password = _password.Text;
        if (string.IsNullOrEmpty(password))
        {
            _message.Text = "Escribí la contraseña.";
            return;
        }

        SetBusy(true);
        try
        {
            var (ok, error) = await _verify(password, CancellationToken.None);
            if (ok)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            _message.Text = error;
        }
        catch (Exception exception)
        {
            _message.Text = $"No se pudo verificar: {exception.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _accept.Enabled = !busy;
        _password.Enabled = !busy;
        _accept.Text = busy ? "Verificando" : "Entrar";
        if (busy) _message.Text = "";
    }
}
