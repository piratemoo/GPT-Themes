namespace ChatGptDesktopSkinner;

internal sealed class Theme
{
	public string Id;

	public string Name;

	public string Bg;

	public string Panel;

	public string Input;

	public string Text;

	public string Accent;

	public string Border;

	public string User;

	public string Pattern;

	public Theme Clone(string id, string name)
	{
		Theme theme = new Theme();
		theme.Id = id;
		theme.Name = name;
		theme.Bg = Bg;
		theme.Panel = Panel;
		theme.Input = Input;
		theme.Text = Text;
		theme.Accent = Accent;
		theme.Border = Border;
		theme.User = User;
		theme.Pattern = Pattern;
		return theme;
	}
}
