using System;
using System.Collections.Generic;
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
                    List<LanguageInfo> languages = Localization.DiscoverLanguages();
                    Localization.SetLanguage(Localization.ResolveSavedLanguage(languages));
                    MessageBox.Show(Localization.Get("AlreadyRunning"), "AOG CAN Bridge",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                    Application.Run(form);
                    stopRegistration.Unregister(null);
                }
            }
        }
    }
}
