namespace RudolphTech;

/// <summary>
/// The WPF application object. It owns no window of its own: Program.cs decides whether the app
/// should even start (the single instance check and "--salir" both run before this is constructed),
/// and TrayApplicationContext is what opens the one window that ever exists, from the tray icon.
/// ShutdownMode is OnExplicitShutdown (set in App.xaml) precisely because that one window can close
/// (Cerrar, or the [x] button) without the tray icon, the schedule timer, or the app itself going
/// away with it.
/// </summary>
public partial class App : System.Windows.Application
{
}
