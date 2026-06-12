using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ChatGptDesktopSkinner;

internal sealed class ThemedSlider : Control
{
	private int _minimum;

	private int _maximum = 100;

	private int _value;

	private bool _dragging;

	public int Minimum
	{
		get
		{
			return _minimum;
		}
		set
		{
			_minimum = value;
			if (_maximum < _minimum)
			{
				_maximum = _minimum;
			}
			Value = _value;
			Invalidate();
		}
	}

	public int Maximum
	{
		get
		{
			return _maximum;
		}
		set
		{
			_maximum = Math.Max(_minimum, value);
			Value = _value;
			Invalidate();
		}
	}

	public int Value
	{
		get
		{
			return _value;
		}
		set
		{
			int num = Math.Max(_minimum, Math.Min(_maximum, value));
			if (_value != num)
			{
				_value = num;
				Invalidate();
				this.ValueChanged?.Invoke(this, EventArgs.Empty);
			}
		}
	}

	public Color ActiveColor { get; set; }

	public Color TrackColor { get; set; }

	public Color ThumbColor { get; set; }

	public event EventHandler ValueChanged;

	public ThemedSlider()
	{
		base.Height = 28;
		ActiveColor = Color.FromArgb(255, 126, 188);
		TrackColor = Color.FromArgb(72, 78, 96);
		ThumbColor = Color.FromArgb(255, 126, 188);
		Cursor = Cursors.Hand;
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
	}

	protected override void OnPaintBackground(PaintEventArgs pevent)
	{
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		Color color = ((BackColor.A > 0 && BackColor != Color.Transparent) ? BackColor : UiShape.SurfaceBackColor(this, Color.FromArgb(17, 20, 31)));
		e.Graphics.Clear(color);
		int num = 14;
		int num2 = num / 2;
		int num3 = Math.Max(num2 + 1, base.Width - num / 2 - 1);
		int num4 = base.Height / 2;
		float num5 = ((_maximum == _minimum) ? 0f : ((float)(_value - _minimum) / (float)(_maximum - _minimum)));
		int num6 = num2 + (int)Math.Round((float)(num3 - num2) * num5);
		using (Pen pen = new Pen(TrackColor, 4f))
		{
			using Pen pen2 = new Pen(ActiveColor, 4f);
			pen.StartCap = LineCap.Round;
			pen.EndCap = LineCap.Round;
			pen2.StartCap = LineCap.Round;
			pen2.EndCap = LineCap.Round;
			e.Graphics.DrawLine(pen, num2, num4, num3, num4);
			e.Graphics.DrawLine(pen2, num2, num4, num6, num4);
		}
		using SolidBrush brush = new SolidBrush(ThumbColor);
		e.Graphics.FillEllipse(brush, num6 - num / 2, num4 - num / 2, num, num);
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);
		if (e.Button == MouseButtons.Left)
		{
			_dragging = true;
			base.Capture = true;
			SetValueFromPoint(e.X);
		}
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);
		if (_dragging)
		{
			SetValueFromPoint(e.X);
		}
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		base.OnMouseUp(e);
		_dragging = false;
		base.Capture = false;
	}

	private void SetValueFromPoint(int x)
	{
		int num = 14;
		int num2 = num / 2;
		int num3 = Math.Max(num2 + 1, base.Width - num / 2 - 1);
		double num4 = (double)(Math.Max(num2, Math.Min(num3, x)) - num2) / (double)(num3 - num2);
		Value = _minimum + (int)Math.Round((double)(_maximum - _minimum) * num4);
	}
}
