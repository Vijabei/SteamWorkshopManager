using System;
using System.Windows.Forms;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Threading;

namespace WorkshopManager
{
    public partial class MainForm : Form
    {
        private readonly TextBox targetDirBox;
        private readonly TextBox scriptFileBox;
        private readonly TextBox steamCmdPathBox;
        private readonly Button browseTargetButton;
        private readonly Button browseScriptButton;
        private readonly Button browseSteamCmdButton;
        private readonly Button installButton;
        private readonly ProgressBar progressBar;
        private readonly TextBox logBox;
        private readonly CheckBox cleanupCheckBox;
        private readonly Label statusLabel;
        private readonly Logger logger;
        private CancellationTokenSource cancellationTokenSource;
        private Button cancelButton;
        private readonly Settings settings;

        public MainForm()
        {
            try
            {
                // Load settings
                settings = Settings.Load();

                // Initialize cancellation token source
                cancellationTokenSource = new CancellationTokenSource();

                // Initialize controls
                targetDirBox = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(5),
                    Text = settings.LastTargetDirectory
                };

                scriptFileBox = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(5),
                    Text = settings.LastScriptFile
                };

                steamCmdPathBox = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(5),
                    Text = settings.SteamCmdPath
                };

                // Initialize buttons
                browseTargetButton = new Button
                {
                    Text = "Browse",
                    Dock = DockStyle.Fill,
                    Margin = new Padding(5)
                };
                browseTargetButton.Click += BrowseTargetDir;

                browseScriptButton = new Button
                {
                    Text = "Browse",
                    Dock = DockStyle.Fill,
                    Margin = new Padding(5)
                };
                browseScriptButton.Click += BrowseScriptFile;

                browseSteamCmdButton = new Button
                {
                    Text = "Browse",
                    Dock = DockStyle.Fill,
                    Margin = new Padding(5)
                };
                browseSteamCmdButton.Click += BrowseSteamCmd;

                installButton = new Button
                {
                    Text = "Install Mods",
                    Dock = DockStyle.Fill,
                    Margin = new Padding(5),
                    Height = 40
                };
                installButton.Click += InstallMods;

                // Initialize cancel button
                cancelButton = new Button
                {
                    Text = "Cancel",
                    Dock = DockStyle.Fill,
                    Margin = new Padding(5),
                    Enabled = false
                };
                cancelButton.Click += CancelInstallation;

