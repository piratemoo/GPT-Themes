using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ChatGptDesktopSkinner;

internal sealed class ThemedComboBox : ComboBox
{
	private struct ComboBoxInfo
	{
		public int cbSize;

		public Rectangle rcItem;

		public Rectangle rcButton;

		public int stateButton;

		public IntPtr hwndCombo;

		public IntPtr hwndItem;

		public IntPtr hwndList;
	}

	private const int CbGetComboBoxInfo = 356;

	private const int GclStyle = -26;

	private const int CsDropShadow = 131072;

	private const uint SwpNoSize = 1u;

	private const uint SwpNoMove = 2u;

	private const uint SwpNoZOrder = 4u;

	private const uint SwpNoActivate = 16u;

	private const uint SwpFrameChanged = 32u;

	public Color ThemeBackColor { get; set; }

	public Color ThemeForeColor { get; set; }

	public Color ThemeAccentColor { get; set; }

	[DllImport("user32.dll", EntryPoint = "SendMessage")]
	private static extern IntPtr SendComboMessage(IntPtr hWnd, int msg, IntPtr wParam, ref ComboBoxInfo lParam);

	[DllImport("user32.dll", EntryPoint = "GetClassLong")]
	private static extern int GetClassLong32(IntPtr hWnd, int nIndex);

	[DllImport("user32.dll", EntryPoint = "GetClassLongPtr")]
	private static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);

	[DllImport("user32.dll", EntryPoint = "SetClassLong")]
	private static extern int SetClassLong32(IntPtr hWnd, int nIndex, int dwNewLong);

	[DllImport("user32.dll", EntryPoint = "SetClassLongPtr")]
	private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

	public ThemedComboBox()
	{
		ThemeBackColor = Color.FromArgb(14, 17, 27);
		ThemeForeColor = Color.FromArgb(245, 247, 251);
		ThemeAccentColor = Color.FromArgb(60, 68, 86);
	}

	protected override void WndProc(ref Message m)
	{
		base.WndProc(ref m);
		if (base.DropDownStyle == ComboBoxStyle.DropDownList && (m.Msg == 15 || m.Msg == 133 || m.Msg == 20))
		{
			PaintShell();
		}
	}

	protected override void OnSelectedIndexChanged(EventArgs e)
	{
		base.OnSelectedIndexChanged(e);
		Invalidate();
	}

	protected override void OnTextChanged(EventArgs e)
	{
		base.OnTextChanged(e);
		Invalidate();
	}

	protected override void OnDropDown(EventArgs e)
	{
		base.OnDropDown(e);
		RemoveDropDownShadow();
	}

	protected override void OnEnabledChanged(EventArgs e)
	{
		base.OnEnabledChanged(e);
		Invalidate();
	}

	private void PaintShell()
	{
		if (!base.IsHandleCreated || base.Width <= 0 || base.Height <= 0)
		{
			return;
		}
		using Graphics graphics = Graphics.FromHwnd(base.Handle);
		using SolidBrush brush = new SolidBrush(base.Enabled ? ThemeBackColor : Color.FromArgb(21, 24, 33));
		using SolidBrush brush2 = new SolidBrush(base.Enabled ? ThemeBackColor : Color.FromArgb(21, 24, 33));
		using SolidBrush brush3 = new SolidBrush(base.Enabled ? ThemeForeColor : Color.FromArgb(92, 98, 112));
		Rectangle rect = new Rectangle(0, 0, base.Width, base.Height);
		graphics.FillRectangle(brush, rect);
		Rectangle rect2 = new Rectangle(Math.Max(0, base.Width - 32), 0, 32, base.Height);
		graphics.FillRectangle(brush2, rect2);
		TextRenderer.DrawText(bounds: new Rectangle(9, 0, Math.Max(1, base.Width - 50), base.Height), dc: graphics, text: Text, font: Font, foreColor: base.Enabled ? ThemeForeColor : Color.FromArgb(92, 98, 112), flags: TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
		int num = rect2.Left + rect2.Width / 2;
		int num2 = rect2.Top + rect2.Height / 2 + 1;
		Point[] points = new Point[3]
		{
			new Point(num - 4, num2 - 2),
			new Point(num + 4, num2 - 2),
			new Point(num, num2 + 3)
		};
		graphics.FillPolygon(brush3, points);
	}

	private void RemoveDropDownShadow()
	{
		if (!base.IsHandleCreated)
		{
			return;
		}
		try
		{
			ComboBoxInfo lParam = new ComboBoxInfo
			{
				cbSize = Marshal.SizeOf(typeof(ComboBoxInfo))
			};
			if (!(SendComboMessage(base.Handle, 356, IntPtr.Zero, ref lParam) == IntPtr.Zero) && !(lParam.hwndList == IntPtr.Zero))
			{
				long num = GetClassStyle(lParam.hwndList).ToInt64();
				if ((num & 0x20000) != 0)
				{
					SetClassStyle(lParam.hwndList, new IntPtr(num & -131073));
					SetWindowPos(lParam.hwndList, IntPtr.Zero, 0, 0, 0, 0, 55u);
				}
			}
		}
		catch
		{
		}
	}

	private static IntPtr GetClassStyle(IntPtr hWnd)
	{
		return (IntPtr.Size == 8) ? GetClassLongPtr64(hWnd, -26) : new IntPtr(GetClassLong32(hWnd, -26));
	}

	private static void SetClassStyle(IntPtr hWnd, IntPtr value)
	{
		if (IntPtr.Size == 8)
		{
			SetClassLongPtr64(hWnd, -26, value);
		}
		else
		{
			SetClassLong32(hWnd, -26, value.ToInt32());
		}
	}
}
