using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace WorkshopManager
{
    /// <summary>
    /// Asks which of the collections a collection links to should be added
    /// along with it. Steam lists linked collections among a collection's
    /// items, and adding them silently mixed their mods into the list.
    ///
    /// Everything starts checked, which is what adding a collection did
    /// before this dialog existed.
    /// </summary>
    public class LinkedCollectionsDialog : Form
    {
        private readonly List<(CheckBox Box, CollectionNode Node)> choices = new();

        /// <summary>Ids of the linked collections the user kept checked.</summary>
        public IReadOnlyCollection<string> IncludedIds =>
            choices.Where(c => c.Box.Checked).Select(c => c.Node.Id).ToList();

        public LinkedCollectionsDialog(CollectionNode root, bool collectionFolders)
        {
            // Written for 96 DPI and scaled on ResumeLayout, as in MainForm
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            var name = string.IsNullOrWhiteSpace(root.Title) ? $"Collection {root.Id}" : root.Title;

            Text = "Linked collections";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 420);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                Padding = new Padding(14)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var intro = new Label
            {
                Text = $"\"{name}\" has {Count(root.ItemIds.Count)} of its own and links to " +
                       $"{root.Linked.Count} other collection{(root.Linked.Count == 1 ? "" : "s")}. " +
                       "Which of them should be added as well?",
                AutoSize = true,
                MaximumSize = new Size(530, 0),
                Margin = new Padding(0, 0, 0, 10)
            };
            layout.Controls.Add(intro, 0, 0);

            var list = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Margin = new Padding(0)
            };

            foreach (var linked in root.Linked)
            {
                var title = string.IsNullOrWhiteSpace(linked.Title) ? $"Collection {linked.Id}" : linked.Title;
                var nested = linked.SelfAndDescendants().Count() - 1;

                var text = $"{title}  ({Count(linked.AllItemIds().Count())}" +
                           (nested > 0 ? $", with {nested} linked collection{(nested == 1 ? "" : "s")} of its own" : "") +
                           ")";

                var box = new CheckBox
                {
                    Text = text,
                    Checked = true,
                    AutoSize = true,
                    Margin = new Padding(3, 3, 3, 3)
                };
                choices.Add((box, linked));
                list.Controls.Add(box);
            }
            layout.Controls.Add(list, 0, 1);

            var note = new Label
            {
                Text = collectionFolders
                    ? $"Each checked collection goes into a subfolder of \"{name}\", " +
                      "named in brackets - for example [" +
                      (string.IsNullOrWhiteSpace(root.Linked[0].Title) ? "Linked collection" : root.Linked[0].Title) +
                      "]. Mods listed in more than one place are installed once."
                    : "Their mods are installed alongside the others, straight into the folder the " +
                      "game loads mods from. In archive mode, with collection folders on, each " +
                      "collection keeps a subfolder of its own.",
                Font = Theme.SmallFont,
                AutoSize = true,
                MaximumSize = new Size(530, 0),
                Margin = new Padding(0, 10, 0, 0)
            };
            layout.Controls.Add(note, 0, 2);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 46,
                Padding = new Padding(10)
            };

            var cancel = new Button { Text = "Cancel", Size = new Size(100, 26), DialogResult = DialogResult.Cancel };
            var ok = new Button { Text = "Add", Size = new Size(100, 26), DialogResult = DialogResult.OK };
            var none = new Button { Text = "None", Size = new Size(80, 26) };
            var all = new Button { Text = "All", Size = new Size(80, 26) };
            none.Click += (s, e) => SetAll(false);
            all.Click += (s, e) => SetAll(true);

            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            buttons.Controls.Add(none);
            buttons.Controls.Add(all);

            Controls.Add(layout);
            Controls.Add(buttons);
            AcceptButton = ok;
            CancelButton = cancel;

            Theme.Apply(this);
            Theme.StylePrimary(ok);
            Theme.ApplyTitleBar(this);

            ResumeLayout(false);
            PerformLayout();
        }

        private void SetAll(bool value)
        {
            foreach (var (box, _) in choices) box.Checked = value;
        }

        private static string Count(int mods) => mods == 1 ? "1 mod" : $"{mods} mods";
    }
}
