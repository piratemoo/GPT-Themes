using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ChatGptDesktopSkinner;

internal sealed class RoundedButton : Button
{
	private bool _hovered;

	private bool _pressed;

	public int Radius { get; set; }

	public Color DisabledBackColor { get; set; }

	public Color DisabledForeColor { get; set; }

	public ButtonIconKind IconKind { get; set; }

	protected override bool ShowFocusCues => false;

	public RoundedButton()
	{
		Radius = 8;
		IconKind = ButtonIconKind.None;
		base.FlatStyle = FlatStyle.Flat;
		base.UseVisualStyleBackColor = false;
		base.TabStop = false;
		base.FlatAppearance.BorderSize = 0;
		DisabledBackColor = Color.FromArgb(25, 28, 38);
		DisabledForeColor = Color.FromArgb(92, 98, 112);
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		SetStyle(ControlStyles.Selectable, value: false);
	}

	protected override void OnPaintBackground(PaintEventArgs pevent)
	{
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		e.Graphics.Clear(UiShape.SurfaceBackColor(this, Color.FromArgb(17, 20, 31)));
		Rectangle bounds = new Rectangle(0, 0, Math.Max(1, base.Width - 1), Math.Max(1, base.Height - 1));
		Color color = (base.Enabled ? StateBackColor(BackColor) : DisabledBackColor);
		Color color2 = (base.Enabled ? ForeColor : DisabledForeColor);
		using (GraphicsPath path = UiShape.RoundedRect(bounds, Radius))
		{
			using SolidBrush brush = new SolidBrush(color);
			e.Graphics.FillPath(brush, path);
		}
		if (base.Image != null)
		{
			int num = Math.Max(20, Math.Min(26, bounds.Height - 8));
			int num2 = ((!string.IsNullOrEmpty(Text)) ? 10 : 0);
			Size size = TextRenderer.MeasureText(e.Graphics, Text, Font, new Size(Math.Max(1, base.Width - 44), base.Height), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
			int num3 = (string.IsNullOrEmpty(Text) ? num : Math.Min(bounds.Width - 18, num + num2 + size.Width));
			int num4 = bounds.Left + Math.Max(10, (bounds.Width - num3) / 2);
			int num5 = bounds.Top + (bounds.Height - num) / 2;
			e.Graphics.DrawImage(base.Image, new Rectangle(num4, num5, num, num));
			if (!string.IsNullOrEmpty(Text))
			{
				TextRenderer.DrawText(bounds: new Rectangle(num4 + num + num2, bounds.Top, Math.Max(1, bounds.Right - num4 - num - num2 - 8), bounds.Height), dc: e.Graphics, text: Text, font: Font, foreColor: color2, flags: TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
			}
			return;
		}
		if (IconKind == ButtonIconKind.None)
		{
			TextRenderer.DrawText(e.Graphics, Text, Font, bounds, color2, TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
			return;
		}
		int num6 = 16;
		int num7 = 9;
		Size size2 = TextRenderer.MeasureText(e.Graphics, Text, Font, new Size(Math.Max(1, base.Width - 36), base.Height), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
		int num8 = Math.Min(bounds.Width - 18, num6 + num7 + size2.Width);
		int num9 = bounds.Left + Math.Max(10, (bounds.Width - num8) / 2);
		int num10 = bounds.Top + (bounds.Height - num6) / 2;
		Rectangle r = new Rectangle(num9, num10, num6, num6);
		Rectangle bounds2 = new Rectangle(r.Right + num7, bounds.Top, Math.Max(1, bounds.Right - r.Right - num7 - 8), bounds.Height);
		if (string.IsNullOrEmpty(Text))
		{
			DrawButtonIcon(r: new Rectangle(bounds.Left + (bounds.Width - num6) / 2, bounds.Top + (bounds.Height - num6) / 2, num6, num6), g: e.Graphics, color: color2);
			return;
		}
		DrawButtonIcon(e.Graphics, r, color2);
		TextRenderer.DrawText(e.Graphics, Text, Font, bounds2, color2, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
	}

	protected override void OnMouseEnter(EventArgs e)
	{
		_hovered = true;
		Invalidate();
		base.OnMouseEnter(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		_hovered = false;
		_pressed = false;
		Invalidate();
		base.OnMouseLeave(e);
	}

	protected override void OnMouseDown(MouseEventArgs mevent)
	{
		if (mevent.Button == MouseButtons.Left)
		{
			_pressed = true;
			Invalidate();
		}
		base.OnMouseDown(mevent);
	}

	protected override void OnMouseUp(MouseEventArgs mevent)
	{
		_pressed = false;
		Invalidate();
		base.OnMouseUp(mevent);
	}

	protected override void OnEnabledChanged(EventArgs e)
	{
		_hovered = false;
		_pressed = false;
		Invalidate();
		base.OnEnabledChanged(e);
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		UpdateRegion();
	}

	protected override void OnParentChanged(EventArgs e)
	{
		base.OnParentChanged(e);
		UpdateRegion();
	}

	private void UpdateRegion()
	{
		if (base.Width <= 0 || base.Height <= 0)
		{
			return;
		}
		using GraphicsPath path = UiShape.RoundedRect(new Rectangle(0, 0, base.Width, base.Height), Radius);
		base.Region = new Region(path);
	}

	private Color StateBackColor(Color baseColor)
	{
		if (_pressed && base.FlatAppearance.MouseDownBackColor != Color.Empty)
		{
			return base.FlatAppearance.MouseDownBackColor;
		}
		if (_hovered && base.FlatAppearance.MouseOverBackColor != Color.Empty)
		{
			return base.FlatAppearance.MouseOverBackColor;
		}
		if (_pressed)
		{
			return Blend(baseColor, Color.Black, IsLight(baseColor) ? 0.12 : 0.06);
		}
		if (_hovered)
		{
			return Blend(baseColor, Color.White, IsLight(baseColor) ? 0.05 : 0.08);
		}
		return baseColor;
	}

	private static bool IsLight(Color color)
	{
		return (color.R * 299 + color.G * 587 + color.B * 114) / 1000 > 160;
	}

	private static Color Blend(Color first, Color second, double amount)
	{
		amount = Math.Max(0.0, Math.Min(1.0, amount));
		return Color.FromArgb(first.A, (int)((double)(int)first.R + (double)(second.R - first.R) * amount), (int)((double)(int)first.G + (double)(second.G - first.G) * amount), (int)((double)(int)first.B + (double)(second.B - first.B) * amount));
	}

	private void DrawButtonIcon(Graphics g, Rectangle r, Color color)
	{
		using Pen pen = new Pen(color, 1.65f);
		using SolidBrush brush = new SolidBrush(color);
		pen.StartCap = LineCap.Round;
		pen.EndCap = LineCap.Round;
		pen.LineJoin = LineJoin.Round;
		if (IconKind == ButtonIconKind.Eye)
		{
			using (GraphicsPath graphicsPath = new GraphicsPath())
			{
				graphicsPath.AddBezier(r.Left + 1, r.Top + r.Height / 2, r.Left + 5, r.Top + 3, r.Right - 5, r.Top + 3, r.Right - 1, r.Top + r.Height / 2);
				graphicsPath.AddBezier(r.Right - 1, r.Top + r.Height / 2, r.Right - 5, r.Bottom - 3, r.Left + 5, r.Bottom - 3, r.Left + 1, r.Top + r.Height / 2);
				g.DrawPath(pen, graphicsPath);
			}
			g.FillEllipse(brush, r.Left + 6, r.Top + 6, 4, 4);
		}
		else if (IconKind == ButtonIconKind.Trash)
		{
			g.DrawLine(pen, r.Left + 4, r.Top + 5, r.Right - 4, r.Top + 5);
			g.DrawLine(pen, r.Left + 6, r.Top + 5, r.Left + 7, r.Bottom - 2);
			g.DrawLine(pen, r.Right - 6, r.Top + 5, r.Right - 7, r.Bottom - 2);
			g.DrawLine(pen, r.Left + 7, r.Bottom - 2, r.Right - 7, r.Bottom - 2);
			g.DrawLine(pen, r.Left + 7, r.Top + 3, r.Right - 7, r.Top + 3);
			g.DrawLine(pen, r.Left + 8, r.Top + 8, r.Left + 8, r.Bottom - 5);
			g.DrawLine(pen, r.Right - 8, r.Top + 8, r.Right - 8, r.Bottom - 5);
		}
		else if (IconKind == ButtonIconKind.Eraser)
		{
			using (GraphicsPath graphicsPath2 = new GraphicsPath())
			{
				graphicsPath2.AddPolygon(new Point[5]
				{
					new Point(r.Left + 5, r.Top + 11),
					new Point(r.Left + 10, r.Top + 5),
					new Point(r.Right - 3, r.Top + 10),
					new Point(r.Right - 8, r.Bottom - 2),
					new Point(r.Left + 5, r.Bottom - 6)
				});
				g.DrawPath(pen, graphicsPath2);
			}
			g.DrawLine(pen, r.Left + 8, r.Bottom - 5, r.Right - 5, r.Bottom - 5);
			g.DrawLine(pen, r.Left + 10, r.Top + 7, r.Right - 6, r.Top + 12);
		}
		else if (IconKind == ButtonIconKind.Check)
		{
			g.DrawLine(pen, r.Left + 3, r.Top + 8, r.Left + 7, r.Top + 12);
			g.DrawLine(pen, r.Left + 7, r.Top + 12, r.Right - 3, r.Top + 4);
		}
		else if (IconKind == ButtonIconKind.External)
		{
			g.DrawRectangle(pen, r.Left + 3, r.Top + 6, r.Width - 9, r.Height - 9);
			g.DrawLine(pen, r.Left + 8, r.Top + 8, r.Right - 3, r.Top + 8);
			g.DrawLine(pen, r.Right - 3, r.Top + 8, r.Right - 3, r.Top + 13);
			g.DrawLine(pen, r.Left + 9, r.Bottom - 9, r.Right - 3, r.Top + 8);
		}
		else if (IconKind == ButtonIconKind.WindowMinimize)
		{
			g.DrawLine(pen, r.Left + 3, r.Top + r.Height / 2, r.Right - 3, r.Top + r.Height / 2);
		}
		else if (IconKind == ButtonIconKind.WindowMaximize)
		{
			g.DrawRectangle(pen, r.Left + 4, r.Top + 4, r.Width - 8, r.Height - 8);
		}
		else if (IconKind == ButtonIconKind.WindowClose)
		{
			g.DrawLine(pen, r.Left + 4, r.Top + 4, r.Right - 4, r.Bottom - 4);
			g.DrawLine(pen, r.Right - 4, r.Top + 4, r.Left + 4, r.Bottom - 4);
		}
	}
}
