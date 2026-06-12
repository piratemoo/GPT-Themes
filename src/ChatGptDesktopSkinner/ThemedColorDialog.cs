using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ChatGptDesktopSkinner;

internal sealed class ThemedColorDialog : Form
{
	private const int WmNclButtonDown = 161;

	private const int HtCaption = 2;

	private readonly ThemedSlider _red;

	private readonly ThemedSlider _green;

	private readonly ThemedSlider _blue;

	private readonly CenteredTextInput _redBox;

	private readonly CenteredTextInput _greenBox;

	private readonly CenteredTextInput _blueBox;

	private readonly CenteredTextInput _hexBox;

	private readonly Panel _preview;

	private bool _updating;

	private bool _normalizingHexText;

	private static readonly Color DialogBg = Color.FromArgb(18, 20, 29);

	private static readonly Color DialogControl = Color.FromArgb(31, 35, 49);

	private static readonly Color DialogText = Color.FromArgb(245, 247, 251);

	private static readonly Color DialogMuted = Color.FromArgb(156, 164, 181);

	private static readonly Color DialogAccent = Color.FromArgb(255, 126, 188);

	private static readonly Color DialogBorder = Color.FromArgb(47, 54, 72);

	public Color SelectedColor { get; private set; }

	[DllImport("user32.dll")]
	private static extern bool ReleaseCapture();

	[DllImport("user32.dll")]
	private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

