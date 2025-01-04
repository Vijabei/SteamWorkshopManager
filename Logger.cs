using System;
using System.IO;
using System.Windows.Forms;

namespace WorkshopManager
{
    public class Logger
    {
        private readonly TextBox logBox;
        private readonly string logFilePath;

        public Logger(TextBox logBox)
        {
            this.logBox = logBox;
            this.logFilePath = Path.Combine(
                Application.StartupPath, 
                $"workshopmanager_{DateTime.Now:yyyyMMdd_HHmmss}.log"
            );
        }

        public void Info(string message) => Log(message, "INFO");
        public void Warning(string message) => Log(message, "WARN");
        public void Error(string message) => Log(message, "ERROR");
        public void Debug(string message) => Log(message, "DEBUG");

        private void Log(string message, string level)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logMessage = $"[{timestamp}] [{level}] {message}";

            // Log to file
            try
            {
                File.AppendAllText(logFilePath, logMessage + Environment.NewLine);
            }
            catch (Exception ex)
            {
                // If file logging fails, at least show it in the UI
                ShowInUi($"Failed to write to log file: {ex.Message}");
            }

            // Log to UI
            ShowInUi(logMessage);
        }

        private void ShowInUi(string message)
        {
            if (logBox.InvokeRequired)
            {
                logBox.Invoke(new Action<string>(ShowInUi), message);
                return;
            }

            logBox.AppendText(message + Environment.NewLine);
            logBox.SelectionStart = logBox.TextLength;
            logBox.ScrollToCaret();
        }
    }
}