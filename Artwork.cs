using System;
using System.Drawing;
using System.Drawing.Drawing2D;
namespace AirPodsQuickConnect
{
    internal static class Artwork
    {
        internal static Icon Make(Color background)
        {
            using (Bitmap bitmap = new Bitmap(64, 64))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                using (Brush fill = new SolidBrush(background)) graphics.FillEllipse(fill, 2, 2, 60, 60);
                using (Pen white = new Pen(Color.White, 6)) graphics.DrawArc(white, 16, 13, 32, 36, 180, 180);
                using (Brush white = new SolidBrush(Color.White))
                {
                    graphics.FillEllipse(white, 12, 29, 13, 22);
                    graphics.FillEllipse(white, 39, 29, 13, 22);
                }
                IntPtr handle = bitmap.GetHicon();
                try { using (Icon borrowed = Icon.FromHandle(handle)) return (Icon)borrowed.Clone(); }
                finally { Native.DestroyIcon(handle); }
            }
        }
    }

}
