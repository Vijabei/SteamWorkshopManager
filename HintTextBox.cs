using System.Drawing;
using System.Windows.Forms;

namespace WorkshopManager
{
    /// <summary>
    /// Text box with a hint whose colour we control.
    ///
    /// WinForms' own PlaceholderText is hard-wired to SystemColors.GrayText.
    /// That is a light-theme colour and stays unchanged regardless of the
    /// system theme, which leaves it barely readable on a dark surface.
    /// </summary>
    public class HintTextBox : TextBox
    {
        private const int WmPaint = 0x000F;

        public string Hint { get; set; } = "";

        public Color HintColor { get; set; } = SystemColors.GrayText;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            // Draw on top of the control's own painting, and only while it is
            // empty - exactly when the native placeholder would show.
            if (m.Msg != WmPaint || Text.Length > 0 || Hint.Length == 0) return;

            using var graphics = CreateGraphics();
            TextRenderer.DrawText(graphics, Hint, Font,
                new Rectangle(1, 0, Width - 2, Height), HintColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }
}
