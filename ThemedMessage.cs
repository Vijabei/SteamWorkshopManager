using System;
using System.Drawing;
using System.Windows.Forms;

namespace WorkshopManager
{
    /// <summary>
    /// The app's replacement for MessageBox. The system box ignores the theme
    /// and the chosen text size: a bright little window over the dark app,
    /// hard to read at high resolutions. This one is built like the other
    /// dialogs - palette, text size, DPI - and marks its kind in colour:
    /// a band and heading in the warning colour for warnings, the error
    /// colour for errors, the accent otherwise.
    ///
    /// Same parameters and results as MessageBox.Show, so a call can switch
    /// by name alone.
    /// </summary>
    public sealed class ThemedMessage : Form
    {
        public static DialogResult Show(
            string text,
            string caption = "",
            MessageBoxButtons buttons = MessageBoxButtons.OK,
            MessageBoxIcon icon = MessageBoxIcon.None)
        {
            var owner = Form.ActiveForm;

            if (owner != null && owner.InvokeRequired)
            {
                return (DialogResult)owner.Invoke(new Func<DialogResult>(() => Show(text, caption, buttons, icon)));
            }

            using var dialog = new ThemedMessage(text, caption, buttons, icon);

            if (owner != null && owner.Visible && !owner.IsDisposed)
            {
                dialog.StartPosition = FormStartPosition.CenterParent;
                return dialog.ShowDialog(owner);
            }

            dialog.StartPosition = FormStartPosition.CenterScreen;
            return dialog.ShowDialog();
        }

        private ThemedMessage(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            // Written for 96 DPI at normal text size, as everywhere else
            SuspendLayout();
            AutoScaleDimensions = Theme.LayoutBase;
            AutoScaleMode = AutoScaleMode.Dpi;

            Text = string.IsNullOrEmpty(caption) ? "Workshop Mod Manager" : caption;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(380, 0);

            var kindColour = ColourFor(icon);

            // A band down the left edge in the colour of the message's kind
            var band = new Panel { Dock = DockStyle.Left, Width = 6 };

            var layout = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 3,
                Padding = new Padding(22, 18, 18, 14),
                Dock = DockStyle.Fill
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var symbol = new SymbolBox(SymbolFor(icon), kindColour)
            {
                Size = new Size(36, 36),
                Margin = new Padding(0, 2, 16, 0),
                Visible = icon != MessageBoxIcon.None
            };
            layout.Controls.Add(symbol, 0, 0);
            layout.SetRowSpan(symbol, 2);

            var heading = new Label
            {
                Text = string.IsNullOrEmpty(caption) ? "" : caption,
                Font = Theme.BoldFont,
                AutoSize = true,
                MaximumSize = new Size(480, 0),
                Margin = new Padding(0, 0, 0, 8),
                Visible = !string.IsNullOrEmpty(caption)
            };
            layout.Controls.Add(heading, 1, 0);

            var message = new Label
            {
                Text = text ?? "",
                Font = Theme.BaseFont,
                AutoSize = true,
                MaximumSize = new Size(480, 0),
                Margin = new Padding(0, 0, 0, 18)
            };
            layout.Controls.Add(message, 1, 1);

            var buttonRow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0)
            };
            layout.Controls.Add(buttonRow, 0, 2);
            layout.SetColumnSpan(buttonRow, 2);

            // Right to left: the last button added sits leftmost, which is
            // where Windows puts the affirmative one.
            Button primary = null;
            switch (buttons)
            {
                case MessageBoxButtons.YesNo:
                    Add(buttonRow, "No", DialogResult.No);
                    primary = Add(buttonRow, "Yes", DialogResult.Yes);
                    CancelButton = (Button)buttonRow.Controls[0];
                    break;
                case MessageBoxButtons.YesNoCancel:
                    CancelButton = Add(buttonRow, "Cancel", DialogResult.Cancel);
                    Add(buttonRow, "No", DialogResult.No);
                    primary = Add(buttonRow, "Yes", DialogResult.Yes);
                    break;
                case MessageBoxButtons.OKCancel:
                    CancelButton = Add(buttonRow, "Cancel", DialogResult.Cancel);
                    primary = Add(buttonRow, "OK", DialogResult.OK);
                    break;
                default:
                    primary = Add(buttonRow, "OK", DialogResult.OK);
                    CancelButton = primary;
                    break;
            }
            AcceptButton = primary;

            Controls.Add(layout);
            Controls.Add(band);

            Theme.Apply(this);
            Theme.StylePrimary(primary);

            // After Apply, which makes panels transparent and dims labels
            band.BackColor = kindColour;
            heading.ForeColor = icon == MessageBoxIcon.None ? Theme.Text : kindColour;
            message.ForeColor = Theme.Text;

            ResumeLayout(false);
            PerformLayout();

            Shown += (s, e) =>
            {
                Theme.ApplyTitleBar(this);
                primary?.Focus();
            };

            // Closing with the X answers like the safe choice
            FormClosing += (s, e) =>
            {
                if (DialogResult != DialogResult.Cancel || buttons == MessageBoxButtons.YesNoCancel ||
                    buttons == MessageBoxButtons.OKCancel) return;

                DialogResult = buttons == MessageBoxButtons.YesNo ? DialogResult.No : DialogResult.OK;
            };
        }

        private static Button Add(FlowLayoutPanel row, string text, DialogResult result)
        {
            var button = new Button
            {
                Text = text,
                DialogResult = result,
                MinimumSize = new Size(96, 30),
                AutoSize = true,
                Padding = new Padding(10, 2, 10, 2),
                Margin = new Padding(8, 0, 0, 0)
            };
            row.Controls.Add(button);
            return button;
        }

        // MessageBoxIcon has aliases (Warning = Exclamation, Error = Hand =
        // Stop, Information = Asterisk), so the values are compared, not names.
        private static Color ColourFor(MessageBoxIcon icon) => icon switch
        {
            MessageBoxIcon.Warning => Theme.Warning,
            MessageBoxIcon.Error => Theme.Error,
            _ => Theme.Accent
        };

        private static string SymbolFor(MessageBoxIcon icon) => icon switch
        {
            MessageBoxIcon.Warning => "!",
            MessageBoxIcon.Error => "×",
            MessageBoxIcon.Question => "?",
            _ => "i"
        };

        /// <summary>A filled circle in the kind's colour with its symbol.</summary>
        private sealed class SymbolBox : Control
        {
            private readonly string symbol;
            private readonly Color colour;

            public SymbolBox(string symbol, Color colour)
            {
                this.symbol = symbol;
                this.colour = colour;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                var circle = new Rectangle(0, 0, Width - 1, Height - 1);
                using (var fill = new SolidBrush(colour))
                {
                    e.Graphics.FillEllipse(fill, circle);
                }

                using var font = new Font("Segoe UI", Height * 0.42f, FontStyle.Bold, GraphicsUnit.Pixel);
                TextRenderer.DrawText(e.Graphics, symbol, font, circle, Theme.Background,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }
}
