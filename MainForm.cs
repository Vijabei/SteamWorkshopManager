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
        private TextBox steamCmdPathBox;
        private TextBox targetDirBox;
        private TextBox urlBox;
        private Button browseSteamCmdButton;
        private Button getSteamCmdButton;
        private Button browseTargetButton;
        private Button addUrlButton;
        private Button loadScriptButton;
        private Button removeSelectedButton;
        private Button clearListButton;
        private Button checkInstalledButton;
        private CheckBox cleanupCheckBox;
        private CheckBox skipInstalledCheckBox;
        private ListView modListView;
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

                InitializeComponent();
                SetupUI();

                logger = new Logger(logBox);

                FormClosing += MainForm_FormClosing;
                Shown += async (s, e) =>
                {
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
            settings.SteamCmdPath = steamCmdPathBox.Text;
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

            Controls.Add(tabControl);
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
            navPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
            navPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            navPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));

            backButton = new Button { Text = "◀", Dock = DockStyle.Fill, Margin = new Padding(3) };
            backButton.Click += (s, e) => { if (webViewReady && webView.CanGoBack) webView.GoBack(); };

            forwardButton = new Button { Text = "▶", Dock = DockStyle.Fill, Margin = new Padding(3) };
            forwardButton.Click += (s, e) => { if (webViewReady && webView.CanGoForward) webView.GoForward(); };

            reloadButton = new Button { Text = "⟳", Dock = DockStyle.Fill, Margin = new Padding(3) };
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
                RowCount = 8,
                Padding = new Padding(10)
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));

            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));   // SteamCMD
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));   // Target dir
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));   // URL add
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));   // Options
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // Mod list
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));   // List buttons
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));   // Install/Cancel
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));   // Progress

            // Row 0: SteamCMD path
            steamCmdPathBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(3),
                Text = settings.SteamCmdPath
            };
            browseSteamCmdButton = new Button { Text = "Browse", Dock = DockStyle.Fill, Margin = new Padding(3) };
            browseSteamCmdButton.Click += BrowseSteamCmd;
            getSteamCmdButton = new Button { Text = "Get SteamCMD", Dock = DockStyle.Fill, Margin = new Padding(3) };
            getSteamCmdButton.Click += DownloadSteamCmd;

            layout.Controls.Add(MakeLabel("SteamCMD:"), 0, 0);
            layout.Controls.Add(steamCmdPathBox, 1, 0);
            layout.Controls.Add(browseSteamCmdButton, 2, 0);
            layout.Controls.Add(getSteamCmdButton, 3, 0);

            // Row 1: Target directory
            targetDirBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(3),
                Text = settings.LastTargetDirectory
            };
            browseTargetButton = new Button { Text = "Browse", Dock = DockStyle.Fill, Margin = new Padding(3) };
            browseTargetButton.Click += BrowseTargetDir;

            layout.Controls.Add(MakeLabel("Install folder:"), 0, 1);
            layout.Controls.Add(targetDirBox, 1, 1);
            layout.Controls.Add(browseTargetButton, 2, 1);

            // Row 2: Add by URL / id + load legacy script
            urlBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(3),
                PlaceholderText = "Workshop collection or mod URL / id, e.g. https://steamcommunity.com/sharedfiles/filedetails/?id=..."
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

            layout.Controls.Add(MakeLabel("Add mods:"), 0, 2);
            layout.Controls.Add(urlBox, 1, 2);
            layout.Controls.Add(addUrlButton, 2, 2);
            layout.Controls.Add(loadScriptButton, 3, 2);

            // Row 3: Options
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

            layout.Controls.Add(optionsPanel, 1, 3);
            layout.SetColumnSpan(optionsPanel, 3);

            // Row 4: Mod list
            modListView = new ListView
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(3),
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false
            };
            modListView.Columns.Add("Title", 320);
            modListView.Columns.Add("Mod ID", 100);
            modListView.Columns.Add("Game ID", 75);
            modListView.Columns.Add("Size", 75);
            modListView.Columns.Add("Updated", 85);
            modListView.Columns.Add("Status", 130);

            layout.Controls.Add(modListView, 0, 4);
            layout.SetColumnSpan(modListView, 4);

            // Row 5: List management buttons
            var listButtonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0)
            };
            removeSelectedButton = new Button { Text = "Remove selected", AutoSize = true, Margin = new Padding(3) };
            removeSelectedButton.Click += RemoveSelectedMods;
            clearListButton = new Button { Text = "Clear list", AutoSize = true, Margin = new Padding(3) };
            clearListButton.Click += ClearModList;
            checkInstalledButton = new Button { Text = "Check installed / updates", AutoSize = true, Margin = new Padding(3) };
            checkInstalledButton.Click += (s, e) => RefreshInstalledStatus();

            listButtonsPanel.Controls.Add(removeSelectedButton);
            listButtonsPanel.Controls.Add(clearListButton);
            listButtonsPanel.Controls.Add(checkInstalledButton);

            layout.Controls.Add(listButtonsPanel, 0, 5);
            layout.SetColumnSpan(listButtonsPanel, 4);

            // Row 6: Install / Cancel
            installButton = new Button { Text = "Install Mods", Dock = DockStyle.Fill, Margin = new Padding(3) };
            installButton.Click += InstallMods;
            cancelButton = new Button { Text = "Cancel", Dock = DockStyle.Fill, Margin = new Padding(3), Enabled = false };
            cancelButton.Click += CancelInstallation;

            layout.Controls.Add(installButton, 1, 6);
            layout.Controls.Add(cancelButton, 2, 6);

            // Row 7: Progress + status
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

            layout.Controls.Add(statusLabel, 0, 7);
            layout.SetColumnSpan(statusLabel, 1);
            layout.Controls.Add(progressBar, 1, 7);
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
                var update = await updateService.CheckForUpdateAsync(CancellationToken.None);

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

                var choice = MessageBox.Show(
                    $"A new version is available: {update.LatestVersion} " +
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

            modListView.BeginUpdate();
            try
            {
                foreach (var item in items)
                {
                    if (!existingIds.Add(item.ModId))
                    {
                        duplicates++;
                        continue;
                    }

                    modItems.Add(item);

                    var lvi = new ListViewItem(item.Title) { Tag = item };
                    lvi.SubItems.Add(item.ModId);
                    lvi.SubItems.Add(item.AppId);
                    lvi.SubItems.Add(item.FileSizeText);
                    lvi.SubItems.Add(item.TimeUpdatedText);
                    lvi.SubItems.Add(item.StatusText);

                    modListView.Items.Add(lvi);
                    listViewItems[item] = lvi;
                    added++;
                }
            }
            finally
            {
                modListView.EndUpdate();
            }

            RefreshInstalledStatus();

            UpdateStatus(duplicates > 0
                ? $"Added {added} mods ({duplicates} already in list)"
                : $"Added {added} mods");

            if (added > 0)
            {
                tabControl.SelectedTab = installTab;
            }
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
                else if (item.Status is WorkshopItemStatus.Installed
                    or WorkshopItemStatus.UpdateAvailable
                    or WorkshopItemStatus.Skipped)
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

            lvi.ForeColor = item.Status switch
            {
                WorkshopItemStatus.Installed => Color.Green,
                WorkshopItemStatus.UpdateAvailable => Color.DarkOrange,
                WorkshopItemStatus.Failed => Color.Red,
                WorkshopItemStatus.Skipped => Color.Gray,
                _ => SystemColors.WindowText
            };
        }

        #endregion

        #region SteamCMD setup

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

        private async void DownloadSteamCmd(object sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select a folder to install SteamCMD into (an empty folder is recommended)"
            };

            if (dialog.ShowDialog() != DialogResult.OK) return;

            getSteamCmdButton.Enabled = false;
            try
            {
                UpdateStatus("Downloading SteamCMD...");
                logger.Info("Downloading SteamCMD from the official Valve CDN...");

                string exePath = await SteamCmdDownloader.DownloadAndExtractAsync(
                    dialog.SelectedPath, CancellationToken.None);

                steamCmdPathBox.Text = exePath;
                UpdateStatus("SteamCMD installed");
                logger.Info($"SteamCMD installed to {exePath}");

                MessageBox.Show(
                    "SteamCMD was downloaded successfully. It will update itself on first use.",
                    "SteamCMD ready", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                logger.Error($"SteamCMD download failed: {ex.Message}");
                MessageBox.Show($"SteamCMD download failed: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateStatus("Ready");
            }
            finally
            {
                getSteamCmdButton.Enabled = true;
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
                RefreshInstalledStatus();
            }
        }

        #endregion

        #region Installation

        private bool ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(steamCmdPathBox.Text) || !Settings.ValidateSteamCmdPath(steamCmdPathBox.Text))
            {
                MessageBox.Show(
                    "Please select a valid steamcmd.exe path (or use 'Get SteamCMD' to download it).",
                    "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                    logger, steamCmdPathBox.Text, targetDirBox.Text, settings);

                var result = await installationService.InstallModsAsync(
                    modItems.ToList(), options, progress, cancellationTokenSource.Token,
                    UpdateListViewItem);

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

            steamCmdPathBox.Enabled = enabled;
            targetDirBox.Enabled = enabled;
            urlBox.Enabled = enabled;
            browseSteamCmdButton.Enabled = enabled;
            getSteamCmdButton.Enabled = enabled;
            browseTargetButton.Enabled = enabled;
            addUrlButton.Enabled = enabled;
            loadScriptButton.Enabled = enabled;
            removeSelectedButton.Enabled = enabled;
            clearListButton.Enabled = enabled;
            checkInstalledButton.Enabled = enabled;
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
