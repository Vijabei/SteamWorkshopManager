using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json.Linq;

namespace WorkshopManager
{
    public partial class MainForm : Form
    {
        private enum BrowserAction { None, AddCollection, CollectSubscriptions }

        private readonly Settings settings;
        private readonly Logger logger;
        private readonly CollectionService collectionService = new();
        private readonly ModLibrary library = new();
        private CancellationTokenSource cancellationTokenSource;

        // Mod list state
        private readonly List<WorkshopItem> modItems = new();
        private readonly Dictionary<WorkshopItem, ListViewItem> listViewItems = new();

        // Tabs
        private TabControl tabControl;
        private TabPage browserTab;
        private TabPage installTab;
        private TabPage logTab;

        // Browser tab
        private WebView2 webView;
        private TextBox addressBox;
        private Button backButton;
        private Button forwardButton;
        private Button reloadButton;
        private Button homeButton;
        private Button addFromBrowserButton;
        private Label browserStatusLabel;
        private BrowserAction currentBrowserAction = BrowserAction.None;
        private bool webViewReady;
        private bool browserBusy;

        // Install tab
        private TextBox targetDirBox;
        private TextBox urlBox;
        private Button browseTargetButton;
        private Button addUrlButton;
        private Button loadScriptButton;
        private Button removeSelectedButton;
        private Button clearListButton;
        private Button checkInstalledButton;
        private Button loadInstalledButton;
        private Button checkRequirementsButton;
        private CheckBox cleanupCheckBox;
        private CheckBox skipInstalledCheckBox;
        private TextBox searchBox;
        private ListView modListView;
        private ModDetailPanel detailPanel;
        private ContextMenuStrip modContextMenu;
        private Button themeButton;
        private Button channelButton;
        private Button settingsButton;
        private Panel bottomBar;
        private SplitContainer listSplit;
        private Button installButton;
        private Button cancelButton;
        private ProgressBar progressBar;
        private Label statusLabel;

        // Log tab
        private TextBox logBox;

        public MainForm()
        {
            try
            {
                settings = Settings.Load();
                cancellationTokenSource = new CancellationTokenSource();

                Theme.SetMode(settings.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase)
                    ? ThemeMode.Light
                    : ThemeMode.Dark);

                InitializeComponent();
                SetupUI();
                ApplyTheme();

                logger = new Logger(logBox);

                FormClosing += MainForm_FormClosing;
                Shown += async (s, e) =>
                {
                    Theme.ApplyTitleBar(this);
                    ApplyInitialSplitterDistance();
                    await InitializeWebViewAsync();
                    await CheckForUpdatesAsync();
                };
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
            settings.LastTargetDirectory = targetDirBox.Text;
            settings.CleanupAfterInstall = cleanupCheckBox.Checked;
            settings.SkipInstalledMods = skipInstalledCheckBox.Checked;
            settings.Save();
        }

        #region UI setup

        private void SetupUI()
        {
            Text = "Workshop Mod Manager";
            Size = new Size(1100, 750);
            MinimumSize = new Size(850, 600);
            StartPosition = FormStartPosition.CenterScreen;

            tabControl = new TabControl { Dock = DockStyle.Fill };

            browserTab = new TabPage("Workshop Browser");
            installTab = new TabPage("Mods && Install");
            logTab = new TabPage("Log");

            SetupBrowserTab();
            SetupInstallTab();
            SetupLogTab();

            tabControl.TabPages.Add(browserTab);
            tabControl.TabPages.Add(installTab);
            tabControl.TabPages.Add(logTab);

            // A slim bar of its own rather than an overlay on the tab strip:
            // WinForms does not reliably clip sibling controls against each
            // other, so the tab control simply painted over a floating button.
            bottomBar = new Panel { Dock = DockStyle.Bottom, Height = 40 };

            themeButton = new Button { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, TabStop = false };
            themeButton.Click += ToggleTheme;
            bottomBar.Controls.Add(themeButton);

            channelButton = new Button { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, TabStop = false };
            channelButton.Click += ToggleUpdateChannel;
            bottomBar.Controls.Add(channelButton);

            settingsButton = new Button { Text = "Settings...", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, TabStop = false };
            settingsButton.Click += OpenSettings;
            bottomBar.Controls.Add(settingsButton);

            var tips = new ToolTip();
            tips.SetToolTip(channelButton,
                "Stable: only finished releases.\r\n" +
                "Beta: pre-release builds as well - newer, but less tested.");
            bottomBar.Resize += (s, e) => PositionThemeButton(bottomBar);
            bottomBar.Paint += (s, e) =>
            {
                using var separator = new Pen(Theme.Border);
                e.Graphics.DrawLine(separator, 0, 0, bottomBar.Width, 0);
            };

            Controls.Add(tabControl);
            Controls.Add(bottomBar);

            PositionThemeButton(bottomBar);
        }

        /// <summary>
        /// Lays the bottom bar out from the right edge. The buttons size
        /// themselves from their text, so this runs again whenever a label
        /// changes and not only on a resize.
        /// </summary>
        private void PositionThemeButton(Panel bar)
        {
            // Room around the label, so a button is never a tight box of text
            foreach (var b in new[] { themeButton, channelButton, settingsButton })
            {
                b.Padding = new Padding(12, 5, 12, 5);
            }

            var top = (bar.ClientSize.Height - themeButton.Height) / 2;
            themeButton.Location = new Point(bar.ClientSize.Width - themeButton.Width - 12, top);
            channelButton.Location = new Point(themeButton.Left - channelButton.Width - 8, top);
            settingsButton.Location = new Point(channelButton.Left - settingsButton.Width - 8, top);
        }

        /// <summary>
        /// Opens the setup dialog and takes over whatever changed. Everything
        /// in there is stored in the settings file, so the main window only
        /// has to re-read it.
        /// </summary>
        private void OpenSettings(object sender, EventArgs e)
        {
            using var dialog = new SettingsForm(settings, library);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            targetDirBox.Text = settings.LastTargetDirectory;
            cleanupCheckBox.Checked = settings.CleanupAfterInstall;
            skipInstalledCheckBox.Checked = settings.SkipInstalledMods;

            Theme.SetMode(settings.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase)
                ? ThemeMode.Light
                : ThemeMode.Dark);
            ApplyTheme();
            RefreshInstalledStatus();
        }