                // Initialize progress bar
                progressBar = new ProgressBar
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(5),
                    Style = ProgressBarStyle.Continuous,
                    Value = 0
                };

                // Initialize log box
                logBox = new TextBox
                {
                    Multiline = true,
                    ScrollBars = ScrollBars.Vertical,
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    BackColor = Color.White,
                    Margin = new Padding(5)
                };

                // Initialize checkbox
                cleanupCheckBox = new CheckBox
                {
                    Text = "Clean up workshop files after installation",
                    Checked = false,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(5)
                };

                // Initialize status label
                statusLabel = new Label
                {
                    Text = "Ready",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft
                };

                // Initialize logger after UI components
                logger = new Logger(logBox);

                // Initialize base form
                InitializeComponent();

                // Setup the UI layout
                SetupUI();

                // Form closing event
                FormClosing += MainForm_FormClosing;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error initializing the application: {ex.Message}",
                    "Initialization Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                throw;
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Save settings
            settings.LastTargetDirectory = targetDirBox.Text;
            settings.LastScriptFile = scriptFileBox.Text;
            settings.SteamCmdPath = steamCmdPathBox.Text;
            settings.Save();
        }

        private void SetupUI()
        {
            // Configure form properties
            Text = "Workshop Mod Manager";
            Size = new Size(800, 600);
            MinimumSize = new Size(600, 400);
            StartPosition = FormStartPosition.CenterScreen;

            // Create layout
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 7,
                Padding = new Padding(10)
            };

            // Configure column styles
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));

            // Create labels
            var steamCmdLabel = new Label
            {
                Text = "SteamCMD Path:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight
            };

            var targetDirLabel = new Label
            {
                Text = "Target Directory:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight
            };

            var scriptFileLabel = new Label
            {
                Text = "Script File:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight
            };

            // Add controls to layout
            layout.Controls.Add(steamCmdLabel, 0, 0);
            layout.Controls.Add(steamCmdPathBox, 1, 0);
            layout.Controls.Add(browseSteamCmdButton, 2, 0);

            layout.Controls.Add(scriptFileLabel, 0, 1);
            layout.Controls.Add(scriptFileBox, 1, 1);
            layout.Controls.Add(browseScriptButton, 2, 1);

            layout.Controls.Add(targetDirLabel, 0, 2);
            layout.Controls.Add(targetDirBox, 1, 2);
            layout.Controls.Add(browseTargetButton, 2, 2);

            layout.Controls.Add(cleanupCheckBox, 1, 3);
            layout.Controls.Add(installButton, 1, 4);
            layout.Controls.Add(cancelButton, 2, 4);
            layout.Controls.Add(progressBar, 1, 5);
            layout.Controls.Add(statusLabel, 0, 5);

            layout.SetColumnSpan(logBox, 3);
            layout.Controls.Add(logBox, 0, 6);

            // Configure row styles
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            Controls.Add(layout);
        }

        private void BrowseSteamCmd(object sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "SteamCMD|steamcmd.exe|All files (*.*)|*.*",
                Title = "Select SteamCMD executable"
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                steamCmdPathBox.Text = dialog.FileName;
            }
        }

        private void BrowseTargetDir(object sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select target directory for mods"
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                targetDirBox.Text = dialog.SelectedPath;
            }
        }

        private void BrowseScriptFile(object sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                Title = "Select SteamCMD script file"
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                scriptFileBox.Text = dialog.FileName;
            }
        }

        private bool ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(steamCmdPathBox.Text) || !Settings.ValidateSteamCmdPath(steamCmdPathBox.Text))
            {
                MessageBox.Show(
                    "Please select a valid steamcmd.exe path.",
                    "Validation Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return false;
            }

            if (string.IsNullOrWhiteSpace(targetDirBox.Text))
            {
                MessageBox.Show(
                    "Please select a target directory.",
                    "Validation Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return false;
            }

            if (string.IsNullOrWhiteSpace(scriptFileBox.Text))
            {
                MessageBox.Show(
                    "Please select a script file.",
                    "Validation Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return false;
            }

            if (!File.Exists(scriptFileBox.Text))
            {
                MessageBox.Show(
                    "The selected script file does not exist.",
                    "Validation Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return false;
            }

            return true;
        }

        private async void InstallMods(object sender, EventArgs e)
        {
            if (!ValidateInputs()) return;

            SetControlsEnabled(false);
            cancelButton.Enabled = true;
            progressBar.Style = ProgressBarStyle.Continuous;

            cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var progress = new Progress<InstallationProgress>(UpdateProgress);
                var installationService = new InstallationService(
                    logger,
                    steamCmdPathBox.Text,
                    targetDirBox.Text,
                    scriptFileBox.Text,
                    cleanupCheckBox.Checked
                );

                await installationService.InstallModsAsync(progress, cancellationTokenSource.Token);

                MessageBox.Show(
                    "Installation completed successfully!",
                    "Success",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (OperationCanceledException)
            {
                logger.Warning("Installation was cancelled");
                MessageBox.Show(
                    "Installation was cancelled by user.",
                    "Cancelled",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                logger.Error($"Installation failed: {ex.Message}");
                MessageBox.Show(
                    $"Installation failed: {ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            finally
            {
                SetControlsEnabled(true);
                cancelButton.Enabled = false;
                progressBar.Value = 0;
                UpdateStatus("Ready");
            }
        }

        private void CancelInstallation(object sender, EventArgs e)
        {
            if (MessageBox.Show(
                "Are you sure you want to cancel the installation?",
                "Confirm Cancellation",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes)
            {
                cancellationTokenSource?.Cancel();
                UpdateStatus("Cancelling...");
            }
        }

        private void UpdateProgress(InstallationProgress progress)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<InstallationProgress>(UpdateProgress), progress);
                return;
            }

            progressBar.Value = progress.ProgressPercentage;
            UpdateStatus(progress.CurrentOperation);
        }

        private void SetControlsEnabled(bool enabled)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<bool>(SetControlsEnabled), enabled);
                return;
            }

            steamCmdPathBox.Enabled = enabled;
            targetDirBox.Enabled = enabled;
            scriptFileBox.Enabled = enabled;
            browseSteamCmdButton.Enabled = enabled;
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                cancellationTokenSource?.Dispose();
                targetDirBox?.Dispose();
                scriptFileBox?.Dispose();
                steamCmdPathBox?.Dispose();
                browseTargetButton?.Dispose();
                browseScriptButton?.Dispose();
                browseSteamCmdButton?.Dispose();
                installButton?.Dispose();
                progressBar?.Dispose();
                logBox?.Dispose();
                cleanupCheckBox?.Dispose();
                statusLabel?.Dispose();
                cancelButton?.Dispose();
                components?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}