using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace AogCanBridge
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            using (Mutex instanceMutex = new Mutex(true, "Local\\AogCanBridge.SingleInstance",
                out bool isFirstInstance))
            {
                if (!isFirstInstance)
                {
                    // Relaunching (e.g. double-clicking the shortcut again while
                    // it's minimized to the tray) should just bring the running
                    // instance forward, the way a normal single-instance app
                    // does - not report "already running" and do nothing.
                    using (EventWaitHandle showEvent = new EventWaitHandle(false,
                        EventResetMode.AutoReset, "Local\\AogCanBridge.Show"))
                    {
                        showEvent.Set();
                    }
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool autoStart = args.Any(argument =>
                    string.Equals(argument, "--autostart", StringComparison.OrdinalIgnoreCase));
                bool minimized = args.Any(argument =>
                    string.Equals(argument, "--minimized", StringComparison.OrdinalIgnoreCase));
                using (EventWaitHandle stopEvent = new EventWaitHandle(false,
                    EventResetMode.AutoReset, "Local\\AogCanBridge.Stop"))
                using (EventWaitHandle showEvent = new EventWaitHandle(false,
                    EventResetMode.AutoReset, "Local\\AogCanBridge.Show"))
                {
                    BridgeForm form = new BridgeForm(autoStart, minimized);
                    RegisteredWaitHandle stopRegistration = ThreadPool.RegisterWaitForSingleObject(
                        stopEvent,
                        (_, __) =>
                        {
                            if (form.IsHandleCreated && !form.IsDisposed)
                                form.BeginInvoke(new Action(form.Close));
                        },
                        null,
                        Timeout.Infinite,
                        true);
                    RegisteredWaitHandle showRegistration = ThreadPool.RegisterWaitForSingleObject(
                        showEvent,
                        (_, __) =>
                        {
                            if (form.IsHandleCreated && !form.IsDisposed)
                                form.BeginInvoke(new Action(form.RestoreFromTray));
                        },
                        null,
                        Timeout.Infinite,
                        true);
                    Application.Run(form);
                    stopRegistration.Unregister(null);
                    showRegistration.Unregister(null);
                }
            }
        }
    }
}
