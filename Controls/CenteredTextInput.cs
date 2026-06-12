using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ChatGptDesktopSkinner;

internal sealed class CenteredTextInput : Control
{
	private int _caretIndex;

	private int _selectionStart;

	private int _selectionLength;

	private string _textValue = "";

	public int MaxLength { get; set; }

	public Func<char, bool> CharacterFilter { get; set; }

	public bool SelectAllOnFocus { get; set; }

	public Color BorderColor { get; set; }

	public int Radius { get; set; }

	public HorizontalAlignment TextAlignment { get; set; }

	public override string Text
	{
		get
		{
			return _textValue;
		}
		set
		{
			SetTextValue(value ?? "", (value ?? "").Length);
		}
	}

	private bool HasSelection => _selectionLength > 0 && _selectionStart >= 0 && _selectionStart < _textValue.Length;

	public CenteredTextInput()
	{
		MaxLength = 5;
		CharacterFilter = (char c) => true;
		SelectAllOnFocus = false;
		TextAlignment = HorizontalAlignment.Center;
		base.Height = 30;
		MinimumSize = new Size(0, 30);
		base.TabStop = true;
		Cursor = Cursors.IBeam;
		BackColor = Color.FromArgb(14, 17, 27);
		ForeColor = Color.FromArgb(245, 247, 251);
		BorderColor = Color.Transparent;
		Radius = 0;
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
	}

	protected override bool IsInputKey(Keys keyData)
	{
		Keys keys = keyData & Keys.KeyCode;
		if (keys == Keys.Left || keys == Keys.Right || keys == Keys.Home || keys == Keys.End || keys == Keys.Back || keys == Keys.Delete)
		{
			return true;
		}
		return base.IsInputKey(keyData);
	}

