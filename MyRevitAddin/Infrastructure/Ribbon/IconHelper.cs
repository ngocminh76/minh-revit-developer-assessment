using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace MyRevitAddin.Infrastructure.Ribbon
{
    /// <summary>
    /// Helper class for loading or generating icons for Ribbon buttons.
    /// </summary>
    public static class IconHelper
    {
        private static readonly string Namespace = "MyRevitAddin";

        /// <summary>
        /// Loads an icon from embedded resources or generates one dynamically from text initials.
        /// Resource format: "MyRevitAddin.Resources.Icons.{resourceName}_{size}.png"
        /// </summary>
        public static BitmapImage GetIcon(string resourceName, int size = 32)
        {
            // 1. Attempt to load from embedded resource
            string fullName = $"{Namespace}.Resources.Icons.{resourceName}_{size}.png";
            var assembly = Assembly.GetExecutingAssembly();
            var stream = assembly.GetManifestResourceStream(fullName);
            if (stream != null)
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = stream;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }

            // 2. Fallback: generate icon from text initials
            return GenerateIconFromText(resourceName, size);
        }

        /// <summary>
        /// Generates a simple placeholder icon from text initials when no image resource is available.
        /// </summary>
        private static BitmapImage GenerateIconFromText(string text, int size)
        {
            // Extract initials from text
            string initials = GetInitials(text);
            Color bgColor = GetColorFromText(text);

            using (var bitmap = new Bitmap(size, size))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                // Background rounded rect
                using (var brush = new SolidBrush(bgColor))
                {
                    int r = size / 5;
                    var rect = new Rectangle(0, 0, size, size);
                    using (var path = RoundedRect(rect, r))
                    {
                        g.FillPath(brush, path);
                    }
                }

                // Text
                float fontSize = size * 0.38f;
                using (var font = new Font("Segoe UI", fontSize, FontStyle.Bold))
                using (var textBrush = new SolidBrush(Color.White))
                {
                    var sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    g.DrawString(initials, font, textBrush, new RectangleF(0, 0, size, size), sf);
                }

                // Convert to BitmapImage
                using (var ms = new MemoryStream())
                {
                    bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Position = 0;

                    var bmpImage = new BitmapImage();
                    bmpImage.BeginInit();
                    bmpImage.StreamSource = ms;
                    bmpImage.CacheOption = BitmapCacheOption.OnLoad;
                    bmpImage.EndInit();
                    bmpImage.Freeze();
                    return bmpImage;
                }
            }
        }

        private static string GetInitials(string name)
        {
            // "adjust_beam" → "AB", "bearing_plate" → "BP"
            var parts = name.Split(new[] { '_', ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return (parts[0][0].ToString() + parts[1][0].ToString()).ToUpper();
            if (parts.Length == 1 && parts[0].Length >= 2)
                return parts[0].Substring(0, 2).ToUpper();
            return name.Length >= 2 ? name.Substring(0, 2).ToUpper() : name.ToUpper();
        }

        private static Color GetColorFromText(string text)
        {
            // Hash-based color palette lookup for consistent button coloring
            int hash = Math.Abs(text.GetHashCode());
            Color[] palette = new[]
            {
                Color.FromArgb(52, 152, 219),   // Blue
                Color.FromArgb(46, 204, 113),   // Green
                Color.FromArgb(155, 89, 182),   // Purple
                Color.FromArgb(231, 76, 60),    // Red
                Color.FromArgb(243, 156, 18),   // Orange
                Color.FromArgb(26, 188, 156),   // Teal
                Color.FromArgb(41, 128, 185),   // Dark Blue
                Color.FromArgb(192, 57, 43),    // Dark Red
            };
            return palette[hash % palette.Length];
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
