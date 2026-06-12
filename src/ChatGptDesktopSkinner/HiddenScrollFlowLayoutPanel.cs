using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ChatGptDesktopSkinner;

internal sealed class HiddenScrollFlowLayoutPanel : FlowLayoutPanel
{
	private const int SbBoth = 3;

	[DllImport("user32.dll")]
	private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

	public HiddenScrollFlowLayoutPanel()
	{
		AutoScroll = true;
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		HideBars();
	}

	protected override void OnResize(EventArgs eventargs)
	{
		base.OnResize(eventargs);
		HideBars();
	}

	protected override void OnScroll(ScrollEventArgs se)
	{
		base.OnScroll(se);
		HideBars();
	}

	protected override void OnMouseWheel(MouseEventArgs e)
	{
		base.OnMouseWheel(e);
		HideBars();
	}

	protected override void WndProc(ref Message m)
	{
		base.WndProc(ref m);
	}

	private void HideBars()
	{
		if (base.IsHandleCreated)
		{
			ShowScrollBar(base.Handle, 3, bShow: false);
		}
	}
}
