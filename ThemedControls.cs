using System;
using System.Drawing;
using System.Windows.Forms;

namespace WorkshopManager
{
    /// <summary>
    /// A tab control that paints all of itself from the palette.
    ///
    /// The plain TabControl only lets the tabs be owner-drawn. The strip to
    /// the right of the last tab and the 3D frame around the page are drawn
    /// by the native control in system colours, and its Paint event never
    /// fires, so there is nothing to hook. That left a light grey band beside
    /// the tabs and a light frame around every page in dark mode.
    ///
    /// With UserPaint the native control stops painting altogether, while it
    /// still does the layout and handles clicks and the keyboard.
    /// </summary>
    public sealed class ThemedTabControl : TabControl
    {
        public ThemedTabControl()
        {
            SetStyle(ControlStyles.UserPaint
                     | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw, true);

            DrawMode = TabDrawMode.OwnerDrawFixed;
            SizeMode = TabSizeMode.Fixed;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Background);

            if (TabCount == 0) return;

            // A hairline under the strip ties the tabs to the page below
            var stripBottom = GetTabRect(0).Bottom;
            using (var line = new Pen(Theme.Border))
            {
                g.DrawLine(line, 0, stripBottom, Width, stripBottom);
            }

            for (int i = 0; i < TabCount; i++)
            {
                DrawTab(g, i);
            }
        }

        private void DrawTab(Graphics g, int index)
        {
            var bounds = GetTabRect(index);
            bool selected = index == SelectedIndex;

            using (var background = new SolidBrush(selected ? Theme.Background : Theme.SurfaceAlt))
            {
                g.FillRectangle(background, bounds);
            }

            if (selected)
            {
                var thickness = Math.Max(3, LogicalToDeviceUnits(3));
                using var underline = new SolidBrush(Theme.Accent);
                g.FillRectangle(underline, bounds.Left, bounds.Bottom - thickness,
                    bounds.Width, thickness);
            }

            TextRenderer.DrawText(g, TabPages[index].Text,
                selected ? Theme.BoldFont : Theme.BaseFont, bounds,
                selected ? Theme.Text : Theme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis);

            if (selected && Focused && ShowFocusCues)
            {
                ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(bounds, -3, -3),
                    Theme.Text, Theme.Background);
            }
        }

        // Selecting a tab only repaints the tab strip natively; the whole
        // control has to be redrawn so the old tab loses its highlight.
        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            base.OnSelectedIndexChanged(e);
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }
    }

    /// <summary>
    /// A flat progress bar in the palette's colours. With visual styles on,
    /// Windows draws the standard bar in its own light theme and ignores
    /// BackColor and ForeColor, which put a bright bar into the dark window.
    /// </summary>
    public sealed class ThemedProgressBar : ProgressBar
    {
        public ThemedProgressBar()
        {
            SetStyle(ControlStyles.UserPaint
                     | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var bounds = ClientRectangle;

            using (var background = new SolidBrush(Theme.SurfaceAlt))
            {
                g.FillRectangle(background, bounds);
            }

            var range = Maximum - Minimum;
            if (range > 0 && Value > Minimum)
            {
                var inner = Rectangle.Inflate(bounds, -1, -1);
                var filled = (int)Math.Round(inner.Width * (double)(Value - Minimum) / range);
                using var bar = new SolidBrush(Theme.Accent);
                g.FillRectangle(bar, inner.Left, inner.Top, filled, inner.Height);
            }

            using var border = new Pen(Theme.Border);
            g.DrawRectangle(border, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
        }
    }
}
