namespace ChatGptDesktopSkinner;

internal sealed class SkinnerSettings
{
	public string ThemeId { get; set; }

	public string Layout { get; set; }

	public string BackgroundMode { get; set; }

	public string BackgroundValue { get; set; }

	public int Port { get; set; }

	public string CustomBg { get; set; }

	public string CustomPanel { get; set; }

	public string CustomInput { get; set; }

	public string CustomText { get; set; }

	public string CustomAccent { get; set; }

	public string CustomBorder { get; set; }

	public string CustomUser { get; set; }

	public int Transparency { get; set; }

	public bool PanelImage { get; set; }

	public string PanelImageMode { get; set; }

	public string PanelImageValue { get; set; }

	public bool GlassSearch { get; set; }

	public string FontFamily { get; set; }

	public string ManualChatGptExePath { get; set; }

	public string ThemeFilePath { get; set; }

	public bool ActiveThemeEnabled { get; set; }

	public AppliedThemeSnapshot ActiveTheme { get; set; }
}
