namespace ChatGptDesktopSkinner;

internal sealed class ThemeDocument
{
	public string Format { get; set; }

	public int Version { get; set; }

	public Theme Theme { get; set; }

	public SkinnerSettings Settings { get; set; }
}
