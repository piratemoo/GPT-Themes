using System.Windows.Forms;

namespace ChatGptDesktopSkinner;

internal static class TextBoxExtensions
{
	// Compatibility shim for older WinForms targets that do not expose native
	// PlaceholderText. The value is UI-only metadata, not saved theme data.
	public static void PlaceholderTextCompat(this TextBox textBox, string text)
	{
		if (textBox.TextLength == 0)
		{
			textBox.Tag = text;
		}
	}
}