	public ThemedColorDialog(string title, Color initial, IEnumerable<Color> palette)
	{
		Text = title;
		SelectedColor = initial;
		base.StartPosition = FormStartPosition.CenterParent;
		base.FormBorderStyle = FormBorderStyle.None;
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		base.ShowInTaskbar = false;
		base.KeyPreview = true;
		MinimumSize = new Size(420, 326);
		base.ClientSize = new Size(430, 326);
		base.Padding = new Padding(0);
		BackColor = DialogBg;
		ForeColor = DialogText;
		Font = new Font("Segoe UI", 9.25f);
		SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(16, 12, 16, 14),
			BackColor = DialogBg,
			ColumnCount = 1,
			RowCount = 5,
			RowStyles = 
			{
				new RowStyle(SizeType.Absolute, 30f),
				new RowStyle(SizeType.Absolute, 64f),
				new RowStyle(SizeType.Absolute, 102f),
				new RowStyle(SizeType.Absolute, 38f),
				new RowStyle(SizeType.Absolute, 50f)
			}
		};
		base.Controls.Add(tableLayoutPanel);
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel();
		tableLayoutPanel2.Dock = DockStyle.Fill;
		tableLayoutPanel2.BackColor = Color.Transparent;
		tableLayoutPanel2.ColumnCount = 2;
		tableLayoutPanel2.RowCount = 1;
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34f));
		tableLayoutPanel2.MouseDown += DragDialog;
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 0);
		Label label = new Label();
		label.Text = (string.Equals(title, "Base color", StringComparison.OrdinalIgnoreCase) ? "BASE COLOR" : title);
		label.Dock = DockStyle.Fill;
		label.ForeColor = DialogText;
		label.Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold);
		label.TextAlign = ContentAlignment.MiddleLeft;
		label.MouseDown += DragDialog;
		tableLayoutPanel2.Controls.Add(label, 0, 0);
		Button button = DialogButton("", Color.FromArgb(26, 30, 42), DialogText);
		button.Dock = DockStyle.None;
		button.Anchor = AnchorStyles.None;
		button.Size = new Size(24, 24);
		button.Margin = new Padding(4, 0, 0, 0);
		if (button is RoundedButton roundedButton)
		{
			roundedButton.IconKind = ButtonIconKind.WindowClose;
			roundedButton.Radius = 12;
		}
		button.Click += delegate
		{
			base.DialogResult = DialogResult.Cancel;
			Close();
		};
		tableLayoutPanel2.Controls.Add(button, 1, 0);
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			BackColor = Color.Transparent,
			WrapContents = true,
			AutoScroll = false,
			Padding = new Padding(0, 5, 0, 0)
		};
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 1);
		foreach (Color item in palette)
		{
			Button swatch = new Button();
			swatch.Width = 24;
			swatch.Height = 24;
			swatch.Margin = new Padding(0, 0, 6, 5);
			swatch.BackColor = item;
			swatch.FlatStyle = FlatStyle.Flat;
			swatch.FlatAppearance.BorderSize = 0;
			swatch.Cursor = Cursors.Hand;
			swatch.Click += delegate
			{
				SetSelectedColor(swatch.BackColor);
			};
			flowLayoutPanel.Controls.Add(swatch);
		}
		TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			BackColor = Color.Transparent,
			ColumnCount = 3,
			RowCount = 3,
			ColumnStyles = 
			{
				new ColumnStyle(SizeType.Absolute, 34f),
				new ColumnStyle(SizeType.Percent, 100f),
				new ColumnStyle(SizeType.Absolute, 58f)
			},
			RowStyles = 
			{
				new RowStyle(SizeType.Absolute, 34f),
				new RowStyle(SizeType.Absolute, 34f),
				new RowStyle(SizeType.Absolute, 34f)
			}
		};
		tableLayoutPanel.Controls.Add(tableLayoutPanel3, 0, 2);
		_red = AddColorSlider(tableLayoutPanel3, 0, "R");
		_green = AddColorSlider(tableLayoutPanel3, 1, "G");
		_blue = AddColorSlider(tableLayoutPanel3, 2, "B");
		_redBox = AddRgbInput(tableLayoutPanel3, 0, "RValue");
		_greenBox = AddRgbInput(tableLayoutPanel3, 1, "GValue");
		_blueBox = AddRgbInput(tableLayoutPanel3, 2, "BValue");
		TableLayoutPanel tableLayoutPanel4 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			BackColor = Color.Transparent,
			ColumnCount = 3,
			RowCount = 1,
			ColumnStyles = 
			{
				new ColumnStyle(SizeType.Absolute, 66f),
				new ColumnStyle(SizeType.Absolute, 162f),
				new ColumnStyle(SizeType.Percent, 100f)
			}
		};
		tableLayoutPanel.Controls.Add(tableLayoutPanel4, 0, 3);
		_preview = new Panel();
		_preview.Dock = DockStyle.Fill;
		_preview.Margin = new Padding(0, 5, 10, 5);
		tableLayoutPanel4.Controls.Add(_preview, 0, 0);
		_hexBox = new CenteredTextInput();
		_hexBox.Dock = DockStyle.Fill;
		_hexBox.Margin = new Padding(0, 5, 10, 5);
		_hexBox.BackColor = Color.FromArgb(24, 28, 40);
		_hexBox.ForeColor = DialogText;
		_hexBox.Font = Font;
		_hexBox.Height = 26;
		_hexBox.MaxLength = 7;
		_hexBox.TextAlignment = HorizontalAlignment.Center;
		_hexBox.SelectAllOnFocus = false;
		_hexBox.CharacterFilter = IsHexInputCharacter;
		_hexBox.TextChanged += delegate
		{
			if (!_updating && !_normalizingHexText)
			{
				string a = CleanHexInput(_hexBox.Text);
				if (!string.Equals(a, _hexBox.Text, StringComparison.Ordinal))
				{
					_normalizingHexText = true;
					_hexBox.Text = a;
					_normalizingHexText = false;
				}
				if (TryParseHex(_hexBox.Text, out var color))
				{
					SetHexInputValid(valid: true);
					SetSelectedColorFromHexInput(color);
				}
				else
				{
					SetHexInputValid(string.IsNullOrWhiteSpace(_hexBox.Text));
				}
			}
		};
		_hexBox.Leave += delegate
		{
			if (TryParseHex(_hexBox.Text, out var color))
			{
				_hexBox.Text = ToHex(color);
				SetHexInputValid(valid: true);
			}
			else
			{
				_hexBox.Text = ToHex(SelectedColor);
				SetHexInputValid(valid: true);
			}
		};
		tableLayoutPanel4.Controls.Add(_hexBox, 1, 0);
		Panel control = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = Color.Transparent
		};
		tableLayoutPanel4.Controls.Add(control, 2, 0);
		TableLayoutPanel tableLayoutPanel5 = new TableLayoutPanel
		{
			Anchor = (AnchorStyles.Bottom | AnchorStyles.Right),
			BackColor = Color.Transparent,
			Margin = new Padding(0, 10, 0, 2),
			Size = new Size(210, 34),
			Width = 210,
			ColumnCount = 2,
			RowCount = 1,
			ColumnStyles = 
			{
				new ColumnStyle(SizeType.Percent, 50f),
				new ColumnStyle(SizeType.Percent, 50f)
			}
		};
		tableLayoutPanel.Controls.Add(tableLayoutPanel5, 0, 4);
		Button button2 = DialogButton("Apply", DialogAccent, Color.White);
		button2.DialogResult = DialogResult.OK;
		button2.Margin = new Padding(0, 0, 8, 0);
		tableLayoutPanel5.Controls.Add(button2, 0, 0);
		Button button3 = DialogButton("Cancel", DialogControl, DialogText);
		button3.DialogResult = DialogResult.Cancel;
		button3.Margin = new Padding(0);
		tableLayoutPanel5.Controls.Add(button3, 1, 0);
		base.AcceptButton = button2;
		base.CancelButton = button3;
		SetSelectedColor(initial);
		UpdateDialogRegion();
	}

	protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
	{
		if (keyData == Keys.Escape)
		{
			base.DialogResult = DialogResult.Cancel;
			Close();
			return true;
		}
		return base.ProcessCmdKey(ref msg, keyData);
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		UpdateDialogRegion();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		using Pen pen = new Pen(DialogBorder, 1f);
		e.Graphics.DrawLine(pen, 0, 0, base.Width, 0);
		e.Graphics.DrawLine(pen, 0, 42, base.Width, 42);
	}

	protected override void OnActivated(EventArgs e)
	{
		base.OnActivated(e);
		Invalidate(invalidateChildren: true);
	}

	protected override void OnDeactivate(EventArgs e)
	{
		base.OnDeactivate(e);
		Invalidate(invalidateChildren: true);
	}

	private void UpdateDialogRegion()
	{
		if (base.Width <= 0 || base.Height <= 0)
		{
			return;
		}
		using GraphicsPath path = UiShape.RoundedRect(new Rectangle(0, 0, base.Width, base.Height), 12);
		base.Region = new Region(path);
	}

	private void DragDialog(object sender, MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left)
		{
			ReleaseCapture();
			SendMessage(base.Handle, 161, 2, 0);
		}
	}

	private ThemedSlider AddColorSlider(TableLayoutPanel grid, int row, string name)
	{
		Label label = new Label();
		label.Text = name;
		label.Dock = DockStyle.Fill;
		label.ForeColor = DialogText;
		label.TextAlign = ContentAlignment.MiddleLeft;
		grid.Controls.Add(label, 0, row);
		ThemedSlider themedSlider = new ThemedSlider();
		themedSlider.Minimum = 0;
		themedSlider.Maximum = 255;
		themedSlider.ActiveColor = DialogAccent;
		themedSlider.TrackColor = Color.FromArgb(72, 78, 96);
		themedSlider.ThumbColor = DialogAccent;
		themedSlider.BackColor = DialogBg;
		themedSlider.Dock = DockStyle.Fill;
		themedSlider.Margin = new Padding(0, 7, 10, 7);
		themedSlider.ValueChanged += delegate
		{
			if (!_updating)
			{
				SetSelectedColor(Color.FromArgb(_red.Value, _green.Value, _blue.Value));
			}
		};
		grid.Controls.Add(themedSlider, 1, row);
		return themedSlider;
	}

	private CenteredTextInput AddRgbInput(TableLayoutPanel grid, int row, string name)
	{
		CenteredTextInput input = new CenteredTextInput();
		input.Name = name;
		input.Dock = DockStyle.Fill;
		input.Margin = new Padding(4, 5, 0, 5);
		input.BackColor = Color.FromArgb(24, 28, 40);
		input.ForeColor = DialogText;
		input.Font = Font;
		input.MaxLength = 3;
		input.TextAlignment = HorizontalAlignment.Center;
		input.CharacterFilter = char.IsDigit;
		input.TextChanged += delegate
		{
			if (!_updating)
			{
				if (!TryParseRgbInput(input.Text, out var parsed))
				{
					SetRgbInputValid(input, string.IsNullOrWhiteSpace(input.Text));
				}
				else
				{
					if (parsed > 255)
					{
						_updating = true;
						input.Text = "255";
						_updating = false;
						parsed = 255;
					}
					SetRgbInputValid(input, valid: true);
					SetSelectedColorFromRgbInput(name, parsed);
				}
			}
		};
		input.LostFocus += delegate
		{
			if (!TryParseRgbInput(input.Text, out var parsed))
			{
				parsed = ((name == "RValue") ? SelectedColor.R : ((name == "GValue") ? SelectedColor.G : SelectedColor.B));
			}
			parsed = Math.Max(0, Math.Min(255, parsed));
			input.Text = parsed.ToString();
			SetRgbInputValid(input, valid: true);
		};
		grid.Controls.Add(input, 2, row);
		return input;
	}

	private Button DialogButton(string text, Color back, Color fore)
	{
		RoundedButton roundedButton = new RoundedButton();
		roundedButton.Text = text;
		roundedButton.Dock = DockStyle.Fill;
		roundedButton.FlatStyle = FlatStyle.Flat;
		roundedButton.FlatAppearance.BorderSize = 0;
		roundedButton.BackColor = back;
		roundedButton.ForeColor = fore;
		roundedButton.Cursor = Cursors.Hand;
		RoundedButton roundedButton2 = roundedButton;
		if (roundedButton2 != null)
		{
			roundedButton2.Radius = 10;
			roundedButton2.IconKind = ((text == "Apply") ? ButtonIconKind.Check : ButtonIconKind.None);
		}
		return roundedButton;
	}

	private void SetSelectedColor(Color color)
	{
		SelectedColor = Color.FromArgb(color.R, color.G, color.B);
		_updating = true;
		_red.Value = SelectedColor.R;
		_green.Value = SelectedColor.G;
		_blue.Value = SelectedColor.B;
		_preview.BackColor = SelectedColor;
		_hexBox.Text = ToHex(SelectedColor);
		UpdateRgbInputs();
		_updating = false;
	}

	private void SetSelectedColorFromHexInput(Color color)
	{
		SelectedColor = Color.FromArgb(color.R, color.G, color.B);
		_updating = true;
		_red.Value = SelectedColor.R;
		_green.Value = SelectedColor.G;
		_blue.Value = SelectedColor.B;
		_preview.BackColor = SelectedColor;
		UpdateRgbInputs();
		_updating = false;
	}

	private void SetSelectedColorFromRgbInput(string inputName, int value)
	{
		int red = ((inputName == "RValue") ? value : SelectedColor.R);
		int green = ((inputName == "GValue") ? value : SelectedColor.G);
		int blue = ((inputName == "BValue") ? value : SelectedColor.B);
		SelectedColor = Color.FromArgb(red, green, blue);
		_updating = true;
		_red.Value = SelectedColor.R;
		_green.Value = SelectedColor.G;
		_blue.Value = SelectedColor.B;
		_preview.BackColor = SelectedColor;
		_hexBox.Text = ToHex(SelectedColor);
		UpdateRgbInputsExcept(inputName);
		_updating = false;
	}

	private void SetHexInputValid(bool valid)
	{
		if (_hexBox != null)
		{
			_hexBox.ForeColor = (valid ? DialogText : Color.FromArgb(255, 146, 146));
		}
	}

	private void SetRgbInputValid(CenteredTextInput input, bool valid)
	{
		if (input != null)
		{
			input.ForeColor = (valid ? DialogText : Color.FromArgb(255, 146, 146));
		}
	}

	private void UpdateRgbInputs()
	{
		if (_redBox != null)
		{
			_redBox.Text = SelectedColor.R.ToString();
		}
		if (_greenBox != null)
		{
			_greenBox.Text = SelectedColor.G.ToString();
		}
		if (_blueBox != null)
		{
			_blueBox.Text = SelectedColor.B.ToString();
		}
	}

	private void UpdateRgbInputsExcept(string inputName)
	{
		if (_redBox != null && inputName != "RValue")
		{
			_redBox.Text = SelectedColor.R.ToString();
		}
		if (_greenBox != null && inputName != "GValue")
		{
			_greenBox.Text = SelectedColor.G.ToString();
		}
		if (_blueBox != null && inputName != "BValue")
		{
			_blueBox.Text = SelectedColor.B.ToString();
		}
	}

	private static bool TryParseRgbInput(string value, out int parsed)
	{
		parsed = 0;
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		return int.TryParse(value.Trim(), out parsed);
	}

	private static bool TryParseHex(string value, out Color color)
	{
		color = Color.Empty;
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		value = value.Trim();
		if (!value.StartsWith("#"))
		{
			value = "#" + value;
		}
		if (value.Length != 7)
		{
			return false;
		}
		try
		{
			color = ColorTranslator.FromHtml(value);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static string CleanHexInput(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return "";
		}
		StringBuilder stringBuilder = new StringBuilder();
		string text = value.Trim();
		foreach (char c in text)
		{
			if (c == '#')
			{
				if (stringBuilder.Length == 0)
				{
					stringBuilder.Append(c);
				}
			}
			else if (IsHexInputCharacter(c) && c != '#')
			{
				int num = ((stringBuilder.Length > 0 && stringBuilder[0] == '#') ? 7 : 6);
				if (stringBuilder.Length >= num)
				{
					break;
				}
				stringBuilder.Append(char.ToUpperInvariant(c));
			}
		}
		return stringBuilder.ToString();
	}

	private static bool IsHexInputCharacter(char c)
	{
		return c == '#' || (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
	}

	public static string ToHex(Color color)
	{
		return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
	}
}