        private async void ToggleUpdateChannel(object sender, EventArgs e)
        {
            settings.UpdateChannel = OnBetaChannel ? "Stable" : "Beta";
            UpdateChannelButtonText();
            settings.Save();

            // Switching to beta should show what is waiting there right away
            if (OnBetaChannel) await CheckForUpdatesAsync();
        }

        private bool OnBetaChannel =>
            settings.UpdateChannel.Equals("Beta", StringComparison.OrdinalIgnoreCase);

        private void UpdateChannelButtonText()
        {
            channelButton.Text = OnBetaChannel ? "Updates: Beta" : "Updates: Stable";
            channelButton.ForeColor = OnBetaChannel ? Theme.Warning : Theme.Text;
            if (bottomBar != null) PositionThemeButton(bottomBar);
        }

        private void ToggleTheme(object sender, EventArgs e)
        {
            Theme.SetMode(Theme.Mode == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark);
            settings.Theme = Theme.Mode.ToString();
            ApplyTheme();
        }

        /// <summary>
        /// Applies the current theme to the whole form. Safe to call again on
        /// every switch: Theme.Apply overwrites what it sets and attaches its
        /// owner-draw handlers only once.
        /// </summary>
        private void ApplyTheme()
        {
            Theme.Apply(this);
            Theme.StylePrimary(installButton);
            detailPanel.RestyleFromTheme();

            // Applied after Theme.Apply, which makes every panel transparent -
            // a transparent bar lets the content above bleed through it.
            bottomBar.BackColor = Theme.Surface;

            themeButton.Text = Theme.Mode == ThemeMode.Dark ? "Light mode" : "Dark mode";
            UpdateChannelButtonText();

            // Row colours are baked into the items, so they have to be redone
            foreach (var item in modItems) UpdateListViewItem(item);

            if (IsHandleCreated) Theme.ApplyTitleBar(this);
            modListView.Invalidate();
            tabControl.Invalidate();
        }

