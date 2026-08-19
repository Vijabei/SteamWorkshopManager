using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WorkshopManager
{
    /// <summary>
    /// Central dark colour palette and control styling, deliberately kept in
    /// one place so the look can be changed without touching the forms.
    /// The palette mirrors the Steam theme of the softknight.de website.
    /// </summary>
    public static class Theme
    {
        // Surfaces
        public static readonly Color Background = Color.FromArgb(27, 40, 56);     // #1B2838
        public static readonly Color Surface = Color.FromArgb(36, 52, 71);        // #243447
        public static readonly Color SurfaceAlt = Color.FromArgb(30, 45, 62);     // #1E2D3E
        public static readonly Color SurfaceHover = Color.FromArgb(46, 66, 90);
        public static readonly Color Border = Color.FromArgb(61, 75, 94);         // #3D4B5E

        // Text
        public static readonly Color Text = Color.FromArgb(199, 213, 224);        // #C7D5E0
        public static readonly Color TextDim = Color.FromArgb(143, 152, 160);     // #8F98A0
        public static readonly Color TextOnAccent = Color.FromArgb(20, 30, 42);

        // Accent and states
        public static readonly Color Accent = Color.FromArgb(102, 192, 244);      // #66C0F4
        public static readonly Color AccentHover = Color.FromArgb(142, 209, 255);
        public static readonly Color Success = Color.FromArgb(164, 208, 7);       // #A4D007
        public static readonly Color Warning = Color.FromArgb(232, 197, 107);
        public static readonly Color Error = Color.FromArgb(255, 123, 123);
        public static readonly Color Muted = Color.FromArgb(120, 132, 145);

        public static readonly Font BaseFont = new("Segoe UI", 9F);
        public static readonly Font BoldFont = new("Segoe UI", 9F, FontStyle.Bold);
        public static readonly Font TitleFont = new("Segoe UI Semibold", 12F);
        public static readonly Font SmallFont = new("Segoe UI", 8.25F);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowScrollBar(IntPtr hwnd, int bar, bool show);

        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int SbHorz = 0;

        /// <summary>
        /// Paints the window chrome dark. Supported from Windows 10 1809 on;
        /// silently ignored elsewhere, which just leaves a light title bar.
        /// </summary>
        public static void ApplyDarkTitleBar(Form form)
        {
            try
            {
                int enabled = 1;
                DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
            }
            catch
            {
                // Cosmetic only - never let this break startup
            }
        }

        /// <summary>Styles a control and everything below it.</summary>
        public static void Apply(Control control)
        {
            switch (control)
            {
                // Styles itself, including per-state label colours that the
                // generic rules below would flatten.
                case ModDetailPanel:
                    return;

                case Form form:
                    form.BackColor = Background;
                    form.ForeColor = Text;
                    form.Font = BaseFont;
                    break;

                case Button button:
                    StyleButton(button);
                    return; // no children worth styling

                case TextBox textBox:
                    textBox.BackColor = SurfaceAlt;
                    textBox.ForeColor = Text;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    break;

                // Deliberately left at the system FlatStyle: a flat check box
                // renders its tick in the fore colour and becomes invisible
                // on a dark surface.
                case CheckBox checkBox:
                    checkBox.ForeColor = Text;
                    checkBox.BackColor = Color.Transparent;
                    break;

                case Label label:
                    label.ForeColor = label.Font.Bold ? Text : TextDim;
                    label.BackColor = Color.Transparent;
                    break;

                // No border: WinForms draws FixedSingle in a system colour,
                // which shows up as a light frame on the dark surface.
                case ListView listView:
                    listView.BackColor = SurfaceAlt;
                    listView.ForeColor = Text;
                    listView.BorderStyle = BorderStyle.None;
                    break;

                case ProgressBar progressBar:
                    progressBar.BackColor = SurfaceAlt;
                    progressBar.ForeColor = Accent;
                    break;

                case TabControl tabControl:
                    StyleTabControl(tabControl);
                    break;

                case TabPage tabPage:
                    tabPage.BackColor = Background;
                    tabPage.ForeColor = Text;
                    break;

                // Must come before Panel: the halves keep the colours the
                // SplitContainer case gives them, but still get styled inside.
                case SplitterPanel:
                    break;

                // Covers TableLayoutPanel and FlowLayoutPanel as well, both
                // derive from Panel.
                case Panel panel:
                    panel.BackColor = Color.Transparent;
                    break;

                case SplitContainer split:
                    split.BackColor = Border;
                    split.Panel1.BackColor = Background;
                    split.Panel2.BackColor = Surface;
                    break;
            }

            foreach (Control child in control.Controls)
            {
                Apply(child);
            }
        }

        /// <summary>Flat accent button used for the main action.</summary>
        public static void StylePrimary(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Accent;
            button.ForeColor = TextOnAccent;
            button.Font = BoldFont;
            button.Cursor = Cursors.Hand;
            button.FlatAppearance.MouseOverBackColor = AccentHover;
            button.FlatAppearance.MouseDownBackColor = AccentHover;
        }

        private static void StyleButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Surface;
            button.ForeColor = Text;
            button.Font = BaseFont;
            button.Cursor = Cursors.Hand;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = SurfaceHover;
            button.FlatAppearance.MouseDownBackColor = SurfaceAlt;
        }


        /// <summary>
        /// Widens the last column to the right edge. The header strip beyond
        /// the last column is drawn by the native header control in system
        /// colours and cannot be painted over, so it is removed instead.
        /// Call again after filling the list: the vertical scroll bar only
        /// appears then, and it takes width away from the client area.
        /// </summary>
        public static void StretchLastColumn(ListView listView)
        {
            if (listView.Columns.Count == 0) return;

            var used = 0;
            for (int i = 0; i < listView.Columns.Count - 1; i++)
            {
                used += listView.Columns[i].Width;
            }

            var last = listView.Columns[listView.Columns.Count - 1];

            // A few pixels short of the client area: filling it exactly makes
            // the control add a horizontal scroll bar.
            var width = listView.ClientSize.Width - used - 4;

            // The equality check also breaks the recursion through
            // ColumnWidthChanged that setting the width would otherwise cause.
            if (width >= 60 && width != last.Width) last.Width = width;

            // The last column always fills the remaining space, so horizontal
            // scrolling is never needed - but the control still shows the bar
            // in system colours while it settles. Hide it outright.
            if (listView.IsHandleCreated) ShowScrollBar(listView.Handle, SbHorz, false);
        }

        /// <summary>
        /// Owner-draws the tab strip and paints over the light 3D frame the
        /// control puts around the page area, which no property can turn off.
        /// </summary>
        public static void StyleTabControl(TabControl tabs)
        {
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.ItemSize = new Size(150, 30);
            tabs.BackColor = Background;

            tabs.DrawItem += (s, e) =>
            {
                bool selected = e.Index == tabs.SelectedIndex;

                using var background = new SolidBrush(selected ? Background : SurfaceAlt);
                e.Graphics.FillRectangle(background, e.Bounds);

                if (selected)
                {
                    using var underline = new SolidBrush(Accent);
                    e.Graphics.FillRectangle(underline, e.Bounds.Left, e.Bounds.Bottom - 3,
                        e.Bounds.Width, 3);
                }

                TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text,
                    selected ? BoldFont : BaseFont, e.Bounds, selected ? Text : TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };

            tabs.Paint += (s, e) =>
            {
                var client = tabs.ClientRectangle;

                // Strip area to the right of the last tab
                var lastTab = tabs.TabCount > 0
                    ? tabs.GetTabRect(tabs.TabCount - 1)
                    : Rectangle.Empty;
                using var fill = new SolidBrush(Background);
                e.Graphics.FillRectangle(fill, lastTab.Right, client.Top,
                    client.Width - lastTab.Right, tabs.ItemSize.Height + 4);

                // Erase the frame around the page, then draw a subtle one
                var top = tabs.ItemSize.Height + 2;
                using var eraser = new Pen(Background, 4);
                e.Graphics.DrawRectangle(eraser, client.Left + 1, top,
                    client.Width - 3, client.Height - top - 3);
            };
        }

        /// <summary>
        /// Hooks owner drawing on a details ListView so header and rows follow
        /// the palette. WinForms draws both in system colours otherwise, which
        /// leaves a bright header on a dark list.
        /// </summary>
        public static void StyleListView(ListView listView)
        {
            listView.OwnerDraw = true;
            listView.FullRowSelect = true;
            listView.GridLines = false;

            listView.DrawColumnHeader += (s, e) =>
            {
                using var background = new SolidBrush(Surface);
                e.Graphics.FillRectangle(background, e.Bounds);

                using var separator = new Pen(Border);
                e.Graphics.DrawLine(separator, e.Bounds.Right - 1, e.Bounds.Top + 4,
                    e.Bounds.Right - 1, e.Bounds.Bottom - 4);
                e.Graphics.DrawLine(separator, e.Bounds.Left, e.Bounds.Bottom - 1,
                    e.Bounds.Right, e.Bounds.Bottom - 1);

                TextRenderer.DrawText(e.Graphics, e.Header.Text, BoldFont,
                    Rectangle.Inflate(e.Bounds, -8, 0), TextDim,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };

            listView.DrawSubItem += (s, e) =>
            {
                bool selected = e.Item.Selected;
                var rowColor = selected
                    ? SurfaceHover
                    : (e.ItemIndex % 2 == 0 ? SurfaceAlt : Background);

                using var background = new SolidBrush(rowColor);
                e.Graphics.FillRectangle(background, e.Bounds);

                if (selected && e.ColumnIndex == 0)
                {
                    using var marker = new SolidBrush(Accent);
                    e.Graphics.FillRectangle(marker, e.Bounds.Left, e.Bounds.Top, 3, e.Bounds.Height);
                }

                var text = e.ColumnIndex == 0 ? e.Item.Text : e.SubItem.Text;
                var color = e.Item.ForeColor == SystemColors.WindowText ? Text : e.Item.ForeColor;

                var bounds = Rectangle.Inflate(e.Bounds, -8, 0);
                if (selected && e.ColumnIndex == 0) bounds.X += 3;

                TextRenderer.DrawText(e.Graphics, text, BaseFont, bounds, color,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };

            // Nothing extra to paint per item; sub item drawing covers the row.
            listView.DrawItem += (s, e) => { };

            // The header is a native child window, so painting over the strip
            // right of the last column does not work - it gets redrawn in
            // system colours. Stretching the last column to the edge removes
            // the strip altogether.
            // ClientSizeChanged, not SizeChanged: the usable width also shrinks
            // when the vertical scroll bar appears, without the control resizing.
            listView.ClientSizeChanged += (s, e) => StretchLastColumn(listView);
            listView.ColumnWidthChanged += (s, e) => StretchLastColumn(listView);
        }
    }
}
