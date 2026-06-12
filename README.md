<img src="https://github.com/piratemoo/GPT-Themes/blob/main/logo.png" alt="Logo" width="75">

<b>GPT Themes</b> is a Windows based skinning/customization tool for the ChatGPT Desktop app. It lets you choose themes, adjust colors/transparency, choose fonts, use local images for backgrounds and apply visual skins while ChatGPT Desktop is running. 

This is a small independent, unofficial customization tool: It is not affiliated with, endorsed by, sponsored by, or approved by OpenAI. ChatGPT and OpenAI are trademarks of OpenAI.

What it doesn't do: 
* Modify OpenAI servers
* Replace the official ChatGPT app
* Modify your ChatGPT account
* Collect chats
* Install executable theme plugins
* Execute code from imported theme files

<img src="https://github.com/piratemoo/GPT-Themes/blob/main/screenshot.png" alt="screenshot" width="600">

Appearance Controls
| Control      | What it does                                                                                           |
| ------------ | ------------------------------------------------------------------------------------------------------ |
| Layout       | Changes how wide the content area feels: Uses more horizontal space; default keeps the standard width. |
| Background   | Sets the main background. Choose a base color, pattern, or local image.                                |
| Panel        | Styles sidebars, menus, and panel areas. Choose a base color or a local image.                         |
| Glass        | Adjusts transparency. Lower values look more solid; higher values look more glass-like.                |

Connection
GPT Themes needs ChatGPT Desktop to be running before it can apply a skin. The connection area tells you whether GPT Themes found ChatGPT and gives you tools to fix detection issues.

Security and Privacy

GPT Themes customizes ChatGPT Desktop using a local Chromium debugging connection. This is required because ChatGPT Desktop does not currently provide an official theming API. GPT Themes uses a local debugging port, typically: `127.0.0.1:9322`. This local connection is used to communicate with ChatGPT Desktop.

* Keep the debugging address bound to `127.0.0.1`.
* Do not change it to `0.0.0.0`.
* Do not expose the debugging port to other machines.
* Do not port-forward the debugging port.
* Do not expose the port over LAN, VPN, container bridges, or remote access tools.

<b>Does GPT Themes permanently modify ChatGPT Desktop?</b> 
No. Themes apply styles while Desktop is running: You can clear the skin/return to a normal appearance on close.

<b>Is this an official OpenAI tool?</b>
No. This is a small independent/unofficial project that is not affiliated with, endorsed by, sponsored by, or approved by OpenAI.

<b>Why did you make this?</b> Because I hated the current lack of choices with ChatGPT Desktop. 

<b>Was this created with AI?</b> Yes, this is technically slop as I learn/play with LLM's.

This is currently a very early work in progress.

Created by - [piratemoo](https://www.piratemoo.com) © 2026
