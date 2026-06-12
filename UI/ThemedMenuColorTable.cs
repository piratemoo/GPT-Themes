using System.Drawing;
using System.Windows.Forms;

namespace ChatGptDesktopSkinner;

internal sealed class ThemedMenuColorTable : ProfessionalColorTable
{
	private readonly Color _back;

	private readonly Color _panel;

	private readonly Color _hover;

	private readonly Color _border;

	public override Color ToolStripDropDownBackground => _panel;

	public override Color ImageMarginGradientBegin => _panel;

	public override Color ImageMarginGradientMiddle => _panel;

	public override Color ImageMarginGradientEnd => _panel;

	public override Color MenuBorder => _panel;

	public override Color MenuItemBorder => _panel;

	public override Color MenuItemSelected => _hover;

	public override Color MenuItemSelectedGradientBegin => _hover;

	public override Color MenuItemSelectedGradientEnd => _hover;

	public override Color MenuItemPressedGradientBegin => _back;

	public override Color MenuItemPressedGradientMiddle => _panel;

	public override Color MenuItemPressedGradientEnd => _panel;

	public override Color SeparatorDark => _border;

	public override Color SeparatorLight => _border;

	public override Color ToolStripBorder => _panel;

	public override Color ToolStripGradientBegin => _back;

	public override Color ToolStripGradientMiddle => _back;

	public override Color ToolStripGradientEnd => _back;

	public ThemedMenuColorTable(Color back, Color panel, Color hover, Color border)
	{
		_back = back;
		_panel = panel;
		_hover = hover;
		_border = border;
	}
}
