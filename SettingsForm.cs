using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace WorkshopManager
{
    /// <summary>
    /// One place for everything that is set up once and then forgotten -
    /// SteamCMD, the default install folder, download behaviour, appearance.
    /// Keeping these out of the main window leaves it to the actual workflow:
    /// pick mods, install them.
    /// </summary>
    public class SettingsForm : Form
    {
        private readonly Settings settings;
        private readonly ModLibrary library;

        private readonly TextBox steamCmdBox;
        private readonly TextBox installFolderBox;
        private readonly ComboBox themeCombo;
        private readonly ComboBox channelCombo;
        private readonly NumericUpDown batchSize;
        private readonly NumericUpDown retries;
        private readonly CheckBox cleanupBox;
        private readonly CheckBox skipInstalledBox;
        private readonly CheckBox checkUpdatesBox;
        private readonly ComboBox folderNamingCombo;
        private readonly CheckBox workshopDatesBox;
        private readonly Label steamCmdState;

        public SettingsForm(Settings settings, ModLibrary library)
        {
            this.settings = settings;
            this.library = library;

            Text = "Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(700, 740);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                Padding = new Padding(14),
                AutoScroll = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));

            int row = 0;

            // Rows are sized for the current base font; it grew, so did these.
            void AddRow(int height = 36) => layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));

            // --- SteamCMD -----------------------------------------------
            AddRow();
            var downloadHeading = SectionLabel("Downloading");
            layout.Controls.Add(downloadHeading, 0, row);
            layout.SetColumnSpan(downloadHeading, 3);
            row++;

            AddRow();
            steamCmdBox = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(3), Text = settings.SteamCmdPath };
            var browseSteamCmd = new Button { Text = "Browse...", Dock = DockStyle.Fill, Margin = new Padding(3) };
            browseSteamCmd.Click += BrowseSteamCmd;
            layout.Controls.Add(FieldLabel("SteamCMD:"), 0, row);
            layout.Controls.Add(steamCmdBox, 1, row);
            layout.Controls.Add(browseSteamCmd, 2, row);
            row++;

            AddRow(34);
            steamCmdState = new Label { Dock = DockStyle.Fill, Font = Theme.SmallFont, Margin = new Padding(3, 0, 3, 0) };
            var getSteamCmd = new Button { Text = "Download it for me", Dock = DockStyle.Fill, Margin = new Padding(3) };
            getSteamCmd.Click += DownloadSteamCmd;
            layout.Controls.Add(steamCmdState, 1, row);
            layout.Controls.Add(getSteamCmd, 2, row);
            row++;

            AddRow();
            installFolderBox = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(3), Text = settings.LastTargetDirectory };
            var browseFolder = new Button { Text = "Browse...", Dock = DockStyle.Fill, Margin = new Padding(3) };
            browseFolder.Click += BrowseInstallFolder;
            layout.Controls.Add(FieldLabel("Install folder:"), 0, row);
            layout.Controls.Add(installFolderBox, 1, row);
            layout.Controls.Add(browseFolder, 2, row);
            row++;

            AddRow();
            batchSize = Spinner(1, 200, settings.BatchSize);
            layout.Controls.Add(FieldLabel("Mods per batch:"), 0, row);
            layout.Controls.Add(batchSize, 1, row);
            layout.Controls.Add(Hint("Smaller batches are\nslower but more reliable"), 2, row);
            row++;

            AddRow();
            retries = Spinner(0, 10, settings.MaxRetries);
            layout.Controls.Add(FieldLabel("Retries:"), 0, row);
            layout.Controls.Add(retries, 1, row);
            row++;

            AddRow(32);
            cleanupBox = new CheckBox
            {
                Text = "Delete the raw SteamCMD downloads after installing",
                Checked = settings.CleanupAfterInstall,
                Dock = DockStyle.Fill,
                Margin = new Padding(3, 0, 3, 0)
            };
            layout.Controls.Add(cleanupBox, 1, row);
            layout.SetColumnSpan(cleanupBox, 2);
            row++;

            AddRow(32);
            skipInstalledBox = new CheckBox
            {
                Text = "Skip mods that are already installed",
                Checked = settings.SkipInstalledMods,
                Dock = DockStyle.Fill,
                Margin = new Padding(3, 0, 3, 0)
            };
            layout.Controls.Add(skipInstalledBox, 1, row);
            layout.SetColumnSpan(skipInstalledBox, 2);
            row++;

            // --- Archiving ------------------------------------------------
            AddRow(42);
            var archiveHeading = SectionLabel("Archiving");
            layout.Controls.Add(archiveHeading, 0, row);
            layout.SetColumnSpan(archiveHeading, 3);
            row++;

            AddRow();
            folderNamingCombo = Choice("Workshop ID", "Mod title");
            folderNamingCombo.SelectedItem =
                "Title".Equals(settings.ModFolderNaming, StringComparison.OrdinalIgnoreCase)
                    ? "Mod title" : "Workshop ID";
            layout.Controls.Add(FieldLabel("Folder names:"), 0, row);
            layout.Controls.Add(folderNamingCombo, 1, row);
            layout.Controls.Add(Hint("Most games need\nthe ID"), 2, row);
            row++;

            AddRow(32);
            workshopDatesBox = new CheckBox
            {
                Text = "Date files by when the mod was published, not downloaded",
                Checked = settings.UseWorkshopDates,
                Dock = DockStyle.Fill,
                Margin = new Padding(3, 0, 3, 0)
            };
            layout.Controls.Add(workshopDatesBox, 1, row);
            layout.SetColumnSpan(workshopDatesBox, 2);
            row++;

            // --- Appearance and updates ---------------------------------
            AddRow(42);
            var appearanceHeading = SectionLabel("Appearance and updates");
            layout.Controls.Add(appearanceHeading, 0, row);
            layout.SetColumnSpan(appearanceHeading, 3);
            row++;

            AddRow();
            themeCombo = Choice("Dark", "Light");
            themeCombo.SelectedItem = settings.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
            layout.Controls.Add(FieldLabel("Theme:"), 0, row);
            layout.Controls.Add(themeCombo, 1, row);
            row++;

            AddRow();
            channelCombo = Choice("Stable", "Beta");
            channelCombo.SelectedItem = settings.UpdateChannel.Equals("Beta", StringComparison.OrdinalIgnoreCase) ? "Beta" : "Stable";
            layout.Controls.Add(FieldLabel("Update channel:"), 0, row);
            layout.Controls.Add(channelCombo, 1, row);
            layout.Controls.Add(Hint("Beta = newer,\nless tested"), 2, row);
            row++;

            AddRow(32);
            checkUpdatesBox = new CheckBox
            {
                Text = "Look for updates when the app starts",
                Checked = settings.CheckForUpdates,
                Dock = DockStyle.Fill,
                Margin = new Padding(3, 0, 3, 0)
            };
            layout.Controls.Add(checkUpdatesBox, 1, row);
            layout.SetColumnSpan(checkUpdatesBox, 2);
            row++;

            // --- Library -------------------------------------------------
            AddRow(42);
            var libraryHeading = SectionLabel("Mod library");
            layout.Controls.Add(libraryHeading, 0, row);
            layout.SetColumnSpan(libraryHeading, 3);
            row++;

            AddRow(46);
            var libraryInfo = new Label
            {
                Dock = DockStyle.Fill,
                Font = Theme.SmallFont,
                Text = $"{library.Count} mods archived\n{library.FilePath}",
                Margin = new Padding(3, 0, 3, 0)
            };
            var openLibrary = new Button { Text = "Open folder", Dock = DockStyle.Fill, Margin = new Padding(3) };
            openLibrary.Click += (s, e) => OpenLibraryFolder();
            layout.Controls.Add(libraryInfo, 1, row);
            layout.Controls.Add(openLibrary, 2, row);
            row++;

            AddRow(34);
            var exportHint = Hint("Readable archive - one file per mod, works without this app");
            var exportButton = new Button { Text = "Export as Markdown", Dock = DockStyle.Fill, Margin = new Padding(3) };
            exportButton.Click += ExportLibrary;
            layout.Controls.Add(exportHint, 1, row);
            layout.Controls.Add(exportButton, 2, row);
            row++;

            // --- Buttons --------------------------------------------------
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 46,
                Padding = new Padding(10)
            };

            var cancel = new Button { Text = "Cancel", Size = new Size(100, 26), DialogResult = DialogResult.Cancel };
            var ok = new Button { Text = "Save", Size = new Size(100, 26) };
            ok.Click += Save;

            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);

            Controls.Add(layout);
            Controls.Add(buttons);
            CancelButton = cancel;

            Theme.Apply(this);
            Theme.StylePrimary(ok);
            Theme.ApplyTitleBar(this);
            RefreshSteamCmdState();
        }

        private static Label SectionLabel(string text) => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            Font = Theme.BoldFont,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(0, 6, 0, 2)
        };

        private static Label FieldLabel(string text) => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(3)
        };

        /// <summary>
        /// The note beside a field. The column is narrow, so the texts carry
        /// their own line breaks rather than being wrapped mid-phrase.
        /// </summary>
        private static Label Hint(string text) => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            Font = Theme.SmallFont,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
            Margin = new Padding(3)
        };

        private static ComboBox Choice(params string[] options)
        {
            var combo = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(3, 4, 3, 4)
            };
            combo.Items.AddRange(options);
            return combo;
        }

        private static NumericUpDown Spinner(int min, int max, int value) => new()
        {
            Dock = DockStyle.Fill,
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Margin = new Padding(3, 4, 3, 4)
        };

        /// <summary>
        /// Says in plain words whether SteamCMD is usable, so nobody has to
        /// guess what belongs in that box.
        /// </summary>
        private void RefreshSteamCmdState()
        {
            if (Settings.ValidateSteamCmdPath(steamCmdBox.Text))
            {
                steamCmdState.Text = "Ready to use.";
                steamCmdState.ForeColor = Theme.Success;
            }
            else if (string.IsNullOrWhiteSpace(steamCmdBox.Text))
            {
                steamCmdState.Text = "Not set up yet - downloading it is the easy way.";
                steamCmdState.ForeColor = Theme.Warning;
            }
            else
            {
                steamCmdState.Text = "That path does not point at steamcmd.exe.";
                steamCmdState.ForeColor = Theme.Error;
            }
        }

        private void BrowseSteamCmd(object sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "SteamCMD|steamcmd.exe|All files (*.*)|*.*",
                Title = "Select steamcmd.exe"
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                steamCmdBox.Text = dialog.FileName;
                RefreshSteamCmdState();
            }
        }

        private async void DownloadSteamCmd(object sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Choose an empty folder to install SteamCMD into"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            var button = (Button)sender;
            button.Enabled = false;
            steamCmdState.Text = "Downloading SteamCMD...";
            steamCmdState.ForeColor = Theme.TextDim;

            try
            {
                steamCmdBox.Text = await SteamCmdDownloader.DownloadAndExtractAsync(
                    dialog.SelectedPath, CancellationToken.None);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"The download failed: {ex.Message}",
                    "SteamCMD", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                button.Enabled = true;
                RefreshSteamCmdState();
            }
        }

        private void BrowseInstallFolder(object sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Choose the folder your game loads mods from"
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                installFolderBox.Text = dialog.SelectedPath;
            }
        }

        private void ExportLibrary(object sender, EventArgs e)
        {
            if (library.Count == 0)
            {
                MessageBox.Show("There is nothing archived yet. The library fills up as you install mods.",
                    "Nothing to export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dialog = new FolderBrowserDialog
            {
                Description = "Choose where to write the Markdown files"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                var written = ModLibraryExport.Export(library, dialog.SelectedPath);
                MessageBox.Show(
                    $"Wrote {written} mods plus an index to:{Environment.NewLine}{dialog.SelectedPath}",
                    "Export finished", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"The export failed: {ex.Message}",
                    "Export", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenLibraryFolder()
        {
            try
            {
                var folder = System.IO.Path.GetDirectoryName(library.FilePath);
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
            catch
            {
                // Explorer missing or blocked - not worth an error dialog
            }
        }

        private void Save(object sender, EventArgs e)
        {
            settings.SteamCmdPath = steamCmdBox.Text.Trim();
            settings.LastTargetDirectory = installFolderBox.Text.Trim();
            settings.BatchSize = (int)batchSize.Value;
            settings.MaxRetries = (int)retries.Value;
            settings.CleanupAfterInstall = cleanupBox.Checked;
            settings.SkipInstalledMods = skipInstalledBox.Checked;
            settings.CheckForUpdates = checkUpdatesBox.Checked;
            settings.ModFolderNaming =
                (folderNamingCombo.SelectedItem as string) == "Mod title" ? "Title" : "Id";
            settings.UseWorkshopDates = workshopDatesBox.Checked;
            settings.Theme = themeCombo.SelectedItem as string ?? "Dark";
            settings.UpdateChannel = channelCombo.SelectedItem as string ?? "Stable";
            settings.Save();

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
