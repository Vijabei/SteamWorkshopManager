using System;
using System.Windows.Forms;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Linq;

namespace WorkshopManager
{
    public partial class MainForm : Form
    {
            private void InitializeComponent()
    {
        // Leere Implementierung ist ausreichend, da wir die UI programmatisch aufbauen
    }
        private readonly TextBox targetDirBox;
        private readonly TextBox scriptFileBox;
        private readonly Button browseTargetButton;
        private readonly Button browseScriptButton;
        private readonly Button installButton;
        private readonly ProgressBar progressBar;
        private readonly TextBox logBox;
        private readonly CheckBox cleanupCheckBox;
        private readonly Label statusLabel;
        private readonly string steamCmdPath;

        public MainForm()
        {
            targetDirBox = new TextBox();
            scriptFileBox = new TextBox();
            browseTargetButton = new Button();
            browseScriptButton = new Button();
            installButton = new Button();
            progressBar = new ProgressBar();
            logBox = new TextBox();
            cleanupCheckBox = new CheckBox();
            statusLabel = new Label();
            
            steamCmdPath = ConfigHandler.GetSteamCmdPath();
            
            InitializeComponent();
            SetupUI();
            CheckSteamCmdExists();
        }

        private void CheckSteamCmdExists()
        {
            if (!ConfigHandler.ValidateSteamCmdPath(steamCmdPath))
            {
                MessageBox.Show(
                    "steamcmd.exe not found at configured path: " + steamCmdPath + 
                    "\nPlease check your configuration file.", 
                    "Error", 
                    MessageBoxButtons.OK, 
                    MessageBoxIcon.Error
                );
                Environment.Exit(1);
            }
        }

        private void SetupUI()
        {
            Text = "Workshop Mod Manager";
            Size = new Size(800, 600);
            MinimumSize = new Size(600, 400);
            StartPosition = FormStartPosition.CenterScreen;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 6,
                Padding = new Padding(10)
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));

            // Configure controls
            targetDirBox.Dock = DockStyle.Fill;
            targetDirBox.Margin = new Padding(5);

            scriptFileBox.Dock = DockStyle.Fill;
            scriptFileBox.Margin = new Padding(5);

            browseTargetButton.Text = "Browse";
            browseTargetButton.Dock = DockStyle.Fill;
            browseTargetButton.Margin = new Padding(5);
            browseTargetButton.Click += BrowseTargetDir;

            browseScriptButton.Text = "Browse";
            browseScriptButton.Dock = DockStyle.Fill;
            browseScriptButton.Margin = new Padding(5);
            browseScriptButton.Click += BrowseScriptFile;

            cleanupCheckBox.Text = "Clean up workshop files after installation";
            cleanupCheckBox.Checked = false;
            cleanupCheckBox.Dock = DockStyle.Fill;
            cleanupCheckBox.Margin = new Padding(5);

            installButton.Text = "Install Mods";
            installButton.Dock = DockStyle.Fill;
            installButton.Margin = new Padding(5);
            installButton.Height = 40;
            installButton.Click += InstallMods;

            progressBar.Dock = DockStyle.Fill;
            progressBar.Margin = new Padding(5);
            progressBar.Style = ProgressBarStyle.Marquee;
            progressBar.MarqueeAnimationSpeed = 0;

            statusLabel.Text = "Ready";
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;

            logBox.Multiline = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.Dock = DockStyle.Fill;
            logBox.ReadOnly = true;
            logBox.BackColor = Color.White;
            logBox.Margin = new Padding(5);

            // Add controls to layout
            layout.Controls.Add(new Label 
            { 
                Text = "Target Directory:", 
                Dock = DockStyle.Fill, 
                TextAlign = ContentAlignment.MiddleRight 
            }, 0, 0);
            layout.Controls.Add(targetDirBox, 1, 0);
            layout.Controls.Add(browseTargetButton, 2, 0);

            layout.Controls.Add(new Label 
            { 
                Text = "Script File:", 
                Dock = DockStyle.Fill, 
                TextAlign = ContentAlignment.MiddleRight 
            }, 0, 1);
            layout.Controls.Add(scriptFileBox, 1, 1);
            layout.Controls.Add(browseScriptButton, 2, 1);

            layout.Controls.Add(cleanupCheckBox, 1, 2);
            layout.Controls.Add(installButton, 1, 3);
            layout.Controls.Add(progressBar, 1, 4);
            layout.Controls.Add(statusLabel, 0, 4);

            layout.SetColumnSpan(logBox, 3);
            layout.Controls.Add(logBox, 0, 5);

