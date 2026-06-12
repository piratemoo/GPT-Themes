namespace ChatGptDesktopSkinner;

// On-disk .gpttheme files carry colors, layout, and local visual preferences.
// They should remain declarative data, never imported executable code.
internal sealed class ThemeDocument
{
	public string Format { get; set; }

	public int Version { get; set; }

	public Theme Theme { get; set; }

	public SkinnerSettings Settings { get; set; }
}
