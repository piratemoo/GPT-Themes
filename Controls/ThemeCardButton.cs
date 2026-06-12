using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ChatGptDesktopSkinner;

internal sealed class ThemeCardButton : Button
{
	public Theme ThemeData { get; set; }

	public bool SelectedTheme { get; set; }

	public int Radius { get; set; }

	public string DisplayName { get; set; }

	protected override bool ShowFocusCues => false;

	public ThemeCardButton()
	{
		Radius = 8;
		base.FlatStyle = FlatStyle.Flat;
		base.UseVisualStyleBackColor = false;
		base.TabStop = false;
		base.FlatAppearance.BorderSize = 0;
		TextAlign = ContentAlignment.MiddleLeft;
		Cursor = Cursors.Hand;
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		SetStyle(ControlStyles.Selectable, value: false);
	}

	protected override void OnPaintBackground(PaintEventArgs pevent)
	{
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		e.Graphics.Clear(UiShape.SurfaceBackColor(this, Color.FromArgb(11, 13, 23)));
		Rectangle bounds = new Rectangle(0, 0, Math.Max(1, base.Width - 1), Math.Max(1, base.Height - 1));
		Theme themeData = ThemeData;
		Color a = ((themeData == null) ? Color.FromArgb(28, 32, 44) : ParseColor(themeData.Panel, Color.FromArgb(28, 32, 44)));
		Color color = ((themeData == null) ? Color.FromArgb(235, 91, 166) : ParseColor(themeData.Accent, Color.FromArgb(235, 91, 166)));
		Color foreColor = Color.FromArgb(246, 248, 252);
		Color color2 = ((themeData == null) ? Color.FromArgb(10, 14, 25) : ParseColor(themeData.Bg, Color.FromArgb(10, 14, 25)));
		Color color3 = (SelectedTheme ? Blend(a, color, 0.22) : Color.FromArgb(18, 22, 34));
		using (GraphicsPath path = UiShape.RoundedRect(bounds, Radius))
		{
			using SolidBrush brush = new SolidBrush(color3);
			e.Graphics.FillPath(brush, path);
		}
		int num = Math.Max(31, Math.Min(41, Math.Min(base.Width - 22, base.Height - 36)));
		Rectangle rectangle = new Rectangle((base.Width - num) / 2, 6, num, num);
		using (GraphicsPath path2 = UiShape.RoundedRect(rectangle, 6))
		{
			using LinearGradientBrush brush2 = new LinearGradientBrush(rectangle, color2, color, LinearGradientMode.ForwardDiagonal);
			e.Graphics.FillPath(brush2, path2);
			using (LinearGradientBrush brush3 = new LinearGradientBrush(new Rectangle(rectangle.Left, rectangle.Top, rectangle.Width, Math.Max(1, rectangle.Height / 2)), Color.FromArgb(86, Color.White), Color.FromArgb(0, Color.White), LinearGradientMode.Vertical))
			{
				e.Graphics.FillPath(brush3, path2);
			}
			using Pen pen = new Pen(Color.FromArgb(55, Color.White), 1f);
			e.Graphics.DrawLine(pen, rectangle.Left + 6, rectangle.Bottom - 8, rectangle.Right - 6, rectangle.Top + 8);
			e.Graphics.DrawLine(pen, rectangle.Left + 16, rectangle.Bottom - 7, rectangle.Right - 5, rectangle.Top + 17);
		}
		string text = ((!string.IsNullOrEmpty(DisplayName)) ? DisplayName : ((themeData == null) ? "" : themeData.Name));
		Rectangle bounds2 = new Rectangle(5, rectangle.Bottom + 4, base.Width - 10, base.Height - rectangle.Bottom - 5);
		Font font = Font;
		bool flag = false;
		if (text.IndexOf(' ') < 0 && TextRenderer.MeasureText(e.Graphics, text, Font, bounds2.Size, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width > bounds2.Width)
		{
			font = new Font(Font.FontFamily, Math.Max(7.2f, Font.Size - 1.1f), Font.Style);
			flag = true;
		}
		TextRenderer.DrawText(e.Graphics, text, font, bounds2, foreColor, TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
		if (flag)
		{
			font.Dispose();
		}
		if (SelectedTheme)
		{
			Rectangle rectangle2 = new Rectangle(base.Width - 22, 6, 17, 17);
			using (SolidBrush brush = new SolidBrush(color))
			{
				e.Graphics.FillEllipse(brush, rectangle2);
			}
			using Font font2 = new Font(Font.FontFamily, 8.5f, FontStyle.Bold);
			TextRenderer.DrawText(e.Graphics, "✓", font2, rectangle2, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
		}
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		base.Region = null;
	}

	private static Color ParseColor(string hex, Color fallback)
	{
		try
		{
			return ColorTranslator.FromHtml(hex);
		}
		catch
		{
			return fallback;
		}
	}

	private static Color Blend(Color a, Color b, double amount)
	{
		amount = Math.Max(0.0, Math.Min(1.0, amount));
		return Color.FromArgb((int)((double)(int)a.R + (double)(b.R - a.R) * amount), (int)((double)(int)a.G + (double)(b.G - a.G) * amount), (int)((double)(int)a.B + (double)(b.B - a.B) * amount));
	}
}
