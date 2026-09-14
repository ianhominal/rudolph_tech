using System.Windows;

namespace RudolphTech.Settings;

/// <summary>
/// A small "type one value" dialog. Used today only by "Cambiar la dirección de la aplicación" in
/// the Vinculación card, for the one state where there is nothing to authenticate yet: not linked.
/// Unlike PasswordWindow, validation is a synchronous, local, pure function (RudolphTech.Core.Web.
/// AppUrl.TryNormalize for the address case), so there is no network call and no busy state to show.
/// </summary>
public partial class TextPromptWindow : Window
{
    private readonly Func<string, (bool Ok, string Result)> _validate;

    /// <summary> The validated, normalized value, once ShowDialog() returns true. Never set otherwise. </summary>
    public string? AcceptedValue { get; private set; }

    public TextPromptWindow(string title, string subtitle, string initialValue, Func<string, (bool Ok, string Result)> validate)
    {
        _validate = validate;

        InitializeComponent();
        TitleText.Text = title;
        Subtitle.Text = subtitle;
        ValueInput.Text = initialValue;
        Loaded += (_, _) =>
        {
            ValueInput.Focus();
            ValueInput.SelectAll();
        };
    }

    private void OnValueKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) Accept();
    }

    private void OnAccept(object sender, RoutedEventArgs e) => Accept();

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Accept()
    {
        var (ok, result) = _validate(ValueInput.Text);
        if (!ok)
        {
            ErrorText.Text = result;
            return;
        }

        AcceptedValue = result;
        DialogResult = true;
        Close();
    }
}
