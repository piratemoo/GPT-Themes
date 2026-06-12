using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ChatGptDesktopSkinner;

internal static class UiShape
{
	public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
	{
		int num = Math.Max(1, radius);
		int num2 = num * 2;
		GraphicsPath graphicsPath = new GraphicsPath();
		graphicsPath.AddArc(bounds.Left, bounds.Top, num2, num2, 180f, 90f);
		graphicsPath.AddArc(bounds.Right - num2, bounds.Top, num2, num2, 270f, 90f);
		graphicsPath.AddArc(bounds.Right - num2, bounds.Bottom - num2, num2, num2, 0f, 90f);
		graphicsPath.AddArc(bounds.Left, bounds.Bottom - num2, num2, num2, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	public static Color SurfaceBackColor(Control control, Color fallback)
	{
		for (Control control2 = control?.Parent; control2 != null; control2 = control2.Parent)
		{
			if (control2.BackColor.A > 0 && control2.BackColor != Color.Transparent)
			{
				return control2.BackColor;
			}
		}
		return fallback;
	}
}
