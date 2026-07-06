using System;
using System.Windows.Forms;

namespace WorkshopManager 
{
    static class Program 
    {
        [STAThread]
        static void Main()
        {
            // Remove the .old exe left behind by a previous self-update
            UpdateService.CleanupAfterUpdate();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}