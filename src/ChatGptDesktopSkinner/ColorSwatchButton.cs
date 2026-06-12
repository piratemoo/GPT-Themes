using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ChatGptDesktopSkinner;

internal sealed class ColorSwatchButton : Button
{
	public Color SwatchColor { get; set; }

	public string LabelText { get; set; }

	public string HexText { get; set; }

	protected override bool ShowFocusCues => false;

	public ColorSwatchButton()
	{
		SwatchColor = Color.FromArgb(31, 35, 49);
		LabelText = "";
		HexText = "";
		base.FlatStyle = FlatStyle.Flat;
		base.UseVisualStyleBackColor = false;
		base.FlatAppearance.BorderSize = 0;
		base.TabStop = false;
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
		e.Graphics.Clear(UiShape.SurfaceBackColor(this, Color.FromArgb(17, 20, 31)));
		Rectangle bounds = new Rectangle(0, 0, Math.Max(1, base.Width - 1), Math.Max(1, base.Height - 1));
		using (GraphicsPath path = UiShape.RoundedRect(bounds, 8))
		{
			using SolidBrush brush = new SolidBrush(Color.FromArgb(18, 21, 32));
			e.Graphics.FillPath(brush, path);
		}
		int num = Math.Min(28, Math.Max(22, base.Width - 30));
		Rectangle bounds2 = new Rectangle((base.Width - num) / 2, 4, num, num);
		using (GraphicsPath path2 = UiShape.RoundedRect(bounds2, 5))
		{
			using SolidBrush brush2 = new SolidBrush(SwatchColor);
			e.Graphics.FillPath(brush2, path2);
			if (IsVeryLight(SwatchColor) || IsVeryDark(SwatchColor))
			{
				using Pen pen = new Pen(Color.FromArgb(42, 132, 143, 165), 1f);
				e.Graphics.DrawPath(pen, path2);
			}
		}
		TextRenderer.DrawText(e.Graphics, LabelText, Font, new Rectangle(0, bounds2.Bottom, base.Width, 14), Color.FromArgb(245, 247, 251), TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
		using Font font = new Font(Font.FontFamily, 6.6f);
		TextRenderer.DrawText(e.Graphics, HexText, font, new Rectangle(0, bounds2.Bottom + 13, base.Width, 12), Color.FromArgb(156, 164, 181), TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
	}

	private static bool IsVeryLight(Color color)
	{
		return (color.R * 299 + color.G * 587 + color.B * 114) / 1000 > 232;
	}

	private static bool IsVeryDark(Color color)
	{
		return (color.R * 299 + color.G * 587 + color.B * 114) / 1000 < 28;
	}
}
