using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace PCCheck
{
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(243, 246, 250);
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(226, 232, 240);
        public static readonly Color BorderSoft = Color.FromArgb(241, 245, 249);
        public static readonly Color Text = Color.FromArgb(15, 23, 42);
        public static readonly Color TextSecondary = Color.FromArgb(71, 85, 105);
        public static readonly Color Muted = Color.FromArgb(100, 116, 139);
        public static readonly Color Faint = Color.FromArgb(148, 163, 184);
        public static readonly Color Blue = Color.FromArgb(37, 99, 235);
        public static readonly Color BlueDark = Color.FromArgb(29, 78, 216);
        public static readonly Color BlueSoft = Color.FromArgb(219, 234, 254);
        public static readonly Color Navy = Color.FromArgb(11, 31, 51);
        public static readonly Color NavyLight = Color.FromArgb(18, 61, 90);
        public static readonly Color Teal = Color.FromArgb(14, 116, 144);
        public static readonly Color Green = Color.FromArgb(22, 163, 74);
        public static readonly Color GreenSoft = Color.FromArgb(220, 252, 231);
        public static readonly Color Amber = Color.FromArgb(217, 119, 6);
        public static readonly Color AmberSoft = Color.FromArgb(254, 243, 199);
        public static readonly Color Red = Color.FromArgb(220, 38, 38);
        public static readonly Color RedSoft = Color.FromArgb(254, 226, 226);
        public static readonly Color GraySoft = Color.FromArgb(241, 245, 249);
        public static readonly Color Hover = Color.FromArgb(248, 250, 252);

        public static readonly Font FontBody;
        public static readonly Font FontBodyBold;
        public static readonly Font FontSmall;
        public static readonly Font FontTiny;
        public static readonly Font FontTitle;
        public static readonly Font FontHeadline;
        public static readonly Font FontSection;
        public static readonly Font FontTab;
        public static readonly Font FontButton;
        public static readonly Font FontMetric;

        public static float Scale = 1f;

        private static readonly string FamilyName;

        static Theme()
        {
            FamilyName = ChooseFontFamily();
            FontBody = new Font(FamilyName, 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            FontBodyBold = new Font(FamilyName, 9.5f, FontStyle.Bold, GraphicsUnit.Point);
            FontSmall = new Font(FamilyName, 8.5f, FontStyle.Regular, GraphicsUnit.Point);
            FontTiny = new Font(FamilyName, 8f, FontStyle.Regular, GraphicsUnit.Point);
            FontTitle = new Font(FamilyName, 20f, FontStyle.Bold, GraphicsUnit.Point);
            FontHeadline = new Font(FamilyName, 15f, FontStyle.Bold, GraphicsUnit.Point);
            FontSection = new Font(FamilyName, 11.5f, FontStyle.Bold, GraphicsUnit.Point);
            FontTab = new Font(FamilyName, 10.5f, FontStyle.Bold, GraphicsUnit.Point);
            FontButton = new Font(FamilyName, 9.5f, FontStyle.Bold, GraphicsUnit.Point);
            FontMetric = new Font(FamilyName, 17f, FontStyle.Bold, GraphicsUnit.Point);
        }

        private static string ChooseFontFamily()
        {
            string[] preferred = new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Segoe UI" };
            try
            {
                foreach (string name in preferred)
                {
                    foreach (FontFamily family in FontFamily.Families)
                    {
                        if (string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase))
                            return family.Name;
                    }
                }
            }
            catch
            {
            }
            return SystemFonts.MessageBoxFont.FontFamily.Name;
        }

        public static void InitializeDpi()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    Scale = g.DpiX / 96f;
                }
            }
            catch
            {
                Scale = 1f;
            }
            if (Scale < 1f) Scale = 1f;
            if (Scale > 3f) Scale = 3f;
        }

        public static int S(int value)
        {
            return (int)Math.Round(value * Scale);
        }

        public static Size S(Size value)
        {
            return new Size(S(value.Width), S(value.Height));
        }

        public static Padding S(Padding value)
        {
            return new Padding(S(value.Left), S(value.Top), S(value.Right), S(value.Bottom));
        }

        public static Color StatusColor(CheckStatus status)
        {
            switch (status)
            {
                case CheckStatus.Ok: return Green;
                case CheckStatus.Warning: return Amber;
                case CheckStatus.Critical: return Red;
                default: return Faint;
            }
        }

        public static Color StatusSoft(CheckStatus status)
        {
            switch (status)
            {
                case CheckStatus.Ok: return GreenSoft;
                case CheckStatus.Warning: return AmberSoft;
                case CheckStatus.Critical: return RedSoft;
                default: return GraySoft;
            }
        }

        public static GraphicsPath RoundRect(Rectangle bounds, int radius)
        {
            int d = Math.Max(1, S(radius) * 2);
            GraphicsPath path = new GraphicsPath();
            if (bounds.Width <= d || bounds.Height <= d)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRoundRect(Graphics g, Brush brush, Rectangle bounds, int radius)
        {
            using (GraphicsPath path = RoundRect(bounds, radius))
            {
                g.FillPath(brush, path);
            }
        }

        public static void DrawRoundRect(Graphics g, Pen pen, Rectangle bounds, int radius)
        {
            using (GraphicsPath path = RoundRect(bounds, radius))
            {
                g.DrawPath(pen, path);
            }
        }

        public static void ApplyRoundedRegion(Control control, int radius)
        {
            if (control.Width <= 0 || control.Height <= 0) return;
            using (GraphicsPath path = RoundRect(new Rectangle(0, 0, control.Width, control.Height), radius))
            {
                Region old = control.Region;
                control.Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        public static void SetHighQuality(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }
    }
}
