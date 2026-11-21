using System;
using System.Windows.Forms;

namespace WorkshopManager 
{
    static class Program 
    {
        [STAThread]
        static void Main()
        {
            // Before any window exists. Without it Windows treats the app as
            // made for 96 DPI and stretches it as a bitmap on any larger
            // scaling, which blurs every letter on 1440p and 4K screens.
            // SystemAware rather than PerMonitorV2: the layout is scaled once
            // at startup (see MainForm), and a few sizes are set by hand that
            // would not follow a later DPI change. On a second monitor with
            // different scaling Windows still stretches - as it did before.
            Application.SetHighDpiMode(HighDpiMode.SystemAware);

            // Remove the .old exe left behind by a previous self-update
            UpdateService.CleanupAfterUpdate();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}