            // Configure row styles
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            Controls.Add(layout);
        }

        private void BrowseTargetDir(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select target directory for mods";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    targetDirBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void BrowseScriptFile(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
                dialog.Title = "Select SteamCMD script file";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    scriptFileBox.Text = dialog.FileName;
                }
            }
        }

        private async void InstallMods(object sender, EventArgs e)
        {
            if (!ValidateInputs()) return;

            SetControlsEnabled(false);
            progressBar.MarqueeAnimationSpeed = 30;

            try
            {
                await Task.Run(RunInstallation);
            }
            catch (Exception ex)
            {
                Log($"Error: {ex.Message}");
                MessageBox.Show($"Installation failed: {ex.Message}", 
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetControlsEnabled(true);
                progressBar.MarqueeAnimationSpeed = 0;
            }
        }

        private bool ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(targetDirBox.Text) || 
                string.IsNullOrWhiteSpace(scriptFileBox.Text))
            {
                MessageBox.Show("Please select both target directory and script file.", 
                    "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        private void RunInstallation()
        {
            Log("Starting mod installation...");
            UpdateStatus("Running SteamCMD...");

            using (var process = new Process())
            {
                process.StartInfo.FileName = steamCmdPath;
                process.StartInfo.Arguments = $"+runscript \"{scriptFileBox.Text}\" +quit";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;

                process.OutputDataReceived += (s, e) => 
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Log($"SteamCMD: {e.Data}");
                };

                process.Start();
                process.BeginOutputReadLine();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new Exception($"SteamCMD exited with code {process.ExitCode}");
                }
            }

            ProcessDownloadedMods();
        }

        private void ProcessDownloadedMods()
        {
            string workshopBase = Path.Combine(
                Path.GetDirectoryName(steamCmdPath), 
                "steamapps", 
                "workshop", 
                "content"
            );
            
            UpdateStatus("Processing mods...");

            if (!Directory.Exists(workshopBase))
            {
                throw new Exception("Workshop directory not found");
            }

            int modsProcessed = 0;
            foreach (var gameDir in Directory.GetDirectories(workshopBase))
            {
                string gameId = Path.GetFileName(gameDir);
                foreach (var modDir in Directory.GetDirectories(gameDir))
                {
                    ProcessMod(modDir, gameId, ref modsProcessed);
                }
            }

            Log($"Installation complete! {modsProcessed} mods processed.");

            if (cleanupCheckBox.Checked && modsProcessed > 0)
            {
                CleanupWorkshopFiles(workshopBase);
            }

            UpdateStatus("Ready");
        }

        private void ProcessMod(string modDir, string gameId, ref int modsProcessed)
        {
            string modId = Path.GetFileName(modDir);
            Log($"Processing mod {modId}...");

            var modsDir = Directory.GetDirectories(modDir, "mods").FirstOrDefault();
            if (modsDir != null)
            {
                Directory.CreateDirectory(targetDirBox.Text);
                CopyDirectory(modsDir, targetDirBox.Text);
                
                File.WriteAllText(
                    Path.Combine(targetDirBox.Text, $"mod_{modId}.info"),
                    $"# Mod Info\nSteam Workshop ID: {modId}\n" +
                    $"Game ID: {gameId}\nInstallation Date: {DateTime.Now}"
                );

                modsProcessed++;
                Log($"Mod {modId} installed successfully");
            }
        }

        private void CleanupWorkshopFiles(string workshopBase)
        {
            UpdateStatus("Cleaning up...");
            try
            {
                Directory.Delete(workshopBase, true);
                Log("Workshop directory cleaned up");
            }
            catch (Exception ex)
            {
                Log($"Cleanup failed: {ex.Message}");
            }
        }

        private void CopyDirectory(string sourceDir, string targetDir)
        {
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(file);
                string destFile = Path.Combine(targetDir, fileName);
                File.Copy(file, destFile, true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(dir);
                string destDir = Path.Combine(targetDir, dirName);
                Directory.CreateDirectory(destDir);
                CopyDirectory(dir, destDir);
            }
        }

        private void SetControlsEnabled(bool enabled)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<bool>(SetControlsEnabled), enabled);
                return;
            }

            targetDirBox.Enabled = enabled;
            scriptFileBox.Enabled = enabled;
            browseTargetButton.Enabled = enabled;
            browseScriptButton.Enabled = enabled;
            installButton.Enabled = enabled;
            cleanupCheckBox.Enabled = enabled;
        }

        private void UpdateStatus(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(UpdateStatus), message);
                return;
            }
            statusLabel.Text = message;
        }

        private void Log(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(Log), message);
                return;
            }

            logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            logBox.SelectionStart = logBox.TextLength;
            logBox.ScrollToCaret();
        }
    }
}