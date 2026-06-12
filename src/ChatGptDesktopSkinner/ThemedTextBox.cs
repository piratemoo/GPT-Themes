using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ChatGptDesktopSkinner;

internal sealed class ThemedTextBox : TextBox
{
	private struct TextRect
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	private const int EmSetRect = 179;

	private bool _normalizing;

	[DllImport("user32.dll")]
	private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref TextRect lParam);

	public ThemedTextBox()
	{
		AutoSize = false;
		base.BorderStyle = BorderStyle.FixedSingle;
		Multiline = true;
		base.WordWrap = false;
		base.ScrollBars = ScrollBars.None;
		ShortcutsEnabled = true;
		base.AcceptsReturn = false;
		base.AcceptsTab = false;
		base.Height = 30;
		MinimumSize = new Size(0, 30);
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		ApplyTextRect();
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		ApplyTextRect();
	}

	protected override void OnEnter(EventArgs e)
	{
		base.OnEnter(e);
		QueueTextRect();
	}

	protected override void OnGotFocus(EventArgs e)
	{
		base.OnGotFocus(e);
		QueueTextRect();
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);
		QueueTextRect();
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		base.OnMouseUp(e);
		QueueTextRect();
	}

	protected override void OnFontChanged(EventArgs e)
	{
		base.OnFontChanged(e);
		ApplyTextRect();
	}

	protected override void OnKeyPress(KeyPressEventArgs e)
	{
		if (e.KeyChar == '\r' || e.KeyChar == '\n')
		{
			e.Handled = true;
		}
		else
		{
			base.OnKeyPress(e);
		}
	}

	protected override void OnTextChanged(EventArgs e)
	{
		if (!_normalizing && (Text.IndexOf('\r') >= 0 || Text.IndexOf('\n') >= 0))
		{
			_normalizing = true;
			int val = base.SelectionStart;
			Text = Text.Replace("\r", "").Replace("\n", "");
			base.SelectionStart = Math.Min(val, TextLength);
			_normalizing = false;
		}
		base.OnTextChanged(e);
		ApplyTextRect();
	}

	protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
	{
		if (base.Enabled && !base.ReadOnly && (keyData == Keys.Back || keyData == Keys.Delete))
		{
			DeleteText(keyData == Keys.Back);
			return true;
		}
		return base.ProcessCmdKey(ref msg, keyData);
	}

	private void DeleteText(bool backwards)
	{
		int num = base.SelectionStart;
		if (SelectionLength > 0)
		{
			SelectedText = "";
		}
		else if (backwards)
		{
			if (num > 0)
			{
				Text = Text.Remove(num - 1, 1);
				base.SelectionStart = num - 1;
			}
		}
		else if (num < TextLength)
		{
			Text = Text.Remove(num, 1);
			base.SelectionStart = num;
		}
	}

	private void ApplyTextRect()
	{
		if (base.IsHandleCreated && base.ClientSize.Width > 0 && base.ClientSize.Height > 0)
		{
			int num = TextRenderer.MeasureText("Ag", Font).Height;
			int num2 = Math.Max(0, (base.ClientSize.Height - num) / 2);
			TextRect lParam = new TextRect
			{
				Left = 8,
				Top = num2,
				Right = Math.Max(9, base.ClientSize.Width - 8),
				Bottom = Math.Min(base.ClientSize.Height, num2 + num + 5)
			};
			SendMessage(base.Handle, 179, IntPtr.Zero, ref lParam);
			Invalidate();
		}
	}

	private void QueueTextRect()
	{
		ApplyTextRect();
		if (!base.IsHandleCreated || base.IsDisposed)
		{
			return;
		}
		try
		{
			BeginInvoke(new MethodInvoker(ApplyTextRect));
		}
		catch
		{
		}
	}

	public void RefreshTextLayout()
	{
		ApplyTextRect();
	}
}
