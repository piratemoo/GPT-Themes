namespace ChatGptDesktopSkinner;

// Represents a Chromium DevTools Protocol target discovered from the local
// ChatGPT Desktop /json/list endpoint after URL and WebSocket validation.
internal sealed class CdpTarget
{
	public string Type;

	public string Title;

	public string Url;

	public string WebSocketDebuggerUrl;
}
