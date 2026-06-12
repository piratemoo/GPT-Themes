using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ChatGptDesktopSkinner;

internal sealed class RoundedPanel : Panel
{
	public int Radius { get; set; }

	public Color BorderColor { get; set; }

	public Color GradientTop { get; set; }

	public Color GradientBottom { get; set; }

	public bool ClipToRadius { get; set; }

	public RoundedPanel()
	{
		Radius = 8;
		BorderColor = Color.Transparent;
		GradientTop = Color.Empty;
		GradientBottom = Color.Empty;
		ClipToRadius = false;
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
	}

	protected override void OnResize(EventArgs eventargs)
	{
		base.OnResize(eventargs);
		if (ClipToRadius)
		{
			UpdateRegion();
		}
		else if (base.Region != null)
		{
			base.Region = null;
		}
	}

	protected override void OnPaintBackground(PaintEventArgs pevent)
	{
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		e.Graphics.Clear(UiShape.SurfaceBackColor(this, Color.FromArgb(17, 20, 31)));
		using (GraphicsPath path = UiShape.RoundedRect(new Rectangle(0, 0, Math.Max(1, base.Width - 1), Math.Max(1, base.Height - 1)), Radius))
		{
			using Brush brush = ((GradientTop == Color.Empty || GradientBottom == Color.Empty) ? ((Brush)new SolidBrush(BackColor)) : ((Brush)new LinearGradientBrush(base.ClientRectangle, GradientTop, GradientBottom, LinearGradientMode.Vertical)));
			e.Graphics.FillPath(brush, path);
			if (BorderColor.A > 0)
			{
				using Pen pen = new Pen(BorderColor, 1f);
				e.Graphics.DrawPath(pen, path);
			}
		}
		base.OnPaint(e);
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
}