        private void SetupBrowserTab()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Navigation bar
            var navPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 6,
                RowCount = 1,
                Margin = new Padding(0)
            };
            navPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
            navPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
            navPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
            navPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
            navPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            navPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));

            backButton = new Button { Text = "◀", Dock = DockStyle.Fill, Margin = new Padding(3) };
            backButton.Click += (s, e) => { if (webViewReady && webView.CanGoBack) webView.GoBack(); };

            forwardButton = new Button { Text = "▶", Dock = DockStyle.Fill, Margin = new Padding(3) };
            forwardButton.Click += (s, e) => { if (webViewReady && webView.CanGoForward) webView.GoForward(); };

            reloadButton = new Button { Text = "↻", Dock = DockStyle.Fill, Margin = new Padding(3) };
            reloadButton.Click += (s, e) => { if (webViewReady) webView.Reload(); };

            homeButton = new Button { Text = "Home", Dock = DockStyle.Fill, Margin = new Padding(3) };
            homeButton.Click += (s, e) => NavigateTo(settings.BrowserHomeUrl);

            addressBox = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(3) };
            addressBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    NavigateTo(addressBox.Text);
                }
            };

            var goButton = new Button { Text = "Go", Dock = DockStyle.Fill, Margin = new Padding(3) };
            goButton.Click += (s, e) => NavigateTo(addressBox.Text);

            navPanel.Controls.Add(backButton, 0, 0);
            navPanel.Controls.Add(forwardButton, 1, 0);
            navPanel.Controls.Add(reloadButton, 2, 0);
            navPanel.Controls.Add(homeButton, 3, 0);
            navPanel.Controls.Add(addressBox, 4, 0);
            navPanel.Controls.Add(goButton, 5, 0);

            // Action bar
            var actionPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            actionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
            actionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            addFromBrowserButton = new Button
            {
                Text = "Add to mod list",
                Dock = DockStyle.Fill,
                Margin = new Padding(3),
                Enabled = false
            };
            addFromBrowserButton.Click += AddFromBrowser;

            browserStatusLabel = new Label
            {
                Text = "Browse the Steam Workshop; collections and subscription pages can be imported directly.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            actionPanel.Controls.Add(addFromBrowserButton, 0, 0);
            actionPanel.Controls.Add(browserStatusLabel, 1, 0);

            webView = new WebView2 { Dock = DockStyle.Fill };

            layout.Controls.Add(navPanel, 0, 0);
            layout.Controls.Add(actionPanel, 0, 1);
            layout.Controls.Add(webView, 0, 2);

            browserTab.Controls.Add(layout);
        }

        private void SetupInstallTab()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 7,
                Padding = new Padding(10)
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));

            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));   // Target dir
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));   // URL add
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));   // Options
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // Mod list
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));   // List buttons
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));   // Install/Cancel
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));   // Progress

            // Row 0: Target directory - SteamCMD and the rest of the setup
            // live in the settings dialog, so this tab stays about the work
            targetDirBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(3),
                Text = settings.LastTargetDirectory
            };
            browseTargetButton = new Button { Text = "Browse", Dock = DockStyle.Fill, Margin = new Padding(3) };
            browseTargetButton.Click += BrowseTargetDir;

            layout.Controls.Add(MakeLabel("Install folder:"), 0, 0);
            layout.Controls.Add(targetDirBox, 1, 0);
            layout.Controls.Add(browseTargetButton, 2, 0);

            // Row 1: Add by URL / id + load legacy script
            urlBox = new HintTextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(3),
                Hint = "Workshop collection or mod URL / id, e.g. https://steamcommunity.com/sharedfiles/filedetails/?id=...",
                HintColor = Theme.TextDim
            };
            urlBox.KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    await AddFromInputAsync(urlBox.Text);
                }
            };
            addUrlButton = new Button { Text = "Add", Dock = DockStyle.Fill, Margin = new Padding(3) };
            addUrlButton.Click += async (s, e) => await AddFromInputAsync(urlBox.Text);
            loadScriptButton = new Button { Text = "Load script...", Dock = DockStyle.Fill, Margin = new Padding(3) };
            loadScriptButton.Click += LoadScriptFile;

            layout.Controls.Add(MakeLabel("Add mods:"), 0, 1);
            layout.Controls.Add(urlBox, 1, 1);
            layout.Controls.Add(addUrlButton, 2, 1);
            layout.Controls.Add(loadScriptButton, 3, 1);

            // Row 2: Options
            var optionsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0)
            };
            cleanupCheckBox = new CheckBox
            {
                Text = "Clean up workshop files after installation",
                Checked = settings.CleanupAfterInstall,
                AutoSize = true,
                Margin = new Padding(3, 6, 15, 3)
            };
            skipInstalledCheckBox = new CheckBox
            {
                Text = "Skip already installed mods",
                Checked = settings.SkipInstalledMods,
                AutoSize = true,
                Margin = new Padding(3, 6, 3, 3)
            };
            optionsPanel.Controls.Add(cleanupCheckBox);
            optionsPanel.Controls.Add(skipInstalledCheckBox);

            layout.Controls.Add(optionsPanel, 1, 2);
            layout.SetColumnSpan(optionsPanel, 3);

            // Row 3: Mod list
            modListView = new ListView
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(3),
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false
            };
            // Narrower than before: the detail pane now takes part of the width
            modListView.Columns.Add("Title", 225);
            modListView.Columns.Add("Mod ID", 85);
            modListView.Columns.Add("Game", 60);
            modListView.Columns.Add("Size", 70);
            modListView.Columns.Add("Updated", 82);
            modListView.Columns.Add("Status", 88);
            // Last column, so it takes the remaining width - requirement lists
            // are the widest thing in the table.
            modListView.Columns.Add("Requires", 200);
            modListView.SelectedIndexChanged += ModListSelectionChanged;
            modListView.DoubleClick += (s, e) => OpenSelectedModInBrowser();

            modContextMenu = new ContextMenuStrip();
            modContextMenu.Opening += BuildModContextMenu;
            Theme.StyleMenu(modContextMenu);
            modListView.ContextMenuStrip = modContextMenu;
            Theme.StyleListView(modListView);

            detailPanel = new ModDetailPanel { Dock = DockStyle.Fill };
            detailPanel.OpenUrlRequested += OpenInInternalBrowser;

            // List on the left, details on the right. The detail pane keeps
            // its width when the window is resized.
            // Panel1MinSize/Panel2MinSize are applied later: the container is
            // still at its design width here and would reject them.
            listSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(3),
                SplitterWidth = 6
            };
            listSplit.Panel1.Controls.Add(modListView);
            listSplit.Panel2.Controls.Add(detailPanel);

            layout.Controls.Add(listSplit, 0, 3);
            layout.SetColumnSpan(listSplit, 4);

            // Row 4: List management buttons
            var listButtonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0)
            };
            searchBox = new HintTextBox
            {
                Width = 250,
                Margin = new Padding(3, 4, 15, 3),
                Hint = "Filter: title, id, tag or description...",
                HintColor = Theme.TextDim
            };
            searchBox.TextChanged += (s, e) => RebuildModListView();

            removeSelectedButton = new Button { Text = "Remove selected", AutoSize = true, Margin = new Padding(3) };
            removeSelectedButton.Click += RemoveSelectedMods;
            clearListButton = new Button { Text = "Clear list", AutoSize = true, Margin = new Padding(3) };
            clearListButton.Click += ClearModList;
            checkInstalledButton = new Button { Text = "Check installed / updates", AutoSize = true, Margin = new Padding(3) };
            checkInstalledButton.Click += (s, e) => RefreshInstalledStatus();
            loadInstalledButton = new Button { Text = "Load installed library", AutoSize = true, Margin = new Padding(3) };
            loadInstalledButton.Click += LoadInstalledLibrary;
            checkRequirementsButton = new Button { Text = "Check requirements", AutoSize = true, Margin = new Padding(3) };
            checkRequirementsButton.Click += CheckRequirements;

            listButtonsPanel.Controls.Add(searchBox);
            listButtonsPanel.Controls.Add(checkRequirementsButton);
            listButtonsPanel.Controls.Add(loadInstalledButton);
            listButtonsPanel.Controls.Add(removeSelectedButton);
            listButtonsPanel.Controls.Add(clearListButton);
            listButtonsPanel.Controls.Add(checkInstalledButton);

            layout.Controls.Add(listButtonsPanel, 0, 4);
            layout.SetColumnSpan(listButtonsPanel, 4);

            // Row 5: Install / Cancel
            installButton = new Button { Text = "Install Mods", Dock = DockStyle.Fill, Margin = new Padding(3) };
            installButton.Click += InstallMods;
            cancelButton = new Button { Text = "Cancel", Dock = DockStyle.Fill, Margin = new Padding(3), Enabled = false };
            cancelButton.Click += CancelInstallation;

            layout.Controls.Add(installButton, 1, 5);
            layout.Controls.Add(cancelButton, 2, 5);

            // Row 6: Progress + status
            statusLabel = new Label
            {
                Text = "Ready",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            progressBar = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(3),
                Style = ProgressBarStyle.Continuous
            };

            layout.Controls.Add(statusLabel, 0, 6);
            layout.SetColumnSpan(statusLabel, 1);
            layout.Controls.Add(progressBar, 1, 6);
            layout.SetColumnSpan(progressBar, 3);

            installTab.Controls.Add(layout);
        }

        private void SetupLogTab()
        {
            logBox = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.White,
                Font = new Font(FontFamily.GenericMonospace, 9)
            };
            logTab.Controls.Add(logBox);
        }

        private static Label MakeLabel(string text) => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight
        };

        #endregion

        #region Updates

        /// <summary>
        /// Silent startup check for a newer release on GitHub. Failures are
        /// only logged - updates must never block using the app.
        /// </summary>
        private async Task CheckForUpdatesAsync()
        {
            if (!settings.CheckForUpdates) return;

            try
            {
                var updateService = new UpdateService();
                var update = await updateService.CheckForUpdateAsync(OnBetaChannel, CancellationToken.None);

                if (!update.UpdateAvailable)
                {
                    logger.Info($"Workshop Manager {update.CurrentVersion} is up to date");
                    return;
                }

                if (settings.SkippedUpdateVersion == update.LatestVersion.ToString())
                {
                    logger.Info($"Update {update.LatestVersion} available but skipped by user choice");
                    return;
                }

                var kind = update.IsPreRelease ? "beta version" : "version";
                var choice = MessageBox.Show(
                    $"A new {kind} is available: {update.LatestVersion} " +
                    $"(installed: {update.CurrentVersion}).\n\n" +
                    "Install now? The app will restart automatically.\n\n" +
                    "Yes = update now\nNo = remind me next time\nCancel = skip this version",
                    "Update available",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Information);

                if (choice == DialogResult.Cancel)
                {
                    settings.SkippedUpdateVersion = update.LatestVersion.ToString();
                    settings.Save();
                    return;
                }

                if (choice != DialogResult.Yes) return;

                SetControlsEnabled(false);
                try
                {
                    await updateService.DownloadAndApplyAsync(
                        update, new Progress<string>(UpdateStatus), CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.Error($"Update failed: {ex.Message}");
                    SetControlsEnabled(true);
                    UpdateStatus("Ready");

                    if (MessageBox.Show(
                        $"The automatic update failed:\n{ex.Message}\n\n" +
                        "Open the download page in the browser instead?",
                        "Update failed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = update.ReleasePageUrl,
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                // No network, rate limit, etc. - never bother the user
                logger.Info($"Update check skipped: {ex.Message}");
            }
        }

        #endregion

        #region Browser

        private async Task InitializeWebViewAsync()
        {
            try
            {
                // User data folder must be writable; the exe folder may not be.
                string userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WorkshopManager", "WebView2");

                var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await webView.EnsureCoreWebView2Async(environment);

                webViewReady = true;
                webView.SourceChanged += (s, e) => OnBrowserNavigated();
                webView.NavigationCompleted += (s, e) => OnBrowserNavigated();

                NavigateTo(settings.BrowserHomeUrl);
                logger.Info("Internal browser initialized");
            }
            catch (Exception ex)
            {
                logger.Warning($"Internal browser unavailable: {ex.Message}");
                browserStatusLabel.Text =
                    "Internal browser unavailable. Please install the Microsoft WebView2 Runtime " +
                    "(https://developer.microsoft.com/microsoft-edge/webview2/).";
            }
        }

        private void NavigateTo(string url)
        {
            if (!webViewReady || string.IsNullOrWhiteSpace(url)) return;

            if (!Regex.IsMatch(url, @"^https?://", RegexOptions.IgnoreCase))
            {
                url = "https://" + url.Trim();
            }

            try
            {
                webView.Source = new Uri(url);
            }
            catch (UriFormatException)
            {
                UpdateStatus("Invalid URL");
            }
        }

        private void OnBrowserNavigated()
        {
            if (!webViewReady) return;

            string url = webView.Source?.ToString() ?? "";
            addressBox.Text = url;

            backButton.Enabled = webView.CanGoBack;
            forwardButton.Enabled = webView.CanGoForward;

            if (browserBusy) return;

            if (Regex.IsMatch(url, @"steamcommunity\.com/(sharedfiles|workshop)/filedetails/.*[?&]id=\d+"))
            {
                currentBrowserAction = BrowserAction.AddCollection;
                addFromBrowserButton.Text = "Add this collection / mod to list";
                addFromBrowserButton.Enabled = true;
            }
            else if (url.Contains("/myworkshopfiles"))
            {
                currentBrowserAction = BrowserAction.CollectSubscriptions;
                addFromBrowserButton.Text = "Collect items from all pages";
                addFromBrowserButton.Enabled = true;
            }
            else
            {
                currentBrowserAction = BrowserAction.None;
                addFromBrowserButton.Text = "Add to mod list";
                addFromBrowserButton.Enabled = false;
            }
        }

        private async void AddFromBrowser(object sender, EventArgs e)
        {
            if (!webViewReady || browserBusy) return;

            string url = webView.Source?.ToString() ?? "";

            browserBusy = true;
            addFromBrowserButton.Enabled = false;

            try
            {
                if (currentBrowserAction == BrowserAction.AddCollection)
                {
                    await AddFromInputAsync(url);
                }
                else if (currentBrowserAction == BrowserAction.CollectSubscriptions)
                {
                    await CollectSubscriptionsAsync();
                }
            }
            finally
            {
                browserBusy = false;
                OnBrowserNavigated();
            }
        }

        /// <summary>
        /// Collects workshop item ids from all pages of a "my workshop files"
        /// listing inside the logged-in browser session. This is the local
        /// equivalent of the softknight.de Tampermonkey script: pages are
        /// fetched in the page context (using the user's own session) with a
        /// polite delay between requests.
        /// </summary>
        private async Task CollectSubscriptionsAsync()
        {
            browserStatusLabel.Text = "Collecting items from all pages...";
            UpdateStatus("Collecting subscribed items from browser...");

            const string script = @"
(async () => {
    try {
        const parseIds = (doc) => Array.from(doc.querySelectorAll('.workshopItemSubscription'))
            .map(el => (el.id.match(/Subscription(\d+)/) || [])[1])
            .filter(Boolean);

        let totalPages = 1;
        const pagingInfo = document.querySelector('.workshopBrowsePagingInfo');
        if (pagingInfo) {
            const nums = (pagingInfo.textContent.replace(/[.,]/g, '').match(/\d+/g) || []).map(Number);
            if (nums.length) totalPages = Math.ceil(Math.max(...nums) / 30);
        }

        const ids = new Set(parseIds(document));
        const url = new URL(location.href);

        for (let p = 1; p <= totalPages; p++) {
            const current = new URL(location.href).searchParams.get('p') || '1';
            if (String(p) === current) continue;
            url.searchParams.set('p', String(p));
            const resp = await fetch(url.toString(), { credentials: 'same-origin' });
            const doc = new DOMParser().parseFromString(await resp.text(), 'text/html');
            parseIds(doc).forEach(id => ids.add(id));
            await new Promise(r => setTimeout(r, 500));
        }

        const appId = new URLSearchParams(location.search).get('appid') || '';
        window.chrome.webview.postMessage(JSON.stringify({ ok: true, appId, ids: [...ids] }));
    } catch (err) {
        window.chrome.webview.postMessage(JSON.stringify({ ok: false, error: String(err) }));
    }
})();";

            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(object s, CoreWebView2WebMessageReceivedEventArgs e)
            {
                try { tcs.TrySetResult(e.TryGetWebMessageAsString()); }
                catch { tcs.TrySetResult(e.WebMessageAsJson); }
            }

            webView.CoreWebView2.WebMessageReceived += Handler;

            try
            {
                await webView.CoreWebView2.ExecuteScriptAsync(script);

                var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromMinutes(5)));
                if (completed != tcs.Task)
                {
                    UpdateStatus("Collecting items timed out");
                    return;
                }

                var result = JObject.Parse(tcs.Task.Result);
                if (!(bool?)result["ok"] ?? true)
                {
                    UpdateStatus($"Collecting items failed: {result["error"]}");
                    return;
                }

                var ids = (result["ids"] as JArray)?.Select(t => (string)t).Where(id => id != null).ToList()
                          ?? new List<string>();
                string fallbackAppId = (string)result["appId"] ?? "";

                if (ids.Count == 0)
                {
                    UpdateStatus("No workshop items found on this page");
                    MessageBox.Show(
                        "No workshop items were found. Make sure you are viewing a subscription " +
                        "list (e.g. 'Subscribed Items') for a specific game.",
                        "Nothing found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                UpdateStatus($"Found {ids.Count} items, fetching details...");
                var items = await collectionService.GetDetailsAsync(
                    ids, CancellationToken.None, new Progress<string>(UpdateStatus), fallbackAppId);

                AddItemsToList(items);
            }
            catch (Exception ex)
            {
                logger.Error($"Collecting subscriptions failed: {ex.Message}");
                UpdateStatus("Collecting items failed");
            }
            finally
            {
                webView.CoreWebView2.WebMessageReceived -= Handler;
                browserStatusLabel.Text = "";
            }
        }

        #endregion

        #region Mod list

        private async Task AddFromInputAsync(string input)
        {
            var id = CollectionService.ExtractWorkshopId(input);
            if (id == null)
            {
                MessageBox.Show(
                    "Please enter a Steam Workshop URL or a numeric workshop id.\n" +
                    "Example: https://steamcommunity.com/sharedfiles/filedetails/?id=123456789",
                    "Invalid input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            addUrlButton.Enabled = false;
            try
            {
                UpdateStatus($"Resolving workshop id {id}...");
                var items = await collectionService.ResolveAsync(
                    id, CancellationToken.None, new Progress<string>(UpdateStatus));

                AddItemsToList(items);
                urlBox.Clear();
            }
            catch (Exception ex)
            {
                logger.Error($"Could not resolve workshop id {id}: {ex.Message}");
                MessageBox.Show(
                    $"Could not resolve the workshop item:\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateStatus("Ready");
            }
            finally
            {
                addUrlButton.Enabled = true;
            }
        }

        /// <summary>
        /// Loads every mod installed in the configured target directories
        /// from its mod_&lt;id&gt;.info file. Works entirely offline and also
        /// lists mods whose Workshop page no longer exists.
        /// </summary>
        private void LoadInstalledLibrary(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(targetDirBox.Text))
            {
                MessageBox.Show(
                    "Please select the install folder first - that is where the mod info files are stored.",
                    "No install folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // The library is the richer source and survives a wiped game
            // folder; the info files still cover anything installed before the
            // library existed, or by an older version.
            var installed = library.All();
            var known = new HashSet<string>(installed.Select(m => m.ModId));

            var fromDisk = InstallationService.LoadInstalledMods(settings, targetDirBox.Text);
            var recovered = fromDisk.Where(m => known.Add(m.ModId)).ToList();
            installed.AddRange(recovered);

            if (installed.Count == 0)
            {
                MessageBox.Show(
                    "No installed mods found yet. The library fills up as you install mods " +
                    "with this app, and it keeps their details even after a mod disappears " +
                    "from the Workshop.",
                    "Nothing found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            logger.Info($"Loaded {installed.Count} mods " +
                        $"({installed.Count - recovered.Count} from the library, " +
                        $"{recovered.Count} from info files on disk)");
            AddItemsToList(installed);
        }

        /// <summary>
        /// Looks up the Required Items and Required DLC each mod declares and
        /// reports which of them are missing from the list.
        ///
        /// Steam publishes this only on the item page, so it costs one request
        /// per mod. The check therefore runs on the current selection when
        /// there is one, and asks before working through a long list.
        /// </summary>
        private async void CheckRequirements(object sender, EventArgs e)
        {
            var targets = modListView.SelectedItems.Count > 0
                ? modListView.SelectedItems.Cast<ListViewItem>()
                    .Select(lvi => lvi.Tag as WorkshopItem).Where(m => m != null).ToList()
                : modItems.ToList();

            if (targets.Count == 0)
            {
                MessageBox.Show("The mod list is empty.", "Nothing to check",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var pending = targets.Where(m => !m.RequirementsChecked).ToList();

            // Roughly a second per mod: one page request plus the delay we
            // keep between them to stay friendly to Steam.
            if (pending.Count > 20)
            {
                var minutes = Math.Max(1, (int)Math.Round(pending.Count * 0.9 / 60.0));
                if (MessageBox.Show(
                    "Steam publishes requirements only on each mod's own page, so this needs " +
                    $"{pending.Count} requests and takes about {minutes} minute(s).\n\n" +
                    "Tip: select a few mods first to check only those.\n\nStart now?",
                    "Check requirements", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return;
                }
            }

            int failed = 0;

            if (pending.Count > 0)
            {
                SetControlsEnabled(false);
                cancelButton.Enabled = true;
                cancellationTokenSource = new CancellationTokenSource();
                var token = cancellationTokenSource.Token;

                try
                {
                    var service = new RequirementsService();

                    for (int i = 0; i < pending.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();

                        var mod = pending[i];
                        UpdateStatus($"Checking requirements {i + 1} of {pending.Count}: {mod.Title}");
                        progressBar.Value = (int)((i + 1) * 100.0 / pending.Count);

                        try
                        {
                            var requirements = await service.FetchAsync(mod.ModId, token);
                            mod.RequiredMods = requirements.RequiredMods;
                            mod.RequiredDlc = requirements.RequiredDlc;
                            mod.RequirementsChecked = true;

                            // The row carries the requirements now, so it has
                            // to be redrawn as each result comes in.
                            UpdateListViewItem(mod);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            logger.Warning($"Could not read requirements for {mod.ModId}: {ex.Message}");
                        }

                        if (i < pending.Count - 1) await Task.Delay(500, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    logger.Warning("Requirement check cancelled");
                }
                finally
                {
                    SetControlsEnabled(true);
                    cancelButton.Enabled = false;
                    progressBar.Value = 0;
                    UpdateStatus("Ready");

                    // Let the detail pane pick up the requirements of whatever
                    // is selected right now
                    ModListSelectionChanged(null, EventArgs.Empty);
                }
            }

            ReportRequirements(targets, failed);
        }

        private void ReportRequirements(List<WorkshopItem> targets, int failed)
        {
            var evaluated = targets.Where(m => m.RequirementsChecked).ToList();
            if (evaluated.Count == 0)
            {
                UpdateStatus("Ready");
                return;
            }

            var known = new HashSet<string>(modItems.Select(m => m.ModId));
            var requiredMods = new Dictionary<string, ModRequirement>();
            var requiredDlc = new Dictionary<string, ModRequirement>();

            foreach (var mod in evaluated)
            {
                foreach (var requirement in mod.RequiredMods) requiredMods[requirement.Id] = requirement;
                foreach (var requirement in mod.RequiredDlc) requiredDlc[requirement.Id] = requirement;
            }

            // Refresh what the library already knows, but do not add mods that
            // were never installed - the library is an inventory, not a wish list.
            var touched = false;
            foreach (var mod in evaluated)
            {
                if (library.Find(mod.ModId) == null) continue;
                library.Record(mod, "");
                touched = true;
            }
            if (touched) library.Save();

            var missing = requiredMods.Values.Where(r => !known.Contains(r.Id)).ToList();

            var message = $"Checked {evaluated.Count} mod(s).\n\n" +
                $"Declared required mods: {requiredMods.Count}\n" +
                $"Missing from your list: {missing.Count}\n" +
                $"Required DLC: {requiredDlc.Count}";

            if (failed > 0) message += $"\nCould not be read: {failed}";

            if (requiredDlc.Count > 0)
            {
                message += "\n\nRequired DLC (must be owned on Steam):\n" +
                    string.Join("\n", requiredDlc.Values.Take(10).Select(r => $"  - {r}"));
            }

            if (missing.Count > 0)
            {
                message += "\n\nMissing mods:\n" +
                    string.Join("\n", missing.Take(15).Select(r => $"  - {r}"));
                if (missing.Count > 15) message += $"\n  ... and {missing.Count - 15} more";

                message += "\n\nAdd the missing mods to the list?";

                if (MessageBox.Show(message, "Requirements", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    _ = AddMissingRequirementsAsync(missing.Select(r => r.Id).ToList());
                }
                return;
            }

            MessageBox.Show(message, "Requirements", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async Task AddMissingRequirementsAsync(List<string> ids)
        {
            try
            {
                UpdateStatus($"Fetching details for {ids.Count} required mods...");
                var items = await collectionService.GetDetailsAsync(
                    ids, CancellationToken.None, new Progress<string>(UpdateStatus));
                AddItemsToList(items);
            }
            catch (Exception ex)
            {
                logger.Error($"Could not add the required mods: {ex.Message}");
                UpdateStatus("Ready");
            }
        }

        private void LoadScriptFile(object sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                Title = "Select a SteamCMD script file (e.g. generated on softknight.de)"
            };

            if (dialog.ShowDialog() != DialogResult.OK) return;

            try
            {
                var items = CollectionService.ParseScript(dialog.FileName);
                if (items.Count == 0)
                {
                    MessageBox.Show(
                        "No 'workshop_download_item' commands were found in the selected file.",
                        "Nothing found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                settings.LastScriptFile = dialog.FileName;
                AddItemsToList(items);

                // Enrich the ids from the script with titles/sizes in the background
                _ = EnrichScriptItemsAsync(items);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read script file: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task EnrichScriptItemsAsync(List<WorkshopItem> scriptItems)
        {
            try
            {
                var ids = scriptItems.Select(i => i.ModId).ToList();
                var detailed = await collectionService.GetDetailsAsync(ids, CancellationToken.None,
                    new Progress<string>(UpdateStatus));
                var byId = detailed.ToDictionary(d => d.ModId);

                foreach (var item in modItems)
                {
                    if (byId.TryGetValue(item.ModId, out var d) && d.TimeUpdated > 0)
                    {
                        item.Title = d.Title;
                        item.FileSize = d.FileSize;
                        item.TimeUpdated = d.TimeUpdated;
                        if (string.IsNullOrEmpty(item.AppId)) item.AppId = d.AppId;
                        UpdateListViewItem(item);
                    }
                }

                RefreshInstalledStatus();
            }
            catch (Exception ex)
            {
                logger.Warning($"Could not fetch details for script mods: {ex.Message}");
                UpdateStatus("Ready");
            }
        }

        private void AddItemsToList(IEnumerable<WorkshopItem> items)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<IEnumerable<WorkshopItem>>(AddItemsToList), items);
                return;
            }

            int added = 0, duplicates = 0;
            var existingIds = new HashSet<string>(modItems.Select(m => m.ModId));

            foreach (var item in items)
            {
                if (!existingIds.Add(item.ModId))
                {
                    duplicates++;
                    continue;
                }

                modItems.Add(item);
                added++;
            }

            RebuildModListView();
            RefreshInstalledStatus();

            UpdateStatus(duplicates > 0
                ? $"Added {added} mods ({duplicates} already in list)"
                : $"Added {added} mods");

            if (added > 0)
            {
                tabControl.SelectedTab = installTab;
            }
        }

        /// <summary>
        /// Repopulates the list view from <see cref="modItems"/>, honouring the
        /// current filter text. Filtering only affects what is displayed -
        /// installing always works on the complete list.
        /// </summary>
        private void RebuildModListView()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(RebuildModListView));
                return;
            }

            var filter = searchBox?.Text ?? "";

            modListView.BeginUpdate();
            try
            {
                modListView.Items.Clear();
                listViewItems.Clear();

                foreach (var item in modItems)
                {
                    if (!item.Matches(filter)) continue;

                    var lvi = new ListViewItem(item.Title) { Tag = item };
                    lvi.SubItems.Add(item.ModId);
                    lvi.SubItems.Add(item.AppId);
                    lvi.SubItems.Add(item.FileSizeText);
                    lvi.SubItems.Add(item.TimeUpdatedText);
                    lvi.SubItems.Add(item.StatusText);
                    lvi.SubItems.Add(item.RequirementsText);

                    modListView.Items.Add(lvi);
                    listViewItems[item] = lvi;
                    ApplyRowAppearance(item, lvi);
                }
            }
            finally
            {
                modListView.EndUpdate();
            }

            // The vertical scroll bar appears only now, so the last column has
            // to be measured against the reduced client width again.
            Theme.StretchLastColumn(modListView);

            if (!string.IsNullOrWhiteSpace(filter))
            {
                UpdateStatus($"Showing {modListView.Items.Count} of {modItems.Count} mods");
            }
        }

        /// <summary>
        /// Gives the detail pane a sensible starting width once the form has
        /// a real size. Setting SplitterDistance earlier throws, because the
        /// container is still at its design size then.
        /// </summary>
        private void ApplyInitialSplitterDistance()
        {
            try
            {
                const int listMin = 320;
                const int detailMin = 260;

                // Distance first, minimum sizes afterwards - the reverse order
                // fails validation while the splitter still sits at its default.
                var max = listSplit.Width - detailMin - listSplit.SplitterWidth;
                if (max <= listMin) return;

                listSplit.SplitterDistance = Math.Clamp(listSplit.Width - 360, listMin, max);
                listSplit.Panel1MinSize = listMin;
                listSplit.Panel2MinSize = detailMin;
            }
            catch
            {
                // Keep the default split rather than failing startup
            }
        }

        /// <summary>
        /// Shows a page in the built-in browser instead of an external one, so
        /// the user stays inside the app and keeps their Steam session.
        /// </summary>
        private void OpenInInternalBrowser(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            tabControl.SelectedTab = browserTab;
            NavigateTo(url);
        }

        private void OpenSelectedModInBrowser()
        {
            if (modListView.SelectedItems.Count == 0) return;
            if (modListView.SelectedItems[0].Tag is WorkshopItem item)
            {
                OpenInInternalBrowser(item.WorkshopUrl);
            }
        }

        /// <summary>
        /// Builds the row menu on demand: the required mods differ per row and
        /// are only known once the requirements have been checked.
        /// </summary>
        private void BuildModContextMenu(object sender, System.ComponentModel.CancelEventArgs e)
        {
            modContextMenu.Items.Clear();

            if (modListView.SelectedItems.Count == 0 ||
                modListView.SelectedItems[0].Tag is not WorkshopItem item)
            {
                e.Cancel = true;
                return;
            }

            void Add(string text, Action action, bool enabled = true)
            {
                var entry = new ToolStripMenuItem(text) { ForeColor = Theme.Text, Enabled = enabled };
                entry.Click += (s, args) => action();
                modContextMenu.Items.Add(entry);
            }

            Add("Open in workshop browser", () => OpenInInternalBrowser(item.WorkshopUrl));
            Add("Open on Steam (external browser)", () => OpenExternally(item.WorkshopUrl));

            if (item.RequiredMods.Count > 0 || item.RequiredDlc.Count > 0)
            {
                modContextMenu.Items.Add(new ToolStripSeparator());

                foreach (var requirement in item.RequiredMods)
                {
                    var url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={requirement.Id}";
                    Add($"Requires: {requirement.Name}", () => OpenInInternalBrowser(url));
                }

                foreach (var dlc in item.RequiredDlc)
                {
                    var url = $"https://store.steampowered.com/app/{dlc.Id}";
                    Add($"Requires DLC: {dlc.Name}", () => OpenInInternalBrowser(url));
                }
            }
            else if (item.RequirementsChecked)
            {
                modContextMenu.Items.Add(new ToolStripSeparator());
                Add("No requirements declared", () => { }, enabled: false);
            }
        }

        private static void OpenExternally(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
                // No browser available - not worth interrupting the user
            }
        }

        private void ModListSelectionChanged(object sender, EventArgs e)
        {
            detailPanel.ShowItem(modListView.SelectedItems.Count > 0
                ? modListView.SelectedItems[0].Tag as WorkshopItem
                : null);
        }

        private void RemoveSelectedMods(object sender, EventArgs e)
        {
            foreach (ListViewItem lvi in modListView.SelectedItems.Cast<ListViewItem>().ToList())
            {
                if (lvi.Tag is WorkshopItem item)
                {
                    modItems.Remove(item);
                    listViewItems.Remove(item);
                }
                modListView.Items.Remove(lvi);
            }
        }

        private void ClearModList(object sender, EventArgs e)
        {
            modItems.Clear();
            listViewItems.Clear();
            modListView.Items.Clear();
        }

        /// <summary>
        /// Marks mods as installed / update available based on the
        /// mod_&lt;id&gt;.info files in the target directory.
        /// </summary>
        private void RefreshInstalledStatus()
        {
            foreach (var item in modItems)
            {
                if (item.Status == WorkshopItemStatus.Downloading) continue;

                var infoPath = InstallationService.GetInfoFilePath(settings, targetDirBox.Text, item);
                if (File.Exists(infoPath))
                {
                    var installedTime = InstallationService.GetInstalledTimeUpdated(infoPath);
                    item.Status = item.TimeUpdated > 0 && installedTime.HasValue && item.TimeUpdated > installedTime.Value
                        ? WorkshopItemStatus.UpdateAvailable
                        : WorkshopItemStatus.Installed;
                }
                else if (item.Banned)
                {
                    // Not installed and gone from the Workshop - there is no
                    // way to download it any more.
                    item.Status = WorkshopItemStatus.Removed;
                }
                else if (item.Status is WorkshopItemStatus.Installed
                    or WorkshopItemStatus.UpdateAvailable
                    or WorkshopItemStatus.Skipped
                    or WorkshopItemStatus.Removed)
                {
                    item.Status = WorkshopItemStatus.Pending;
                }

                UpdateListViewItem(item);
            }
        }

        private void UpdateListViewItem(WorkshopItem item)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<WorkshopItem>(UpdateListViewItem), item);
                return;
            }

            if (!listViewItems.TryGetValue(item, out var lvi)) return;

            lvi.Text = item.Title;
            lvi.SubItems[1].Text = item.ModId;
            lvi.SubItems[2].Text = item.AppId;
            lvi.SubItems[3].Text = item.FileSizeText;
            lvi.SubItems[4].Text = item.TimeUpdatedText;
            lvi.SubItems[5].Text = item.StatusText;
            lvi.SubItems[6].Text = item.RequirementsText;

            ApplyRowAppearance(item, lvi);
        }

        private static void ApplyRowAppearance(WorkshopItem item, ListViewItem lvi)
        {
            // Theme colours throughout: the named System.Drawing colours are
            // light-theme values and lose their contrast on a dark surface.
            lvi.ForeColor = item.Status switch
            {
                // An installed copy of a removed item still works, but the
                // user should see that it can no longer be re-downloaded.
                WorkshopItemStatus.Installed => item.Banned ? Theme.Warning : Theme.Success,
                WorkshopItemStatus.UpdateAvailable => Theme.Warning,
                WorkshopItemStatus.Failed => Theme.Error,
                WorkshopItemStatus.Removed => Theme.Error,
                WorkshopItemStatus.Skipped => Theme.Muted,
                _ => item.Banned ? Theme.Error : Theme.Text
            };
        }

        #endregion

        #region SteamCMD setup

        private void BrowseTargetDir(object sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select target directory for mods"
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                targetDirBox.Text = dialog.SelectedPath;
                RefreshInstalledStatus();
            }
        }

        #endregion

        #region Installation

        private bool ValidateInputs()
        {
            if (!Settings.ValidateSteamCmdPath(settings.SteamCmdPath))
            {
                // Offer the fix instead of just naming the problem: most people
                // hitting this have never heard of SteamCMD.
                var answer = MessageBox.Show(
                    "SteamCMD is needed to download mods from Steam, and it is not set up yet.\n\n" +
                    "Open the settings now? It can download and configure SteamCMD for you.",
                    "SteamCMD missing", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                if (answer == DialogResult.Yes) OpenSettings(this, EventArgs.Empty);
                return false;
            }

            if (string.IsNullOrWhiteSpace(targetDirBox.Text))
            {
                MessageBox.Show(
                    "Please select an install folder.",
                    "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (modItems.Count == 0)
            {
                MessageBox.Show(
                    "The mod list is empty. Add a collection or mod first (via the Workshop Browser, " +
                    "a URL, or a SteamCMD script file).",
                    "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }

        private async void InstallMods(object sender, EventArgs e)
        {
            if (!ValidateInputs()) return;

            SetControlsEnabled(false);
            cancelButton.Enabled = true;
            progressBar.Value = 0;

            cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var progress = new Progress<InstallationProgress>(UpdateProgress);
                var options = new InstallationOptions
                {
                    CleanupWorkshopFiles = cleanupCheckBox.Checked,
                    SkipInstalledMods = skipInstalledCheckBox.Checked,
                    BatchSize = settings.BatchSize,
                    MaxRetries = settings.MaxRetries
                };

                var installationService = new InstallationService(
                    logger, settings.SteamCmdPath, targetDirBox.Text, settings);

                var result = await installationService.InstallModsAsync(
                    modItems.ToList(), options, progress, cancellationTokenSource.Token,
                    UpdateListViewItem);

                ArchiveInstalledMods();

                var icon = result.Failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning;
                MessageBox.Show(
                    $"Installation finished.\n\nInstalled: {result.Installed}\n" +
                    $"Skipped (already installed): {result.Skipped}\nFailed: {result.Failed}" +
                    (result.Failed > 0 ? "\n\nSee the Log tab for details. Failed items may require a Steam login." : ""),
                    "Installation finished", MessageBoxButtons.OK, icon);
            }
            catch (OperationCanceledException)
            {
                logger.Warning("Installation was cancelled");
                MessageBox.Show(
                    "Installation was cancelled by user.",
                    "Cancelled", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                logger.Error($"Installation failed: {ex.Message}");
                MessageBox.Show(
                    $"Installation failed: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetControlsEnabled(true);
                cancelButton.Enabled = false;
                progressBar.Value = 0;
                UpdateStatus("Ready");
                RefreshInstalledStatus();
            }
        }

        /// <summary>
        /// Writes everything that is now on disk into the library. Recording
        /// is not optional: once an item disappears from the Workshop its
        /// metadata cannot be fetched again, so whatever is not stored here at
        /// install time is lost for good.
        /// </summary>
        private void ArchiveInstalledMods()
        {
            var archived = 0;

            foreach (var mod in modItems)
            {
                if (mod.Status is not (WorkshopItemStatus.Installed
                    or WorkshopItemStatus.UpdateAvailable
                    or WorkshopItemStatus.Skipped))
                {
                    continue;
                }

                library.Record(mod, InstallationService.ResolveTargetDir(settings, targetDirBox.Text, mod.AppId));
                archived++;
            }

            if (archived == 0) return;

            if (library.Save())
            {
                logger.Info($"Library now holds {library.Count} mods ({library.FilePath})");
            }
            else
            {
                logger.Warning("Could not write the mod library file");
            }
        }

        private void CancelInstallation(object sender, EventArgs e)
        {
            if (MessageBox.Show(
                "Are you sure you want to cancel the running operation?",
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

            progressBar.Value = Math.Min(100, progress.ProgressPercentage);
            UpdateStatus(progress.CurrentOperation);
        }

        private void SetControlsEnabled(bool enabled)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<bool>(SetControlsEnabled), enabled);
                return;
            }

            targetDirBox.Enabled = enabled;
            urlBox.Enabled = enabled;
            browseTargetButton.Enabled = enabled;
            addUrlButton.Enabled = enabled;
            loadScriptButton.Enabled = enabled;
            removeSelectedButton.Enabled = enabled;
            clearListButton.Enabled = enabled;
            checkInstalledButton.Enabled = enabled;
            loadInstalledButton.Enabled = enabled;
            checkRequirementsButton.Enabled = enabled;
            settingsButton.Enabled = enabled;
            installButton.Enabled = enabled;
            cleanupCheckBox.Enabled = enabled;
            skipInstalledCheckBox.Enabled = enabled;
            addFromBrowserButton.Enabled = enabled && currentBrowserAction != BrowserAction.None;
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

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                cancellationTokenSource?.Dispose();
                components?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
