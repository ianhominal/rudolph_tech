using RudolphTech.Core.Web;

namespace RudolphTech;

internal static class Program
{
    /// <summary> Per user, not per machine: the app runs inside somebody's desktop session. </summary>
    private const string SingleInstanceMutex = @"Local\RudolphTech.SingleInstance";

    /// <summary> Signalled by "RudolphTech.exe --salir" so the running copy closes on its own terms. </summary>
    internal const string ExitEventName = @"Local\RudolphTech.Exit";

    /// <summary> Passed by the autostart shortcut and by the Run key entry, never by a person. </summary>
    private const string StartupSwitch = "--startup";

    /// <summary> Asks the running copy to close (used by the uninstaller, and handy for scripts). </summary>
    private const string ExitSwitch = "--salir";

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains(ExitSwitch, StringComparer.OrdinalIgnoreCase))
        {
            RequestExitOfRunningInstance();
            return;
        }

        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutex, out var isOnlyInstance);
        if (!isOnlyInstance)
        {
            // Both the Startup shortcut and the Run key can fire at logon; whichever loses the race
            // must disappear quietly instead of greeting the person with a dialog.
            if (args.Contains(StartupSwitch, StringComparer.OrdinalIgnoreCase)) return;

            // Somebody clicked the web app's "Abrir Rudolph Tech" link while the program was open all
            // along, which means the web app is not receiving its heartbeat rather than that the program
            // is closed. Say so, instead of leaving them clicking a link that appears to do nothing.
            var message = AppProtocol.IsProtocolLaunch(args)
                ? "Rudolph Tech ya está abierto. Miralo en los iconos al lado del reloj." + Environment.NewLine + Environment.NewLine
                    + "Si la web dice que está cerrado, revisá la conexión a internet de esta PC."
                : "Rudolph Tech ya está abierto. Miralo en los iconos al lado del reloj.";

            MessageBox.Show(message, "Rudolph Tech", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Anything else (including a rudolph:// launch, which Windows passes as the whole URI) means
        // "just start normally": the link's only job is getting the program open.

        // The tray icon is a System.Windows.Forms.NotifyIcon (WPF has no tray icon of its own), so
        // its visuals still go through the Windows Forms rendering setup, exactly like before.
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        var app = new App();
        app.InitializeComponent();
        using var tray = new TrayApplicationContext();
        app.Run();
    }

    private static void RequestExitOfRunningInstance()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(ExitEventName);
            signal.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // Nothing is running, which is exactly the state the caller wanted.
        }
    }
}
