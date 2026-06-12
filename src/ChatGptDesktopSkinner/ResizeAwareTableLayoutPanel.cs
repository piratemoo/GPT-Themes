using System;
using System.Drawing;
using System.Windows.Forms;

namespace ChatGptDesktopSkinner;

internal sealed class ResizeAwareTableLayoutPanel : TableLayoutPanel
{
	public Func<Point, IntPtr> ResizeHitTest { get; set; }

	public ResizeAwareTableLayoutPanel()
	{
		SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
	}

	protected override void WndProc(ref Message m)
	{
		if (m.Msg == 132 && ResizeHitTest != null)
		{
			IntPtr intPtr = ResizeHitTest(ScreenPointFromLParam(m.LParam));
			if (intPtr != IntPtr.Zero)
			{
				m.Result = intPtr;
				return;
			}
		}
		base.WndProc(ref m);
	}

	private static Point ScreenPointFromLParam(IntPtr lParam)
	{
		long num = lParam.ToInt64();
		int num2 = (short)(num & 0xFFFF);
		int num3 = (short)((num >> 16) & 0xFFFF);
		return new Point(num2, num3);
	}
}
