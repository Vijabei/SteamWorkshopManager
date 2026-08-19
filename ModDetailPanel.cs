using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace WorkshopManager
{
    /// <summary>
    /// Shows everything known about the selected mod: preview image,
    /// metadata, declared requirements and the archived description.
    /// Reads only from the WorkshopItem plus the image cache, so it stays
    /// useful offline and for mods that vanished from the Workshop.
    /// </summary>
    public class ModDetailPanel : Panel
    {
        private readonly PictureBox preview;
        private readonly Label titleLabel;
        private readonly Label metaLabel;
        private readonly Label statusLabel;
        private readonly Label requirementsLabel;
        private readonly TextBox descriptionBox;
        private readonly Button openOnSteamButton;
        private readonly Label emptyHint;

        private WorkshopItem current;
        private CancellationTokenSource imageCts;

        public ModDetailPanel()
        {
            Padding = new Padding(12);
            BackColor = Theme.Surface;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7,
                BackColor = Color.Transparent
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));  // preview
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));   // title
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));   // meta
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));   // status
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));   // requirements
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // description
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));   // action

            preview = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Theme.SurfaceAlt,
                Margin = new Padding(0, 0, 0, 8)
            };

            titleLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = Theme.TitleFont,
                ForeColor = Theme.Text,
                Margin = new Padding(0)
            };

            metaLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = Theme.SmallFont,
                ForeColor = Theme.TextDim,
                Margin = new Padding(0)
            };

            statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = Theme.BoldFont,
                ForeColor = Theme.Accent,
                Margin = new Padding(0, 0, 0, 4)
            };

            requirementsLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = Theme.SmallFont,
                ForeColor = Theme.TextDim,
                Margin = new Padding(0, 0, 0, 6)
            };

            descriptionBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Theme.SurfaceAlt,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 0, 0, 8)
            };

            openOnSteamButton = new Button
            {
                Text = "Open on Steam",
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.SurfaceAlt,
                ForeColor = Theme.Accent,
                Cursor = Cursors.Hand
            };
            openOnSteamButton.FlatAppearance.BorderColor = Theme.Border;
            openOnSteamButton.FlatAppearance.MouseOverBackColor = Theme.SurfaceHover;
            openOnSteamButton.Click += OpenOnSteam;

            layout.Controls.Add(preview, 0, 0);
            layout.Controls.Add(titleLabel, 0, 1);
            layout.Controls.Add(metaLabel, 0, 2);
            layout.Controls.Add(statusLabel, 0, 3);
            layout.Controls.Add(requirementsLabel, 0, 4);
            layout.Controls.Add(descriptionBox, 0, 5);
            layout.Controls.Add(openOnSteamButton, 0, 6);

            emptyHint = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Select a mod to see its preview, description and requirements.",
                ForeColor = Theme.TextDim,
                TextAlign = ContentAlignment.MiddleCenter
            };

            Controls.Add(layout);
            Controls.Add(emptyHint);
            emptyHint.BringToFront();

            RestyleFromTheme();
            ShowItem(null);
        }

        /// <summary>
        /// Re-reads every colour from the theme. Called after the user
        /// switches between light and dark; Theme.Apply deliberately skips
        /// this panel because it manages per-state colours itself.
        /// </summary>
        public void RestyleFromTheme()
        {
            BackColor = Theme.Surface;
            preview.BackColor = Theme.SurfaceAlt;
            titleLabel.ForeColor = Theme.Text;
            metaLabel.ForeColor = Theme.TextDim;
            requirementsLabel.ForeColor = Theme.TextDim;
            emptyHint.ForeColor = Theme.TextDim;

            descriptionBox.BackColor = Theme.SurfaceAlt;
            descriptionBox.ForeColor = Theme.Text;

            openOnSteamButton.BackColor = Theme.SurfaceAlt;
            openOnSteamButton.ForeColor = Theme.Accent;
            openOnSteamButton.FlatAppearance.BorderColor = Theme.Border;
            openOnSteamButton.FlatAppearance.MouseOverBackColor = Theme.SurfaceHover;

            // The status colour depends on the item, so let it redo itself
            if (current != null) ShowItem(current);
        }

        /// <summary>Displays a mod, or the placeholder when null.</summary>
        public void ShowItem(WorkshopItem item)
        {
            current = item;
            emptyHint.Visible = item == null;
            if (item == null) return;

            titleLabel.Text = item.Title;

            var meta = $"Mod {item.ModId}  ·  Game {item.AppId}";
            if (!string.IsNullOrEmpty(item.FileSizeText)) meta += $"  ·  {item.FileSizeText}";
            if (!string.IsNullOrEmpty(item.TimeUpdatedText)) meta += $"  ·  updated {item.TimeUpdatedText}";
            if (!string.IsNullOrEmpty(item.Tags)) meta += $"\n{item.Tags}";
            metaLabel.Text = meta;

            statusLabel.Text = item.StatusText;
            statusLabel.ForeColor = item.Status switch
            {
                WorkshopItemStatus.Installed => item.Banned ? Theme.Warning : Theme.Success,
                WorkshopItemStatus.UpdateAvailable => Theme.Warning,
                WorkshopItemStatus.Failed => Theme.Error,
                WorkshopItemStatus.Removed => Theme.Error,
                WorkshopItemStatus.Skipped => Theme.Muted,
                // Neutral: the accent is green now and would read as "done"
                _ => Theme.TextDim
            };

            requirementsLabel.Text = DescribeRequirements(item);

            descriptionBox.Text = string.IsNullOrWhiteSpace(item.Description)
                ? "(no description archived - it is stored when the mod is installed)"
                : item.Description.Replace("\n", Environment.NewLine);
            descriptionBox.Select(0, 0);

            LoadPreview(item);
        }

        private static string DescribeRequirements(WorkshopItem item)
        {
            if (!item.RequirementsChecked)
            {
                return "Requirements: not checked yet - use \"Check requirements\".";
            }

            if (item.RequiredMods.Count == 0 && item.RequiredDlc.Count == 0)
            {
                return "Requirements: none declared.";
            }

            var lines = "";
            if (item.RequiredMods.Count > 0)
            {
                lines += "Requires mods: " +
                    string.Join(", ", item.RequiredMods.Select(r => r.Name)) + "\n";
            }
            if (item.RequiredDlc.Count > 0)
            {
                lines += "Requires DLC: " +
                    string.Join(", ", item.RequiredDlc.Select(r => r.Name));
            }

            return lines.TrimEnd();
        }

        private async void LoadPreview(WorkshopItem item)
        {
            preview.Image = null;

            // Abandon a pending download when the selection moved on, so a
            // slow response cannot overwrite a newer selection's image.
            imageCts?.Cancel();
            imageCts = new CancellationTokenSource();
            var token = imageCts.Token;

            if (string.IsNullOrEmpty(item.PreviewUrl)) return;

            try
            {
                var image = await ImageCache.GetAsync(item.ModId, item.PreviewUrl, token);
                if (!token.IsCancellationRequested && current == item)
                {
                    preview.Image = image;
                }
            }
            catch (OperationCanceledException)
            {
                // Selection changed - nothing to do
            }
        }

        private void OpenOnSteam(object sender, EventArgs e)
        {
            if (current == null) return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"https://steamcommunity.com/sharedfiles/filedetails/?id={current.ModId}",
                    UseShellExecute = true
                });
            }
            catch
            {
                // No browser available - not worth interrupting the user
            }
        }
    }
}
