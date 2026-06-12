using System.Windows.Forms;

namespace ChatGptDesktopSkinner;

internal static class TextBoxExtensions
{
	public static void PlaceholderTextCompat(this TextBox textBox, string text)
	{
		if (textBox.TextLength == 0)
		{
			textBox.Tag = text;
		}
	}
}
