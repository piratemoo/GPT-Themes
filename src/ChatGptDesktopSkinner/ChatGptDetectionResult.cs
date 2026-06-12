using System.Collections.Generic;

namespace ChatGptDesktopSkinner;

internal sealed class ChatGptDetectionResult
{
	public bool Found { get; set; }

	public string ExePath { get; set; }

	public string AppUserModelId { get; set; }

	public string Method { get; set; }

	public List<string> Checks { get; private set; }

	public string Summary
	{
		get
		{
			if (Found)
			{
				return "ChatGPT Found" + (string.IsNullOrEmpty(Method) ? "" : (" via " + Method)) + ".";
			}
			return "ChatGPT Not Found.";
		}
	}

	public string Diagnostics => (Checks.Count == 0) ? "No detection checks have run yet." : string.Join(" ", Checks.ToArray());

	public ChatGptDetectionResult()
	{
		Checks = new List<string>();
	}
}