	protected override void OnTextChanged(EventArgs e)
	{
		base.OnTextChanged(e);
		_caretIndex = Math.Max(0, Math.Min(_caretIndex, _textValue.Length));
		Invalidate();
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);
		bool focused = Focused;
		Focus();
		if ((SelectAllOnFocus && !focused) || e.Clicks > 1)
		{
			SelectAllText();
			return;
		}
		_caretIndex = CaretIndexFromX(e.X);
		ClearSelection();
		Invalidate();
	}

	protected override void OnGotFocus(EventArgs e)
	{
		base.OnGotFocus(e);
		if (SelectAllOnFocus)
		{
			SelectAllText();
			return;
		}
		_caretIndex = Text.Length;
		ClearSelection();
		Invalidate();
	}

	protected override void OnLostFocus(EventArgs e)
	{
		base.OnLostFocus(e);
		Invalidate();
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		if (e.Control && e.KeyCode == Keys.A)
		{
			SelectAllText();
			e.SuppressKeyPress = true;
		}
		else if (e.Control && e.KeyCode == Keys.C)
		{
			CopySelection();
			e.SuppressKeyPress = true;
		}
		else if (e.Control && e.KeyCode == Keys.X)
		{
			CutSelection();
			e.SuppressKeyPress = true;
		}
		else if (e.Control && e.KeyCode == Keys.V)
		{
			PasteAllowedText();
			e.SuppressKeyPress = true;
		}
		else if (e.KeyCode == Keys.Back)
		{
			if (!DeleteSelection() && _caretIndex > 0 && Text.Length > 0)
			{
				int num = _caretIndex - 1;
				SetTextValue(Text.Remove(num, 1), num);
			}
			e.SuppressKeyPress = true;
		}
		else if (e.KeyCode == Keys.Delete)
		{
			if (!DeleteSelection() && _caretIndex < _textValue.Length)
			{
				SetTextValue(_textValue.Remove(_caretIndex, 1), _caretIndex);
			}
			e.SuppressKeyPress = true;
		}
		else if (e.KeyCode == Keys.Left)
		{
			_caretIndex = Math.Max(0, _caretIndex - 1);
			ClearSelection();
			Invalidate();
			e.SuppressKeyPress = true;
		}
		else if (e.KeyCode == Keys.Right)
		{
			_caretIndex = Math.Min(_textValue.Length, _caretIndex + 1);
			ClearSelection();
			Invalidate();
			e.SuppressKeyPress = true;
		}
		else if (e.KeyCode == Keys.Home)
		{
			_caretIndex = 0;
			ClearSelection();
			Invalidate();
			e.SuppressKeyPress = true;
		}
		else if (e.KeyCode == Keys.End)
		{
			_caretIndex = _textValue.Length;
			ClearSelection();
			Invalidate();
			e.SuppressKeyPress = true;
		}
		else
		{
			base.OnKeyDown(e);
		}
	}

	protected override void OnKeyPress(KeyPressEventArgs e)
	{
		if (!char.IsControl(e.KeyChar) && CharacterFilter != null && CharacterFilter(e.KeyChar))
		{
			InsertText(e.KeyChar.ToString());
			e.Handled = true;
		}
		else if (!char.IsControl(e.KeyChar))
		{
			e.Handled = true;
		}
		else
		{
			base.OnKeyPress(e);
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		e.Graphics.Clear(UiShape.SurfaceBackColor(this, BackColor));
		Rectangle rectangle = new Rectangle(0, 0, Math.Max(1, base.Width - 1), Math.Max(1, base.Height - 1));
		if (Radius > 0)
		{
			using GraphicsPath path = UiShape.RoundedRect(rectangle, Radius);
			using SolidBrush brush = new SolidBrush(BackColor);
			e.Graphics.FillPath(brush, path);
			if (BorderColor.A > 0)
			{
				using Pen pen = new Pen(BorderColor, 1f);
				e.Graphics.DrawPath(pen, path);
			}
		}
		else
		{
			using SolidBrush brush = new SolidBrush(BackColor);
			e.Graphics.FillRectangle(brush, rectangle);
		}
		Rectangle rectangle2 = new Rectangle(8, 0, Math.Max(1, base.Width - 16), base.Height);
		DrawSelection(e.Graphics, rectangle2);
		TextFormatFlags textFormatFlags = ((TextAlignment != HorizontalAlignment.Left) ? ((TextAlignment != HorizontalAlignment.Right) ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Right) : TextFormatFlags.Default);
		TextRenderer.DrawText(e.Graphics, _textValue, Font, rectangle2, ForeColor, textFormatFlags | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
		if (Focused && !HasSelection)
		{
			DrawCaret(e.Graphics, rectangle2);
		}
	}

	private void PasteAllowedText()
	{
		try
		{
			if (Clipboard.ContainsText())
			{
				string value = new string((from c in Clipboard.GetText()
					where CharacterFilter == null || CharacterFilter(c)
					select c).ToArray());
				InsertText(value);
			}
		}
		catch
		{
		}
	}

	private void InsertText(string value)
	{
		if (!string.IsNullOrEmpty(value))
		{
			int num = (HasSelection ? _selectionLength : 0);
			int num2 = (HasSelection ? _selectionStart : _caretIndex);
			string text = ((num > 0) ? _textValue.Remove(num2, num) : _textValue);
			int num3 = Math.Max(0, MaxLength - text.Length);
			if (num3 != 0)
			{
				string text2 = ((value.Length > num3) ? value.Substring(0, num3) : value);
				SetTextValue(text.Insert(num2, text2), num2 + text2.Length);
			}
		}
	}

	private void SetTextValue(string next, int caretIndex)
	{
		next = next ?? "";
		if (MaxLength > 0 && next.Length > MaxLength)
		{
			next = next.Substring(0, MaxLength);
		}
		bool flag = !string.Equals(_textValue, next, StringComparison.Ordinal);
		_textValue = next;
		_caretIndex = Math.Max(0, Math.Min(caretIndex, _textValue.Length));
		ClearSelection();
		if (flag)
		{
			base.OnTextChanged(EventArgs.Empty);
		}
		Invalidate();
	}

	private void ClearSelection()
	{
		_selectionStart = _caretIndex;
		_selectionLength = 0;
	}

	private void SelectAllText()
	{
		_selectionStart = 0;
		_selectionLength = _textValue.Length;
		_caretIndex = _textValue.Length;
		Invalidate();
	}

	private bool DeleteSelection()
	{
		if (!HasSelection)
		{
			return false;
		}
		int selectionStart = _selectionStart;
		SetTextValue(_textValue.Remove(_selectionStart, _selectionLength), selectionStart);
		return true;
	}

	private void CopySelection()
	{
		if (!HasSelection)
		{
			return;
		}
		try
		{
			Clipboard.SetText(_textValue.Substring(_selectionStart, _selectionLength));
		}
		catch
		{
		}
	}

	private void CutSelection()
	{
		if (HasSelection)
		{
			CopySelection();
			DeleteSelection();
		}
	}

	private void DrawSelection(Graphics graphics, Rectangle textRect)
	{
		if (!Focused || !HasSelection)
		{
			return;
		}
		int num = Math.Max(0, Math.Min(_selectionStart, _textValue.Length));
		int num2 = Math.Max(0, Math.Min(_selectionLength, _textValue.Length - num));
		if (num2 == 0)
		{
			return;
		}
		Size size = MeasureText(graphics, _textValue);
		Size size2 = MeasureText(graphics, (num == 0) ? "" : _textValue.Substring(0, num));
		Size size3 = MeasureText(graphics, _textValue.Substring(num, num2));
		int num3 = TextLeft(textRect, size.Width);
		int num4 = Math.Min(20, Math.Max(14, base.Height - 8));
		int num5 = (base.Height - num4) / 2;
		Rectangle rect = new Rectangle(Math.Max(textRect.Left, num3 + size2.Width), num5, Math.Max(4, size3.Width + 3), num4);
		using SolidBrush brush = new SolidBrush(Color.FromArgb(92, 10, 132, 255));
		graphics.FillRectangle(brush, rect);
	}

	private void DrawCaret(Graphics graphics, Rectangle textRect)
	{
		string value = ((_caretIndex <= 0) ? "" : _textValue.Substring(0, Math.Min(_caretIndex, _textValue.Length)));
		Size size = MeasureText(graphics, _textValue);
		Size size2 = MeasureText(graphics, value);
		int num = TextLeft(textRect, size.Width);
		int num2 = Math.Max(textRect.Left + 2, Math.Min(textRect.Right - 2, num + size2.Width + 1));
		int num3 = Math.Min(18, Math.Max(12, base.Height - 10));
		int num4 = (base.Height - num3) / 2;
		using Pen pen = new Pen(ForeColor, 1f);
		graphics.DrawLine(pen, num2, num4, num2, num4 + num3);
	}

	private Size MeasureText(Graphics graphics, string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return Size.Empty;
		}
		return TextRenderer.MeasureText(graphics, value, Font, new Size(int.MaxValue, base.Height), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
	}

	private int TextLeft(Rectangle textRect, int textWidth)
	{
		if (TextAlignment == HorizontalAlignment.Left)
		{
			return textRect.Left;
		}
		if (TextAlignment == HorizontalAlignment.Right)
		{
			return textRect.Right - textWidth;
		}
		return textRect.Left + (textRect.Width - textWidth) / 2;
	}

	private int CaretIndexFromX(int x)
	{
		if (string.IsNullOrEmpty(_textValue))
		{
			return 0;
		}
		using (Graphics graphics = CreateGraphics())
		{
			Rectangle textRect = new Rectangle(8, 0, Math.Max(1, base.Width - 16), base.Height);
			int num = TextLeft(textRect, MeasureText(graphics, _textValue).Width);
			for (int i = 0; i <= _textValue.Length; i++)
			{
				string value = ((i <= 0) ? "" : _textValue.Substring(0, i));
				int num2 = MeasureText(graphics, value).Width;
				if (x <= num + num2 + 3)
				{
					return i;
				}
			}
		}
		return _textValue.Length;
	}
}
