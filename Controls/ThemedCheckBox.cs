using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ChatGptDesktopSkinner;

internal sealed class ThemedCheckBox : CheckBox
{
	public Color BoxBackColor { get; set; }

	public Color BoxBorderColor { get; set; }

	public Color CheckedBackColor { get; set; }

	public Color CheckMarkColor { get; set; }

	public Color TextColor { get; set; }

	public ThemedCheckBox()
	{
		BoxBackColor = Color.FromArgb(18, 21, 31);
		BoxBorderColor = Color.FromArgb(78, 86, 108);
		CheckedBackColor = Color.FromArgb(255, 126, 188);
		CheckMarkColor = Color.White;
		TextColor = Color.FromArgb(245, 247, 251);
		Cursor = Cursors.Hand;
		base.TabStop = false;
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
	}

	protected override void OnPaintBackground(PaintEventArgs pevent)
	{
	}

	protected override void OnCheckedChanged(EventArgs e)
	{
		base.OnCheckedChanged(e);
		Invalidate();
	}

	protected override void OnEnabledChanged(EventArgs e)
	{
		base.OnEnabledChanged(e);
		Invalidate();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		Color color = ((BackColor.A > 0 && BackColor != Color.Transparent) ? BackColor : UiShape.SurfaceBackColor(this, Color.FromArgb(17, 20, 31)));
		e.Graphics.Clear(color);
		int num = 16;
		Rectangle bounds = new Rectangle(0, Math.Max(0, (base.Height - num) / 2), num, num);
		Color color2 = (base.Checked ? CheckedBackColor : BoxBackColor);
		Color color3 = (base.Checked ? CheckedBackColor : BoxBorderColor);
		Color foreColor = (base.Enabled ? TextColor : Color.FromArgb(110, 116, 130));
		using (GraphicsPath path = UiShape.RoundedRect(bounds, 4))
		{
			using SolidBrush brush = new SolidBrush(base.Enabled ? color2 : Color.FromArgb(28, 31, 42));
			e.Graphics.FillPath(brush, path);
			if (color3.A > 0)
			{
				using Pen pen = new Pen(base.Enabled ? color3 : Color.FromArgb(52, 58, 72), 1.2f);
				e.Graphics.DrawPath(pen, path);
			}
		}
		if (base.Checked)
		{
			using Pen pen2 = new Pen(CheckMarkColor, 1.8f);
			pen2.StartCap = LineCap.Round;
			pen2.EndCap = LineCap.Round;
			e.Graphics.DrawLine(pen2, bounds.Left + 4, bounds.Top + 8, bounds.Left + 7, bounds.Top + 11);
			e.Graphics.DrawLine(pen2, bounds.Left + 7, bounds.Top + 11, bounds.Right - 4, bounds.Top + 5);
		}
		TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(bounds.Right + 8, 0, Math.Max(1, base.Width - bounds.Right - 8), base.Height), foreColor, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
	}
}
