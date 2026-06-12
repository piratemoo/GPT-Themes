using System.Drawing;
using System.Windows.Forms;

namespace ChatGptDesktopSkinner;

internal sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
{
	private readonly Color _back;

	private readonly Color _panel;

	private readonly Color _text;

	private readonly Color _muted;

	private readonly Color _hover;

	private readonly Color _border;

	public ThemedMenuRenderer(Color back, Color panel, Color text, Color muted, Color hover, Color border)
		: base(new ThemedMenuColorTable(back, panel, hover, border))
	{
		_back = back;
		_panel = panel;
		_text = text;
		_muted = muted;
		_hover = hover;
		_border = border;
		base.RoundedEdges = false;
	}

	protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
	{
		Color color = ((e.ToolStrip is MenuStrip) ? _back : _panel);
		using SolidBrush brush = new SolidBrush(color);
		e.Graphics.FillRectangle(brush, e.AffectedBounds);
	}

	protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
	{
		bool flag = e.Item.Owner is MenuStrip;
		bool flag2 = e.Item.Selected || (e.Item is ToolStripMenuItem && ((ToolStripMenuItem)e.Item).Pressed);
		Color color = ((!flag) ? (flag2 ? _hover : _panel) : (flag2 ? _hover : _back));
		using SolidBrush brush = new SolidBrush(color);
		e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
	}

	protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
	{
	}

	protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
	{
		e.TextColor = (e.Item.Enabled ? _text : _muted);
		base.OnRenderItemText(e);
	}

	protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
	{
		e.ArrowColor = (e.Item.Enabled ? _text : _muted);
		base.OnRenderArrow(e);
	}
}
