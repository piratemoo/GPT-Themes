using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ChatGptDesktopSkinner;

internal sealed class SkinnerForm : Form
{
	[Flags]
	private enum ActivateOptions
	{
		None = 0,
		DesignMode = 1,
		NoErrorUi = 2,
		NoSplashScreen = 4
	}

	[ComImport]
	[Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
	private class ApplicationActivationManager
	{
	}

	[ComImport]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
	private interface IApplicationActivationManager
	{
		[PreserveSig]
		int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, [MarshalAs(UnmanagedType.LPWStr)] string arguments, ActivateOptions options, out int processId);

		[PreserveSig]
		int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, IntPtr itemArray, [MarshalAs(UnmanagedType.LPWStr)] string verb, out int processId);

		[PreserveSig]
		int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, IntPtr itemArray, out int processId);
	}

	private sealed class WindowChromeMessageFilter : IMessageFilter
	{
		private readonly SkinnerForm _owner;

		public WindowChromeMessageFilter(SkinnerForm owner)
		{
			_owner = owner;
		}

		public bool PreFilterMessage(ref Message m)
		{
			if (UseNativeWindowChrome)
			{
				return false;
			}
			if (m.Msg != 513 || _owner == null || _owner.IsDisposed || !_owner.Visible)
			{
				return false;
			}
			Control target = Control.FromHandle(m.HWnd);
			if (!_owner.IsOwnedMessageTarget(target))
			{
				return false;
			}
			Point mousePosition = Control.MousePosition;
			Point point = _owner.PointToClient(mousePosition);
			if (!_owner.ClientRectangle.Contains(point))
			{
				return false;
			}
			if (!UseNativeWindowChrome)
			{
				IntPtr intPtr = _owner.ResolveResizeHitTest(mousePosition);
				if (intPtr != IntPtr.Zero)
				{
					_owner.BeginSystemMoveOrResize(intPtr.ToInt32());
					return true;
				}
			}
			if (_owner.IsCaptionDragPoint(point, target))
			{
				_owner.BeginSystemMoveOrResize(2);
				return true;
			}
			return false;
		}
	}

	private const string StyleId = "chatgpt-desktop-skinner-style";

	private const int WmNclButtonDown = 161;

	private const int WmNcHitTest = 132;

	private const int WmNcCalcSize = 131;

	private const int WmNcActivate = 134;

	private const int WmLButtonDown = 513;

	private const int WmEnterSizeMove = 561;

	private const int WmExitSizeMove = 562;

	private const int HtClient = 1;

	private const int HtCaption = 2;

	private const int HtLeft = 10;

	private const int HtRight = 11;

	private const int HtTop = 12;

	private const int HtTopLeft = 13;

	private const int HtTopRight = 14;

	private const int HtBottom = 15;

	private const int HtBottomLeft = 16;

	private const int HtBottomRight = 17;

	private const int SwRestore = 9;

	private const int ChromeResizeGrip = 14;

	private const int ChromeTopResizeGrip = 8;

	private const int TitleBarHeight = 34;

	private const int WsThickFrame = 262144;

	private const int WsMinimizeBox = 131072;

	private const int WsMaximizeBox = 65536;

	private const int WsSysMenu = 524288;

	private const uint SwpNoSize = 1u;

	private const uint SwpNoMove = 2u;

	private const uint SwpNoZOrder = 4u;

	private const uint SwpNoActivate = 16u;

	private const uint SwpFrameChanged = 32u;

	private const string ChatGptPackageName = "OpenAI.ChatGPT-Desktop";

	private const string ChatGptPublisherId = "2p2nqsd0c76g0";

	private const int SectionHeaderHeight = 28;

	private const float SectionHeaderFontSize = 10.5f;

	private const int MaxImageBytes = 8388608;

	private const int MaxThemeFileBytes = 1048576;

	private const int MaxThemeTextLength = 512;

	private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

	private readonly Dictionary<string, Theme> _themes = new Dictionary<string, Theme>(StringComparer.OrdinalIgnoreCase);

	private readonly List<Button> _themeButtons = new List<Button>();

	private readonly System.Windows.Forms.Timer _watchTimer = new System.Windows.Forms.Timer();

	private readonly System.Windows.Forms.Timer _previewRefreshTimer = new System.Windows.Forms.Timer();

	private readonly System.Windows.Forms.Timer _themeApplyDebounceTimer = new System.Windows.Forms.Timer();

	private readonly System.Windows.Forms.Timer _resizeSettleTimer = new System.Windows.Forms.Timer();

	private static readonly Color AppleBg = Color.FromArgb(8, 10, 16);

	private static readonly Color AppleSidebar = Color.FromArgb(18, 20, 29);

	private static readonly Color AppleCard = Color.FromArgb(24, 27, 38);

	private static readonly Color AppleCardTop = Color.FromArgb(30, 34, 47);

	private static readonly Color AppleCardBottom = Color.FromArgb(17, 20, 30);

	private static readonly Color AppleBorder = Color.FromArgb(47, 54, 72);

	private static readonly Color AppleText = Color.FromArgb(245, 247, 251);

	private static readonly Color AppleMuted = Color.FromArgb(156, 164, 181);

	private static readonly Color AppleAccent = Color.FromArgb(10, 132, 255);

	private static readonly Color ApplePink = Color.FromArgb(255, 55, 145);

	private static readonly Color AppleControl = Color.FromArgb(31, 35, 49);

	private static readonly Color AppleInput = Color.FromArgb(14, 17, 27);

	private static readonly bool UseNativeWindowChrome = false;

	private Theme _customTheme;

	private string _themeId = "plum";

	private bool _applying;

	private FlowLayoutPanel _themePanel;

	private ComboBox _layoutCombo;

	private ComboBox _fontCombo;

	private ComboBox _backgroundModeCombo;

	private TextBox _backgroundTextBox;

	private CenteredTextInput _portTextBox;

	private Label _statusLabel;

	private RoundedPanel _previewPanel;

	private Button _applyButton;

	private Button _watchButton;

	private Button _clearButton;

	private Button _browseButton;

	private Button _backgroundOkButton;

	private Label _activeThemeLabel;

	private RoundedPanel _backgroundPreviewPanel;

	private RoundedPanel _panelImagePreviewPanel;

	private Button _imageOkButton;

	private Button _baseColorButton;

	private Button _panelColorButton;

	private Button _inputColorButton;

	private Button _textColorButton;

	private Button _accentColorButton;

	private Button _borderColorButton;

	private Button _userColorButton;

	private Button _testPortButton;

	private Button _relaunchButton;

	private Button _chooseChatGptButton;

	private Label _chatGptStatusLabel;

	private Label _chatGptDiagnosticsLabel;

	private ComboBox _panelImageModeCombo;

	private TextBox _panelImageTextBox;

	private Button _panelImageBrowseButton;

	private CheckBox _glassSearchCheckBox;

	private ThemedSlider _transparencyTrackBar;

	private Label _transparencyValueLabel;

	private Label _titleLabel;

	private MenuStrip _menuStrip;

	private TableLayoutPanel _settingsStack;

	private Panel _settingsScrollHost;

	private bool _previewSkinEnabled = true;

	private bool _activeThemeEnabled;

	private AppliedThemeSnapshot _activeTheme;

	private string _loadedLayout = "Standard";

	private string _loadedFontFamily = "Default";

	private string _loadedBackgroundMode = "solid";

	private string _loadedBackgroundValue = "";

	private string _loadedPanelImageMode = "off";

	private string _loadedPanelImageValue = "";

	private int _loadedPort = 9322;

	private int _transparency = 12;

	private bool _glassSearch;

	private string _manualChatGptExePath = "";

	private string _currentThemeFilePath = "";

	private Image _programLogo;

	private Image _cachedBackgroundPreviewImage;

	private string _cachedBackgroundPreviewKey = "";

	private Image _cachedPanelPreviewImage;

	private string _cachedPanelPreviewKey = "";

	private int _lastThemeButtonWidth = -1;

	private string _lastThemeActivationId = "";

	private long _lastThemeActivationTicks;

	private bool _isLiveResizing;

	private bool _inSystemSizeMove;

	private bool _resizeRedrawSuspended;

	private bool _uiBuilt;

	private bool _loadingUiState;

	private WindowChromeMessageFilter _chromeMessageFilter;

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams createParams = base.CreateParams;
			if (!UseNativeWindowChrome)
			{
				createParams.Style |= 983040;
			}
			return createParams;
		}
	}

	[DllImport("user32.dll")]
	private static extern bool ReleaseCapture();

	[DllImport("user32.dll")]
	private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

	protected override void WndProc(ref Message m)
	{
		if (!UseNativeWindowChrome && m.Msg == 131 && m.WParam != IntPtr.Zero)
		{
			m.Result = IntPtr.Zero;
			return;
		}
		if (!UseNativeWindowChrome && m.Msg == 134)
		{
			m.Result = (IntPtr)1;
			return;
		}
		if (m.Msg == 561)
		{
			_inSystemSizeMove = true;
			BeginLiveResize();
		}
		else if (m.Msg == 562)
		{
			_inSystemSizeMove = false;
			EndLiveResize();
		}
		if (m.Msg == 132)
		{
			base.WndProc(ref m);
			Point p;
			Point point;
			Control target;
			if (UseNativeWindowChrome)
			{
				if (m.Result == (IntPtr)1)
				{
					p = ScreenPointFromLParam(m.LParam);
					point = PointToClient(p);
					target = DeepChildAtPoint(this, point);
					if (IsCaptionDragPoint(point, target))
					{
						m.Result = (IntPtr)2;
					}
				}
				return;
			}
			p = ScreenPointFromLParam(m.LParam);
			IntPtr intPtr = ResolveResizeHitTest(p);
			if (intPtr != IntPtr.Zero)
			{
				m.Result = intPtr;
				return;
			}
			point = PointToClient(p);
			target = DeepChildAtPoint(this, point);
			if (IsCaptionDragPoint(point, target))
			{
				m.Result = (IntPtr)2;
			}
		}
		else
		{
			base.WndProc(ref m);
		}
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		ApplyNativeWindowTheme();
		InstallChromeMessageFilter();
	}

	protected override void OnActivated(EventArgs e)
	{
		base.OnActivated(e);
		ApplyNativeWindowTheme();
		Invalidate(invalidateChildren: true);
	}

	protected override void OnDeactivate(EventArgs e)
	{
		base.OnDeactivate(e);
		ApplyNativeWindowTheme();
		Invalidate(invalidateChildren: true);
	}

	protected override void OnHandleDestroyed(EventArgs e)
	{
		RemoveChromeMessageFilter();
		base.OnHandleDestroyed(e);
	}

	protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
	{
		switch (keyData)
		{
		case Keys.N | Keys.Control:
			NewTheme();
			return true;
		case Keys.S | Keys.Control:
			SaveTheme(saveAs: false);
			return true;
		case Keys.S | Keys.Shift | Keys.Control:
			SaveTheme(saveAs: true);
			return true;
		case Keys.O | Keys.Control:
			ImportTheme();
			return true;
		case Keys.F1:
			OpenHelpDocumentation();
			return true;
		default:
			return base.ProcessCmdKey(ref msg, keyData);
		}
	}

	private IntPtr ResolveResizeHitTest(Point screenPoint)
	{
		if (base.WindowState == FormWindowState.Maximized)
		{
			return IntPtr.Zero;
		}
		Point point = PointToClient(screenPoint);
		if (point.X < 0 || point.Y < 0 || point.X > base.ClientSize.Width || point.Y > base.ClientSize.Height)
		{
			return IntPtr.Zero;
		}
		bool flag = point.X <= 14;
		bool flag2 = point.X >= base.ClientSize.Width - 14;
		bool flag3 = point.Y <= 8;
		bool flag4 = point.Y >= base.ClientSize.Height - 14;
		if (flag && flag3)
		{
			return (IntPtr)13;
		}
		if (flag2 && flag3)
		{
			return (IntPtr)14;
		}
		if (flag && flag4)
		{
			return (IntPtr)16;
		}
		if (flag2 && flag4)
		{
			return (IntPtr)17;
		}
		if (flag)
		{
			return (IntPtr)10;
		}
		if (flag2)
		{
			return (IntPtr)11;
		}
		if (flag3)
		{
			return (IntPtr)12;
		}
		if (flag4)
		{
			return (IntPtr)15;
		}
		return IntPtr.Zero;
	}

	private void InstallChromeMessageFilter()
	{
		if (!UseNativeWindowChrome && _chromeMessageFilter == null)
		{
			_chromeMessageFilter = new WindowChromeMessageFilter(this);
			Application.AddMessageFilter(_chromeMessageFilter);
		}
	}

	private void RemoveChromeMessageFilter()
	{
		if (_chromeMessageFilter != null)
		{
			Application.RemoveMessageFilter(_chromeMessageFilter);
			_chromeMessageFilter = null;
		}
	}

	private void BeginSystemMoveOrResize(int hitTest)
	{
		if (base.IsHandleCreated && !base.IsDisposed && (base.WindowState != FormWindowState.Maximized || hitTest == 2))
		{
			ReleaseCapture();
			SendMessage(base.Handle, 161, hitTest, 0);
		}
	}

	private bool IsCaptionDragPoint(Point clientPoint, Control target)
	{
		if (base.WindowState == FormWindowState.Maximized)
		{
			return false;
		}
		if (clientPoint.Y < 8 || clientPoint.Y >= 34)
		{
			return false;
		}
		if (clientPoint.X < 0 || clientPoint.X >= base.ClientSize.Width)
		{
			return false;
		}
		if (_menuStrip != null)
		{
			Point point = _menuStrip.PointToClient(PointToScreen(clientPoint));
			if (_menuStrip.ClientRectangle.Contains(point))
			{
				return _menuStrip.GetItemAt(point) == null;
			}
		}
		return !IsInteractiveChromeControl(target);
	}

	private bool IsOwnedMessageTarget(Control target)
	{
		if (target == null)
		{
			return true;
		}
		Form form = target.FindForm();
		return form == null || form == this;
	}

	private static Control DeepChildAtPoint(Control root, Point point)
	{
		if (root == null)
		{
			return null;
		}
		Control control = root;
		Point point2 = point;
		while (control != null)
		{
			Control childAtPoint = control.GetChildAtPoint(point2, GetChildAtPointSkip.Invisible);
			if (childAtPoint == null)
			{
				return control;
			}
			point2 = childAtPoint.PointToClient(control.PointToScreen(point2));
			control = childAtPoint;
		}
		return root;
	}

	private static bool IsInteractiveChromeControl(Control control)
	{
		for (Control control2 = control; control2 != null; control2 = control2.Parent)
		{
			if (control2 is MenuStrip || control2 is ToolStrip || control2 is Button || control2 is ComboBox || control2 is TextBox || control2 is ThemedSlider || control2 is CheckBox || control2 is LinkLabel || control2 is ColorSwatchButton || control2 is CenteredTextInput)
			{
				return true;
			}
		}
		return false;
	}

	private static Point ScreenPointFromLParam(IntPtr lParam)
	{
		long num = lParam.ToInt64();
		int num2 = (short)(num & 0xFFFF);
		int num3 = (short)((num >> 16) & 0xFFFF);
		return new Point(num2, num3);
	}

	protected override void OnResizeBegin(EventArgs e)
	{
		BeginLiveResize();
		base.OnResizeBegin(e);
	}

	protected override void OnResizeEnd(EventArgs e)
	{
		EndLiveResize();
		base.OnResizeEnd(e);
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		QueueResizeSettle();
	}

	private void QueueResizeSettle()
	{
		if (_uiBuilt && base.IsHandleCreated && !base.IsDisposed && !base.Disposing && base.WindowState != FormWindowState.Minimized)
		{
			BeginLiveResize();
			if (!_inSystemSizeMove)
			{
				_resizeSettleTimer.Stop();
				_resizeSettleTimer.Start();
			}
		}
	}

	private void BeginLiveResize()
	{
		if (!_isLiveResizing)
		{
			_isLiveResizing = true;
			SetResizeRedrawSuspended(suspended: true);
			if (_previewPanel != null)
			{
				_previewPanel.Invalidate();
			}
		}
	}

	private void EndLiveResize()
	{
		_resizeSettleTimer.Stop();
		if (_isLiveResizing)
		{
			_isLiveResizing = false;
			ResizeThemeButtons();
			SetResizeRedrawSuspended(suspended: false);
			if (_previewPanel != null)
			{
				_previewPanel.Invalidate();
			}
		}
	}

	private void SetResizeRedrawSuspended(bool suspended)
	{
		if (_resizeRedrawSuspended == suspended)
		{
			return;
		}
		_resizeRedrawSuspended = suspended;
		if (suspended)
		{
			return;
		}
		Control control = ((base.Controls.Count > 0) ? base.Controls[0] : null);
		Control[] array = new Control[4] { control, _themePanel, _settingsScrollHost, _previewPanel };
		Control[] array2 = array;
		foreach (Control control2 in array2)
		{
			if (control2 != null && !control2.IsDisposed && control2.IsHandleCreated)
			{
				control2.Invalidate(invalidateChildren: true);
			}
		}
		Invalidate(invalidateChildren: true);
	}

	public SkinnerForm()
	{
		DoubleBuffered = true;
		SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		_json.MaxJsonLength = 16777216;
		_programLogo = LoadProgramLogo();
		LoadThemes();
		LoadSettings();
		_watchTimer.Interval = 2500;
		System.Windows.Forms.Timer watchTimer = _watchTimer;
		EventHandler value = async delegate
		{
			await ApplySkinAsync(quiet: true);
		};
		watchTimer.Tick += value;
		_previewRefreshTimer.Interval = 80;
		_previewRefreshTimer.Tick += delegate
		{
			_previewRefreshTimer.Stop();
			RefreshSkinnedPreview();
		};
		_themeApplyDebounceTimer.Interval = 520;
		_themeApplyDebounceTimer.Tick += async delegate
		{
			_themeApplyDebounceTimer.Stop();
			await ApplySkinAsync(quiet: true);
		};
		_resizeSettleTimer.Interval = 160;
		_resizeSettleTimer.Tick += delegate
		{
			EndLiveResize();
		};
		BuildUi();
		ApplyUiState();
	}

	protected override async void OnShown(EventArgs e)
	{
		base.OnShown(e);
		await RefreshChatGptDetectionStatusAsync(verbose: false);
		await RestoreActiveThemeOnStartupAsync();
	}

	protected override void OnFormClosed(FormClosedEventArgs e)
	{
		RemoveChromeMessageFilter();
		SetResizeRedrawSuspended(suspended: false);
		_resizeSettleTimer.Stop();
		_resizeSettleTimer.Dispose();
		_themeApplyDebounceTimer.Stop();
		_themeApplyDebounceTimer.Dispose();
		_previewRefreshTimer.Stop();
		_previewRefreshTimer.Dispose();
		_watchTimer.Stop();
		_watchTimer.Dispose();
		if (_programLogo != null)
		{
			_programLogo.Dispose();
			_programLogo = null;
		}
		ClearPreviewImageCaches();
		base.OnFormClosed(e);
	}

	private void LoadThemes()
	{
		AddTheme("midnight", "Midnight", "#0c111d", "#151c2b", "#101826", "#eff6ff", "#7cc7ff", "#6f86a5", "#244a69", "radial-gradient(circle at 12% 8%, rgb(124 199 255 / 0.2), transparent 32%), radial-gradient(circle at 85% 18%, rgb(142 240 176 / 0.14), transparent 30%)");
		AddTheme("plum", "Plum", "#160b22", "#2a1740", "#211031", "#fff6ff", "#f4b8ff", "#b985d0", "#5c2c78", "radial-gradient(circle at 18% 14%, rgb(244 184 255 / 0.28), transparent 30%), radial-gradient(circle at 82% 18%, rgb(124 199 255 / 0.16), transparent 30%), linear-gradient(135deg, rgb(244 184 255 / 0.08), transparent 38%)");
		AddTheme("sakura", "Sakura", "#1c0f1b", "#3a1c32", "#2b1426", "#fff4fb", "#ff9fd5", "#e88abf", "#7d315b", "radial-gradient(circle at 14% 12%, rgb(255 159 213 / 0.28), transparent 30%), radial-gradient(circle at 82% 18%, rgb(255 226 169 / 0.18), transparent 26%), linear-gradient(145deg, rgb(255 255 255 / 0.08), transparent 40%)");
		AddTheme("bubblegum", "Bubblegum", "#171129", "#2f1f58", "#251848", "#fff7ff", "#ffa7f3", "#93dbff", "#683b8f", "radial-gradient(circle at 15% 16%, rgb(255 167 243 / 0.3), transparent 28%), radial-gradient(circle at 88% 16%, rgb(147 219 255 / 0.22), transparent 30%), radial-gradient(circle at 50% 88%, rgb(255 235 153 / 0.12), transparent 32%)");
		AddTheme("seafoam", "Seafoam", "#061817", "#123b38", "#0d2a28", "#f1fffc", "#98ffe0", "#72c9bd", "#1d6d68", "radial-gradient(circle at 14% 12%, rgb(152 255 224 / 0.25), transparent 30%), radial-gradient(circle at 85% 20%, rgb(139 200 255 / 0.18), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.06), transparent 42%)");
		AddTheme("sunset", "Sunset Pop", "#211019", "#4a2033", "#351726", "#fff8f1", "#ffca7a", "#ff8fb8", "#823858", "radial-gradient(circle at 18% 14%, rgb(255 202 122 / 0.3), transparent 30%), radial-gradient(circle at 82% 16%, rgb(255 143 184 / 0.22), transparent 30%), linear-gradient(150deg, rgb(255 255 255 / 0.07), transparent 42%)");
		AddTheme("jade", "Jade", "#091512", "#10231e", "#0c1b17", "#effcf8", "#7af2c4", "#5c9584", "#1b5a4a", "radial-gradient(circle at 14% 10%, rgb(122 242 196 / 0.2), transparent 32%), radial-gradient(circle at 82% 22%, rgb(126 199 255 / 0.12), transparent 28%)");
		AddTheme("ember", "Ember", "#17120f", "#261a14", "#1d130f", "#fff6ed", "#ffb36b", "#a97858", "#66391f", "radial-gradient(circle at 20% 12%, rgb(255 179 107 / 0.24), transparent 30%), radial-gradient(circle at 80% 18%, rgb(255 118 118 / 0.14), transparent 28%)");
		AddTheme("graphite", "Graphite", "#101214", "#1e2226", "#171a1e", "#f1f4f6", "#d3dde8", "#7b858f", "#343c46", "linear-gradient(135deg, rgb(255 255 255 / 0.06), transparent 42%), radial-gradient(circle at 88% 12%, rgb(211 221 232 / 0.1), transparent 30%)");
		AddTheme("daylight", "Daylight", "#f4f7fb", "#ffffff", "#eef3f8", "#172033", "#3267d6", "#9ba9bd", "#dbe9ff", "radial-gradient(circle at 12% 8%, rgb(50 103 214 / 0.12), transparent 30%), radial-gradient(circle at 88% 14%, rgb(20 142 106 / 0.1), transparent 28%)");
		AddTheme("pearl", "Pearl", "#f7f9fe", "#ffffff", "#eef2fb", "#17213a", "#6d7dff", "#c4cad8", "#e5e9f6", "radial-gradient(circle at 12% 10%, rgb(109 125 255 / 0.14), transparent 28%), radial-gradient(circle at 86% 18%, rgb(126 211 255 / 0.14), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.7), transparent 45%)");
		AddTheme("cloudberry", "Cloudberry", "#fff6fb", "#ffffff", "#f6eefa", "#261729", "#ff6fb7", "#ddb6d0", "#ffe2f1", "radial-gradient(circle at 14% 12%, rgb(255 111 183 / 0.18), transparent 30%), radial-gradient(circle at 84% 18%, rgb(150 193 255 / 0.14), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.66), transparent 42%)");
		AddTheme("mintlight", "Mint Light", "#f3fffb", "#ffffff", "#eafff7", "#10251f", "#16b884", "#aad9cd", "#dbfff2", "radial-gradient(circle at 15% 12%, rgb(22 184 132 / 0.15), transparent 30%), radial-gradient(circle at 86% 18%, rgb(94 234 212 / 0.12), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.68), transparent 42%)");
		AddTheme("skyglass", "Sky Glass", "#f1f8ff", "#ffffff", "#e8f3ff", "#10223a", "#1884ff", "#aecce9", "#d8ecff", "radial-gradient(circle at 12% 10%, rgb(24 132 255 / 0.15), transparent 30%), radial-gradient(circle at 86% 20%, rgb(80 220 255 / 0.13), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.7), transparent 42%)");
		AddTheme("lilacmist", "Lilac Mist", "#faf7ff", "#ffffff", "#f0eaff", "#241a38", "#9b6dff", "#cfc2e8", "#eadfff", "radial-gradient(circle at 14% 12%, rgb(155 109 255 / 0.15), transparent 30%), radial-gradient(circle at 84% 18%, rgb(255 140 213 / 0.12), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.68), transparent 42%)");
		AddTheme("morning", "Morning", "#f5fbff", "#ffffff", "#edf7ff", "#1b2433", "#ff8a4d", "#b9cce0", "#dff2ff", "radial-gradient(circle at 13% 12%, rgb(255 138 77 / 0.14), transparent 28%), radial-gradient(circle at 84% 18%, rgb(83 190 255 / 0.14), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.7), transparent 42%)");
		AddTheme("cupertino", "Cupertino", "#0a0d13", "#171b24", "#202634", "#f7f8fb", "#0a84ff", "#7d8596", "#d9ecff", "linear-gradient(145deg, rgb(255 255 255 / 0.08), transparent 40%), radial-gradient(circle at 18% 12%, rgb(10 132 255 / 0.18), transparent 28%), radial-gradient(circle at 84% 20%, rgb(255 255 255 / 0.09), transparent 26%)");
		AddTheme("aurora", "Aurora", "#071318", "#102833", "#0b1d27", "#eefcff", "#68f7d2", "#6aaee8", "#1d536a", "radial-gradient(circle at 16% 12%, rgb(104 247 210 / 0.24), transparent 30%), radial-gradient(circle at 84% 16%, rgb(106 174 232 / 0.18), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.05), transparent 42%)");
		AddTheme("lavender", "Lavender", "#181224", "#2c2241", "#211a35", "#fbf7ff", "#c7a8ff", "#9f8ad8", "#5c4a88", "radial-gradient(circle at 16% 14%, rgb(199 168 255 / 0.28), transparent 30%), radial-gradient(circle at 82% 18%, rgb(255 170 232 / 0.14), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.06), transparent 42%)");
		AddTheme("rosequartz", "Rose Quartz", "#1c1117", "#3a202d", "#2a1722", "#fff5f8", "#ffacc7", "#e48aa8", "#7f3a55", "radial-gradient(circle at 18% 12%, rgb(255 172 199 / 0.28), transparent 30%), radial-gradient(circle at 82% 18%, rgb(255 223 238 / 0.14), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.07), transparent 42%)");
		AddTheme("cottoncandy", "Cotton Candy", "#15142a", "#2d2755", "#221e44", "#fff8ff", "#ff9ee8", "#8fd7ff", "#644caa", "radial-gradient(circle at 15% 16%, rgb(255 158 232 / 0.3), transparent 28%), radial-gradient(circle at 86% 16%, rgb(143 215 255 / 0.24), transparent 30%), radial-gradient(circle at 55% 92%, rgb(255 241 179 / 0.13), transparent 32%)");
		AddTheme("blueberry", "Blueberry", "#091226", "#14244a", "#0e1b39", "#f2f7ff", "#82b7ff", "#6f8bd8", "#284f91", "radial-gradient(circle at 14% 12%, rgb(130 183 255 / 0.24), transparent 32%), radial-gradient(circle at 84% 20%, rgb(167 139 250 / 0.16), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.05), transparent 42%)");
		AddTheme("mintchip", "Mint Chip", "#081512", "#163128", "#0f241e", "#f3fffb", "#8cffc7", "#79d5ac", "#275f4c", "radial-gradient(circle at 14% 12%, rgb(140 255 199 / 0.26), transparent 30%), radial-gradient(circle at 84% 20%, rgb(226 255 247 / 0.1), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.05), transparent 42%)");
		AddTheme("peachfizz", "Peach Fizz", "#211318", "#442636", "#321b29", "#fff8f2", "#ffc08a", "#ff8fb3", "#8b4a63", "radial-gradient(circle at 16% 12%, rgb(255 192 138 / 0.28), transparent 30%), radial-gradient(circle at 84% 18%, rgb(255 143 179 / 0.22), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.07), transparent 42%)");
		AddTheme("honeycomb", "Honeycomb", "#17130b", "#30230e", "#241a0b", "#fff7dc", "#ffd166", "#c6933d", "#6a4a14", "radial-gradient(circle at 16% 12%, rgb(255 209 102 / 0.26), transparent 30%), radial-gradient(circle at 84% 18%, rgb(255 159 28 / 0.14), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.06), transparent 42%)");
		AddTheme("lagoon", "Lagoon", "#06151d", "#113341", "#0c2532", "#f0fcff", "#5eead4", "#67c7f2", "#1c6175", "radial-gradient(circle at 15% 12%, rgb(94 234 212 / 0.24), transparent 30%), radial-gradient(circle at 84% 20%, rgb(103 199 242 / 0.2), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.05), transparent 42%)");
		AddTheme("orchid", "Orchid", "#190f25", "#33204c", "#25183a", "#fff6ff", "#e8a7ff", "#b481e2", "#70439a", "radial-gradient(circle at 18% 14%, rgb(232 167 255 / 0.3), transparent 30%), radial-gradient(circle at 84% 18%, rgb(180 129 226 / 0.2), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.06), transparent 42%)");
		AddTheme("raspberry", "Raspberry", "#210d18", "#41172e", "#301123", "#fff5fa", "#ff75b7", "#d65d95", "#8e285b", "radial-gradient(circle at 16% 14%, rgb(255 117 183 / 0.3), transparent 30%), radial-gradient(circle at 84% 18%, rgb(214 93 149 / 0.18), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.06), transparent 42%)");
		AddTheme("frostbite", "Frostbite", "#07111d", "#142236", "#0e192a", "#f5fbff", "#9be7ff", "#7ea6c8", "#244a68", "radial-gradient(circle at 12% 10%, rgb(155 231 255 / 0.24), transparent 32%), radial-gradient(circle at 84% 16%, rgb(255 255 255 / 0.12), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.05), transparent 42%)");
		AddTheme("icepop", "Ice Pop", "#0a1421", "#172d46", "#10243a", "#f4fbff", "#76e4ff", "#a6b6ff", "#2b5b82", "radial-gradient(circle at 14% 12%, rgb(118 228 255 / 0.26), transparent 30%), radial-gradient(circle at 84% 18%, rgb(166 182 255 / 0.2), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.05), transparent 42%)");
		AddTheme("starlight", "Starlight", "#0b1020", "#1a2240", "#121934", "#f8fbff", "#ffe082", "#94a3ff", "#3b4a8f", "radial-gradient(circle at 16% 12%, rgb(255 224 130 / 0.18), transparent 26%), radial-gradient(circle at 84% 18%, rgb(148 163 255 / 0.2), transparent 30%), radial-gradient(circle at 50% 80%, rgb(255 255 255 / 0.08), transparent 34%)");
		AddTheme("galaxy", "Galaxy", "#100d22", "#211844", "#171233", "#f7f3ff", "#b388ff", "#ff7ac8", "#5a3fa0", "radial-gradient(circle at 16% 14%, rgb(179 136 255 / 0.28), transparent 30%), radial-gradient(circle at 84% 18%, rgb(255 122 200 / 0.2), transparent 30%), radial-gradient(circle at 50% 92%, rgb(108 92 231 / 0.14), transparent 32%)");
		AddTheme("cyberlime", "Cyberlime", "#07140b", "#102415", "#0b1a10", "#f2fff2", "#b6ff4d", "#57d87a", "#2f7a34", "radial-gradient(circle at 16% 14%, rgb(182 255 77 / 0.28), transparent 30%), radial-gradient(circle at 84% 20%, rgb(87 216 122 / 0.18), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.05), transparent 42%)");
		AddTheme("neonnoir", "Neon Noir", "#080a12", "#151927", "#10131f", "#f8fbff", "#ff4fd8", "#4df3ff", "#3b2c66", "radial-gradient(circle at 15% 12%, rgb(255 79 216 / 0.26), transparent 28%), radial-gradient(circle at 86% 18%, rgb(77 243 255 / 0.2), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.04), transparent 42%)");
		AddTheme("watermelon", "Watermelon", "#10170f", "#23321f", "#172415", "#f9fff4", "#ff6f91", "#7fe3a1", "#4d7d43", "radial-gradient(circle at 16% 14%, rgb(255 111 145 / 0.24), transparent 30%), radial-gradient(circle at 84% 20%, rgb(127 227 161 / 0.18), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.05), transparent 42%)");
		AddTheme("cherrycola", "Cherry Cola", "#1b0c12", "#361722", "#271019", "#fff4f7", "#ff5f85", "#b9657a", "#6c2234", "radial-gradient(circle at 16% 12%, rgb(255 95 133 / 0.28), transparent 30%), radial-gradient(circle at 84% 18%, rgb(185 101 122 / 0.18), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.05), transparent 42%)");
		AddTheme("moonstone", "Moonstone", "#0d1117", "#1a2230", "#121923", "#f4f8fb", "#c8d9e8", "#8da2b8", "#3d4f66", "radial-gradient(circle at 16% 12%, rgb(200 217 232 / 0.18), transparent 30%), radial-gradient(circle at 84% 18%, rgb(141 162 184 / 0.14), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.06), transparent 42%)");
		AddTheme("solarflare", "Solar Flare", "#1c100a", "#3a1e10", "#2a160c", "#fff6ea", "#ff9f1c", "#ff5d73", "#84451f", "radial-gradient(circle at 16% 12%, rgb(255 159 28 / 0.3), transparent 30%), radial-gradient(circle at 84% 18%, rgb(255 93 115 / 0.2), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.06), transparent 42%)");
		AddTheme("prism", "Prism", "#10111f", "#20243a", "#171b2d", "#fbfbff", "#a78bfa", "#60a5fa", "#3b2d6e", "radial-gradient(circle at 12% 12%, rgb(255 121 198 / 0.24), transparent 28%), radial-gradient(circle at 88% 16%, rgb(96 165 250 / 0.2), transparent 30%), radial-gradient(circle at 52% 88%, rgb(52 211 153 / 0.14), transparent 32%)");
		AddTheme("matcha", "Matcha", "#0b140d", "#182718", "#111e12", "#f6fff0", "#b7f57a", "#8eb36d", "#405f2d", "radial-gradient(circle at 16% 12%, rgb(183 245 122 / 0.22), transparent 30%), radial-gradient(circle at 84% 18%, rgb(142 179 109 / 0.14), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.05), transparent 42%)");
		AddTheme("lilacdream", "Lilac Dream", "#171125", "#302346", "#241a36", "#fff8ff", "#d8b4fe", "#f0abfc", "#6e4c91", "radial-gradient(circle at 16% 14%, rgb(216 180 254 / 0.28), transparent 30%), radial-gradient(circle at 84% 18%, rgb(240 171 252 / 0.18), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.06), transparent 42%)");
		AddTheme("tropical", "Tropical", "#071610", "#123520", "#0c2517", "#f4fff8", "#4ade80", "#22d3ee", "#206a45", "radial-gradient(circle at 16% 12%, rgb(74 222 128 / 0.24), transparent 30%), radial-gradient(circle at 84% 18%, rgb(34 211 238 / 0.18), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.05), transparent 42%)");
		_customTheme = _themes["plum"].Clone("custom", "Custom");
		_themes["custom"] = _customTheme;
	}

	private void AddTheme(string id, string name, string bg, string panel, string input, string text, string accent, string border, string user, string pattern)
	{
		_themes[id] = new Theme
		{
			Id = id,
			Name = name,
			Bg = bg,
			Panel = panel,
			Input = input,
			Text = text,
			Accent = accent,
			Border = border,
			User = user,
			Pattern = pattern
		};
	}

	private void LoadSettings()
	{
		SkinnerSettings skinnerSettings = ReadSettings();
		if (skinnerSettings == null)
		{
			_themeId = "plum";
			ApplyStartupSnapshot(DefaultStartupSnapshot());
			return;
		}
		_activeTheme = SanitizeAppliedThemeSnapshot(skinnerSettings.ActiveTheme);
		_activeThemeEnabled = skinnerSettings.ActiveThemeEnabled && _activeTheme != null;
		AppliedThemeSnapshot snapshot = (_activeThemeEnabled ? _activeTheme : SnapshotFromSettings(skinnerSettings));
		ApplyStartupSnapshot(snapshot);
		_manualChatGptExePath = (IsChatGptExecutablePath(skinnerSettings.ManualChatGptExePath) ? skinnerSettings.ManualChatGptExePath : "");
		_currentThemeFilePath = (string.IsNullOrWhiteSpace(skinnerSettings.ThemeFilePath) ? "" : skinnerSettings.ThemeFilePath);
	}

	private AppliedThemeSnapshot DefaultStartupSnapshot()
	{
		AppliedThemeSnapshot appliedThemeSnapshot = new AppliedThemeSnapshot();
		appliedThemeSnapshot.ThemeId = "plum";
		appliedThemeSnapshot.ThemeName = "Plum";
		appliedThemeSnapshot.Layout = "Standard";
		appliedThemeSnapshot.BackgroundMode = "solid";
		appliedThemeSnapshot.BackgroundValue = "";
		appliedThemeSnapshot.Port = 9322;
		appliedThemeSnapshot.CustomBg = _customTheme.Bg;
		appliedThemeSnapshot.CustomPanel = _customTheme.Panel;
		appliedThemeSnapshot.CustomInput = _customTheme.Input;
		appliedThemeSnapshot.CustomText = _customTheme.Text;
		appliedThemeSnapshot.CustomAccent = _customTheme.Accent;
		appliedThemeSnapshot.CustomBorder = _customTheme.Border;
		appliedThemeSnapshot.CustomUser = _customTheme.User;
		appliedThemeSnapshot.Transparency = _transparency;
		appliedThemeSnapshot.PanelImage = false;
		appliedThemeSnapshot.PanelImageMode = "off";
		appliedThemeSnapshot.PanelImageValue = "";
		appliedThemeSnapshot.GlassSearch = _glassSearch;
		appliedThemeSnapshot.FontFamily = "Default";
		return appliedThemeSnapshot;
	}

	private AppliedThemeSnapshot SnapshotFromSettings(SkinnerSettings settings)
	{
		if (settings == null)
		{
			return DefaultStartupSnapshot();
		}
		return SanitizeAppliedThemeSnapshot(new AppliedThemeSnapshot
		{
			ThemeId = settings.ThemeId,
			ThemeName = ThemeNameForId(settings.ThemeId),
			Layout = settings.Layout,
			BackgroundMode = settings.BackgroundMode,
			BackgroundValue = settings.BackgroundValue,
			Port = settings.Port,
			CustomBg = settings.CustomBg,
			CustomPanel = settings.CustomPanel,
			CustomInput = settings.CustomInput,
			CustomText = settings.CustomText,
			CustomAccent = settings.CustomAccent,
			CustomBorder = settings.CustomBorder,
			CustomUser = settings.CustomUser,
			Transparency = settings.Transparency,
			PanelImage = settings.PanelImage,
			PanelImageMode = settings.PanelImageMode,
			PanelImageValue = settings.PanelImageValue,
			GlassSearch = settings.GlassSearch,
			FontFamily = settings.FontFamily
		}) ?? DefaultStartupSnapshot();
	}

	private void ApplyStartupSnapshot(AppliedThemeSnapshot snapshot)
	{
		if (snapshot == null)
		{
			snapshot = DefaultStartupSnapshot();
		}
		if (!string.IsNullOrEmpty(snapshot.ThemeId) && _themes.ContainsKey(snapshot.ThemeId))
		{
			_themeId = snapshot.ThemeId;
		}
		else
		{
			_themeId = "custom";
		}
		_customTheme.Bg = NonEmpty(snapshot.CustomBg, _customTheme.Bg);
		_customTheme.Panel = NonEmpty(snapshot.CustomPanel, _customTheme.Panel);
		_customTheme.Input = NonEmpty(snapshot.CustomInput, _customTheme.Input);
		_customTheme.Text = NonEmpty(snapshot.CustomText, _customTheme.Text);
		_customTheme.Accent = NonEmpty(snapshot.CustomAccent, _customTheme.Accent);
		_customTheme.Border = NonEmpty(snapshot.CustomBorder, _customTheme.Border);
		_customTheme.User = NonEmpty(snapshot.CustomUser, _customTheme.User);
		_transparency = Math.Max(0, Math.Min(75, snapshot.Transparency));
		_glassSearch = snapshot.GlassSearch;
		_loadedLayout = LayoutDisplayName(NonEmpty(snapshot.Layout, "standard"));
		_loadedFontFamily = NonEmpty(snapshot.FontFamily, "Default");
		_loadedBackgroundMode = NormalizeChoice(snapshot.BackgroundMode, new string[3] { "solid", "pattern", "file" }, "solid");
		_loadedBackgroundValue = NonEmpty(snapshot.BackgroundValue, "");
		_loadedPanelImageMode = NormalizeChoice(snapshot.PanelImageMode, new string[3] { "off", "same", "file" }, snapshot.PanelImage ? "same" : "off");
		_loadedPanelImageValue = NonEmpty(snapshot.PanelImageValue, "");
		_loadedPort = ((snapshot.Port > 0 && snapshot.Port <= 65535) ? snapshot.Port : 9322);
	}

	private AppliedThemeSnapshot SanitizeAppliedThemeSnapshot(AppliedThemeSnapshot snapshot)
	{
		if (snapshot == null)
		{
			return null;
		}
		string text = NonEmpty(snapshot.ThemeId, "custom");
		if (!_themes.ContainsKey(text))
		{
			text = "custom";
		}
		AppliedThemeSnapshot appliedThemeSnapshot = new AppliedThemeSnapshot();
		appliedThemeSnapshot.ThemeId = text;
		appliedThemeSnapshot.ThemeName = NonEmpty(snapshot.ThemeName, ThemeNameForId(text));
		appliedThemeSnapshot.Layout = NormalizeChoice(snapshot.Layout, new string[4] { "standard", "wide", "compact", "focus" }, "standard");
		appliedThemeSnapshot.BackgroundMode = NormalizeChoice(snapshot.BackgroundMode, new string[3] { "solid", "pattern", "file" }, "solid");
		appliedThemeSnapshot.BackgroundValue = LimitThemeText(snapshot.BackgroundValue ?? "", 512);
		appliedThemeSnapshot.Port = ((snapshot.Port > 0 && snapshot.Port <= 65535) ? snapshot.Port : 9322);
		appliedThemeSnapshot.CustomBg = NormalizeHex(snapshot.CustomBg);
		appliedThemeSnapshot.CustomPanel = NormalizeHex(snapshot.CustomPanel);
		appliedThemeSnapshot.CustomInput = NormalizeHex(snapshot.CustomInput);
		appliedThemeSnapshot.CustomText = NormalizeHex(snapshot.CustomText);
		appliedThemeSnapshot.CustomAccent = NormalizeHex(snapshot.CustomAccent);
		appliedThemeSnapshot.CustomBorder = NormalizeHex(snapshot.CustomBorder);
		appliedThemeSnapshot.CustomUser = NormalizeHex(snapshot.CustomUser);
		appliedThemeSnapshot.Transparency = Math.Max(0, Math.Min(75, snapshot.Transparency));
		appliedThemeSnapshot.PanelImage = snapshot.PanelImage;
		appliedThemeSnapshot.PanelImageMode = NormalizeChoice(snapshot.PanelImageMode, new string[3] { "off", "same", "file" }, snapshot.PanelImage ? "same" : "off");
		appliedThemeSnapshot.PanelImageValue = LimitThemeText(snapshot.PanelImageValue ?? "", 512);
		appliedThemeSnapshot.GlassSearch = snapshot.GlassSearch;
		appliedThemeSnapshot.FontFamily = NormalizeFontChoice(snapshot.FontFamily);
		return appliedThemeSnapshot;
	}

	private string ThemeNameForId(string themeId)
	{
		return (!string.IsNullOrEmpty(themeId) && _themes.ContainsKey(themeId)) ? _themes[themeId].Name : "Custom";
	}

	private static string LayoutDisplayName(string layout)
	{
		string text = NormalizeChoice(layout, new string[4] { "standard", "wide", "compact", "focus" }, "standard");
		return char.ToUpperInvariant(text[0]) + text.Substring(1);
	}

	private SkinnerSettings ReadSettings()
	{
		try
		{
			string path = SettingsPath();
			if (!File.Exists(path))
			{
				return null;
			}
			return _json.Deserialize<SkinnerSettings>(File.ReadAllText(path));
		}
		catch
		{
			return null;
		}
	}

	private void SaveSettings()
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath()));
			File.WriteAllText(SettingsPath(), _json.Serialize(CurrentSettingsSnapshot()));
		}
		catch
		{
		}
	}

	private SkinnerSettings CurrentSettingsSnapshot()
	{
		SkinnerSettings skinnerSettings = new SkinnerSettings();
		skinnerSettings.ThemeId = _themeId;
		skinnerSettings.Layout = SelectedLayout();
		skinnerSettings.BackgroundMode = SelectedBackgroundMode();
		skinnerSettings.BackgroundValue = ((_backgroundTextBox == null) ? "" : _backgroundTextBox.Text.Trim());
		skinnerSettings.Port = SelectedPort();
		skinnerSettings.CustomBg = _customTheme.Bg;
		skinnerSettings.CustomPanel = _customTheme.Panel;
		skinnerSettings.CustomInput = _customTheme.Input;
		skinnerSettings.CustomText = _customTheme.Text;
		skinnerSettings.CustomAccent = _customTheme.Accent;
		skinnerSettings.CustomBorder = _customTheme.Border;
		skinnerSettings.CustomUser = _customTheme.User;
		skinnerSettings.Transparency = SelectedTransparency();
		skinnerSettings.PanelImage = SelectedPanelImageMode() != "off";
		skinnerSettings.PanelImageMode = SelectedPanelImageMode();
		skinnerSettings.PanelImageValue = ((_panelImageTextBox == null) ? "" : _panelImageTextBox.Text.Trim());
		skinnerSettings.GlassSearch = SelectedGlassSearch();
		skinnerSettings.FontFamily = SelectedFontFamily();
		skinnerSettings.ManualChatGptExePath = _manualChatGptExePath;
		skinnerSettings.ThemeFilePath = _currentThemeFilePath;
		skinnerSettings.ActiveThemeEnabled = _activeThemeEnabled && _activeTheme != null;
		skinnerSettings.ActiveTheme = (_activeThemeEnabled ? _activeTheme : null);
		return skinnerSettings;
	}

	private AppliedThemeSnapshot CaptureCurrentAppliedTheme(Theme theme)
	{
		return SanitizeAppliedThemeSnapshot(new AppliedThemeSnapshot
		{
			ThemeId = _themeId,
			ThemeName = ((theme == null) ? ThemeNameForId(_themeId) : theme.Name),
			Layout = SelectedLayout(),
			BackgroundMode = SelectedBackgroundMode(),
			BackgroundValue = ((_backgroundTextBox == null) ? "" : _backgroundTextBox.Text.Trim()),
			Port = SelectedPort(),
			CustomBg = _customTheme.Bg,
			CustomPanel = _customTheme.Panel,
			CustomInput = _customTheme.Input,
			CustomText = _customTheme.Text,
			CustomAccent = _customTheme.Accent,
			CustomBorder = _customTheme.Border,
			CustomUser = _customTheme.User,
			Transparency = SelectedTransparency(),
			PanelImage = (SelectedPanelImageMode() != "off"),
			PanelImageMode = SelectedPanelImageMode(),
			PanelImageValue = ((_panelImageTextBox == null) ? "" : _panelImageTextBox.Text.Trim()),
			GlassSearch = SelectedGlassSearch(),
			FontFamily = SelectedFontFamily()
		});
	}

	private static string SettingsPath()
	{
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChatGPTDesktopSkinner", "settings.json");
	}

	private void BuildUi()
	{
		Text = "GPT Themes";
		LoadWindowIcon();
		base.StartPosition = FormStartPosition.CenterScreen;
		MinimumSize = new Size(1106, 414);
		base.Size = StartupSizeWithinWorkingArea();
		base.TopMost = false;
		base.ShowInTaskbar = true;
		base.Opacity = 1.0;
		BackColor = AppleBg;
		ForeColor = AppleText;
		Font = new Font("Segoe UI", 9.25f);
		base.FormBorderStyle = (UseNativeWindowChrome ? FormBorderStyle.Sizable : FormBorderStyle.None);
		base.Padding = (UseNativeWindowChrome ? new Padding(0) : new Padding(2));
		ResizeAwareTableLayoutPanel resizeAwareTableLayoutPanel = new ResizeAwareTableLayoutPanel();
		resizeAwareTableLayoutPanel.ResizeHitTest = (UseNativeWindowChrome ? null : new Func<Point, IntPtr>(ResolveResizeHitTest));
		resizeAwareTableLayoutPanel.Dock = DockStyle.Fill;
		resizeAwareTableLayoutPanel.ColumnCount = 1;
		resizeAwareTableLayoutPanel.RowCount = 2;
		resizeAwareTableLayoutPanel.Padding = new Padding(0);
		resizeAwareTableLayoutPanel.BackColor = AppleBg;
		resizeAwareTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
		resizeAwareTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		base.Controls.Add(resizeAwareTableLayoutPanel);
		Control control = BuildTitleBar();
		resizeAwareTableLayoutPanel.Controls.Add(control, 0, 0);
		RoundedPanel roundedPanel = new RoundedPanel();
		roundedPanel.Dock = DockStyle.Fill;
		roundedPanel.Margin = new Padding(0);
		roundedPanel.Padding = new Padding(4, 0, 4, 4);
		roundedPanel.Radius = 0;
		roundedPanel.BackColor = AppleBg;
		roundedPanel.BorderColor = Color.Transparent;
		roundedPanel.GradientTop = Color.Empty;
		roundedPanel.GradientBottom = Color.Empty;
		resizeAwareTableLayoutPanel.Controls.Add(roundedPanel, 0, 1);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.ColumnCount = 3;
		tableLayoutPanel.RowCount = 1;
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 357f));
		tableLayoutPanel.BackColor = AppleCard;
		roundedPanel.Controls.Add(tableLayoutPanel);
		Panel panel = new Panel();
		panel.Dock = DockStyle.Fill;
		panel.Margin = new Padding(0, 0, 5, 0);
		panel.Padding = new Padding(6, 3, 0, 0);
		panel.BackColor = Color.Transparent;
		tableLayoutPanel.Controls.Add(panel, 0, 0);
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel();
		tableLayoutPanel2.Dock = DockStyle.Fill;
		tableLayoutPanel2.BackColor = Color.Transparent;
		tableLayoutPanel2.ColumnCount = 1;
		tableLayoutPanel2.RowCount = 2;
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		panel.Controls.Add(tableLayoutPanel2);
		Label control2 = SectionHeaderLabel("THEMES");
		tableLayoutPanel2.Controls.Add(control2, 0, 0);
		_themePanel = new HiddenScrollFlowLayoutPanel();
		_themePanel.Dock = DockStyle.Fill;
		_themePanel.AutoScroll = true;
		_themePanel.FlowDirection = FlowDirection.LeftToRight;
		_themePanel.WrapContents = true;
		_themePanel.BackColor = Color.Transparent;
		_themePanel.Margin = new Padding(0, 0, 0, 0);
		_themePanel.Padding = new Padding(0);
		_themePanel.HorizontalScroll.Enabled = false;
		_themePanel.HorizontalScroll.Visible = false;
		_themePanel.Resize += delegate
		{
			if (!_isLiveResizing)
			{
				ResizeThemeButtons();
			}
		};
		tableLayoutPanel2.Controls.Add(_themePanel, 0, 1);
		string[] array = new string[43]
		{
			"cupertino", "plum", "sakura", "bubblegum", "cottoncandy", "rosequartz", "lavender", "lilacdream", "orchid", "raspberry",
			"cherrycola", "watermelon", "seafoam", "mintchip", "jade", "matcha", "tropical", "lagoon", "aurora", "icepop",
			"frostbite", "blueberry", "midnight", "starlight", "galaxy", "neonnoir", "cyberlime", "prism", "sunset", "peachfizz",
			"honeycomb", "solarflare", "ember", "moonstone", "graphite", "daylight", "pearl", "cloudberry", "mintlight", "skyglass",
			"lilacmist", "morning", "custom"
		};
		foreach (string id in array)
		{
			AddThemeButton(id);
		}
		Panel center = new Panel();
		center.Dock = DockStyle.Fill;
		center.Margin = new Padding(0, 0, 6, 0);
		center.Padding = new Padding(0);
		center.BackColor = Color.Transparent;
		tableLayoutPanel.Controls.Add(center, 1, 0);
		Panel settingsCard = new Panel();
		settingsCard.Dock = DockStyle.None;
		settingsCard.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
		settingsCard.Padding = new Padding(0);
		settingsCard.BackColor = Color.Transparent;
		center.Controls.Add(settingsCard);
		center.Resize += delegate
		{
			settingsCard.SetBounds(-10, 0, center.Width + 10, center.Height);
		};
		settingsCard.SetBounds(-10, 0, center.Width + 10, center.Height);
		_settingsScrollHost = new HiddenScrollPanel();
		_settingsScrollHost.Dock = DockStyle.Fill;
		_settingsScrollHost.AutoScroll = true;
		_settingsScrollHost.BackColor = Color.Transparent;
		_settingsScrollHost.Padding = new Padding(0);
		settingsCard.Controls.Add(_settingsScrollHost);
		TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel();
		tableLayoutPanel3.Dock = DockStyle.Top;
		tableLayoutPanel3.AutoSize = true;
		tableLayoutPanel3.AutoSizeMode = AutoSizeMode.GrowAndShrink;
		tableLayoutPanel3.RowCount = 4;
		tableLayoutPanel3.ColumnCount = 1;
		tableLayoutPanel3.BackColor = Color.Transparent;
		_settingsStack = tableLayoutPanel3;
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Absolute, 180f));
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Absolute, 76f));
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Absolute, 96f));
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));
		_settingsScrollHost.Controls.Add(tableLayoutPanel3);
		tableLayoutPanel3.Controls.Add(BuildLayoutPanel(), 0, 0);
		tableLayoutPanel3.Controls.Add(BuildCustomColorPanel(), 0, 1);
		tableLayoutPanel3.Controls.Add(BuildConnectionPanel(), 0, 2);
		TableLayoutPanel tableLayoutPanel4 = new TableLayoutPanel();
		tableLayoutPanel4.Dock = DockStyle.Fill;
		tableLayoutPanel4.BackColor = Color.Transparent;
		tableLayoutPanel4.Margin = new Padding(0);
		tableLayoutPanel4.Padding = new Padding(18, 0, 6, 0);
		tableLayoutPanel4.ColumnCount = 1;
		tableLayoutPanel4.RowCount = 3;
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 286f));
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 78f));
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.Controls.Add(tableLayoutPanel4, 2, 0);
		_previewPanel = BuildPreviewPanel();
		tableLayoutPanel4.Controls.Add(_previewPanel, 0, 0);
		tableLayoutPanel4.Controls.Add(BuildActionsPanel(), 0, 1);
		Panel panel2 = new Panel();
		panel2.Dock = DockStyle.Fill;
		panel2.BackColor = Color.Transparent;
		tableLayoutPanel4.Controls.Add(panel2, 0, 2);
		_uiBuilt = true;
	}

	private Size StartupSizeWithinWorkingArea()
	{
		Rectangle rectangle = ((Screen.PrimaryScreen == null) ? new Rectangle(0, 0, 1380, 720) : Screen.PrimaryScreen.WorkingArea);
		int num = Math.Min(1106, Math.Max(MinimumSize.Width, rectangle.Width - 16));
		int num2 = Math.Min(429, Math.Max(MinimumSize.Height, rectangle.Height - 16));
		return new Size(num, num2);
	}

	private Control BuildTitleBar()
	{
		Panel bar = new Panel();
		bar.Dock = DockStyle.Fill;
		bar.BackColor = Color.Transparent;
		bar.MouseDown += DragWindow;
		bar.DoubleClick += delegate
		{
			ToggleWindowMaximized();
		};
		if (_programLogo != null)
		{
			PictureBox pictureBox = new PictureBox();
			pictureBox.SetBounds(8, 3, 30, 30);
			pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
			pictureBox.Image = _programLogo;
			pictureBox.BackColor = Color.Transparent;
			pictureBox.Margin = new Padding(0);
			pictureBox.MouseDown += DragWindow;
			pictureBox.DoubleClick += delegate
			{
				ToggleWindowMaximized();
			};
			bar.Controls.Add(pictureBox);
		}
		else
		{
			Label label = new Label();
			label.SetBounds(8, 3, 30, 30);
			label.Text = "GPT";
			label.Font = new Font(Font.FontFamily, 7.5f, FontStyle.Bold);
			label.ForeColor = Color.White;
			label.BackColor = Color.Transparent;
			label.TextAlign = ContentAlignment.MiddleCenter;
			label.MouseDown += DragWindow;
			label.DoubleClick += delegate
			{
				ToggleWindowMaximized();
			};
			bar.Controls.Add(label);
		}
		_titleLabel = Label("GPT Themes");
		_titleLabel.SetBounds(40, 0, 110, 34);
		_titleLabel.Font = new Font(Font.FontFamily, 12.5f, FontStyle.Bold);
		_titleLabel.ForeColor = AppleText;
		_titleLabel.TextAlign = ContentAlignment.MiddleLeft;
		_titleLabel.MouseDown += DragWindow;
		_titleLabel.DoubleClick += delegate
		{
			ToggleWindowMaximized();
		};
		bar.Controls.Add(_titleLabel);
		_menuStrip = BuildMenuStrip();
		_menuStrip.MouseDown += DragMenuStripEmptyArea;
		bar.Controls.Add(_menuStrip);
		base.MainMenuStrip = _menuStrip;
		if (UseNativeWindowChrome)
		{
			bar.Resize += delegate
			{
				LayoutTitleBar(bar, null, null, null);
			};
			LayoutTitleBar(bar, null, null, null);
		}
		else
		{
			Button closeButton = WindowButton(ButtonIconKind.WindowClose);
			Button maximizeButton = WindowButton(ButtonIconKind.WindowMaximize);
			Button minimizeButton = WindowButton(ButtonIconKind.WindowMinimize);
			closeButton.Click += delegate
			{
				Close();
			};
			maximizeButton.Click += delegate
			{
				ToggleWindowMaximized();
			};
			minimizeButton.Click += delegate
			{
				base.WindowState = FormWindowState.Minimized;
			};
			bar.Controls.Add(closeButton);
			bar.Controls.Add(maximizeButton);
			bar.Controls.Add(minimizeButton);
			bar.Resize += delegate
			{
				LayoutTitleBar(bar, minimizeButton, maximizeButton, closeButton);
			};
			LayoutTitleBar(bar, minimizeButton, maximizeButton, closeButton);
		}
		return bar;
	}

	private MenuStrip BuildMenuStrip()
	{
		MenuStrip menuStrip = new MenuStrip();
		menuStrip.Dock = DockStyle.None;
		menuStrip.AutoSize = false;
		menuStrip.BackColor = AppleBg;
		menuStrip.ForeColor = AppleText;
		menuStrip.Font = new Font(Font.FontFamily, 9.1f, FontStyle.Regular);
		menuStrip.Padding = new Padding(0);
		menuStrip.Margin = new Padding(0);
		menuStrip.GripStyle = ToolStripGripStyle.Hidden;
		menuStrip.RenderMode = ToolStripRenderMode.Professional;
		menuStrip.Renderer = new ThemedMenuRenderer(AppleBg, AppleBg, AppleText, AppleMuted, AppleCard, AppleBorder);
		ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem("File");
		toolStripMenuItem.DropDownItems.Add(MenuItem("New Theme", Keys.N | Keys.Control, delegate
		{
			NewTheme();
		}));
		toolStripMenuItem.DropDownItems.Add(MenuItem("Save Theme", Keys.S | Keys.Control, delegate
		{
			SaveTheme(saveAs: false);
		}));
		toolStripMenuItem.DropDownItems.Add(MenuItem("Save Theme As...", Keys.S | Keys.Shift | Keys.Control, delegate
		{
			SaveTheme(saveAs: true);
		}));
		toolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
		toolStripMenuItem.DropDownItems.Add(MenuItem("Import Theme", Keys.O | Keys.Control, delegate
		{
			ImportTheme();
		}));
		toolStripMenuItem.DropDownItems.Add(MenuItem("Export Theme", Keys.None, delegate
		{
			ExportTheme();
		}));
		toolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
		toolStripMenuItem.DropDownItems.Add(MenuItem("Exit", Keys.None, delegate
		{
			Close();
		}));
		ToolStripMenuItem toolStripMenuItem2 = new ToolStripMenuItem("Help");
		toolStripMenuItem2.DropDownItems.Add(MenuItem("Help Documentation", Keys.F1, delegate
		{
			OpenHelpDocumentation();
		}));
		toolStripMenuItem2.DropDownItems.Add(MenuItem("Project Information", Keys.None, delegate
		{
			ShowProjectInformation();
		}));
		toolStripMenuItem2.DropDownItems.Add(MenuItem("Visit piratemoo.com", Keys.None, delegate
		{
			OpenUrl("https://piratemoo.com");
		}));
		toolStripMenuItem2.DropDownItems.Add(new ToolStripSeparator());
		toolStripMenuItem2.DropDownItems.Add(MenuItem("About", Keys.None, delegate
		{
			ShowAboutDialog();
		}));
		menuStrip.Items.Add(toolStripMenuItem);
		menuStrip.Items.Add(toolStripMenuItem2);
		StyleMenuItems(menuStrip.Items, topLevel: true);
		return menuStrip;
	}

	private ToolStripMenuItem MenuItem(string text, Keys shortcut, EventHandler click)
	{
		ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem(text);
		toolStripMenuItem.ShortcutKeys = shortcut;
		toolStripMenuItem.ShowShortcutKeys = shortcut != Keys.None;
		toolStripMenuItem.Click += click;
		toolStripMenuItem.BackColor = AppleBg;
		toolStripMenuItem.ForeColor = AppleText;
		return toolStripMenuItem;
	}

	private void StyleMenuItems(ToolStripItemCollection items)
	{
		StyleMenuItems(items, topLevel: false);
	}

	private void StyleMenuItems(ToolStripItemCollection items, bool topLevel)
	{
		if (items == null)
		{
			return;
		}
		Color appleText = AppleText;
		Color appleMuted = AppleMuted;
		foreach (ToolStripItem item in items)
		{
			item.BackColor = AppleBg;
			item.ForeColor = (item.Enabled ? appleText : appleMuted);
			if (_menuStrip != null)
			{
				item.Font = _menuStrip.Font;
			}
			if (item is ToolStripMenuItem toolStripMenuItem)
			{
				toolStripMenuItem.DropDown.BackColor = AppleBg;
				toolStripMenuItem.DropDown.ForeColor = appleText;
				StyleMenuItems(toolStripMenuItem.DropDownItems, topLevel: false);
			}
			if (item is ToolStripSeparator toolStripSeparator)
			{
				toolStripSeparator.BackColor = AppleBg;
				toolStripSeparator.ForeColor = Blend(appleText, AppleBg, 0.45);
			}
		}
	}

	private void LoadWindowIcon()
	{
		try
		{
			string text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gpt-themes.ico");
			if (File.Exists(text))
			{
				base.Icon = new Icon(text);
				return;
			}
			Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
			if (icon != null)
			{
				base.Icon = icon;
			}
		}
		catch
		{
		}
	}

	private Image LoadProgramLogo()
	{
		try
		{
			string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
			if (!File.Exists(path))
			{
				string text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "logo.png");
				if (File.Exists(text))
				{
					path = text;
				}
			}
			if (!File.Exists(path))
			{
				return null;
			}
			using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using Image original = Image.FromStream(stream);
			return new Bitmap(original);
		}
		catch
		{
			return null;
		}
	}

	private Button WindowButton(ButtonIconKind icon)
	{
		Button button = Button("");
		if (button is RoundedButton roundedButton)
		{
			roundedButton.Radius = 1;
			roundedButton.IconKind = icon;
		}
		button.Font = new Font(Font.FontFamily, 9.5f, FontStyle.Regular);
		button.FlatAppearance.BorderSize = 0;
		button.BackColor = AppleBg;
		button.ForeColor = AppleText;
		button.FlatAppearance.MouseOverBackColor = ((icon == ButtonIconKind.WindowClose) ? Color.FromArgb(196, 54, 72) : Color.FromArgb(24, 28, 40));
		button.FlatAppearance.MouseDownBackColor = ((icon == ButtonIconKind.WindowClose) ? Color.FromArgb(154, 37, 52) : Color.FromArgb(34, 39, 54));
		button.Width = 46;
		button.Height = 34;
		button.TabStop = false;
		return button;
	}

	private void LayoutTitleBar(Control bar, Button minimizeButton, Button maximizeButton, Button closeButton)
	{
		int left = bar.Width;
		if (closeButton != null && maximizeButton != null && minimizeButton != null)
		{
			int num = 0;
			int num2 = 46;
			int num3 = 34;
			int num4 = 0;
			int num5 = Math.Max(0, bar.Width - num2);
			closeButton.SetBounds(num5, num, num2, num3);
			maximizeButton.SetBounds(num5 - num2 - num4, num, num2, num3);
			minimizeButton.SetBounds(num5 - (num2 + num4) * 2, num, num2, num3);
			left = minimizeButton.Left;
		}
		if (_titleLabel != null)
		{
			_titleLabel.SetBounds(40, 0, 110, 34);
		}
		if (_menuStrip != null)
		{
			int num6 = 152;
			int num7 = (UseNativeWindowChrome ? 96 : Math.Max(120, left - 8 - num6));
			_menuStrip.SetBounds(num6, 3, num7, 28);
		}
	}

	private void ToggleWindowMaximized()
	{
		if (base.WindowState == FormWindowState.Maximized)
		{
			base.WindowState = FormWindowState.Normal;
			return;
		}
		base.MaximizedBounds = Screen.FromHandle(base.Handle).WorkingArea;
		base.WindowState = FormWindowState.Maximized;
	}

	private void DragWindow(object sender, MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left)
		{
			ReleaseCapture();
			SendMessage(base.Handle, 161, 2, 0);
		}
	}

	private void DragMenuStripEmptyArea(object sender, MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left && _menuStrip != null && _menuStrip.GetItemAt(e.Location) == null)
		{
			DragWindow(sender, e);
		}
	}

	private Control BuildLayoutPanel()
	{
		Panel panel = CardPanel();
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.Padding = new Padding(14, 4, 14, 4);
		tableLayoutPanel.BackColor = Color.Transparent;
		tableLayoutPanel.ColumnCount = 1;
		tableLayoutPanel.RowCount = 3;
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 68f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		panel.Controls.Add(tableLayoutPanel);
		Label control = SectionHeaderLabel("APPEARANCE");
		tableLayoutPanel.Controls.Add(control, 0, 0);
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel();
		tableLayoutPanel2.Dock = DockStyle.Fill;
		tableLayoutPanel2.BackColor = Color.Transparent;
		tableLayoutPanel2.ColumnCount = 2;
		tableLayoutPanel2.RowCount = 1;
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 244f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.Margin = new Padding(0, 0, 0, 0);
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 1);
		TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel();
		tableLayoutPanel3.Dock = DockStyle.Fill;
		tableLayoutPanel3.BackColor = Color.Transparent;
		tableLayoutPanel3.ColumnCount = 2;
		tableLayoutPanel3.RowCount = 3;
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86f));
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel3.Margin = new Padding(0, 0, 14, 0);
		tableLayoutPanel2.Controls.Add(tableLayoutPanel3, 0, 0);
		Label label = Label("Layout");
		label.Dock = DockStyle.Fill;
		label.TextAlign = ContentAlignment.MiddleLeft;
		tableLayoutPanel3.Controls.Add(label, 0, 0);
		_layoutCombo = new ThemedComboBox();
		_layoutCombo.DropDownStyle = ComboBoxStyle.DropDownList;
		StyleCombo(_layoutCombo);
		_layoutCombo.Items.AddRange(new object[4] { "Standard", "Wide", "Compact", "Focus" });
		_layoutCombo.Dock = DockStyle.Fill;
		_layoutCombo.Margin = new Padding(0, 3, 0, 3);
		_layoutCombo.SelectedIndexChanged += delegate
		{
			RefreshSkinnedPreview();
			HandleThemeStateChanged(queueLiveApply: false);
		};
		tableLayoutPanel3.Controls.Add(_layoutCombo, 1, 0);
		Label label2 = Label("Font");
		label2.Dock = DockStyle.Fill;
		label2.TextAlign = ContentAlignment.MiddleLeft;
		tableLayoutPanel3.Controls.Add(label2, 0, 1);
		_fontCombo = new ThemedComboBox();
		_fontCombo.DropDownStyle = ComboBoxStyle.DropDownList;
		StyleCombo(_fontCombo);
		_fontCombo.Items.AddRange(new object[11]
		{
			"Default", "System UI", "Segoe UI", "Inter", "Arial", "Verdana", "Georgia", "Courier New", "Comic Sans MS", "Atkinson Hyperlegible",
			"OpenDyslexic"
		});
		_fontCombo.Dock = DockStyle.Fill;
		_fontCombo.Margin = new Padding(0, 3, 0, 3);
		_fontCombo.SelectedIndexChanged += delegate
		{
			RefreshSkinnedPreview();
			HandleThemeStateChanged(queueLiveApply: false);
		};
		tableLayoutPanel3.Controls.Add(_fontCombo, 1, 1);
		TableLayoutPanel tableLayoutPanel4 = new TableLayoutPanel();
		tableLayoutPanel4.Dock = DockStyle.Fill;
		tableLayoutPanel4.BackColor = Color.Transparent;
		tableLayoutPanel4.ColumnCount = 1;
		tableLayoutPanel4.RowCount = 3;
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 14f));
		tableLayoutPanel4.Margin = new Padding(0, 0, 0, 0);
		tableLayoutPanel2.Controls.Add(tableLayoutPanel4, 1, 0);
		TableLayoutPanel tableLayoutPanel5 = new TableLayoutPanel();
		tableLayoutPanel5.Dock = DockStyle.Fill;
		tableLayoutPanel5.BackColor = Color.Transparent;
		tableLayoutPanel5.Margin = new Padding(0);
		tableLayoutPanel5.Padding = new Padding(0);
		tableLayoutPanel5.ColumnCount = 3;
		tableLayoutPanel5.RowCount = 1;
		tableLayoutPanel5.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70f));
		tableLayoutPanel5.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160f));
		tableLayoutPanel5.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel4.Controls.Add(tableLayoutPanel5, 0, 0);
		Label label3 = Label("Glass");
		label3.Dock = DockStyle.Fill;
		label3.TextAlign = ContentAlignment.MiddleLeft;
		tableLayoutPanel5.Controls.Add(label3, 0, 0);
		_glassSearchCheckBox = new ThemedCheckBox();
		_glassSearchCheckBox.Text = "Glass Search";
		_glassSearchCheckBox.Checked = _glassSearch;
		_glassSearchCheckBox.Dock = DockStyle.Fill;
		_glassSearchCheckBox.ForeColor = AppleText;
		_glassSearchCheckBox.BackColor = Color.Transparent;
		_glassSearchCheckBox.Font = Font;
		_glassSearchCheckBox.Margin = new Padding(0, 0, 0, 0);
		if (_glassSearchCheckBox is ThemedCheckBox themedCheckBox)
		{
			themedCheckBox.BoxBorderColor = Color.Transparent;
			themedCheckBox.BoxBackColor = Color.FromArgb(14, 17, 27);
			themedCheckBox.CheckedBackColor = ApplePink;
			themedCheckBox.CheckMarkColor = Color.White;
			themedCheckBox.TextColor = AppleText;
		}
		_glassSearchCheckBox.CheckedChanged += delegate
		{
			_glassSearch = _glassSearchCheckBox.Checked;
			RefreshSkinnedPreview();
			HandleThemeStateChanged(queueLiveApply: false);
		};
		tableLayoutPanel5.Controls.Add(_glassSearchCheckBox, 1, 0);
		TableLayoutPanel tableLayoutPanel6 = new TableLayoutPanel();
		tableLayoutPanel6.Dock = DockStyle.Fill;
		tableLayoutPanel6.BackColor = Color.Transparent;
		tableLayoutPanel6.Margin = new Padding(0);
		tableLayoutPanel6.Padding = new Padding(0);
		tableLayoutPanel6.ColumnCount = 3;
		tableLayoutPanel6.RowCount = 1;
		tableLayoutPanel6.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240f));
		tableLayoutPanel6.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68f));
		tableLayoutPanel6.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel4.Controls.Add(tableLayoutPanel6, 0, 1);
		_transparencyTrackBar = new ThemedSlider();
		_transparencyTrackBar.Minimum = 0;
		_transparencyTrackBar.Maximum = 75;
		_transparencyTrackBar.Value = _transparency;
		_transparencyTrackBar.Dock = DockStyle.Fill;
		_transparencyTrackBar.Margin = new Padding(0, 0, 10, 0);
		_transparencyTrackBar.BackColor = Color.Transparent;
		_transparencyTrackBar.ActiveColor = Color.FromArgb(255, 126, 188);
		_transparencyTrackBar.ThumbColor = Color.FromArgb(255, 126, 188);
		_transparencyTrackBar.TrackColor = Color.FromArgb(72, 78, 96);
		_transparencyTrackBar.ValueChanged += delegate
		{
			_transparency = _transparencyTrackBar.Value;
			UpdateTransparencyLabel();
			QueueSkinnedPreviewRefresh();
			HandleThemeStateChanged(queueLiveApply: false);
		};
		tableLayoutPanel6.Controls.Add(_transparencyTrackBar, 0, 0);
		_transparencyValueLabel = Label("");
		StyleMuted(_transparencyValueLabel);
		_transparencyValueLabel.Dock = DockStyle.Fill;
		_transparencyValueLabel.TextAlign = ContentAlignment.MiddleLeft;
		tableLayoutPanel6.Controls.Add(_transparencyValueLabel, 1, 0);
		TableLayoutPanel tableLayoutPanel7 = new TableLayoutPanel();
		tableLayoutPanel7.Dock = DockStyle.Fill;
		tableLayoutPanel7.BackColor = Color.Transparent;
		tableLayoutPanel7.Margin = new Padding(0);
		tableLayoutPanel7.Padding = new Padding(0);
		tableLayoutPanel7.ColumnCount = 3;
		tableLayoutPanel7.RowCount = 1;
		tableLayoutPanel7.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240f));
		tableLayoutPanel7.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68f));
		tableLayoutPanel7.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel4.Controls.Add(tableLayoutPanel7, 0, 2);
		TableLayoutPanel tableLayoutPanel8 = new TableLayoutPanel();
		tableLayoutPanel8.Dock = DockStyle.Fill;
		tableLayoutPanel8.BackColor = Color.Transparent;
		tableLayoutPanel8.ColumnCount = 2;
		tableLayoutPanel8.RowCount = 1;
		tableLayoutPanel8.Margin = new Padding(0, 0, 10, 0);
		tableLayoutPanel8.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel8.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel7.Controls.Add(tableLayoutPanel8, 0, 0);
		Label label4 = Label("More Opaque");
		StyleMuted(label4);
		label4.Dock = DockStyle.Fill;
		label4.Font = new Font(Font.FontFamily, 7.8f);
		label4.TextAlign = ContentAlignment.MiddleLeft;
		tableLayoutPanel8.Controls.Add(label4, 0, 0);
		Label label5 = Label("More Glass");
		StyleMuted(label5);
		label5.Dock = DockStyle.Fill;
		label5.Font = new Font(Font.FontFamily, 7.8f);
		label5.TextAlign = ContentAlignment.MiddleRight;
		tableLayoutPanel8.Controls.Add(label5, 1, 0);
		RoundedPanel roundedPanel = new RoundedPanel();
		roundedPanel.Dock = DockStyle.Fill;
		roundedPanel.Margin = new Padding(0, 0, 0, 0);
		roundedPanel.Padding = new Padding(0);
		roundedPanel.Radius = 12;
		roundedPanel.BackColor = AppleCard;
		roundedPanel.BorderColor = Color.Transparent;
		roundedPanel.GradientTop = Color.Empty;
		roundedPanel.GradientBottom = Color.Empty;
		tableLayoutPanel.Controls.Add(roundedPanel, 0, 2);
		TableLayoutPanel tableLayoutPanel9 = new TableLayoutPanel();
		tableLayoutPanel9.Dock = DockStyle.Fill;
		tableLayoutPanel9.BackColor = Color.Transparent;
		tableLayoutPanel9.ColumnCount = 7;
		tableLayoutPanel9.RowCount = 5;
		tableLayoutPanel9.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86f));
		tableLayoutPanel9.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144f));
		tableLayoutPanel9.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44f));
		tableLayoutPanel9.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f));
		tableLayoutPanel9.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60f));
		tableLayoutPanel9.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46f));
		tableLayoutPanel9.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel9.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
		tableLayoutPanel9.RowStyles.Add(new RowStyle(SizeType.Absolute, 2f));
		tableLayoutPanel9.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
		tableLayoutPanel9.RowStyles.Add(new RowStyle(SizeType.Absolute, 2f));
		tableLayoutPanel9.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		roundedPanel.Controls.Add(tableLayoutPanel9);
		Label label6 = Label("Background");
		label6.Dock = DockStyle.Fill;
		label6.TextAlign = ContentAlignment.MiddleLeft;
		tableLayoutPanel9.Controls.Add(label6, 0, 0);
		_backgroundModeCombo = new ThemedComboBox();
		_backgroundModeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
		StyleCombo(_backgroundModeCombo);
		_backgroundModeCombo.Items.AddRange(new object[3] { "Base Color", "Theme Pattern", "Image File" });
		_backgroundModeCombo.Dock = DockStyle.Fill;
		_backgroundModeCombo.Margin = new Padding(0, 2, 0, 2);
		_backgroundModeCombo.SelectedIndexChanged += delegate
		{
			UpdateBackgroundInputState();
			RefreshBackgroundPreview();
			RefreshSkinnedPreview();
			HandleThemeStateChanged(queueLiveApply: false);
		};
		tableLayoutPanel9.Controls.Add(_backgroundModeCombo, 1, 0);
		_backgroundPreviewPanel = new RoundedPanel();
		_backgroundPreviewPanel.Dock = DockStyle.Fill;
		_backgroundPreviewPanel.Margin = new Padding(8, 0, 8, 0);
		_backgroundPreviewPanel.Radius = 8;
		_backgroundPreviewPanel.BackColor = AppleCard;
		_backgroundPreviewPanel.BorderColor = Color.FromArgb(42, 48, 64);
		_backgroundPreviewPanel.Paint += PaintBackgroundPreviewPanel;
		tableLayoutPanel9.Controls.Add(_backgroundPreviewPanel, 2, 0);
		_backgroundTextBox = new ThemedTextBox();
		StyleTextBox(_backgroundTextBox);
		_backgroundTextBox.Dock = DockStyle.Fill;
		_backgroundTextBox.Margin = new Padding(0, 2, 10, 2);
		_backgroundTextBox.TextChanged += delegate
		{
			RefreshBackgroundPreview();
			RefreshSkinnedPreview();
			HandleThemeStateChanged(queueLiveApply: false);
		};
		tableLayoutPanel9.Controls.Add(_backgroundTextBox, 3, 0);
		_browseButton = Button("Browse");
		_browseButton.Dock = DockStyle.Fill;
		_browseButton.Margin = new Padding(0, 2, 0, 2);
		_browseButton.Click += BrowseBackground;
		tableLayoutPanel9.Controls.Add(_browseButton, 4, 0);
		_backgroundOkButton = Button("OK");
		_backgroundOkButton.Dock = DockStyle.Fill;
		_backgroundOkButton.Margin = new Padding(4, 2, 0, 2);
		_backgroundOkButton.Click += async delegate
		{
			await ApplySkinAsync(quiet: false);
		};
		tableLayoutPanel9.Controls.Add(_backgroundOkButton, 5, 0);
		Label label7 = Label("Panel");
		label7.Dock = DockStyle.Fill;
		label7.Margin = new Padding(1, 0, 0, 0);
		label7.TextAlign = ContentAlignment.MiddleLeft;
		tableLayoutPanel9.Controls.Add(label7, 0, 2);
		_panelImageModeCombo = new ThemedComboBox();
		_panelImageModeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
		StyleCombo(_panelImageModeCombo);
		_panelImageModeCombo.Items.AddRange(new object[3] { "Base Color", "Same Image", "Image File" });
		_panelImageModeCombo.Dock = DockStyle.Fill;
		_panelImageModeCombo.Margin = new Padding(0, 2, 0, 2);
		_panelImageModeCombo.SelectedIndexChanged += delegate
		{
			UpdatePanelImageInputState();
			RefreshPanelImagePreview();
			RefreshSkinnedPreview();
			HandleThemeStateChanged(queueLiveApply: false);
		};
		tableLayoutPanel9.Controls.Add(_panelImageModeCombo, 1, 2);
		_panelImagePreviewPanel = new RoundedPanel();
		_panelImagePreviewPanel.Dock = DockStyle.Fill;
		_panelImagePreviewPanel.Margin = new Padding(8, 0, 8, 0);
		_panelImagePreviewPanel.Radius = 8;
		_panelImagePreviewPanel.BackColor = AppleCard;
		_panelImagePreviewPanel.BorderColor = Color.FromArgb(42, 48, 64);
		_panelImagePreviewPanel.Paint += PaintPanelImagePreviewPanel;
		tableLayoutPanel9.Controls.Add(_panelImagePreviewPanel, 2, 2);
		_panelImageTextBox = new ThemedTextBox();
		StyleTextBox(_panelImageTextBox);
		_panelImageTextBox.Dock = DockStyle.Fill;
		_panelImageTextBox.Margin = new Padding(0, 2, 10, 2);
		_panelImageTextBox.TextChanged += delegate
		{
			RefreshPanelImagePreview();
			RefreshSkinnedPreview();
			HandleThemeStateChanged(queueLiveApply: false);
		};
		tableLayoutPanel9.Controls.Add(_panelImageTextBox, 3, 2);
		_panelImageBrowseButton = Button("Browse");
		_panelImageBrowseButton.Dock = DockStyle.Fill;
		_panelImageBrowseButton.Margin = new Padding(0, 2, 0, 2);
		_panelImageBrowseButton.Click += BrowsePanelImage;
		tableLayoutPanel9.Controls.Add(_panelImageBrowseButton, 4, 2);
		_imageOkButton = Button("OK");
		_imageOkButton.Dock = DockStyle.Fill;
		_imageOkButton.Margin = new Padding(4, 2, 0, 2);
		_imageOkButton.Click += async delegate
		{
			await ApplySkinAsync(quiet: false);
		};
		tableLayoutPanel9.Controls.Add(_imageOkButton, 5, 2);
		UpdateTransparencyLabel();
		return panel;
	}

	private void RefreshBackgroundPreview()
	{
		if (_backgroundPreviewPanel != null)
		{
			_backgroundPreviewPanel.Invalidate();
		}
		if (SelectedPanelImageMode() == "same")
		{
			RefreshPanelImagePreview();
		}
	}

	private void RefreshPanelImagePreview()
	{
		if (_panelImagePreviewPanel != null)
		{
			_panelImagePreviewPanel.Invalidate();
		}
	}

	private void RefreshSkinnedPreview()
	{
		_previewSkinEnabled = true;
		if (_previewPanel != null)
		{
			_previewPanel.Invalidate();
		}
	}

	private void QueueSkinnedPreviewRefresh()
	{
		_previewSkinEnabled = true;
		if (_previewRefreshTimer == null)
		{
			RefreshSkinnedPreview();
			return;
		}
		_previewRefreshTimer.Stop();
		_previewRefreshTimer.Start();
	}

	private void HandleThemeStateChanged(bool queueLiveApply)
	{
		if (!_loadingUiState && _uiBuilt && !base.IsDisposed && !base.Disposing)
		{
			_previewSkinEnabled = true;
			SaveSettings();
			UpdateActiveThemeLabel();
		}
	}

	private void PaintPanelImagePreviewPanel(object sender, PaintEventArgs e)
	{
		if (!(sender is Control control))
		{
			return;
		}
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		e.Graphics.Clear(UiShape.SurfaceBackColor(control, AppleCard));
		Rectangle rectangle = new Rectangle(0, 0, Math.Max(1, control.Width - 1), Math.Max(1, control.Height - 1));
		Theme theme = CurrentTheme();
		Color color = ColorFromHex(theme.Panel);
		Color b = ColorFromHex(theme.Accent);
		string text = SelectedPanelImageMode();
		using GraphicsPath graphicsPath = UiShape.RoundedRect(rectangle, 8);
		Image image = LoadPreviewPanelImage();
		if (image != null)
		{
			Region clip = e.Graphics.Clip;
			e.Graphics.SetClip(graphicsPath);
			e.Graphics.DrawImage(image, CoverRectangle(image.Size, rectangle));
			using (SolidBrush brush = new SolidBrush(Color.FromArgb(76, color)))
			{
				e.Graphics.FillRectangle(brush, rectangle);
			}
			e.Graphics.Clip = clip;
		}
		else
		{
			using LinearGradientBrush brush2 = new LinearGradientBrush(rectangle, Blend(color, b, (text == "off") ? 0.05 : 0.12), Blend(color, Color.Black, 0.1), LinearGradientMode.ForwardDiagonal);
			e.Graphics.FillPath(brush2, graphicsPath);
		}
		using LinearGradientBrush brush3 = new LinearGradientBrush(rectangle, Color.FromArgb(42, Color.White), Color.FromArgb(0, Color.White), LinearGradientMode.ForwardDiagonal);
		using Pen pen = new Pen(Color.FromArgb(58, 66, 84), 1f);
		e.Graphics.FillPath(brush3, graphicsPath);
		e.Graphics.DrawPath(pen, graphicsPath);
	}

	private void PaintBackgroundPreviewPanel(object sender, PaintEventArgs e)
	{
		if (!(sender is Control control))
		{
			return;
		}
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		e.Graphics.Clear(UiShape.SurfaceBackColor(control, AppleCard));
		Rectangle rectangle = new Rectangle(0, 0, Math.Max(1, control.Width - 1), Math.Max(1, control.Height - 1));
		Theme theme = CurrentTheme();
		Color color = ColorFromHex(theme.Bg);
		Color b = ColorFromHex(theme.Accent);
		string text = SelectedBackgroundMode();
		using GraphicsPath graphicsPath = UiShape.RoundedRect(rectangle, 8);
		if (text == "file")
		{
			Image image = LoadPreviewBackgroundImage();
			if (image != null)
			{
				Region clip = e.Graphics.Clip;
				e.Graphics.SetClip(graphicsPath);
				e.Graphics.DrawImage(image, CoverRectangle(image.Size, rectangle));
				e.Graphics.Clip = clip;
			}
			else
			{
				using LinearGradientBrush brush = new LinearGradientBrush(rectangle, Blend(color, b, 0.14), Blend(color, Color.Black, 0.1), LinearGradientMode.ForwardDiagonal);
				e.Graphics.FillPath(brush, graphicsPath);
			}
		}
		else if (text == "solid")
		{
			using SolidBrush brush2 = new SolidBrush(color);
			e.Graphics.FillPath(brush2, graphicsPath);
		}
		else
		{
			using LinearGradientBrush brush = new LinearGradientBrush(rectangle, Blend(color, b, 0.12), Blend(color, Color.Black, 0.1), LinearGradientMode.ForwardDiagonal);
			e.Graphics.FillPath(brush, graphicsPath);
		}
		using LinearGradientBrush brush3 = new LinearGradientBrush(rectangle, Color.FromArgb(58, Color.White), Color.FromArgb(0, Color.White), LinearGradientMode.ForwardDiagonal);
		using Pen pen = new Pen(Color.FromArgb(58, 66, 84), 1f);
		e.Graphics.FillPath(brush3, graphicsPath);
		e.Graphics.DrawPath(pen, graphicsPath);
	}

	private Control BuildCustomColorPanel()
	{
		Panel panel = CardPanel();
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.Padding = new Padding(104, 0, 14, 0);
		tableLayoutPanel.BackColor = Color.Transparent;
		tableLayoutPanel.ColumnCount = 1;
		tableLayoutPanel.RowCount = 1;
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		panel.Controls.Add(tableLayoutPanel);
		_baseColorButton = ColorButton("Base", () => _customTheme.Bg, delegate(string v)
		{
			_customTheme.Bg = v;
		});
		_panelColorButton = ColorButton("Panel", () => _customTheme.Panel, delegate(string v)
		{
			_customTheme.Panel = v;
		});
		_inputColorButton = ColorButton("Input", () => _customTheme.Input, delegate(string v)
		{
			_customTheme.Input = v;
		});
		_textColorButton = ColorButton("Text", () => _customTheme.Text, delegate(string v)
		{
			_customTheme.Text = v;
		});
		_accentColorButton = ColorButton("Accent", () => _customTheme.Accent, delegate(string v)
		{
			_customTheme.Accent = v;
		});
		_borderColorButton = ColorButton("Border", () => _customTheme.Border, delegate(string v)
		{
			_customTheme.Border = v;
		});
		_userColorButton = ColorButton("User", () => _customTheme.User, delegate(string v)
		{
			_customTheme.User = v;
		});
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel();
		flowLayoutPanel.Dock = DockStyle.Fill;
		flowLayoutPanel.BackColor = Color.Transparent;
		flowLayoutPanel.FlowDirection = FlowDirection.LeftToRight;
		flowLayoutPanel.WrapContents = true;
		flowLayoutPanel.AutoScroll = false;
		flowLayoutPanel.Padding = new Padding(0, 2, 0, 0);
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 0);
		Button[] array = new Button[7] { _baseColorButton, _panelColorButton, _inputColorButton, _textColorButton, _accentColorButton, _borderColorButton, _userColorButton };
		for (int num = 0; num < array.Length; num++)
		{
			array[num].Size = new Size(52, 58);
			array[num].Margin = new Padding(0, 0, 4, 0);
			array[num].Padding = new Padding(0);
			if (array[num] is RoundedButton roundedButton)
			{
				roundedButton.Radius = 8;
			}
			flowLayoutPanel.Controls.Add(array[num]);
		}
		return panel;
	}

	private Control BuildConnectionPanel()
	{
		Panel panel = CardPanel();
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.Padding = new Padding(14, 3, 14, 3);
		tableLayoutPanel.BackColor = Color.Transparent;
		tableLayoutPanel.ColumnCount = 4;
		tableLayoutPanel.RowCount = 4;
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
		panel.Controls.Add(tableLayoutPanel);
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel();
		tableLayoutPanel2.Dock = DockStyle.Fill;
		tableLayoutPanel2.BackColor = Color.Transparent;
		tableLayoutPanel2.Margin = new Padding(0);
		tableLayoutPanel2.Padding = new Padding(0);
		tableLayoutPanel2.ColumnCount = 3;
		tableLayoutPanel2.RowCount = 1;
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 0);
		tableLayoutPanel.SetColumnSpan(tableLayoutPanel2, 4);
		Label label = Label("CONNECTION  -");
		label.Dock = DockStyle.Fill;
		label.Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold);
		label.TextAlign = ContentAlignment.MiddleLeft;
		label.Margin = new Padding(0);
		tableLayoutPanel2.Controls.Add(label, 0, 0);
		_chatGptStatusLabel = Label("ChatGPT Not Checked");
		_chatGptStatusLabel.Dock = DockStyle.Fill;
		_chatGptStatusLabel.Font = new Font(Font.FontFamily, 9.1f, FontStyle.Bold);
		_chatGptStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
		_chatGptStatusLabel.Margin = new Padding(0);
		tableLayoutPanel2.Controls.Add(_chatGptStatusLabel, 1, 0);
		_chatGptDiagnosticsLabel = Label("Detection checks running process, per-user installs, Program Files, Start Menu, and Windows app package.");
		StyleMuted(_chatGptDiagnosticsLabel);
		_chatGptDiagnosticsLabel.Dock = DockStyle.Fill;
		_chatGptDiagnosticsLabel.TextAlign = ContentAlignment.MiddleLeft;
		_chatGptDiagnosticsLabel.AutoEllipsis = true;
		_chatGptDiagnosticsLabel.Margin = new Padding(4, 0, 0, 0);
		tableLayoutPanel2.Controls.Add(_chatGptDiagnosticsLabel, 2, 0);
		TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel();
		tableLayoutPanel3.Dock = DockStyle.Fill;
		tableLayoutPanel3.BackColor = Color.Transparent;
		tableLayoutPanel3.ColumnCount = 2;
		tableLayoutPanel3.RowCount = 1;
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44f));
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel3.Margin = new Padding(0, 1, 8, 1);
		tableLayoutPanel.Controls.Add(tableLayoutPanel3, 0, 2);
		Label label2 = Label("Port");
		label2.Dock = DockStyle.Fill;
		label2.TextAlign = ContentAlignment.MiddleLeft;
		tableLayoutPanel3.Controls.Add(label2, 0, 0);
		_portTextBox = new CenteredTextInput();
		_portTextBox.Text = "9322";
		_portTextBox.MaxLength = 5;
		_portTextBox.SelectAllOnFocus = true;
		_portTextBox.CharacterFilter = char.IsDigit;
		_portTextBox.TextAlignment = HorizontalAlignment.Center;
		_portTextBox.Dock = DockStyle.Fill;
		_portTextBox.Margin = new Padding(0, 0, 0, 0);
		_portTextBox.Font = Font;
		_portTextBox.TextChanged += delegate
		{
			HandleThemeStateChanged(queueLiveApply: false);
		};
		tableLayoutPanel3.Controls.Add(_portTextBox, 1, 0);
		Label label3 = Label("Use Relaunch with Port if Apply cannot connect.");
		StyleMuted(label3);
		label3.Dock = DockStyle.Fill;
		label3.TextAlign = ContentAlignment.MiddleLeft;
		label3.AutoEllipsis = true;
		label3.Margin = new Padding(0, 0, 0, 0);
		tableLayoutPanel.Controls.Add(label3, 1, 2);
		tableLayoutPanel.SetColumnSpan(label3, 3);
		_testPortButton = Button("Test Port");
		_testPortButton.Dock = DockStyle.Top;
		_testPortButton.Height = 22;
		_testPortButton.Margin = new Padding(0, 5, 8, 0);
		_testPortButton.Click += async delegate
		{
			await TestPortAsync(quiet: false);
		};
		tableLayoutPanel.Controls.Add(_testPortButton, 0, 3);
		_relaunchButton = Button("Relaunch with Port");
		_relaunchButton.Dock = DockStyle.Top;
		_relaunchButton.Height = 22;
		_relaunchButton.Margin = new Padding(0, 5, 8, 0);
		_relaunchButton.Click += async delegate
		{
			await RelaunchChatGptWithPortAsync();
		};
		tableLayoutPanel.Controls.Add(_relaunchButton, 1, 3);
		_chooseChatGptButton = Button("Choose ChatGPT.exe");
		_chooseChatGptButton.Dock = DockStyle.Top;
		_chooseChatGptButton.Height = 22;
		_chooseChatGptButton.Margin = new Padding(0, 5, 0, 0);
		_chooseChatGptButton.Click += ChooseChatGptExe;
		tableLayoutPanel.Controls.Add(_chooseChatGptButton, 2, 3);
		return panel;
	}

	private RoundedPanel BuildPreviewPanel()
	{
		RoundedPanel roundedPanel = new RoundedPanel();
		roundedPanel.Dock = DockStyle.Fill;
		roundedPanel.Height = 380;
		roundedPanel.Radius = 1;
		roundedPanel.Margin = new Padding(0);
		roundedPanel.BackColor = Color.Transparent;
		roundedPanel.BorderColor = Color.Transparent;
		roundedPanel.GradientTop = Color.Empty;
		roundedPanel.GradientBottom = Color.Empty;
		roundedPanel.Paint += PaintPreviewPanel;
		return roundedPanel;
	}

	private void PaintPreviewPanel(object sender, PaintEventArgs e)
	{
		if (!(sender is Control control))
		{
			return;
		}
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		e.Graphics.Clear(UiShape.SurfaceBackColor(control, AppleCard));
		if (_isLiveResizing)
		{
			PaintFastPreviewPanel(control, e.Graphics);
			return;
		}
		PreviewThemeTokens previewThemeTokens = CurrentPreviewTokens();
		Color bg = previewThemeTokens.Bg;
		Color panel = previewThemeTokens.Panel;
		Color sidebar = previewThemeTokens.Sidebar;
		Color user = previewThemeTokens.User;
		Color input = previewThemeTokens.Input;
		Color color = previewThemeTokens.Text;
		Color accent = previewThemeTokens.Accent;
		string layout = previewThemeTokens.Layout;
		double glass = previewThemeTokens.Glass;
		int sidebarAlpha = previewThemeTokens.SidebarAlpha;
		int inputAlpha = previewThemeTokens.InputAlpha;
		int composerAlpha = previewThemeTokens.ComposerAlpha;
		int cardAlpha = previewThemeTokens.CardAlpha;
		int overlayAlpha = previewThemeTokens.OverlayAlpha;
		Rectangle clientRectangle = control.ClientRectangle;
		using (Font font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold))
		{
			TextRenderer.DrawText(e.Graphics, "LIVE PREVIEW", font, new Rectangle(14, 4, 150, 28), AppleText, TextFormatFlags.VerticalCenter);
		}
		int num = Math.Max(188, clientRectangle.Height - 48);
		int num2 = Math.Max(80, Math.Min(clientRectangle.Width - 28, 640));
		Rectangle rectangle = new Rectangle(Math.Max(14, (clientRectangle.Width - num2) / 2), 40, num2, num);
		using (GraphicsPath graphicsPath = UiShape.RoundedRect(rectangle, 12))
		{
			using (SolidBrush brush = new SolidBrush(bg))
			{
				e.Graphics.FillPath(brush, graphicsPath);
			}
			Image image = (_previewSkinEnabled ? LoadPreviewBackgroundImage() : null);
			if (image != null)
			{
				GraphicsState gstate = e.Graphics.Save();
				e.Graphics.SetClip(graphicsPath);
				e.Graphics.DrawImage(image, CoverRectangle(image.Size, rectangle));
				e.Graphics.Restore(gstate);
			}
			using LinearGradientBrush linearGradientBrush = new LinearGradientBrush(rectangle, Color.FromArgb(overlayAlpha, bg), Color.FromArgb(previewThemeTokens.BackgroundEndAlpha, bg), LinearGradientMode.ForwardDiagonal);
			ColorBlend colorBlend = new ColorBlend(3);
			colorBlend.Positions = new float[3] { 0f, 0.52f, 1f };
			colorBlend.Colors = new Color[3]
			{
				Color.FromArgb(overlayAlpha, bg),
				Color.FromArgb(previewThemeTokens.BackgroundAccentAlpha, accent),
				Color.FromArgb(previewThemeTokens.BackgroundEndAlpha, bg)
			};
			linearGradientBrush.InterpolationColors = colorBlend;
			e.Graphics.FillPath(linearGradientBrush, graphicsPath);
		}
		GraphicsState gstate2 = e.Graphics.Save();
		using (GraphicsPath clip = UiShape.RoundedRect(rectangle, 12))
		{
			e.Graphics.SetClip(clip);
		}
		int num3 = Math.Min(layout switch
		{
			"focus" => Math.Max(76, Math.Min(128, rectangle.Width / 4)), 
			"compact" => Math.Max(96, Math.Min(132, rectangle.Width / 3)), 
			"wide" => Math.Max(104, Math.Min(176, rectangle.Width / 3)), 
			_ => Math.Max(94, Math.Min(152, rectangle.Width / 3)), 
		}, Math.Max(70, rectangle.Width - 120));
		Rectangle rectangle2 = new Rectangle(rectangle.Left, rectangle.Top, num3, rectangle.Height);
		Image image2 = (_previewSkinEnabled ? LoadPreviewPanelImage() : null);
		int alpha = ((image2 == null) ? sidebarAlpha : 255);
		using (SolidBrush brush2 = new SolidBrush(Color.FromArgb(alpha, sidebar)))
		{
			e.Graphics.FillRectangle(brush2, rectangle2);
		}
		if (image2 != null)
		{
			GraphicsState gstate3 = e.Graphics.Save();
			e.Graphics.SetClip(rectangle2);
			e.Graphics.DrawImage(image2, CoverRectangle(image2.Size, rectangle2));
			using (SolidBrush brush3 = new SolidBrush(Color.FromArgb(previewThemeTokens.PanelImageShadeAlpha, sidebar)))
			{
				e.Graphics.FillRectangle(brush3, rectangle2);
			}
			e.Graphics.Restore(gstate3);
		}
		if (layout == "focus")
		{
			using (Font font2 = new Font(Font.FontFamily, 8.6f, FontStyle.Bold))
			{
				TextRenderer.DrawText(e.Graphics, "ChatGPT", font2, new Rectangle(rectangle2.Left + 14, rectangle2.Top + 20, rectangle2.Width - 24, 22), accent, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
			}
			DrawPreviewRow(e.Graphics, rectangle2.Left + 12, rectangle2.Top + 62, rectangle2.Width - 22, "New", PreviewTextForSurface(accent, previewThemeTokens.Selected), previewThemeTokens.Selected, previewThemeTokens.RowAlpha);
			DrawPreviewRow(e.Graphics, rectangle2.Left + 12, rectangle2.Top + 101, rectangle2.Width - 22, "Search", PreviewTextForSurface(color, previewThemeTokens.Hover), previewThemeTokens.Hover, previewThemeTokens.HoverAlpha);
			DrawPreviewRow(e.Graphics, rectangle2.Left + 12, rectangle2.Top + 140, rectangle2.Width - 22, "Library", color, Color.Empty, 0);
		}
		else
		{
			using (Font font2 = new Font(Font.FontFamily, 10f, FontStyle.Bold))
			{
				TextRenderer.DrawText(e.Graphics, "ChatGPT", font2, new Rectangle(rectangle2.Left + 18, rectangle2.Top + 20, rectangle2.Width - 30, 24), accent, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
			}
			string text = ((layout == "compact") ? "New" : "New chat");
			string text2 = ((layout == "compact") ? "GPTs" : "GPTs");
			DrawPreviewRow(e.Graphics, rectangle2.Left + 18, rectangle2.Top + 68, rectangle2.Width - 30, text, PreviewTextForSurface(accent, previewThemeTokens.Selected), previewThemeTokens.Selected, previewThemeTokens.RowAlpha);
			DrawPreviewRow(e.Graphics, rectangle2.Left + 18, rectangle2.Top + 110, rectangle2.Width - 30, "Search", PreviewTextForSurface(color, previewThemeTokens.Hover), previewThemeTokens.Hover, previewThemeTokens.HoverAlpha);
			DrawPreviewRow(e.Graphics, rectangle2.Left + 18, rectangle2.Top + 152, rectangle2.Width - 30, "Library", color, Color.Empty, 0);
			if (rectangle.Height > 252)
			{
				DrawPreviewRow(e.Graphics, rectangle2.Left + 18, rectangle2.Top + 194, rectangle2.Width - 30, text2, color, Color.Empty, 0);
			}
			TextRenderer.DrawText(e.Graphics, "Settings", Font, new Rectangle(rectangle2.Left + 22, rectangle2.Bottom - 42, rectangle2.Width - 36, 24), color, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
		}
		Rectangle rect = new Rectangle(rectangle2.Right + 1, rectangle.Top, rectangle.Width - rectangle2.Width - 1, rectangle.Height);
		using (LinearGradientBrush brush4 = new LinearGradientBrush(rect, Color.FromArgb((int)(18.0 + glass * 42.0), accent), Color.FromArgb(0, bg), LinearGradientMode.Horizontal))
		{
			e.Graphics.FillRectangle(brush4, rect);
		}
		int num4 = Math.Max(58, Math.Min(92, rect.Width - 24));
		DrawPreviewPill(e.Graphics, new Rectangle(rect.Right - num4 - 20, rect.Top + 14, num4, 24), _previewSkinEnabled ? layout : "Unskinned", input, color, inputAlpha);
		int num5 = Math.Max(12, Math.Min(24, rect.Width / 10));
		int num6 = Math.Max(68, rect.Width - num5 * 2);
		int val = ((layout == "wide") ? num6 : Math.Min((layout == "compact") ? 190 : 235, num6));
		int num7 = Math.Max(68, val);
		DrawBubble(e.Graphics, new Rectangle(rect.Right - num7 - num5, rect.Top + 48, num7, 38), "Hello!", user, color, 230);
		int num8 = Math.Max(80, (layout == "wide") ? num6 : Math.Min((layout == "compact") ? 205 : 250, num6));
		int num9 = ((layout == "compact") ? 62 : Math.Max(78, Math.Min(108, rect.Height - 172)));
		DrawBubble(e.Graphics, new Rectangle(rect.Left + num5, rect.Top + 100, num8, num9), _previewSkinEnabled ? "Preview updates as you customize." : "Skin cleared. ChatGPT is back to its default look.", panel, color, cardAlpha);
		int num10 = ((layout == "compact") ? Math.Min(num6, 310) : num6);
		int num11 = ((layout == "compact") ? (rect.Left + (rect.Width - num10) / 2) : (rect.Left + num5));
		DrawGradientBubble(e.Graphics, new Rectangle(num11, rect.Bottom - 64, num10, 46), "Message ChatGPT...", input, composerAlpha, panel, previewThemeTokens.ComposerPanelAlpha, accent, previewThemeTokens.ComposerAccentAlpha, Color.FromArgb(180, color));
		e.Graphics.Restore(gstate2);
	}

	private PreviewThemeTokens CurrentPreviewTokens()
	{
		Theme theme2;
		if (!_previewSkinEnabled)
		{
			Theme theme = new Theme();
			theme.Id = "default";
			theme.Name = "Default";
			theme.Bg = "#202123";
			theme.Panel = "#171717";
			theme.Input = "#2f2f2f";
			theme.Text = "#ececf1";
			theme.Accent = "#ececf1";
			theme.Border = "#4a4a4a";
			theme.User = "#2f2f2f";
			theme.Pattern = "";
			theme2 = theme;
		}
		else
		{
			theme2 = CurrentTheme();
		}
		Theme theme3 = theme2;
		string hex = EnsureReadableText(theme3.Text, theme3.Bg);
		string panel = theme3.Panel;
		Color bg = ColorFromHex(theme3.Bg);
		Color color = ColorFromHex(theme3.Panel);
		Color sidebar = ColorFromHex(panel);
		Color color2 = ColorFromHex(theme3.Input);
		Color user = ColorFromHex(theme3.User);
		Color color3 = ColorFromHex(hex);
		Color color4 = ColorFromHex(theme3.Accent);
		Color border = ColorFromHex(theme3.Border);
		string layout = (_previewSkinEnabled ? SelectedLayout() : "standard");
		int val = (_previewSkinEnabled ? SelectedTransparency() : 0);
		double num = (double)Math.Max(0, Math.Min(75, val)) / 75.0;
		bool flag = _previewSkinEnabled && SelectedGlassSearch();
		bool flag2 = IsLightColor(theme3.Bg);
		double val2 = (string.Equals(theme3.Bg, "#f4f7fb", StringComparison.OrdinalIgnoreCase) ? 0.94 : 0.84) - num * 0.72;
		double val3 = 0.96 - num * 0.72;
		double val4 = 0.92 - num * 0.68;
		double val5 = 0.88 - num * 0.58;
		double val6 = 0.94 - num * 0.7;
		val2 = Math.Max(0.36, val2);
		val3 = Math.Max(0.4, val3);
		val4 = Math.Max(0.44, val4);
		val5 = Math.Max(0.42, val5);
		val6 = Math.Max(0.5, val6);
		double num2 = (flag ? Math.Max(flag2 ? 0.78 : 0.62, val4 * 0.9) : val4);
		bool flag3 = false;
		if (_previewSkinEnabled && string.Equals(SelectedBackgroundMode(), "file", StringComparison.OrdinalIgnoreCase))
		{
			string text = ((_backgroundTextBox == null) ? "" : _backgroundTextBox.Text.Trim());
			try
			{
				flag3 = !string.IsNullOrWhiteSpace(text) && File.Exists(Path.GetFullPath(text));
			}
			catch
			{
				flag3 = false;
			}
		}
		double num3 = (flag3 ? Math.Max(0.16, (flag2 ? 0.28 : 0.38) - num * 0.12) : Math.Max(0.3, (flag2 ? 0.48 : 0.7) - num * 0.45));
		double alpha = ((!flag3) ? (flag2 ? 0.09 : 0.1) : (flag2 ? 0.07 : 0.08));
		Color composer = Blend(Blend(color2, color, 0.28), color4, 0.08);
		PreviewThemeTokens previewThemeTokens = new PreviewThemeTokens();
		previewThemeTokens.Bg = bg;
		previewThemeTokens.Panel = color;
		previewThemeTokens.Sidebar = sidebar;
		previewThemeTokens.Input = color2;
		previewThemeTokens.User = user;
		previewThemeTokens.Composer = composer;
		previewThemeTokens.Text = color3;
		previewThemeTokens.Accent = color4;
		previewThemeTokens.Border = border;
		previewThemeTokens.Hover = color4;
		previewThemeTokens.Active = Blend(color2, color4, flag2 ? 0.26 : 0.22);
		previewThemeTokens.Selected = Blend(color2, color4, 0.18);
		previewThemeTokens.PanelAlpha = AlphaByte(val2);
		previewThemeTokens.SidebarAlpha = AlphaByte(val3);
		previewThemeTokens.InputAlpha = AlphaByte(num2);
		previewThemeTokens.ComposerAlpha = AlphaByte(val6);
		previewThemeTokens.SearchPanelAlpha = AlphaByte(Math.Max(0.34, num2 * 0.86));
		previewThemeTokens.SearchAccentAlpha = AlphaByte(0.1);
		previewThemeTokens.ComposerPanelAlpha = AlphaByte(Math.Max(0.24, val6 * 0.72));
		previewThemeTokens.ComposerAccentAlpha = AlphaByte(0.08);
		previewThemeTokens.CardAlpha = AlphaByte(val2);
		previewThemeTokens.OverlayAlpha = AlphaByte(num3);
		previewThemeTokens.BackgroundAccentAlpha = AlphaByte(alpha);
		previewThemeTokens.BackgroundEndAlpha = AlphaByte(Math.Max(0.08, num3 * 0.62));
		previewThemeTokens.PanelImageShadeAlpha = AlphaByte(Math.Max(0.18, Math.Min(0.46, val3 * 0.52)));
		previewThemeTokens.RowAlpha = AlphaByte(Math.Max(0.56, val5));
		previewThemeTokens.HoverAlpha = AlphaByte(flag2 ? 0.12 : 0.1);
		previewThemeTokens.Glass = num;
		previewThemeTokens.Layout = layout;
		previewThemeTokens.GlassSearch = flag;
		return previewThemeTokens;
	}

	private static int AlphaByte(double alpha)
	{
		return Math.Max(0, Math.Min(255, (int)Math.Round(alpha * 255.0)));
	}

	private void PaintFastPreviewPanel(Control panel, Graphics g)
	{
		PreviewThemeTokens previewThemeTokens = CurrentPreviewTokens();
		Color bg = previewThemeTokens.Bg;
		Color panel2 = previewThemeTokens.Panel;
		Color sidebar = previewThemeTokens.Sidebar;
		Color baseColor = previewThemeTokens.Text;
		Color accent = previewThemeTokens.Accent;
		Rectangle clientRectangle = panel.ClientRectangle;
		using (Font font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold))
		{
			TextRenderer.DrawText(g, "LIVE PREVIEW", font, new Rectangle(14, 4, 150, 28), AppleText, TextFormatFlags.VerticalCenter);
		}
		int num = Math.Max(80, Math.Min(clientRectangle.Width - 28, 640));
		Rectangle rectangle = new Rectangle(Math.Max(14, (clientRectangle.Width - num) / 2), 40, num, Math.Max(170, clientRectangle.Height - 54));
		using (GraphicsPath path = UiShape.RoundedRect(rectangle, 12))
		{
			using (SolidBrush brush = new SolidBrush(bg))
			{
				g.FillPath(brush, path);
			}
			using (LinearGradientBrush linearGradientBrush = new LinearGradientBrush(rectangle, Color.FromArgb(previewThemeTokens.OverlayAlpha, bg), Color.FromArgb(previewThemeTokens.BackgroundEndAlpha, bg), LinearGradientMode.ForwardDiagonal))
			{
				ColorBlend colorBlend = new ColorBlend(3);
				colorBlend.Positions = new float[3] { 0f, 0.52f, 1f };
				colorBlend.Colors = new Color[3]
				{
					Color.FromArgb(previewThemeTokens.OverlayAlpha, bg),
					Color.FromArgb(previewThemeTokens.BackgroundAccentAlpha, accent),
					Color.FromArgb(previewThemeTokens.BackgroundEndAlpha, bg)
				};
				linearGradientBrush.InterpolationColors = colorBlend;
				g.FillPath(linearGradientBrush, path);
			}
			int num2 = Math.Max(72, Math.Min(132, rectangle.Width / 4));
			using (SolidBrush brush2 = new SolidBrush(Color.FromArgb(previewThemeTokens.SidebarAlpha, sidebar)))
			{
				g.FillRectangle(brush2, new Rectangle(rectangle.Left, rectangle.Top, num2, rectangle.Height));
			}
			using (SolidBrush brush3 = new SolidBrush(Color.FromArgb(previewThemeTokens.ComposerAlpha, previewThemeTokens.Composer)))
			{
				Rectangle bounds = new Rectangle(rectangle.Left + num2 + 28, rectangle.Bottom - 62, Math.Max(80, rectangle.Width - num2 - 56), 42);
				using GraphicsPath path2 = UiShape.RoundedRect(bounds, 21);
				g.FillPath(brush3, path2);
			}
			using SolidBrush brush4 = new SolidBrush(Color.FromArgb(previewThemeTokens.CardAlpha, panel2));
			Rectangle bounds2 = new Rectangle(rectangle.Left + num2 + 34, rectangle.Top + 88, Math.Max(110, Math.Min(260, rectangle.Width - num2 - 72)), 72);
			using GraphicsPath path3 = UiShape.RoundedRect(bounds2, 14);
			g.FillPath(brush4, path3);
		}
		TextRenderer.DrawText(g, "Preview refreshes after resizing", Font, new Rectangle(rectangle.Left, rectangle.Bottom - 26, rectangle.Width, 18), Color.FromArgb(185, baseColor), TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
	}

	private Image LoadPreviewBackgroundImage()
	{
		if (!string.Equals(SelectedBackgroundMode(), "file", StringComparison.OrdinalIgnoreCase))
		{
			ClearCachedImage(ref _cachedBackgroundPreviewImage, ref _cachedBackgroundPreviewKey);
			return null;
		}
		return LoadCachedFilePreviewImage(ref _cachedBackgroundPreviewImage, ref _cachedBackgroundPreviewKey, (_backgroundTextBox == null) ? "" : _backgroundTextBox.Text.Trim());
	}

	private Image LoadPreviewPanelImage()
	{
		string text = SelectedPanelImageMode();
		if (text == "same")
		{
			ClearCachedImage(ref _cachedPanelPreviewImage, ref _cachedPanelPreviewKey);
			return LoadPreviewBackgroundImage();
		}
		if (text != "file")
		{
			ClearCachedImage(ref _cachedPanelPreviewImage, ref _cachedPanelPreviewKey);
			return null;
		}
		return LoadCachedFilePreviewImage(ref _cachedPanelPreviewImage, ref _cachedPanelPreviewKey, (_panelImageTextBox == null) ? "" : _panelImageTextBox.Text.Trim());
	}

	private Image LoadCachedFilePreviewImage(ref Image cachedImage, ref string cachedKey, string path)
	{
		if (!TryGetValidatedLocalImageFile(path, out var info, out var _, out var _))
		{
			ClearCachedImage(ref cachedImage, ref cachedKey);
			return null;
		}
		string text = PreviewImageKey(info);
		if (cachedImage != null && string.Equals(cachedKey, text, StringComparison.OrdinalIgnoreCase))
		{
			return cachedImage;
		}
		ClearCachedImage(ref cachedImage, ref cachedKey);
		try
		{
			using FileStream stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using Image original = Image.FromStream(stream);
			cachedImage = new Bitmap(original);
			cachedKey = text;
			return cachedImage;
		}
		catch
		{
			cachedImage = null;
			cachedKey = "";
			return null;
		}
	}

	private static string PreviewImageKey(FileInfo info)
	{
		try
		{
			return info.FullName + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
		}
		catch
		{
			return "";
		}
	}

	private void ClearPreviewImageCaches()
	{
		ClearCachedImage(ref _cachedBackgroundPreviewImage, ref _cachedBackgroundPreviewKey);
		ClearCachedImage(ref _cachedPanelPreviewImage, ref _cachedPanelPreviewKey);
	}

	private static void ClearCachedImage(ref Image cachedImage, ref string cachedKey)
	{
		if (cachedImage != null)
		{
			cachedImage.Dispose();
			cachedImage = null;
		}
		cachedKey = "";
	}

	private static Rectangle CoverRectangle(Size imageSize, Rectangle dest)
	{
		if (imageSize.Width <= 0 || imageSize.Height <= 0)
		{
			return dest;
		}
		double num = Math.Max((double)dest.Width / (double)imageSize.Width, (double)dest.Height / (double)imageSize.Height);
		int num2 = (int)Math.Ceiling((double)imageSize.Width * num);
		int num3 = (int)Math.Ceiling((double)imageSize.Height * num);
		return new Rectangle(dest.Left + (dest.Width - num2) / 2, dest.Top + (dest.Height - num3) / 2, num2, num3);
	}

	private void DrawPreviewRow(Graphics g, int x, int y, int w, string text, Color textColor, Color fillColor, int fillAlpha)
	{
		Rectangle bounds = new Rectangle(x, y, w, 32);
		if (fillAlpha > 0 && fillColor != Color.Empty)
		{
			using GraphicsPath path = UiShape.RoundedRect(bounds, 8);
			using SolidBrush brush = new SolidBrush(Color.FromArgb(Math.Max(24, Math.Min(220, fillAlpha)), fillColor));
			g.FillPath(brush, path);
		}
		using Font font = PreviewChatFont(Font.Size, FontStyle.Regular);
		TextRenderer.DrawText(g, text, font, new Rectangle(x + 14, y, Math.Max(1, w - 18), 32), textColor, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
	}

	private static Color PreviewTextForSurface(Color preferred, Color surface)
	{
		if (ContrastRatio(preferred, surface) >= 3.4)
		{
			return preferred;
		}
		return ReadableTextColor(surface);
	}

	private void DrawPreviewPill(Graphics g, Rectangle rect, string copy, Color fill, Color text, int alpha)
	{
		using (GraphicsPath path = UiShape.RoundedRect(rect, 12))
		{
			using SolidBrush brush = new SolidBrush(Color.FromArgb(Math.Max(42, Math.Min(230, alpha)), fill));
			g.FillPath(brush, path);
		}
		using Font font = new Font(Font.FontFamily, 7.8f, FontStyle.Bold);
		TextRenderer.DrawText(g, copy, font, rect, text, TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
	}

	private void DrawBubble(Graphics g, Rectangle rect, string copy, Color fill, Color text, int alpha)
	{
		using (GraphicsPath path = UiShape.RoundedRect(rect, 18))
		{
			using SolidBrush brush = new SolidBrush(Color.FromArgb(Math.Max(42, Math.Min(230, alpha)), fill));
			g.FillPath(brush, path);
		}
		using Font font = PreviewChatFont(Font.Size, FontStyle.Regular);
		TextRenderer.DrawText(g, copy, font, new Rectangle(rect.Left + 20, rect.Top + 10, Math.Max(1, rect.Width - 34), Math.Max(1, rect.Height - 18)), text, TextFormatFlags.EndEllipsis | TextFormatFlags.WordBreak);
	}

	private void DrawGradientBubble(Graphics g, Rectangle rect, string copy, Color first, int firstAlpha, Color middle, int middleAlpha, Color last, int lastAlpha, Color text)
	{
		using (GraphicsPath path = UiShape.RoundedRect(rect, 18))
		{
			using LinearGradientBrush linearGradientBrush = new LinearGradientBrush(rect, Color.FromArgb(Math.Max(42, Math.Min(240, firstAlpha)), first), Color.FromArgb(Math.Max(18, Math.Min(240, lastAlpha)), last), LinearGradientMode.ForwardDiagonal);
			ColorBlend colorBlend = new ColorBlend(3);
			colorBlend.Positions = new float[3] { 0f, 0.52f, 1f };
			colorBlend.Colors = new Color[3]
			{
				Color.FromArgb(Math.Max(42, Math.Min(240, firstAlpha)), first),
				Color.FromArgb(Math.Max(18, Math.Min(240, middleAlpha)), middle),
				Color.FromArgb(Math.Max(18, Math.Min(240, lastAlpha)), last)
			};
			linearGradientBrush.InterpolationColors = colorBlend;
			g.FillPath(linearGradientBrush, path);
		}
		using Font font = PreviewChatFont(Font.Size, FontStyle.Regular);
		TextRenderer.DrawText(g, copy, font, new Rectangle(rect.Left + 20, rect.Top + 10, Math.Max(1, rect.Width - 34), Math.Max(1, rect.Height - 18)), text, TextFormatFlags.EndEllipsis | TextFormatFlags.WordBreak);
	}

	private Font PreviewChatFont(float size, FontStyle style)
	{
		string text = SelectedFontFamily();
		if (string.IsNullOrWhiteSpace(text) || string.Equals(text, "Default", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "System UI", StringComparison.OrdinalIgnoreCase))
		{
			return new Font(Font.FontFamily, size, style);
		}
		string text2 = SanitizeFontName(text.Split(',')[0]);
		if (string.IsNullOrEmpty(text2))
		{
			return new Font(Font.FontFamily, size, style);
		}
		try
		{
			return new Font(text2, size, style);
		}
		catch
		{
			return new Font(Font.FontFamily, size, style);
		}
	}

	private static Color Blend(Color a, Color b, double amount)
	{
		amount = Math.Max(0.0, Math.Min(1.0, amount));
		return Color.FromArgb((int)((double)(int)a.R + (double)(b.R - a.R) * amount), (int)((double)(int)a.G + (double)(b.G - a.G) * amount), (int)((double)(int)a.B + (double)(b.B - a.B) * amount));
	}

	private Control BuildActionsPanel()
	{
		RoundedPanel roundedPanel = new RoundedPanel();
		roundedPanel.Dock = DockStyle.Fill;
		roundedPanel.Margin = new Padding(0, 0, 0, 0);
		roundedPanel.Padding = new Padding(6, 0, 6, 0);
		roundedPanel.Radius = 1;
		roundedPanel.BackColor = Color.Transparent;
		roundedPanel.BorderColor = Color.Transparent;
		roundedPanel.GradientTop = Color.Empty;
		roundedPanel.GradientBottom = Color.Empty;
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.BackColor = Color.Transparent;
		tableLayoutPanel.ColumnCount = 1;
		tableLayoutPanel.RowCount = 3;
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 23f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		roundedPanel.Controls.Add(tableLayoutPanel);
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel();
		tableLayoutPanel2.Dock = DockStyle.Fill;
		tableLayoutPanel2.BackColor = Color.Transparent;
		tableLayoutPanel2.Margin = new Padding(0);
		tableLayoutPanel2.Padding = new Padding(0);
		tableLayoutPanel2.ColumnCount = 1;
		tableLayoutPanel2.RowCount = 1;
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 0);
		_statusLabel = Label("Ready.");
		_statusLabel.Dock = DockStyle.Fill;
		_statusLabel.ForeColor = AppleMuted;
		_statusLabel.TextAlign = ContentAlignment.MiddleLeft;
		_statusLabel.Margin = new Padding(0, 0, 6, 1);
		_statusLabel.Padding = new Padding(0, 0, 0, 4);
		_statusLabel.AutoEllipsis = true;
		tableLayoutPanel2.Controls.Add(_statusLabel, 0, 0);
		_activeThemeLabel = Label("");
		_activeThemeLabel.Visible = false;
		TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel();
		tableLayoutPanel3.Dock = DockStyle.Fill;
		tableLayoutPanel3.BackColor = Color.Transparent;
		tableLayoutPanel3.Margin = new Padding(0);
		tableLayoutPanel3.Padding = new Padding(0);
		tableLayoutPanel3.ColumnCount = 2;
		tableLayoutPanel3.RowCount = 1;
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		_watchButton = Button("Watch: Off");
		SetButtonIcon(_watchButton, ButtonIconKind.Eye);
		_watchButton.Dock = DockStyle.Fill;
		_watchButton.Margin = new Padding(0, 0, 8, 0);
		_watchButton.Click += ToggleWatch;
		tableLayoutPanel3.Controls.Add(_watchButton, 0, 0);
		_clearButton = Button("Clear Theme");
		SetButtonIcon(_clearButton, ButtonIconKind.None);
		_clearButton.Dock = DockStyle.Fill;
		_clearButton.Margin = new Padding(0);
		_clearButton.Click += async delegate
		{
			await ClearSkinAsync();
		};
		tableLayoutPanel3.Controls.Add(_clearButton, 1, 0);
		tableLayoutPanel.Controls.Add(tableLayoutPanel3, 0, 1);
		TableLayoutPanel tableLayoutPanel4 = new TableLayoutPanel();
		tableLayoutPanel4.Dock = DockStyle.Fill;
		tableLayoutPanel4.BackColor = Color.Transparent;
		tableLayoutPanel4.ColumnCount = 1;
		tableLayoutPanel4.RowCount = 1;
		tableLayoutPanel4.Margin = new Padding(0, 2, 0, 0);
		tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.Controls.Add(tableLayoutPanel4, 0, 2);
		_applyButton = Button("Apply");
		_applyButton.Text = "Apply Theme";
		SetButtonIcon(_applyButton, ButtonIconKind.Check);
		_applyButton.Dock = DockStyle.Fill;
		_applyButton.Margin = new Padding(0);
		_applyButton.BackColor = ApplePink;
		_applyButton.ForeColor = Color.White;
		_applyButton.FlatAppearance.MouseOverBackColor = ApplePink;
		_applyButton.FlatAppearance.MouseDownBackColor = ApplePink;
		if (_applyButton is RoundedButton roundedButton)
		{
			roundedButton.DisabledBackColor = Blend(ApplePink, AppleControl, 0.45);
			roundedButton.DisabledForeColor = Color.White;
		}
		_applyButton.Click += async delegate
		{
			await ApplySkinAsync(quiet: false);
		};
		tableLayoutPanel4.Controls.Add(_applyButton, 0, 0);
		return roundedPanel;
	}

	private Panel CardPanel()
	{
		Panel panel = new Panel();
		panel.Dock = DockStyle.Fill;
		panel.Margin = new Padding(0);
		panel.BackColor = Color.Transparent;
		return panel;
	}

	private Label Label(string text)
	{
		Label label = new Label();
		label.Text = text;
		label.AutoSize = false;
		label.ForeColor = AppleText;
		label.BackColor = Color.Transparent;
		return label;
	}

	private Label SectionHeaderLabel(string text)
	{
		Label label = Label(text);
		label.Dock = DockStyle.Fill;
		label.Height = 28;
		label.Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold);
		label.ForeColor = AppleText;
		label.TextAlign = ContentAlignment.MiddleLeft;
		label.Margin = new Padding(0);
		label.Padding = new Padding(0);
		return label;
	}

	private void StyleCombo(ComboBox combo)
	{
		if (combo != null)
		{
			combo.FlatStyle = FlatStyle.Flat;
			combo.BackColor = Color.FromArgb(14, 17, 27);
			combo.ForeColor = AppleText;
			combo.DrawMode = DrawMode.OwnerDrawFixed;
			combo.ItemHeight = 24;
			combo.Height = 30;
			combo.MinimumSize = new Size(0, 30);
			combo.DropDownHeight = 320;
			combo.MaxDropDownItems = 16;
			if (combo is ThemedComboBox themedComboBox)
			{
				themedComboBox.ThemeBackColor = Color.FromArgb(14, 17, 27);
				themedComboBox.ThemeForeColor = AppleText;
				themedComboBox.ThemeAccentColor = Color.FromArgb(60, 68, 86);
			}
			combo.DrawItem -= DrawComboItem;
			combo.DrawItem += DrawComboItem;
		}
	}

	private void DrawComboItem(object sender, DrawItemEventArgs e)
	{
		if (sender is ComboBox comboBox && e.Bounds.Width > 0 && e.Bounds.Height > 0)
		{
			bool flag = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
			ThemedComboBox themedComboBox = comboBox as ThemedComboBox;
			Color color = themedComboBox?.ThemeBackColor ?? Color.FromArgb(14, 17, 27);
			Color color2 = themedComboBox?.ThemeAccentColor ?? Color.FromArgb(31, 35, 49);
			Color foreColor = themedComboBox?.ThemeForeColor ?? AppleText;
			Color color3 = (flag ? color2 : color);
			using (SolidBrush brush = new SolidBrush(color3))
			{
				e.Graphics.FillRectangle(brush, e.Bounds);
			}
			string text = ((e.Index >= 0 && e.Index < comboBox.Items.Count) ? Convert.ToString(comboBox.Items[e.Index]) : comboBox.Text);
			TextRenderer.DrawText(e.Graphics, text, Font, new Rectangle(e.Bounds.Left + 9, e.Bounds.Top, Math.Max(1, e.Bounds.Width - 18), e.Bounds.Height), foreColor, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
		}
	}

	private void StyleTextBox(TextBox textBox)
	{
		if (textBox != null)
		{
			textBox.BackColor = AppleInput;
			textBox.ForeColor = AppleText;
			textBox.Font = Font;
			textBox.AutoSize = false;
			textBox.Height = 30;
			textBox.MinimumSize = new Size(0, 30);
			textBox.BorderStyle = BorderStyle.None;
			if (textBox is ThemedTextBox themedTextBox)
			{
				themedTextBox.RefreshTextLayout();
			}
		}
	}

	private void StyleMuted(Label label)
	{
		if (label != null)
		{
			label.ForeColor = AppleMuted;
		}
	}

	private Button Button(string text)
	{
		RoundedButton roundedButton = new RoundedButton();
		roundedButton.Radius = 12;
		roundedButton.Text = text;
		roundedButton.FlatStyle = FlatStyle.Flat;
		roundedButton.FlatAppearance.BorderSize = 0;
		roundedButton.BackColor = AppleControl;
		roundedButton.ForeColor = AppleText;
		roundedButton.FlatAppearance.BorderColor = roundedButton.BackColor;
		roundedButton.FlatAppearance.MouseOverBackColor = AppleControl;
		roundedButton.FlatAppearance.MouseDownBackColor = AppleControl;
		roundedButton.Cursor = Cursors.Hand;
		return roundedButton;
	}

	private void SetButtonIcon(Button button, ButtonIconKind icon)
	{
		if (button is RoundedButton roundedButton)
		{
			roundedButton.IconKind = icon;
			roundedButton.Invalidate();
		}
	}

	private void NewTheme()
	{
		Theme theme = (_themes.ContainsKey("plum") ? _themes["plum"] : CurrentTheme());
		_customTheme = theme.Clone("custom", "Custom");
		_themes["custom"] = _customTheme;
		_themeId = "custom";
		_currentThemeFilePath = "";
		if (_backgroundModeCombo != null)
		{
			_backgroundModeCombo.SelectedIndex = 0;
		}
		if (_backgroundTextBox != null)
		{
			_backgroundTextBox.Text = "";
		}
		if (_panelImageModeCombo != null)
		{
			_panelImageModeCombo.SelectedIndex = 0;
		}
		if (_panelImageTextBox != null)
		{
			_panelImageTextBox.Text = "";
		}
		_previewSkinEnabled = true;
		ApplyUiState();
		SaveSettings();
		SetStatus("Started a new custom theme.", isError: false);
	}

	private void SaveTheme(bool saveAs)
	{
		SaveTheme(saveAs, includeLocalImagePaths: true);
	}

	private void ExportTheme()
	{
		SaveTheme(saveAs: true, includeLocalImagePaths: false);
	}

	private void SaveTheme(bool saveAs, bool includeLocalImagePaths)
	{
		string text = _currentThemeFilePath;
		if (saveAs || string.IsNullOrWhiteSpace(text))
		{
			using SaveFileDialog saveFileDialog = new SaveFileDialog();
			saveFileDialog.Filter = "GPT Themes files|*.gpttheme|All files|*.*";
			saveFileDialog.DefaultExt = "gpttheme";
			saveFileDialog.AddExtension = true;
			saveFileDialog.FileName = SafeFileName(CurrentTheme().Name) + ".gpttheme";
			if (saveFileDialog.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			text = EnsureThemeFileExtension(saveFileDialog.FileName);
		}
		try
		{
			text = (_currentThemeFilePath = EnsureThemeFileExtension(text));
			SaveThemeDocument(text, includeLocalImagePaths);
			SaveSettings();
			SetStatus((includeLocalImagePaths ? "Saved theme to " : "Exported shareable theme to ") + Path.GetFileName(text) + ".", isError: false);
		}
		catch
		{
			SetStatus("Could not save theme. Choose a folder you can write to.", isError: true);
		}
	}

	private static string EnsureThemeFileExtension(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return path;
		}
		string extension = Path.GetExtension(path);
		if (string.IsNullOrEmpty(extension))
		{
			return path.TrimEnd() + ".gpttheme";
		}
		return path;
	}

	private void SaveThemeDocument(string path, bool includeLocalImagePaths)
	{
		Theme theme = CurrentTheme().Clone(CurrentTheme().Id, CurrentTheme().Name);
		ThemeDocument themeDocument = new ThemeDocument();
		themeDocument.Format = "gpt-themes.theme";
		themeDocument.Version = 1;
		themeDocument.Theme = theme;
		themeDocument.Settings = ExportSettingsSnapshot(includeLocalImagePaths);
		ThemeDocument obj = themeDocument;
		string directoryName = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		File.WriteAllText(path, _json.Serialize(obj), Encoding.UTF8);
	}

	private SkinnerSettings ExportSettingsSnapshot(bool includeLocalImagePaths)
	{
		SkinnerSettings skinnerSettings = CurrentSettingsSnapshot();
		skinnerSettings.ManualChatGptExePath = "";
		skinnerSettings.ThemeFilePath = "";
		if (!includeLocalImagePaths && string.Equals(skinnerSettings.BackgroundMode, "file", StringComparison.OrdinalIgnoreCase))
		{
			skinnerSettings.BackgroundMode = "solid";
			skinnerSettings.BackgroundValue = "";
		}
		if (!includeLocalImagePaths && string.Equals(skinnerSettings.PanelImageMode, "file", StringComparison.OrdinalIgnoreCase))
		{
			skinnerSettings.PanelImageMode = "off";
			skinnerSettings.PanelImageValue = "";
			skinnerSettings.PanelImage = false;
		}
		return skinnerSettings;
	}

	private void ImportTheme()
	{
		using OpenFileDialog openFileDialog = new OpenFileDialog();
		openFileDialog.Filter = "GPT Themes files|*.gpttheme;*.json|All files|*.*";
		if (openFileDialog.ShowDialog(this) != DialogResult.OK)
		{
			return;
		}
		try
		{
			FileInfo fileInfo = new FileInfo(openFileDialog.FileName);
			if (fileInfo.Length > 1048576)
			{
				throw new InvalidOperationException("Theme files must be 1 MB or smaller.");
			}
			string input = File.ReadAllText(fileInfo.FullName, Encoding.UTF8);
			ThemeDocument themeDocument = _json.Deserialize<ThemeDocument>(input);
			if (themeDocument == null || themeDocument.Settings == null)
			{
				throw new InvalidOperationException("The selected file is not a GPT Themes theme file.");
			}
			NormalizeImportedThemeDocument(themeDocument);
			ApplyImportedTheme(themeDocument, fileInfo.FullName);
			SetStatus("Imported theme from " + Path.GetFileName(fileInfo.FullName) + ".", isError: false);
		}
		catch (Exception ex)
		{
			SetStatus("Could not import theme: " + SafeExceptionMessage(ex), isError: true);
		}
	}

	private void NormalizeImportedThemeDocument(ThemeDocument doc)
	{
		if (!string.IsNullOrEmpty(doc.Format) && !string.Equals(doc.Format, "gpt-themes.theme", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Unsupported theme file format.");
		}
		if (doc.Theme != null)
		{
			doc.Theme.Id = "custom";
			doc.Theme.Name = LimitThemeText(NonEmpty(doc.Theme.Name, "Custom"), 64);
			doc.Theme.Bg = NormalizeHex(doc.Theme.Bg);
			doc.Theme.Panel = NormalizeHex(doc.Theme.Panel);
			doc.Theme.Input = NormalizeHex(doc.Theme.Input);
			doc.Theme.Text = NormalizeHex(doc.Theme.Text);
			doc.Theme.Accent = NormalizeHex(doc.Theme.Accent);
			doc.Theme.Border = NormalizeHex(doc.Theme.Border);
			doc.Theme.User = NormalizeHex(doc.Theme.User);
			doc.Theme.Pattern = SanitizeThemePattern(doc.Theme.Pattern);
		}
		SkinnerSettings settings = doc.Settings;
		settings.ThemeId = (_themes.ContainsKey(settings.ThemeId ?? "") ? settings.ThemeId : "custom");
		settings.Layout = NormalizeChoice(settings.Layout, new string[4] { "standard", "wide", "compact", "focus" }, "standard");
		settings.BackgroundMode = NormalizeChoice(settings.BackgroundMode, new string[3] { "solid", "pattern", "file" }, "solid");
		settings.PanelImageMode = NormalizeChoice(settings.PanelImageMode, new string[3] { "off", "same", "file" }, "off");
		settings.FontFamily = NormalizeFontChoice(settings.FontFamily);
		settings.Port = ((settings.Port > 0 && settings.Port <= 65535) ? settings.Port : 9322);
		settings.Transparency = Math.Max(0, Math.Min(75, settings.Transparency));
		settings.CustomBg = NormalizeHex(settings.CustomBg);
		settings.CustomPanel = NormalizeHex(settings.CustomPanel);
		settings.CustomInput = NormalizeHex(settings.CustomInput);
		settings.CustomText = NormalizeHex(settings.CustomText);
		settings.CustomAccent = NormalizeHex(settings.CustomAccent);
		settings.CustomBorder = NormalizeHex(settings.CustomBorder);
		settings.CustomUser = NormalizeHex(settings.CustomUser);
		settings.ManualChatGptExePath = "";
		settings.ThemeFilePath = "";
		settings.BackgroundValue = ((settings.BackgroundMode == "file") ? LimitThemeText(settings.BackgroundValue ?? "", 512) : "");
		if (settings.PanelImageMode == "file")
		{
			settings.PanelImageValue = LimitThemeText(settings.PanelImageValue ?? "", 512);
			settings.PanelImage = true;
		}
		else
		{
			settings.PanelImageValue = "";
			settings.PanelImage = settings.PanelImageMode == "same";
		}
	}

	private static string LimitThemeText(string value, int maxLength)
	{
		if (string.IsNullOrEmpty(value))
		{
			return "";
		}
		value = value.Trim();
		if (value.Length > maxLength)
		{
			value = value.Substring(0, maxLength);
		}
		StringBuilder stringBuilder = new StringBuilder();
		string text = value;
		foreach (char c in text)
		{
			if (!char.IsControl(c))
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString();
	}

	private static string SanitizeThemePattern(string pattern)
	{
		if (string.IsNullOrWhiteSpace(pattern))
		{
			return "";
		}
		pattern = LimitThemeText(pattern, 512);
		string text = pattern.ToLowerInvariant();
		if (text.IndexOf("url(", StringComparison.Ordinal) >= 0 || text.IndexOf("@import", StringComparison.Ordinal) >= 0 || text.IndexOf("expression", StringComparison.Ordinal) >= 0 || pattern.IndexOf(';') >= 0 || pattern.IndexOf('{') >= 0 || pattern.IndexOf('}') >= 0 || pattern.IndexOf('<') >= 0 || pattern.IndexOf('>') >= 0)
		{
			return "";
		}
		string[] array = pattern.Split(',');
		string[] array2 = array;
		foreach (string text2 in array2)
		{
			string text3 = text2.TrimStart().ToLowerInvariant();
			if (!text3.StartsWith("linear-gradient(", StringComparison.Ordinal) && !text3.StartsWith("radial-gradient(", StringComparison.Ordinal) && !text3.StartsWith("transparent", StringComparison.Ordinal) && !text3.StartsWith("rgb(", StringComparison.Ordinal))
			{
				return "";
			}
		}
		return pattern;
	}

	private static string NormalizeChoice(string value, IEnumerable<string> allowed, string fallback)
	{
		string a = (value ?? "").Trim().ToLowerInvariant();
		foreach (string item in allowed)
		{
			if (string.Equals(a, item, StringComparison.OrdinalIgnoreCase))
			{
				return item;
			}
		}
		return fallback;
	}

	private static string NormalizeFontChoice(string value)
	{
		value = LimitThemeText(value, 64);
		if (string.IsNullOrWhiteSpace(value))
		{
			return "Default";
		}
		string[] array = new string[11]
		{
			"Default", "System UI", "Segoe UI", "Inter", "Arial", "Verdana", "Georgia", "Courier New", "Comic Sans MS", "Atkinson Hyperlegible",
			"OpenDyslexic"
		};
		foreach (string text in array)
		{
			if (string.Equals(value, text, StringComparison.OrdinalIgnoreCase))
			{
				return text;
			}
		}
		string text2 = SanitizeFontName(value);
		return string.IsNullOrEmpty(text2) ? "Default" : text2;
	}

	private void ApplyImportedTheme(ThemeDocument doc, string filePath)
	{
		SkinnerSettings settings = doc.Settings;
		if (doc.Theme != null)
		{
			_customTheme = doc.Theme.Clone("custom", NonEmpty(doc.Theme.Name, "Custom"));
			_themes["custom"] = _customTheme;
			_themeId = "custom";
		}
		else if (!string.IsNullOrEmpty(settings.ThemeId) && _themes.ContainsKey(settings.ThemeId))
		{
			_themeId = settings.ThemeId;
		}
		_customTheme.Bg = NonEmpty(settings.CustomBg, _customTheme.Bg);
		_customTheme.Panel = NonEmpty(settings.CustomPanel, _customTheme.Panel);
		_customTheme.Input = NonEmpty(settings.CustomInput, _customTheme.Input);
		_customTheme.Text = NonEmpty(settings.CustomText, _customTheme.Text);
		_customTheme.Accent = NonEmpty(settings.CustomAccent, _customTheme.Accent);
		_customTheme.Border = NonEmpty(settings.CustomBorder, _customTheme.Border);
		_customTheme.User = NonEmpty(settings.CustomUser, _customTheme.User);
		_transparency = Math.Max(0, Math.Min(75, settings.Transparency));
		_glassSearch = settings.GlassSearch;
		_manualChatGptExePath = (IsChatGptExecutablePath(settings.ManualChatGptExePath) ? settings.ManualChatGptExePath : _manualChatGptExePath);
		_currentThemeFilePath = filePath;
		SetComboText(_layoutCombo, string.IsNullOrEmpty(settings.Layout) ? "Standard" : settings.Layout);
		SetComboText(_fontCombo, NonEmpty(settings.FontFamily, "Default"));
		if (_transparencyTrackBar != null)
		{
			_transparencyTrackBar.Value = _transparency;
		}
		if (_glassSearchCheckBox != null)
		{
			_glassSearchCheckBox.Checked = _glassSearch;
		}
		if (_backgroundModeCombo != null)
		{
			_backgroundModeCombo.SelectedIndex = BackgroundModeToIndex(NonEmpty(settings.BackgroundMode, "solid"));
		}
		if (_backgroundTextBox != null)
		{
			_backgroundTextBox.Text = NonEmpty(settings.BackgroundValue, "");
		}
		if (_panelImageModeCombo != null)
		{
			_panelImageModeCombo.SelectedIndex = PanelImageModeToIndex(NonEmpty(settings.PanelImageMode, settings.PanelImage ? "same" : "off"));
		}
		if (_panelImageTextBox != null)
		{
			_panelImageTextBox.Text = NonEmpty(settings.PanelImageValue, "");
		}
		if (_portTextBox != null)
		{
			_portTextBox.Text = ((settings.Port > 0) ? settings.Port.ToString() : "9322");
		}
		_previewSkinEnabled = true;
		ApplyUiState();
		SaveSettings();
	}

	private void OpenHelpDocumentation()
	{
		string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs", "user-guide.html");
		if (!File.Exists(path))
		{
			path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "README.md");
		}
		if (File.Exists(path))
		{
			OpenPath(path);
		}
		else
		{
			ShowThemedInfoDialog("Help Documentation", "The local user guide was not found next to the application.", null);
		}
	}

	private void ShowProjectInformation()
	{
		ShowThemedInfoDialog("Project Information", "GPT Themes lets you customize the appearance of the ChatGPT Desktop application with ready-made or custom themes. Create a theme, adjust colors, glass, layout, fonts, and images, preview the look live, then apply it when you are happy with it." + Environment.NewLine + Environment.NewLine + "You can save themes for later, import themes from other people, and export your own themes to share.", new string[2] { "https://github.com/piratemoo/gpt-themes", "https://piratemoo.com" });
	}

	private void ShowAboutDialog()
	{
		string text = (string.IsNullOrWhiteSpace(Application.ProductVersion) ? "0.1.0" : Application.ProductVersion);
		ShowThemedInfoDialog("About GPT Themes", "GPT Themes" + Environment.NewLine + "Version " + text + Environment.NewLine + Environment.NewLine + "Created by @piratemoo" + Environment.NewLine + "Copyright 2026", new string[2] { "https://piratemoo.com", "https://github.com/piratemoo/gpt-themes" });
	}

	private void ShowThemedInfoDialog(string title, string body, IEnumerable<string> links)
	{
		Theme theme = CurrentTheme();
		Color backColor = ColorFromHex(theme.Bg);
		Color backColor2 = ColorFromHex(theme.Panel);
		Color backColor3 = ColorFromHex(theme.Input);
		Color foreColor = ColorFromHex(EnsureReadableText(theme.Text, theme.Bg));
		Color color = ColorFromHex(theme.Accent);
		Form form = new Form();
		try
		{
			form.Text = title;
			form.StartPosition = FormStartPosition.CenterParent;
			form.FormBorderStyle = FormBorderStyle.None;
			form.ClientSize = new Size(500, (links == null) ? 220 : 316);
			form.MinimumSize = new Size(440, 220);
			form.BackColor = backColor;
			form.ForeColor = foreColor;
			form.Font = Font;
			form.KeyPreview = true;
			form.KeyDown += delegate(object sender, KeyEventArgs e)
			{
				if (e.KeyCode == Keys.Escape)
				{
					form.Close();
				}
			};
			TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
			tableLayoutPanel.Dock = DockStyle.Fill;
			tableLayoutPanel.Padding = new Padding(18, 14, 18, 16);
			tableLayoutPanel.BackColor = backColor2;
			tableLayoutPanel.ColumnCount = 1;
			tableLayoutPanel.RowCount = 4;
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, (links != null) ? 54 : 0));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
			form.Controls.Add(tableLayoutPanel);
			Label label = Label(title);
			label.Dock = DockStyle.Fill;
			label.Font = new Font(Font.FontFamily, 12f, FontStyle.Bold);
			label.ForeColor = foreColor;
			label.TextAlign = ContentAlignment.MiddleLeft;
			tableLayoutPanel.Controls.Add(label, 0, 0);
			Label label2 = Label(body);
			label2.Dock = DockStyle.Fill;
			label2.ForeColor = foreColor;
			label2.TextAlign = ContentAlignment.TopLeft;
			label2.Padding = new Padding(0, 8, 0, 0);
			tableLayoutPanel.Controls.Add(label2, 0, 1);
			FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel();
			flowLayoutPanel.Dock = DockStyle.Fill;
			flowLayoutPanel.BackColor = Color.Transparent;
			flowLayoutPanel.FlowDirection = FlowDirection.TopDown;
			flowLayoutPanel.WrapContents = false;
			tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 2);
			if (links != null)
			{
				foreach (string url in links)
				{
					LinkLabel linkLabel = new LinkLabel();
					linkLabel.Text = url;
					linkLabel.AutoSize = true;
					linkLabel.LinkColor = color;
					linkLabel.ActiveLinkColor = color;
					linkLabel.VisitedLinkColor = color;
					linkLabel.BackColor = Color.Transparent;
					linkLabel.Margin = new Padding(0, 2, 0, 0);
					EventHandler value = delegate
					{
						OpenUrl(url);
					};
					linkLabel.Click += value;
					flowLayoutPanel.Controls.Add(linkLabel);
				}
			}
			Button button = Button("OK");
			button.Dock = DockStyle.Right;
			button.Width = 112;
			button.BackColor = backColor3;
			button.ForeColor = foreColor;
			button.Click += delegate
			{
				form.Close();
			};
			tableLayoutPanel.Controls.Add(button, 0, 3);
			form.ShowDialog(this);
		}
		finally
		{
			if (form != null)
			{
				((IDisposable)form).Dispose();
			}
		}
	}

	private static string SafeFileName(string value)
	{
		value = (string.IsNullOrWhiteSpace(value) ? "theme" : value.Trim());
		char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
		foreach (char oldChar in invalidFileNameChars)
		{
			value = value.Replace(oldChar, '-');
		}
		return value;
	}

	private void OpenPath(string path)
	{
		try
		{
			ProcessStartInfo processStartInfo = new ProcessStartInfo();
			processStartInfo.FileName = path;
			processStartInfo.UseShellExecute = true;
			Process.Start(processStartInfo);
		}
		catch (Exception ex)
		{
			SetStatus("Could not open file: " + SafeExceptionMessage(ex), isError: true);
		}
	}

	private void OpenUrl(string url)
	{
		try
		{
			ProcessStartInfo processStartInfo = new ProcessStartInfo();
			processStartInfo.FileName = url;
			processStartInfo.UseShellExecute = true;
			Process.Start(processStartInfo);
		}
		catch (Exception ex)
		{
			SetStatus("Could not open link: " + SafeExceptionMessage(ex), isError: true);
		}
	}

	private Button ColorButton(string label, Func<string> getColor, Action<string> setColor)
	{
		ColorSwatchButton colorSwatchButton = new ColorSwatchButton();
		colorSwatchButton.LabelText = label;
		colorSwatchButton.Text = label;
		colorSwatchButton.Font = new Font(Font.FontFamily, 8.4f);
		colorSwatchButton.BackColor = Color.Transparent;
		colorSwatchButton.ForeColor = AppleText;
		colorSwatchButton.Click += delegate
		{
			using ThemedColorDialog themedColorDialog = new ThemedColorDialog(label + " color", ColorFromHex(getColor()), PaletteColors());
			if (themedColorDialog.ShowDialog(this) == DialogResult.OK)
			{
				setColor(ThemedColorDialog.ToHex(themedColorDialog.SelectedColor));
				_themeId = "custom";
				_previewSkinEnabled = true;
				ApplyUiState();
				HandleThemeStateChanged(queueLiveApply: false);
			}
		};
		return colorSwatchButton;
	}

	private IEnumerable<Color> PaletteColors()
	{
		List<Color> list = new List<Color>();
		AddPaletteColor(list, CurrentTheme().Bg);
		AddPaletteColor(list, CurrentTheme().Panel);
		AddPaletteColor(list, CurrentTheme().Input);
		AddPaletteColor(list, CurrentTheme().Text);
		AddPaletteColor(list, CurrentTheme().Accent);
		AddPaletteColor(list, CurrentTheme().Border);
		AddPaletteColor(list, CurrentTheme().User);
		AddPaletteColor(list, "#0B0F19");
		AddPaletteColor(list, "#141824");
		AddPaletteColor(list, "#F8FAFC");
		AddPaletteColor(list, "#60A5FA");
		AddPaletteColor(list, "#8B5CF6");
		AddPaletteColor(list, "#F472B6");
		AddPaletteColor(list, "#34D399");
		AddPaletteColor(list, "#F59E0B");
		AddPaletteColor(list, "#EF4444");
		return list;
	}

	private void AddPaletteColor(List<Color> colors, string hex)
	{
		Color item = ColorFromHex(hex);
		foreach (Color color in colors)
		{
			if (color.ToArgb() == item.ToArgb())
			{
				return;
			}
		}
		colors.Add(item);
	}

	private void AddThemeButton(string id)
	{
		Theme theme = _themes[id];
		ThemeCardButton button = new ThemeCardButton();
		button.Text = "";
		button.DisplayName = theme.Name;
		button.ThemeData = theme;
		button.Tag = id;
		button.Width = ThemeButtonWidth();
		button.Height = button.Width;
		button.Margin = new Padding(0, 0, 4, 4);
		button.TextAlign = ContentAlignment.MiddleCenter;
		button.Font = new Font(Font.FontFamily, 7.8f, FontStyle.Regular);
		button.FlatStyle = FlatStyle.Flat;
		button.FlatAppearance.BorderSize = 0;
		button.BackColor = Color.FromArgb(18, 22, 34);
		button.ForeColor = AppleText;
		button.FlatAppearance.BorderColor = button.BackColor;
		button.FlatAppearance.MouseOverBackColor = button.BackColor;
		button.FlatAppearance.MouseDownBackColor = button.BackColor;
		button.Click += delegate
		{
			SelectTheme((string)button.Tag);
		};
		button.MouseUp += delegate(object sender, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left && button.ClientRectangle.Contains(e.Location))
			{
				SelectTheme((string)button.Tag);
			}
		};
		button.KeyDown += delegate(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Return || e.KeyCode == Keys.Space)
			{
				SelectTheme((string)button.Tag);
				e.Handled = true;
			}
		};
		_themePanel.Controls.Add(button);
		_themeButtons.Add(button);
		ResizeThemeButtons();
	}

	private void SelectTheme(string id)
	{
		if (string.IsNullOrWhiteSpace(id) || !_themes.ContainsKey(id))
		{
			return;
		}
		long ticks = DateTime.UtcNow.Ticks;
		if (!string.Equals(id, _lastThemeActivationId, StringComparison.OrdinalIgnoreCase) || ticks - _lastThemeActivationTicks >= 1800000)
		{
			_lastThemeActivationId = id;
			_lastThemeActivationTicks = ticks;
			_previewSkinEnabled = true;
			_themeId = id;
			if (_themeId != "custom")
			{
				_customTheme = _themes[_themeId].Clone("custom", "Custom");
				_themes["custom"] = _customTheme;
			}
			ApplyUiState();
			HandleThemeStateChanged(queueLiveApply: false);
		}
	}

	private int ThemeButtonWidth()
	{
		if (_themePanel == null || _themePanel.ClientSize.Width <= 0)
		{
			return 69;
		}
		int num = Math.Max(70, _themePanel.ClientSize.Width - 1);
		int num2 = ((num < 132) ? 1 : 2);
		int num3 = 4;
		int val = (num - num2 * num3) / num2;
		return Math.Max(61, Math.Min(71, val));
	}

	private void ResizeThemeButtons()
	{
		if (_themePanel == null)
		{
			return;
		}
		int num = ThemeButtonWidth();
		if (num == _lastThemeButtonWidth)
		{
			return;
		}
		_lastThemeButtonWidth = num;
		foreach (Button themeButton in _themeButtons)
		{
			themeButton.Width = num;
			themeButton.Height = num;
			themeButton.Margin = new Padding(0, 0, 4, 4);
		}
		try
		{
			_themePanel.HorizontalScroll.Value = 0;
		}
		catch
		{
		}
	}

	private void ApplyUiState()
	{
		_loadingUiState = true;
		try
		{
			foreach (Button themeButton in _themeButtons)
			{
				string text = (string)themeButton.Tag;
				bool selectedTheme = string.Equals(text, _themeId, StringComparison.OrdinalIgnoreCase);
				Theme themeData = _themes[text];
				if (themeButton is ThemeCardButton themeCardButton)
				{
					themeCardButton.ThemeData = themeData;
					themeCardButton.SelectedTheme = selectedTheme;
					themeCardButton.Invalidate();
				}
				themeButton.FlatAppearance.BorderSize = 0;
				themeButton.BackColor = Color.FromArgb(18, 22, 34);
				themeButton.ForeColor = AppleText;
				themeButton.FlatAppearance.BorderColor = themeButton.BackColor;
				themeButton.FlatAppearance.MouseOverBackColor = themeButton.BackColor;
				themeButton.FlatAppearance.MouseDownBackColor = themeButton.BackColor;
			}
			if (_layoutCombo.SelectedIndex < 0)
			{
				SetComboText(_layoutCombo, _loadedLayout);
			}
			if (_fontCombo != null && string.IsNullOrWhiteSpace(_fontCombo.Text))
			{
				SetComboText(_fontCombo, _loadedFontFamily);
			}
			if (_backgroundModeCombo.SelectedIndex < 0)
			{
				_backgroundModeCombo.SelectedIndex = BackgroundModeToIndex(_loadedBackgroundMode);
				_backgroundTextBox.Text = _loadedBackgroundValue;
				_portTextBox.Text = _loadedPort.ToString();
				if (_panelImageModeCombo != null)
				{
					_panelImageModeCombo.SelectedIndex = PanelImageModeToIndex(_loadedPanelImageMode);
					_panelImageTextBox.Text = _loadedPanelImageValue;
				}
			}
			UpdateColorButtons();
			ApplyChromeTheme();
			if (_glassSearchCheckBox != null)
			{
				_glassSearchCheckBox.Checked = _glassSearch;
			}
			UpdateBackgroundInputState();
			UpdatePanelImageInputState();
		}
		finally
		{
			_loadingUiState = false;
		}
		if (_previewPanel != null)
		{
			_previewPanel.Invalidate();
		}
		UpdateActiveThemeLabel();
		SetStatus("Ready. Apply targets ChatGPT Desktop on localhost:" + SelectedPort() + ".", isError: false);
	}

	private void ApplyChromeTheme()
	{
		Color appleInput = AppleInput;
		Color appleAccent = AppleAccent;
		Color appleBorder = AppleBorder;
		Color appleText = AppleText;
		Color appleMuted = AppleMuted;
		Color color = Blend(AppleControl, AppleAccent, 0.18);
		if (_titleLabel != null)
		{
			_titleLabel.ForeColor = appleText;
		}
		if (_activeThemeLabel != null)
		{
			_activeThemeLabel.ForeColor = appleMuted;
		}
		if (_menuStrip != null)
		{
			_menuStrip.BackColor = AppleBg;
			_menuStrip.ForeColor = appleText;
			_menuStrip.Renderer = new ThemedMenuRenderer(AppleBg, AppleBg, appleText, appleMuted, AppleCard, appleBorder);
			StyleMenuItems(_menuStrip.Items, topLevel: true);
			_menuStrip.Invalidate();
		}
		ComboBox[] array = new ComboBox[4] { _layoutCombo, _fontCombo, _backgroundModeCombo, _panelImageModeCombo };
		foreach (ComboBox comboBox in array)
		{
			if (comboBox != null)
			{
				comboBox.BackColor = AppleInput;
				comboBox.ForeColor = appleText;
				if (comboBox is ThemedComboBox themedComboBox)
				{
					themedComboBox.ThemeBackColor = AppleInput;
					themedComboBox.ThemeForeColor = appleText;
					themedComboBox.ThemeAccentColor = color;
					themedComboBox.Invalidate();
				}
			}
		}
		TextBox[] array2 = new TextBox[2] { _backgroundTextBox, _panelImageTextBox };
		foreach (TextBox textBox in array2)
		{
			if (textBox != null)
			{
				textBox.BackColor = AppleInput;
				textBox.ForeColor = appleText;
			}
		}
		if (_portTextBox != null)
		{
			_portTextBox.BackColor = AppleInput;
			_portTextBox.ForeColor = appleText;
		}
		if (_transparencyTrackBar != null)
		{
			_transparencyTrackBar.ActiveColor = appleAccent;
			_transparencyTrackBar.ThumbColor = appleAccent;
			_transparencyTrackBar.TrackColor = Color.FromArgb(72, 78, 96);
			_transparencyTrackBar.BackColor = Color.Transparent;
			_transparencyTrackBar.Invalidate();
		}
		if (_glassSearchCheckBox != null && _glassSearchCheckBox is ThemedCheckBox themedCheckBox)
		{
			themedCheckBox.CheckedBackColor = appleAccent;
			themedCheckBox.BoxBackColor = AppleInput;
			themedCheckBox.BoxBorderColor = Color.Transparent;
			themedCheckBox.TextColor = appleText;
			themedCheckBox.Invalidate();
		}
		Button[] array3 = new Button[9] { _watchButton, _clearButton, _testPortButton, _relaunchButton, _browseButton, _backgroundOkButton, _imageOkButton, _panelImageBrowseButton, _chooseChatGptButton };
		foreach (Button button in array3)
		{
			if (button != null)
			{
				button.BackColor = AppleControl;
				button.ForeColor = appleText;
				button.FlatAppearance.BorderSize = 0;
				button.FlatAppearance.MouseOverBackColor = color;
				button.FlatAppearance.MouseDownBackColor = Blend(appleInput, appleAccent, 0.24);
				button.Invalidate();
			}
		}
		if (_applyButton != null)
		{
			_applyButton.BackColor = ApplePink;
			_applyButton.ForeColor = Color.White;
			_applyButton.FlatAppearance.MouseOverBackColor = ApplePink;
			_applyButton.FlatAppearance.MouseDownBackColor = ApplePink;
			if (_applyButton is RoundedButton roundedButton)
			{
				roundedButton.DisabledBackColor = Blend(ApplePink, AppleControl, 0.45);
				roundedButton.DisabledForeColor = Color.White;
			}
			_applyButton.Invalidate();
		}
		ApplyNativeWindowTheme();
	}

	private void ApplyNativeWindowTheme()
	{
		if (!base.IsHandleCreated)
		{
			return;
		}
		try
		{
			int attrValue = 1;
			DwmSetWindowAttribute(base.Handle, 20, ref attrValue, 4);
			DwmSetWindowAttribute(base.Handle, 19, ref attrValue, 4);
			int attrValue2 = ColorToColorRef(AppleBg);
			int attrValue3 = ColorToColorRef(AppleBg);
			int attrValue4 = ColorToColorRef(AppleText);
			DwmSetWindowAttribute(base.Handle, 34, ref attrValue2, 4);
			DwmSetWindowAttribute(base.Handle, 35, ref attrValue3, 4);
			DwmSetWindowAttribute(base.Handle, 36, ref attrValue4, 4);
			SetWindowPos(base.Handle, IntPtr.Zero, 0, 0, 0, 0, 55u);
		}
		catch
		{
		}
	}

	private static int ColorToColorRef(Color color)
	{
		return color.R | (color.G << 8) | (color.B << 16);
	}

	private void UpdateColorButtons()
	{
		PaintColorButton(_baseColorButton, "Base", _customTheme.Bg);
		PaintColorButton(_panelColorButton, "Panel", _customTheme.Panel);
		PaintColorButton(_inputColorButton, "Input", _customTheme.Input);
		PaintColorButton(_textColorButton, "Text", _customTheme.Text);
		PaintColorButton(_accentColorButton, "Accent", _customTheme.Accent);
		PaintColorButton(_borderColorButton, "Border", _customTheme.Border);
		PaintColorButton(_userColorButton, "User", _customTheme.User);
	}

	private void PaintColorButton(Button button, string text, string color)
	{
		if (button != null)
		{
			button.Text = text;
			if (button is ColorSwatchButton colorSwatchButton)
			{
				colorSwatchButton.LabelText = text;
				colorSwatchButton.HexText = NormalizeHex(color);
				colorSwatchButton.SwatchColor = ColorFromHex(color);
				colorSwatchButton.BackColor = Color.Transparent;
				colorSwatchButton.ForeColor = AppleText;
				colorSwatchButton.Invalidate();
			}
			else
			{
				button.BackColor = ColorFromHex(color);
				button.ForeColor = ReadableTextColor(button.BackColor);
				button.FlatAppearance.BorderSize = 0;
				button.FlatAppearance.BorderColor = button.BackColor;
				button.FlatAppearance.MouseOverBackColor = button.BackColor;
				button.FlatAppearance.MouseDownBackColor = button.BackColor;
			}
		}
	}

	private void UpdateBackgroundInputState()
	{
		string text = SelectedBackgroundMode();
		bool flag = text == "file";
		if (!flag && !string.IsNullOrEmpty(_backgroundTextBox.Text))
		{
			_backgroundTextBox.Text = "";
		}
		_backgroundTextBox.Enabled = true;
		_backgroundTextBox.ReadOnly = !flag;
		_backgroundTextBox.BackColor = Color.FromArgb(14, 17, 27);
		_backgroundTextBox.ForeColor = (flag ? AppleText : AppleMuted);
		_browseButton.Enabled = true;
		_backgroundTextBox.PlaceholderTextCompat("Select a local background image");
		if (_backgroundOkButton != null)
		{
			_backgroundOkButton.Enabled = true;
		}
		RefreshBackgroundPreview();
	}

	private void UpdatePanelImageInputState()
	{
		string text = SelectedPanelImageMode();
		bool flag = text == "file";
		if (_panelImageTextBox != null)
		{
			if (!flag && !string.IsNullOrEmpty(_panelImageTextBox.Text))
			{
				_panelImageTextBox.Text = "";
			}
			_panelImageTextBox.Enabled = true;
			_panelImageTextBox.ReadOnly = !flag;
			_panelImageTextBox.BackColor = Color.FromArgb(14, 17, 27);
			_panelImageTextBox.ForeColor = (flag ? AppleText : AppleMuted);
		}
		if (_panelImageBrowseButton != null)
		{
			_panelImageBrowseButton.Enabled = true;
		}
		if (_panelImageTextBox != null)
		{
			_panelImageTextBox.PlaceholderTextCompat("Select a local panel image");
		}
		RefreshPanelImagePreview();
	}

	private void BrowseBackground(object sender, EventArgs e)
	{
		if (_backgroundModeCombo != null && _backgroundModeCombo.SelectedIndex != 2)
		{
			_backgroundModeCombo.SelectedIndex = 2;
			UpdateBackgroundInputState();
		}
		using OpenFileDialog openFileDialog = new OpenFileDialog();
		openFileDialog.Filter = "Image files|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|All files|*.*";
		if (openFileDialog.ShowDialog(this) == DialogResult.OK)
		{
			_backgroundTextBox.Text = openFileDialog.FileName;
		}
	}

	private void BrowsePanelImage(object sender, EventArgs e)
	{
		if (_panelImageModeCombo != null && _panelImageModeCombo.SelectedIndex != 2)
		{
			_panelImageModeCombo.SelectedIndex = 2;
			UpdatePanelImageInputState();
		}
		using OpenFileDialog openFileDialog = new OpenFileDialog();
		openFileDialog.Filter = "Image files|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|All files|*.*";
		if (openFileDialog.ShowDialog(this) == DialogResult.OK)
		{
			_panelImageTextBox.Text = openFileDialog.FileName;
		}
	}

	private async void ChooseChatGptExe(object sender, EventArgs e)
	{
		using OpenFileDialog dialog = new OpenFileDialog();
		dialog.Title = "Choose ChatGPT.exe";
		dialog.Filter = "ChatGPT executable|ChatGPT.exe|Executables|*.exe|All files|*.*";
		if (dialog.ShowDialog(this) == DialogResult.OK)
		{
			if (!IsChatGptExecutablePath(dialog.FileName))
			{
				SetStatus("That does not look like the official ChatGPT.exe. Choose the ChatGPT Desktop executable.", isError: true);
				return;
			}
			_manualChatGptExePath = dialog.FileName;
			SaveSettings();
			await RefreshChatGptDetectionStatusAsync(verbose: true);
			SetStatus("Saved manual ChatGPT.exe location.", isError: false);
		}
	}

	private async Task RefreshChatGptDetectionStatusAsync(bool verbose)
	{
		ChatGptDetectionResult result = await Task.Run(() => DetectChatGpt());
		UpdateChatGptDetectionUi(result);
		if (verbose)
		{
			SetStatus(result.Summary + " " + result.Diagnostics, !result.Found);
		}
	}

	private void UpdateChatGptDetectionUi(ChatGptDetectionResult result)
	{
		if (result != null)
		{
			if (_chatGptStatusLabel != null)
			{
				_chatGptStatusLabel.Text = (result.Found ? "ChatGPT Detected - Ready" : "ChatGPT Not Detected");
				_chatGptStatusLabel.ForeColor = (result.Found ? Color.FromArgb(126, 242, 196) : Color.FromArgb(255, 105, 97));
			}
			if (_chatGptDiagnosticsLabel != null)
			{
				_chatGptDiagnosticsLabel.Text = (result.Found ? result.Summary : result.Diagnostics);
			}
			if (_chooseChatGptButton != null)
			{
				_chooseChatGptButton.Text = (result.Found ? "Auto Detected" : "Choose ChatGPT.exe");
				_chooseChatGptButton.Enabled = !result.Found;
			}
		}
	}

	private ChatGptDetectionResult DetectChatGpt()
	{
		ChatGptDetectionResult chatGptDetectionResult = new ChatGptDetectionResult();
		try
		{
			foreach (string item in RunningChatGptExePaths())
			{
				if (UseDetectedChatGptExe(chatGptDetectionResult, item, "running ChatGPT process", "Found a running ChatGPT process."))
				{
					return chatGptDetectionResult;
				}
			}
			chatGptDetectionResult.Checks.Add("No running ChatGPT process was found.");
		}
		catch
		{
			chatGptDetectionResult.Checks.Add("Process check failed.");
		}
		foreach (string item2 in CommonChatGptExePathCandidates())
		{
			if (UseDetectedChatGptExe(chatGptDetectionResult, item2, "installed app", "Found ChatGPT in an installed app location."))
			{
				return chatGptDetectionResult;
			}
		}
		chatGptDetectionResult.Checks.Add("Common per-user and Program Files install locations did not contain ChatGPT.exe.");
		foreach (ChatGptShortcutCandidate item3 in FindChatGptStartMenuCandidates())
		{
			if (UseDetectedChatGptExe(chatGptDetectionResult, item3.ExePath, "Start Menu shortcut", "Found ChatGPT through a Start Menu shortcut."))
			{
				return chatGptDetectionResult;
			}
			if (!string.IsNullOrWhiteSpace(item3.AppUserModelId))
			{
				chatGptDetectionResult.Found = true;
				chatGptDetectionResult.AppUserModelId = PreferredChatGptAppUserModelId(item3.AppUserModelId);
				chatGptDetectionResult.Method = "Start Menu app shortcut";
				chatGptDetectionResult.Checks.Add("Found ChatGPT through a Start Menu app shortcut.");
				return chatGptDetectionResult;
			}
		}
		chatGptDetectionResult.Checks.Add("Start Menu shortcuts did not reveal ChatGPT.");
		string text = FindChatGptAppUserModelIdFromWindowsAppsAlias();
		if (!string.IsNullOrWhiteSpace(text))
		{
			chatGptDetectionResult.Found = true;
			chatGptDetectionResult.AppUserModelId = PreferredChatGptAppUserModelId(text);
			chatGptDetectionResult.Method = "Windows app package alias";
			chatGptDetectionResult.Checks.Add("Found ChatGPT through the current user's Windows app alias.");
			return chatGptDetectionResult;
		}
		string manualChatGptExePath = _manualChatGptExePath;
		if (!string.IsNullOrWhiteSpace(manualChatGptExePath))
		{
			if (UseDetectedChatGptExe(chatGptDetectionResult, manualChatGptExePath, "manual fallback", "Manual executable is valid."))
			{
				return chatGptDetectionResult;
			}
			chatGptDetectionResult.Checks.Add("Manual executable is missing or invalid.");
		}
		else
		{
			chatGptDetectionResult.Checks.Add("No manual executable has been selected.");
		}
		chatGptDetectionResult.AppUserModelId = PreferredChatGptAppUserModelId(TryFindChatGptAppUserModelId());
		chatGptDetectionResult.Checks.Add("Manual chooser is available as a fallback.");
		return chatGptDetectionResult;
	}

	private bool UseDetectedChatGptExe(ChatGptDetectionResult result, string path, string method, string checkMessage)
	{
		if (!IsChatGptExecutablePath(path))
		{
			return false;
		}
		result.Found = true;
		result.ExePath = Path.GetFullPath(path);
		result.Method = method;
		result.AppUserModelId = PreferredChatGptAppUserModelId(AppUserModelIdFromPath(path));
		if (!string.IsNullOrEmpty(checkMessage))
		{
			result.Checks.Add(checkMessage);
		}
		return true;
	}

	private static IEnumerable<string> RunningChatGptExePaths()
	{
		List<string> list = new List<string>();
		try
		{
			Process[] processesByName = Process.GetProcessesByName("ChatGPT");
			foreach (Process process in processesByName)
			{
				try
				{
					string fileName = process.MainModule.FileName;
					if (!string.IsNullOrWhiteSpace(fileName))
					{
						list.Add(fileName);
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		return list;
	}

	private static IEnumerable<string> CommonChatGptExePathCandidates()
	{
		List<string> list = new List<string>();
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		AddCandidatePath(list, Path.Combine(folderPath, "Programs", "ChatGPT", "ChatGPT.exe"));
		AddCandidatePath(list, Path.Combine(folderPath, "Programs", "OpenAI ChatGPT", "ChatGPT.exe"));
		AddCandidatePath(list, Path.Combine(folderPath, "ChatGPT", "ChatGPT.exe"));
		AddCandidatePath(list, Path.Combine(folderPath, "Microsoft", "WindowsApps", "chatgpt.exe"));
		AddCandidatePath(list, Path.Combine(folderPath, "Microsoft", "WindowsApps", "OpenAI.ChatGPT-Desktop_2p2nqsd0c76g0", "chatgpt.exe"));
		string[] array = new string[2]
		{
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
			Environment.GetEnvironmentVariable("ProgramFiles(x86)")
		};
		foreach (string text in array)
		{
			if (!string.IsNullOrWhiteSpace(text))
			{
				AddCandidatePath(list, Path.Combine(text, "ChatGPT", "ChatGPT.exe"));
				AddCandidatePath(list, Path.Combine(text, "OpenAI", "ChatGPT", "ChatGPT.exe"));
				AddCandidatePath(list, Path.Combine(text, "OpenAI ChatGPT", "ChatGPT.exe"));
			}
		}
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
		if (Directory.Exists(path))
		{
			try
			{
				foreach (string item in from result in Directory.GetDirectories(path, "OpenAI.ChatGPT-Desktop_*")
					orderby result descending
					select result)
				{
					AddCandidatePath(list, Path.Combine(item, "app", "ChatGPT.exe"));
				}
			}
			catch
			{
			}
		}
		return list;
	}

	private static void AddCandidatePath(List<string> candidates, string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}
		foreach (string candidate in candidates)
		{
			if (string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
		}
		candidates.Add(path);
	}

	private static IEnumerable<ChatGptShortcutCandidate> FindChatGptStartMenuCandidates()
	{
		List<ChatGptShortcutCandidate> list = new List<ChatGptShortcutCandidate>();
		string[] array = new string[2]
		{
			Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
			Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
		};
		foreach (string text in array)
		{
			if (string.IsNullOrWhiteSpace(text) || !Directory.Exists(text))
			{
				continue;
			}
			IEnumerable<string> enumerable;
			try
			{
				enumerable = Directory.EnumerateFiles(text, "*.lnk", SearchOption.AllDirectories).ToArray();
			}
			catch
			{
				continue;
			}
			foreach (string item in enumerable)
			{
				string text2 = Path.GetFileNameWithoutExtension(item) ?? "";
				if (text2.IndexOf("ChatGPT", StringComparison.OrdinalIgnoreCase) >= 0 || text2.IndexOf("OpenAI", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					ChatGptShortcutCandidate chatGptShortcutCandidate = ReadChatGptShortcutCandidate(item);
					if (chatGptShortcutCandidate != null)
					{
						list.Add(chatGptShortcutCandidate);
					}
				}
			}
		}
		return list;
	}

	private static ChatGptShortcutCandidate ReadChatGptShortcutCandidate(string shortcutPath)
	{
		object obj = null;
		object obj2 = null;
		try
		{
			Type typeFromProgID = Type.GetTypeFromProgID("WScript.Shell");
			if (typeFromProgID == null)
			{
				return null;
			}
			obj = Activator.CreateInstance(typeFromProgID);
			obj2 = typeFromProgID.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, obj, new object[1] { shortcutPath });
			if (obj2 == null)
			{
				return null;
			}
			Type type = obj2.GetType();
			string text = Convert.ToString(type.InvokeMember("TargetPath", BindingFlags.GetProperty, null, obj2, null));
			string text2 = Convert.ToString(type.InvokeMember("Arguments", BindingFlags.GetProperty, null, obj2, null));
			if (IsChatGptExecutablePath(text))
			{
				ChatGptShortcutCandidate chatGptShortcutCandidate = new ChatGptShortcutCandidate();
				chatGptShortcutCandidate.ExePath = text;
				chatGptShortcutCandidate.ShortcutPath = shortcutPath;
				return chatGptShortcutCandidate;
			}
			string text3 = ExtractChatGptAppUserModelId((text ?? "") + " " + (text2 ?? ""));
			if (!string.IsNullOrEmpty(text3))
			{
				ChatGptShortcutCandidate chatGptShortcutCandidate2 = new ChatGptShortcutCandidate();
				chatGptShortcutCandidate2.AppUserModelId = text3;
				chatGptShortcutCandidate2.ShortcutPath = shortcutPath;
				return chatGptShortcutCandidate2;
			}
		}
		catch
		{
		}
		finally
		{
			try
			{
				if (obj2 != null && Marshal.IsComObject(obj2))
				{
					Marshal.FinalReleaseComObject(obj2);
				}
				if (obj != null && Marshal.IsComObject(obj))
				{
					Marshal.FinalReleaseComObject(obj);
				}
			}
			catch
			{
			}
		}
		return null;
	}

	private static string ExtractChatGptAppUserModelId(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "";
		}
		Match match = Regex.Match(value, "OpenAI\\.ChatGPT-Desktop_[^\\s\"']+!ChatGPT", RegexOptions.IgnoreCase);
		return match.Success ? match.Value : "";
	}

	private void ToggleWatch(object sender, EventArgs e)
	{
		if (_watchTimer.Enabled)
		{
			_watchTimer.Stop();
			_watchButton.Text = "Watch: Off";
			SetStatus("Watch stopped.", isError: false);
		}
		else
		{
			SaveSettings();
			_watchTimer.Start();
			_watchButton.Text = "Watch: On";
			SetStatus("Watch is on. The skin will be reapplied automatically.", isError: false);
		}
	}

	private async Task ApplySkinAsync(bool quiet)
	{
		if (_applying)
		{
			return;
		}
		_applying = true;
		try
		{
			SaveSettings();
			_previewSkinEnabled = true;
			if (_previewPanel != null)
			{
				_previewPanel.Invalidate();
			}
			Theme theme = CurrentTheme();
			List<string> imageWarnings = new List<string>();
			string backgroundImageDataUrl = ResolveBackgroundImageDataUrl(SelectedBackgroundMode(), _backgroundTextBox.Text.Trim(), imageWarnings);
			string css = BuildCss(panelImageDataUrl: ResolvePanelImageDataUrl(backgroundImageDataUrl, imageWarnings), theme: theme, layout: SelectedLayout(), backgroundMode: SelectedBackgroundMode(), backgroundValue: _backgroundTextBox.Text.Trim(), transparency: SelectedTransparency(), backgroundImageDataUrl: backgroundImageDataUrl, glassSearch: SelectedGlassSearch(), fontFamily: SelectedFontFamily());
			string expression = BuildInjectionExpression(css);
			int port = SelectedPort();
			List<CdpTarget> targets;
			try
			{
				targets = await GetTargetsAsync(port);
			}
			catch
			{
				if (quiet)
				{
					return;
				}
				targets = new List<CdpTarget>();
			}
			if (targets.Count == 0)
			{
				if (quiet)
				{
					return;
				}
				SetStatus("Opening ChatGPT with the local skinning port " + port + "...", isError: false);
				targets = await StartOrRestartChatGptAndWaitAsync(port, restartRunning: true, "Opening ChatGPT with the local skinning port " + port);
				if (targets.Count == 0)
				{
					SetStatus("ChatGPT opened, but the local skinning port " + port + " is not ready yet. Try Apply again after it finishes loading.", isError: true);
					return;
				}
			}
			foreach (CdpTarget target in targets)
			{
				await SendEvaluateAsync(target.WebSocketDebuggerUrl, expression, port);
			}
			_activeTheme = CaptureCurrentAppliedTheme(theme);
			_activeThemeEnabled = _activeTheme != null;
			SaveSettings();
			UpdateActiveThemeLabel();
			if (!quiet)
			{
				SetStatus((imageWarnings.Count == 0) ? ("Applied " + theme.Name + " to ChatGPT.") : ("Applied " + theme.Name + " to ChatGPT with image fallback: " + imageWarnings[0]), isError: false);
			}
		}
		catch (Exception ex)
		{
			AppendDiagnosticLog("apply", "Skin apply failed.", ex);
			SetStatus(SafeExceptionMessage(ex), isError: true);
		}
		finally
		{
			_applying = false;
		}
	}

	private async Task RestoreActiveThemeOnStartupAsync()
	{
		UpdateActiveThemeLabel();
		if (_activeThemeEnabled && _activeTheme != null)
		{
			SetStatus("Active theme loaded: " + ActiveThemeDisplayName() + ". Restoring ChatGPT theme...", isError: false);
			await ApplySkinAsync(quiet: false);
		}
	}

	private async Task TestPortAsync(bool quiet)
	{
		try
		{
			List<CdpTarget> targets = await GetTargetsAsync(SelectedPort());
			if (targets.Count == 0)
			{
				SetStatus("Port " + SelectedPort() + " is open, but no ChatGPT page target was found yet.", isError: true);
			}
			else if (!quiet)
			{
				SetStatus("Connected to ChatGPT on port " + SelectedPort() + " (" + targets.Count + " target(s)).", isError: false);
			}
		}
		catch (Exception ex)
		{
			SetStatus(SafeExceptionMessage(ex), isError: true);
		}
	}

	private async Task OpenWithChatGptAsync()
	{
		int port = SelectedPort();
		try
		{
			SetStatus("Opening ChatGPT with the local theming port " + port + "...", isError: false);
			if ((await GetTargetsAsync(port)).Count > 0)
			{
				FocusChatGptWindow();
				if (_activeThemeEnabled && _activeTheme != null)
				{
					await ApplySkinAsync(quiet: false);
				}
				else
				{
					SetStatus("ChatGPT is open on port " + port + ". Apply a theme when ready.", isError: false);
				}
				return;
			}
		}
		catch
		{
		}
		try
		{
			List<CdpTarget> targets = await StartOrRestartChatGptAndWaitAsync(port, restartRunning: true, "Opening ChatGPT with the local theming port " + port);
			FocusChatGptWindow();
			if (targets.Count == 0)
			{
				SetStatus("ChatGPT opened, but the local theming port " + port + " is not ready. Use Relaunch with Port if Apply cannot connect.", isError: true);
			}
			else if (_activeThemeEnabled && _activeTheme != null)
			{
				await ApplySkinAsync(quiet: false);
			}
			else
			{
				SetStatus("ChatGPT is open on port " + port + ". Apply a theme when ready.", isError: false);
			}
		}
		catch (Exception ex)
		{
			SetStatus("Could not open ChatGPT: " + SafeExceptionMessage(ex), isError: true);
		}
	}

	private async Task RelaunchChatGptWithPortAsync()
	{
		int port = SelectedPort();
		DialogResult answer = MessageBox.Show(this, "This will close the running ChatGPT desktop app and reopen it with the local skinning port " + port + ".", "Relaunch ChatGPT?", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation);
		if (answer != DialogResult.OK)
		{
			return;
		}
		try
		{
			List<CdpTarget> targets = await StartOrRestartChatGptAndWaitAsync(port, restartRunning: true, "Relaunching ChatGPT with the local skinning port " + port);
			FocusChatGptWindow();
			SetStatus((targets.Count > 0) ? ("ChatGPT is ready on port " + port + ". Apply a skin when ready.") : ("ChatGPT relaunched, but the local skinning port " + port + " is not ready yet. Try Apply again after it finishes loading."), targets.Count == 0);
		}
		catch (Exception ex)
		{
			SetStatus("Could not relaunch ChatGPT: " + SafeExceptionMessage(ex), isError: true);
		}
	}

	private async Task<List<CdpTarget>> StartOrRestartChatGptAndWaitAsync(int port, bool restartRunning, string statusPrefix)
	{
		return await StartOrRestartChatGptAndWaitAsync(port, restartRunning, statusPrefix, 28);
	}

	private async Task<List<CdpTarget>> StartOrRestartChatGptAndWaitAsync(int port, bool restartRunning, string statusPrefix, int waitAttempts)
	{
		ChatGptDetectionResult detection = DetectChatGpt();
		UpdateChatGptDetectionUi(detection);
		string preferredExePath = ((!string.IsNullOrWhiteSpace(detection.ExePath)) ? detection.ExePath : TryFindChatGptExePath());
		string preferredAppId = NonEmpty(detection.AppUserModelId, TryFindChatGptAppUserModelId());
		bool chatGptRunning = CountValidatedChatGptProcesses() > 0;
		if (restartRunning && chatGptRunning)
		{
			SetStatus("Restarting ChatGPT with the local skinning port " + port + "...", isError: false);
			StopChatGptProcesses();
		}
		StartChatGptWithPort(port, preferredExePath, preferredAppId, out var launchMethod);
		SetStatus(statusPrefix + " using " + launchMethod + "...", isError: false);
		for (int i = 0; i < waitAttempts; i++)
		{
			await Task.Delay(500);
			try
			{
				List<CdpTarget> targets = await GetTargetsAsync(port);
				if (targets.Count > 0)
				{
					if (!FocusChatGptWindow())
					{
						OpenChatGptNormally(preferredAppId, out launchMethod);
						await WaitForChatGptWindowAsync(12, 250);
					}
					return targets;
				}
			}
			catch
			{
			}
		}
		return new List<CdpTarget>();
	}

	private static void StopChatGptProcesses()
	{
		Process[] processesByName = Process.GetProcessesByName("ChatGPT");
		foreach (Process process in processesByName)
		{
			try
			{
				if (IsValidatedChatGptProcess(process))
				{
					process.Kill();
					process.WaitForExit(2500);
				}
			}
			catch
			{
			}
		}
		for (int j = 0; j < 20; j++)
		{
			try
			{
				if (CountValidatedChatGptProcesses() == 0)
				{
					break;
				}
			}
			catch
			{
				break;
			}
			Thread.Sleep(150);
		}
	}

	private static int CountValidatedChatGptProcesses()
	{
		int num = 0;
		Process[] processesByName = Process.GetProcessesByName("ChatGPT");
		foreach (Process process in processesByName)
		{
			try
			{
				if (IsValidatedChatGptProcess(process))
				{
					num++;
				}
			}
			catch
			{
			}
		}
		return num;
	}

	private static bool IsValidatedChatGptProcess(Process process)
	{
		if (process == null)
		{
			return false;
		}
		try
		{
			return IsChatGptExecutablePath(process.MainModule.FileName);
		}
		catch
		{
			return false;
		}
	}

	private void StartChatGptWithPort(int port, out string launchMethod)
	{
		StartChatGptWithPort(port, "", "", out launchMethod);
	}

	private void StartChatGptWithPort(int port, string preferredExePath, string preferredAppId, out string launchMethod)
	{
		string arguments = "--remote-debugging-address=127.0.0.1 --remote-debugging-port=" + port;
		List<string> list = new List<string>();
		string text = (IsChatGptExecutablePath(preferredExePath) ? preferredExePath : "");
		try
		{
			if (string.IsNullOrEmpty(text))
			{
				text = FindChatGptExePath();
			}
		}
		catch (Exception ex)
		{
			list.Add("installed EXE lookup: " + ex.Message);
		}
		if (!string.IsNullOrEmpty(text) && !IsWindowsAppsPath(text))
		{
			try
			{
				ProcessStartInfo processStartInfo = new ProcessStartInfo();
				processStartInfo.FileName = text;
				processStartInfo.Arguments = arguments;
				processStartInfo.UseShellExecute = false;
				string directoryName = Path.GetDirectoryName(text);
				if (!string.IsNullOrEmpty(directoryName) && Directory.Exists(directoryName))
				{
					processStartInfo.WorkingDirectory = directoryName;
				}
				Process.Start(processStartInfo);
				launchMethod = (IsWindowsAppsPath(text) ? "packaged ChatGPT.exe" : "ChatGPT.exe");
				return;
			}
			catch (Exception ex)
			{
				list.Add("installed EXE: " + ex.Message);
			}
		}
		else if (!string.IsNullOrEmpty(text))
		{
			list.Add("packaged ChatGPT.exe with port: skipped because Windows app package paths cannot be launched safely by direct file path.");
		}
		if (!string.IsNullOrEmpty(text) && IsChatGptAppExecutionAlias(text))
		{
			try
			{
				ProcessStartInfo processStartInfo2 = new ProcessStartInfo();
				processStartInfo2.FileName = text;
				processStartInfo2.Arguments = arguments;
				processStartInfo2.UseShellExecute = true;
				Process.Start(processStartInfo2);
				launchMethod = "ChatGPT app alias";
				return;
			}
			catch (Exception ex)
			{
				list.Add("chatgpt.exe alias with port: " + ex.Message);
			}
		}
		else
		{
			string text2 = FindChatGptAppExecutionAliasPath();
			if (!string.IsNullOrEmpty(text2))
			{
				try
				{
					ProcessStartInfo processStartInfo2 = new ProcessStartInfo();
					processStartInfo2.FileName = text2;
					processStartInfo2.Arguments = arguments;
					processStartInfo2.UseShellExecute = true;
					Process.Start(processStartInfo2);
					launchMethod = "ChatGPT app alias";
					return;
				}
				catch (Exception ex)
				{
					list.Add("chatgpt.exe alias with port: " + ex.Message);
				}
			}
			else
			{
				list.Add("chatgpt.exe alias with port: no current-user app alias was found.");
			}
		}
		if (TryActivateChatGptPackage(preferredAppId, arguments, "Windows app activation with port", list, out launchMethod))
		{
			return;
		}
		try
		{
			OpenChatGptNormally(preferredAppId, out launchMethod);
			return;
		}
		catch (Exception ex)
		{
			list.Add("Windows app entry: " + ex.Message);
		}
		throw new InvalidOperationException("Could not start ChatGPT. " + string.Join(" | ", list.ToArray()));
	}

	private void OpenChatGptNormally(string preferredAppId, out string launchMethod)
	{
		List<string> list = new List<string>();
		if (TryActivateChatGptPackage(preferredAppId, "", "Windows app activation", list, out launchMethod))
		{
			return;
		}
		try
		{
			ProcessStartInfo processStartInfo = new ProcessStartInfo();
			processStartInfo.FileName = "explorer.exe";
			processStartInfo.Arguments = "shell:AppsFolder\\" + PreferredChatGptAppUserModelId(preferredAppId);
			processStartInfo.UseShellExecute = false;
			Process.Start(processStartInfo);
			launchMethod = "Windows app entry";
			return;
		}
		catch (Exception ex)
		{
			list.Add("Windows app entry: " + ex.Message);
		}
		try
		{
			ProcessStartInfo processStartInfo2 = new ProcessStartInfo();
			processStartInfo2.FileName = "chatgpt.exe";
			processStartInfo2.UseShellExecute = true;
			Process.Start(processStartInfo2);
			launchMethod = "chatgpt.exe alias";
			return;
		}
		catch (Exception ex)
		{
			list.Add("app alias: " + ex.Message);
		}
		try
		{
			string text = TryFindChatGptExePath();
			if (!string.IsNullOrEmpty(text) && !IsWindowsAppsPath(text))
			{
				ProcessStartInfo processStartInfo3 = new ProcessStartInfo();
				processStartInfo3.FileName = text;
				processStartInfo3.UseShellExecute = false;
				string directoryName = Path.GetDirectoryName(text);
				if (!string.IsNullOrEmpty(directoryName) && Directory.Exists(directoryName))
				{
					processStartInfo3.WorkingDirectory = directoryName;
				}
				Process.Start(processStartInfo3);
				launchMethod = "ChatGPT.exe";
				return;
			}
		}
		catch (Exception ex)
		{
			list.Add("installed EXE: " + ex.Message);
		}
		throw new InvalidOperationException(string.Join(" | ", list.ToArray()));
	}

	private bool TryActivateChatGptPackage(string preferredAppId, string arguments, string methodName, List<string> errors, out string launchMethod)
	{
		launchMethod = "";
		try
		{
			string appUserModelId = PreferredChatGptAppUserModelId(preferredAppId);
			IApplicationActivationManager applicationActivationManager = (IApplicationActivationManager)new ApplicationActivationManager();
			int processId;
			int num = applicationActivationManager.ActivateApplication(appUserModelId, arguments ?? "", ActivateOptions.None, out processId);
			if (num >= 0)
			{
				launchMethod = methodName;
				return true;
			}
			uint num2 = (uint)num;
			errors.Add(methodName + ": activation failed 0x" + num2.ToString("X8"));
		}
		catch (Exception ex)
		{
			errors.Add(methodName + ": " + ex.Message);
		}
		return false;
	}

	private string PreferredChatGptAppUserModelId(string preferredAppId)
	{
		if (!string.IsNullOrWhiteSpace(preferredAppId))
		{
			return preferredAppId;
		}
		return "OpenAI.ChatGPT-Desktop_2p2nqsd0c76g0!ChatGPT";
	}

	private string TryFindChatGptExePath()
	{
		try
		{
			return FindChatGptExePath();
		}
		catch
		{
			return "";
		}
	}

	private string TryFindChatGptAppUserModelId()
	{
		try
		{
			return FindChatGptAppUserModelId();
		}
		catch
		{
			return "";
		}
	}

	private bool FocusChatGptWindow()
	{
		try
		{
			Process[] processesByName = Process.GetProcessesByName("ChatGPT");
			foreach (Process process in processesByName)
			{
				try
				{
					process.Refresh();
					IntPtr mainWindowHandle = process.MainWindowHandle;
					if (mainWindowHandle == IntPtr.Zero)
					{
						continue;
					}
					ShowWindow(mainWindowHandle, 9);
					SetForegroundWindow(mainWindowHandle);
					return true;
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		return false;
	}

	private async Task<bool> WaitForChatGptWindowAsync(int attempts, int delayMs)
	{
		for (int i = 0; i < attempts; i++)
		{
			if (FocusChatGptWindow())
			{
				return true;
			}
			await Task.Delay(delayMs);
		}
		return false;
	}

	private string FindChatGptAppUserModelId()
	{
		string text = "OpenAI.ChatGPT-Desktop_2p2nqsd0c76g0";
		try
		{
			Process[] processesByName = Process.GetProcessesByName("ChatGPT");
			foreach (Process process in processesByName)
			{
				try
				{
					string fileName = process.MainModule.FileName;
					string text2 = PackageFamilyFromWindowsAppsPath(fileName);
					if (!string.IsNullOrEmpty(text2))
					{
						return text2 + "!ChatGPT";
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		foreach (ChatGptShortcutCandidate item in FindChatGptStartMenuCandidates())
		{
			if (!string.IsNullOrWhiteSpace(item.AppUserModelId))
			{
				return item.AppUserModelId;
			}
			string text3 = AppUserModelIdFromPath(item.ExePath);
			if (!string.IsNullOrWhiteSpace(text3))
			{
				return text3;
			}
		}
		string text4 = FindChatGptAppUserModelIdFromWindowsAppsAlias();
		if (!string.IsNullOrWhiteSpace(text4))
		{
			return text4;
		}
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
		if (Directory.Exists(path))
		{
			try
			{
				foreach (string item2 in from result in Directory.GetDirectories(path, "OpenAI.ChatGPT-Desktop_*")
					orderby result descending
					select result)
				{
					string text5 = PackageFamilyFromWindowsAppsPath(Path.Combine(item2, "app", "ChatGPT.exe"));
					if (!string.IsNullOrWhiteSpace(text5))
					{
						return text5 + "!ChatGPT";
					}
				}
			}
			catch
			{
			}
		}
		return text + "!ChatGPT";
	}

	private static string FindChatGptAppUserModelIdFromWindowsAppsAlias()
	{
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");
		if (!Directory.Exists(text))
		{
			return "";
		}
		try
		{
			foreach (string item in from result in Directory.GetDirectories(text, "OpenAI.ChatGPT-Desktop_*")
				orderby result descending
				select result)
			{
				string fileName = Path.GetFileName(item);
				if (!string.IsNullOrWhiteSpace(fileName) && fileName.StartsWith("OpenAI.ChatGPT-Desktop_", StringComparison.OrdinalIgnoreCase) && fileName.EndsWith("_2p2nqsd0c76g0", StringComparison.OrdinalIgnoreCase))
				{
					return fileName + "!ChatGPT";
				}
			}
		}
		catch
		{
		}
		string path = Path.Combine(text, "chatgpt.exe");
		return File.Exists(path) ? "OpenAI.ChatGPT-Desktop_2p2nqsd0c76g0!ChatGPT" : "";
	}

	private static string AppUserModelIdFromPath(string path)
	{
		string text = PackageFamilyFromWindowsAppsPath(path);
		return string.IsNullOrEmpty(text) ? "" : (text + "!ChatGPT");
	}

	private static string PackageFamilyFromWindowsAppsPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return "";
		}
		string text = path.Replace('/', '\\');
		string text2 = "\\WindowsApps\\";
		int num = text.IndexOf(text2, StringComparison.OrdinalIgnoreCase);
		if (num < 0)
		{
			return "";
		}
		num += text2.Length;
		int num2 = text.IndexOf("\\", num, StringComparison.OrdinalIgnoreCase);
		if (num2 <= num)
		{
			return "";
		}
		string text3 = text.Substring(num, num2 - num);
		int num3 = text3.IndexOf('_');
		int num4 = text3.LastIndexOf("__", StringComparison.Ordinal);
		if (num3 > 0 && num4 < 0 && text3.StartsWith("OpenAI.ChatGPT-Desktop_", StringComparison.OrdinalIgnoreCase) && text3.EndsWith("_2p2nqsd0c76g0", StringComparison.OrdinalIgnoreCase))
		{
			return text3;
		}
		if (num3 <= 0 || num4 < 0 || num4 + 2 >= text3.Length)
		{
			return "";
		}
		return text3.Substring(0, num3) + "_" + text3.Substring(num4 + 2);
	}

	private string FindChatGptExePath()
	{
		foreach (string item in RunningChatGptExePaths())
		{
			if (IsChatGptExecutablePath(item))
			{
				return Path.GetFullPath(item);
			}
		}
		foreach (string item2 in CommonChatGptExePathCandidates())
		{
			if (IsChatGptExecutablePath(item2))
			{
				return Path.GetFullPath(item2);
			}
		}
		foreach (ChatGptShortcutCandidate item3 in FindChatGptStartMenuCandidates())
		{
			if (IsChatGptExecutablePath(item3.ExePath))
			{
				return Path.GetFullPath(item3.ExePath);
			}
		}
		if (IsChatGptExecutablePath(_manualChatGptExePath))
		{
			return Path.GetFullPath(_manualChatGptExePath);
		}
		throw new InvalidOperationException("Could not find a real ChatGPT.exe. Open ChatGPT from the Start menu once, then try Relaunch with Port again.");
	}

	private static bool IsChatGptExecutablePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}
		string fullPath;
		try
		{
			fullPath = Path.GetFullPath(path);
		}
		catch
		{
			return false;
		}
		if (!File.Exists(fullPath))
		{
			return false;
		}
		string fileName = Path.GetFileName(fullPath);
		if (!string.Equals(fileName, "ChatGPT.exe", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		string text = fullPath.Replace('/', '\\');
		if (text.IndexOf("OpenAI.Codex", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return false;
		}
		if (text.IndexOf("\\Codex", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return false;
		}
		if (IsChatGptAppExecutionAlias(text))
		{
			return true;
		}
		if (IsExpectedPackagedChatGptPath(text))
		{
			return true;
		}
		return HasTrustedChatGptSignature(fullPath);
	}

	private static bool IsChatGptAppExecutionAlias(string normalizedPath)
	{
		if (string.IsNullOrWhiteSpace(normalizedPath))
		{
			return false;
		}
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps").Replace('/', '\\').TrimEnd('\\');
		string b = text + "\\chatgpt.exe";
		string b2 = text + "\\OpenAI.ChatGPT-Desktop_2p2nqsd0c76g0\\chatgpt.exe";
		return string.Equals(normalizedPath, b, StringComparison.OrdinalIgnoreCase) || string.Equals(normalizedPath, b2, StringComparison.OrdinalIgnoreCase);
	}

	private static string FindChatGptAppExecutionAliasPath()
	{
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");
		string text = Path.Combine(path, "OpenAI.ChatGPT-Desktop_2p2nqsd0c76g0", "chatgpt.exe");
		if (File.Exists(text))
		{
			return text;
		}
		string text2 = Path.Combine(path, "chatgpt.exe");
		return File.Exists(text2) ? text2 : "";
	}

	private static bool IsExpectedPackagedChatGptPath(string normalizedPath)
	{
		if (string.IsNullOrWhiteSpace(normalizedPath))
		{
			return false;
		}
		return normalizedPath.IndexOf("\\WindowsApps\\OpenAI.ChatGPT-Desktop_", StringComparison.OrdinalIgnoreCase) >= 0 && normalizedPath.EndsWith("\\app\\ChatGPT.exe", StringComparison.OrdinalIgnoreCase);
	}

	private static bool HasTrustedChatGptSignature(string path)
	{
		try
		{
			X509Certificate2 x509Certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
			X509Chain x509Chain = new X509Chain();
			x509Chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
			x509Chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
			if (!x509Chain.Build(x509Certificate))
			{
				return false;
			}
			string text = (x509Certificate.Subject + " " + x509Certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false) + " " + x509Certificate.GetNameInfo(X509NameType.DnsName, forIssuer: false)).ToLowerInvariant();
			return text.IndexOf("openai", StringComparison.Ordinal) >= 0;
		}
		catch
		{
			return false;
		}
	}

	private static bool IsWindowsAppsPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}
		string text = path.Replace('/', '\\');
		return text.IndexOf("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private async Task ClearSkinAsync()
	{
		_activeThemeEnabled = false;
		_activeTheme = null;
		_previewSkinEnabled = false;
		SaveSettings();
		UpdateActiveThemeLabel();
		if (_previewPanel != null)
		{
			_previewPanel.Invalidate();
		}
		try
		{
			List<CdpTarget> targets = await GetTargetsAsync(SelectedPort());
			if (targets.Count == 0)
			{
				SetStatus("Default ChatGPT theme restored. No running ChatGPT target needed clearing.", isError: false);
				return;
			}
			string styleLiteral = _json.Serialize("chatgpt-desktop-skinner-style");
			string expression = "(() => { if (window.__cgtdsResizeHandler) window.removeEventListener('resize', window.__cgtdsResizeHandler); window.clearTimeout(window.__cgtdsResizeTimer); if (window.__cgtdsSidebarObserver) window.__cgtdsSidebarObserver.disconnect(); window.clearTimeout(window.__cgtdsSidebarTimer); const s = document.getElementById(" + styleLiteral + "); if (s) s.remove(); document.querySelectorAll('[data-cgtds-surface-fix]').forEach((el) => { ['background','background-color','background-image','background-position','background-repeat','background-size','border','border-color','border-style','border-width','border-radius','box-shadow','outline','overflow','color','pointer-events','position','isolation','z-index','mask-image','-webkit-mask-image','backdrop-filter','-webkit-backdrop-filter','--tw-ring-color','--tw-ring-shadow','--tw-shadow','--tw-shadow-colored'].forEach((name) => el.style.removeProperty(name)); el.removeAttribute('data-cgtds-surface-fix'); }); delete window.__cgtdsSidebarObserver; delete document.documentElement.dataset.chatgptDesktopSkinner; return true; })()";
			foreach (CdpTarget target in targets)
			{
				await SendEvaluateAsync(target.WebSocketDebuggerUrl, expression, SelectedPort());
			}
			SetStatus("Default ChatGPT theme restored.", isError: false);
		}
		catch (Exception ex)
		{
			SetStatus("Default ChatGPT theme restored for future sessions. Current ChatGPT session was not reachable: " + SafeExceptionMessage(ex), isError: false);
		}
	}

	private async Task<List<CdpTarget>> GetTargetsAsync(int port)
	{
		if (!IsPortOwnedByValidatedChatGptProcess(port))
		{
			throw new InvalidOperationException("Port " + port + " is not owned by a verified ChatGPT Desktop process. Use Relaunch with Port to start ChatGPT safely on localhost.");
		}
		string url = "http://127.0.0.1:" + port + "/json/list";
		string json;
		using (WebClient client = new WebClient())
		{
			client.Proxy = null;
			try
			{
				json = await client.DownloadStringTaskAsync(new Uri(url));
			}
			catch (WebException)
			{
				AppendDiagnosticLog("connection", "Could not download the ChatGPT debugging target list.", null);
				throw new InvalidOperationException("GPT Themes could not connect to ChatGPT on 127.0.0.1:" + port + ". Likely causes: ChatGPT is not open with the local skinning port enabled, ChatGPT is still starting, another app is using that port, or security software blocked localhost. Try Relaunch with Port, Choose ChatGPT.exe, or use a different port. The port is bound to 127.0.0.1 only; do not expose it to your network.");
			}
		}
		List<CdpTarget> result = new List<CdpTarget>();
		if (!(_json.DeserializeObject(json) is object[] data))
		{
			return result;
		}
		object[] array = data;
		foreach (object obj in array)
		{
			if (obj is Dictionary<string, object> map)
			{
				string text = GetString(map, "type");
				string text2 = GetString(map, "url");
				string text3 = GetString(map, "webSocketDebuggerUrl");
				if (!string.IsNullOrEmpty(text3) && !text2.StartsWith("devtools:", StringComparison.OrdinalIgnoreCase) && (!(text != "page") || !(text != "webview") || !(text != "iframe")) && IsChatGptTargetUrl(text2) && IsExpectedLocalCdpWebSocket(text3, port))
				{
					result.Add(new CdpTarget
					{
						Type = text,
						Title = GetString(map, "title"),
						Url = text2,
						WebSocketDebuggerUrl = text3
					});
				}
			}
		}
		if (true)
		{
		}
		return result;
	}

	private static bool IsPortOwnedByValidatedChatGptProcess(int port)
	{
		foreach (int item in ListeningProcessIdsForPort(port))
		{
			try
			{
				using Process process = Process.GetProcessById(item);
				if (IsValidatedChatGptProcess(process))
				{
					return true;
				}
			}
			catch
			{
			}
		}
		return false;
	}

	private static List<int> ListeningProcessIdsForPort(int port)
	{
		List<int> list = new List<int>();
		try
		{
			string text = Path.Combine(Environment.SystemDirectory, "netstat.exe");
			if (!File.Exists(text))
			{
				return list;
			}
			ProcessStartInfo processStartInfo = new ProcessStartInfo();
			processStartInfo.FileName = text;
			processStartInfo.Arguments = "-ano -p TCP";
			processStartInfo.UseShellExecute = false;
			processStartInfo.CreateNoWindow = true;
			processStartInfo.RedirectStandardOutput = true;
			using Process process = Process.Start(processStartInfo);
			if (process == null)
			{
				return list;
			}
			string text2 = process.StandardOutput.ReadToEnd();
			process.WaitForExit(2500);
			string[] array = text2.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (string text3 in array)
			{
				string[] array2 = text3.Split(new char[2] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
				if (array2.Length >= 5 && string.Equals(array2[0], "TCP", StringComparison.OrdinalIgnoreCase) && string.Equals(array2[3], "LISTENING", StringComparison.OrdinalIgnoreCase) && IsLoopbackEndpointForPort(array2[1], port) && int.TryParse(array2[array2.Length - 1], out var result) && result > 0 && !list.Contains(result))
				{
					list.Add(result);
				}
			}
		}
		catch
		{
		}
		return list;
	}

	private static bool IsLoopbackEndpointForPort(string endpoint, int port)
	{
		if (string.IsNullOrWhiteSpace(endpoint))
		{
			return false;
		}
		int num = endpoint.LastIndexOf(':');
		if (num < 0 || num >= endpoint.Length - 1)
		{
			return false;
		}
		if (!int.TryParse(endpoint.Substring(num + 1), out var result) || result != port)
		{
			return false;
		}
		string ipString = endpoint.Substring(0, num).Trim('[', ']');
		IPAddress address;
		return IPAddress.TryParse(ipString, out address) && IPAddress.IsLoopback(address);
	}

	private static bool IsChatGptTargetUrl(string targetUrl)
	{
		if (string.IsNullOrWhiteSpace(targetUrl))
		{
			return false;
		}
		if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var result))
		{
			return false;
		}
		if (!string.Equals(result.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		string text = result.Host ?? "";
		return string.Equals(text, "chatgpt.com", StringComparison.OrdinalIgnoreCase) || text.EndsWith(".chatgpt.com", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsExpectedLocalCdpWebSocket(string webSocketUrl, int expectedPort)
	{
		if (!Uri.TryCreate(webSocketUrl, UriKind.Absolute, out var result))
		{
			return false;
		}
		if (!string.Equals(result.Scheme, "ws", StringComparison.OrdinalIgnoreCase) && !string.Equals(result.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (result.Port != expectedPort)
		{
			return false;
		}
		if (!result.AbsolutePath.StartsWith("/devtools/", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		bool flag = string.Equals(result.Host, "localhost", StringComparison.OrdinalIgnoreCase);
		IPAddress address;
		bool flag2 = IPAddress.TryParse(result.Host, out address) && IPAddress.IsLoopback(address);
		return flag || flag2;
	}

	private async Task SendEvaluateAsync(string webSocketUrl, string expression, int expectedPort)
	{
		if (!IsExpectedLocalCdpWebSocket(webSocketUrl, expectedPort))
		{
			throw new InvalidOperationException("Refusing to connect to an unexpected ChatGPT debugging endpoint.");
		}
		Uri webSocketUri = new Uri(webSocketUrl);
		using ClientWebSocket socket = new ClientWebSocket();
		using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8.0));
		CancellationToken ct = timeout.Token;
		await socket.ConnectAsync(webSocketUri, ct);
		Dictionary<string, object> payload = new Dictionary<string, object>
		{
			["id"] = 1,
			["method"] = "Runtime.evaluate",
			["params"] = new Dictionary<string, object>
			{
				{ "expression", expression },
				{ "returnByValue", true },
				{ "awaitPromise", false }
			}
		};
		byte[] bytes = Encoding.UTF8.GetBytes(_json.Serialize(payload));
		await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, ct);
		while (socket.State == WebSocketState.Open)
		{
			byte[] buffer = new byte[65536];
			List<byte> chunks = new List<byte>();
			WebSocketReceiveResult received;
			do
			{
				received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
				if (received.MessageType == WebSocketMessageType.Close)
				{
					return;
				}
				chunks.AddRange(buffer.Take(received.Count));
			}
			while (!received.EndOfMessage);
			string text = Encoding.UTF8.GetString(chunks.ToArray());
			Dictionary<string, object> response = _json.DeserializeObject(text) as Dictionary<string, object>;
			if (response != null && response.ContainsKey("id") && Convert.ToInt32(response["id"]) == 1)
			{
				ThrowIfEvaluateFailed(response);
				break;
			}
		}
		if (socket.State == WebSocketState.Open)
		{
			await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ct);
		}
	}

	private void ThrowIfEvaluateFailed(Dictionary<string, object> response)
	{
		if (response != null)
		{
			if (response.ContainsKey("error"))
			{
				string value = ((!(response["error"] is Dictionary<string, object> map)) ? Convert.ToString(response["error"]) : GetString(map, "message"));
				throw new InvalidOperationException("ChatGPT rejected the skin update: " + NonEmpty(value, "unknown websocket error"));
			}
			Dictionary<string, object> dictionary = (response.ContainsKey("result") ? (response["result"] as Dictionary<string, object>) : null);
			Dictionary<string, object> dictionary2 = ((dictionary != null && dictionary.ContainsKey("exceptionDetails")) ? (dictionary["exceptionDetails"] as Dictionary<string, object>) : null);
			if (dictionary2 != null)
			{
				string value2 = GetString(dictionary2, "text");
				Dictionary<string, object> dictionary3 = (dictionary2.ContainsKey("exception") ? (dictionary2["exception"] as Dictionary<string, object>) : null);
				string value3 = ((dictionary3 == null) ? "" : GetString(dictionary3, "description"));
				throw new InvalidOperationException("ChatGPT could not apply the skin: " + NonEmpty(value3, NonEmpty(value2, "JavaScript evaluation failed")));
			}
		}
	}

	private string BuildInjectionExpression(string css)
	{
		string newValue = _json.Serialize(css);
		string newValue2 = _json.Serialize("chatgpt-desktop-skinner-style");
		string text = "\n(() => {\n  const css = __CSS__;\n  const styleId = __STYLE_ID__;\n  if (window.__cgtdsSidebarObserver) {\n    window.__cgtdsSidebarObserver.disconnect();\n  }\n  window.clearTimeout(window.__cgtdsSidebarTimer);\n  const clearFixes = () => {\n    const cleaned = document.querySelectorAll('[data-cgtds-surface-fix]');\n    cleaned.forEach((el) => {\n      ['background','background-color','background-image','background-position','background-repeat','background-size','border','border-color','border-style','border-width','border-radius','box-shadow','outline','overflow','color','pointer-events','position','isolation','z-index','mask-image','-webkit-mask-image','backdrop-filter','-webkit-backdrop-filter','--tw-ring-color','--tw-ring-shadow','--tw-shadow','--tw-shadow-colored'].forEach((name) => el.style.removeProperty(name));\n      el.removeAttribute('data-cgtds-surface-fix');\n    });\n    document.querySelectorAll('header, #page-header, [data-testid*=header], [data-testid*=thread-header], [class*=topbar], [class*=top-bar]').forEach((el) => {\n      ['background','background-color','background-image','position','z-index','min-height','overflow','border-color','box-shadow'].forEach((name) => el.style.removeProperty(name));\n    });\n    return cleaned.length;\n  };\n  let cleanedCount = 0;\n  let style = document.getElementById(styleId);\n  if (!style) {\n    style = document.createElement('style');\n    style.id = styleId;\n    style.dataset.owner = 'chatgpt-desktop-skinner';\n    (document.head || document.documentElement).appendChild(style);\n  }\n  style.textContent = css;\n  document.documentElement.dataset.chatgptDesktopSkinner = 'enabled';\n  cleanedCount += clearFixes();\n  if (window.__cgtdsResizeHandler) {\n    window.removeEventListener('resize', window.__cgtdsResizeHandler);\n  }\n  window.clearTimeout(window.__cgtdsResizeTimer);\n  window.__cgtdsResizeHandler = () => {\n    window.clearTimeout(window.__cgtdsResizeTimer);\n    window.__cgtdsResizeTimer = window.setTimeout(() => {\n      const currentStyle = document.getElementById(styleId);\n      if (currentStyle && currentStyle.textContent !== css) currentStyle.textContent = css;\n    }, 220);\n  };\n  window.addEventListener('resize', window.__cgtdsResizeHandler, { passive: true });\n  const cssCustomValue = (name) => {\n    const escapedName = name.replace(/[.*+?^${}()|[\\]\\\\]/g, '\\\\$&');\n    const match = css.match(new RegExp(escapedName + '\\\\s*:\\\\s*([\\\\s\\\\S]*?)\\\\s*!important\\\\s*;'));\n    return match ? match[1].trim() : '';\n  };\n  const panelBackgroundValue = cssCustomValue('--cgtds-panel-background') || 'var(--cgtds-panel-background)';\n\n  const nearDark = (value) => {\n    const m = String(value || '').match(/rgba?\\((\\d+),\\s*(\\d+),\\s*(\\d+)(?:,\\s*([\\d.]+))?\\)/);\n    if (!m) return false;\n    const a = m[4] == null ? 1 : Number(m[4]);\n    return a > 0.72 && Number(m[1]) < 48 && Number(m[2]) < 48 && Number(m[3]) < 48;\n  };\n  const maxRadius = (style) => Math.max(\n    parseFloat(style.borderTopLeftRadius) || 0,\n    parseFloat(style.borderTopRightRadius) || 0,\n    parseFloat(style.borderBottomRightRadius) || 0,\n    parseFloat(style.borderBottomLeftRadius) || 0\n  );\n  const hasVisibleBorder = (style) => (\n    (parseFloat(style.borderTopWidth) || 0) +\n    (parseFloat(style.borderRightWidth) || 0) +\n    (parseFloat(style.borderBottomWidth) || 0) +\n    (parseFloat(style.borderLeftWidth) || 0)\n  ) > 0;\n  const classText = (el) => String(el.className && typeof el.className === 'string' ? el.className : '').toLowerCase();\n  const inputSelector = 'input, textarea, [contenteditable]:not([contenteditable=false]), [role=textbox], [role=searchbox]';\n  const sidebarSurfaceSelector = '#stage-slideover-sidebar, aside, nav[class*=sidebar], nav[class*=bg-token-sidebar]';\n  const isLikelySidebarSurface = (el) => {\n    if (!el || !el.matches) return false;\n    const classes = classText(el);\n    const rect = el.getBoundingClientRect();\n    if (!rect || rect.width <= 0 || rect.height <= 0) return false;\n    if (el.id === 'stage-slideover-sidebar') return true;\n    if (/(tiny-bar|sidebar-rail|rail-width)/.test(classes) || rect.width <= 96) return false;\n    if (el.matches('aside')) return rect.width >= 160 && rect.height >= Math.max(220, window.innerHeight * 0.45);\n    if (!el.matches('nav')) return false;\n    const leftDocked = rect.left <= 4 || rect.right <= Math.min(420, window.innerWidth * 0.42);\n    const tallEnough = rect.height >= Math.max(220, window.innerHeight * 0.45);\n    return leftDocked && tallEnough && !/(toolbar|header|topbar|menubar|breadcrumb|tab)/.test(classes);\n  };\n  const closestSidebarSurface = (el) => {\n    const surface = el && el.closest ? el.closest(sidebarSurfaceSelector) : null;\n    return isLikelySidebarSurface(surface) ? surface : null;\n  };\n  const mark = (el) => { if (el) el.dataset.cgtdsSurfaceFix = 'true'; };\n  const flat = (el) => {\n    if (!el) return;\n    el.style.setProperty('background', 'transparent', 'important');\n    el.style.setProperty('background-color', 'transparent', 'important');\n    el.style.setProperty('background-image', 'none', 'important');\n    el.style.setProperty('mask-image', 'none', 'important');\n    el.style.setProperty('-webkit-mask-image', 'none', 'important');\n    el.style.setProperty('border', '0 none transparent', 'important');\n    el.style.setProperty('border-color', 'transparent', 'important');\n    el.style.setProperty('border-style', 'none', 'important');\n    el.style.setProperty('border-width', '0px', 'important');\n    el.style.setProperty('box-shadow', 'none', 'important');\n    el.style.setProperty('outline', 'none', 'important');\n    el.style.setProperty('--tw-ring-color', 'transparent', 'important');\n    el.style.setProperty('--tw-ring-shadow', '0 0 #0000', 'important');\n    el.style.setProperty('--tw-shadow', '0 0 #0000', 'important');\n    el.style.setProperty('--tw-shadow-colored', '0 0 #0000', 'important');\n    mark(el);\n  };\n  const stripBox = (el) => {\n    if (!el) return;\n    flat(el);\n    el.style.setProperty('border', '0 none transparent', 'important');\n    el.style.setProperty('border-color', 'transparent', 'important');\n    el.style.setProperty('border-style', 'none', 'important');\n    el.style.setProperty('border-width', '0px', 'important');\n    el.style.setProperty('border-radius', '0px', 'important');\n    el.style.setProperty('box-shadow', 'none', 'important');\n    el.style.setProperty('outline', 'none', 'important');\n  };\n  const makePill = (el) => {\n    if (!el) return;\n    el.style.setProperty('background', 'var(--cgtds-composer-background)', 'important');\n    el.style.setProperty('background-color', 'var(--composer-surface)', 'important');\n    el.style.setProperty('color', 'var(--text-primary)', 'important');\n    el.style.setProperty('border', '0 none transparent', 'important');\n    el.style.setProperty('border-color', 'transparent', 'important');\n    el.style.setProperty('border-style', 'none', 'important');\n    el.style.setProperty('border-width', '0px', 'important');\n    el.style.setProperty('border-radius', '999px', 'important');\n    el.style.setProperty('box-shadow', 'none', 'important');\n    el.style.setProperty('backdrop-filter', 'var(--cgtds-composer-blur)', 'important');\n    el.style.setProperty('-webkit-backdrop-filter', 'var(--cgtds-composer-blur)', 'important');\n    el.style.setProperty('outline', 'none', 'important');\n    el.style.setProperty('overflow', 'visible', 'important');\n    el.querySelectorAll('button, [role=\"button\"], input, textarea, [contenteditable], [role=\"textbox\"], [role=\"searchbox\"], span, div, label').forEach((child) => {\n      if (skip.has(child.tagName)) return;\n      child.style.setProperty('background', 'transparent', 'important');\n      child.style.setProperty('background-color', 'transparent', 'important');\n      child.style.setProperty('background-image', 'none', 'important');\n      child.style.setProperty('color', 'var(--text-primary)', 'important');\n      child.style.setProperty('border', '0 none transparent', 'important');\n      child.style.setProperty('box-shadow', 'none', 'important');\n      child.style.setProperty('outline', 'none', 'important');\n      child.style.setProperty('--tw-ring-color', 'transparent', 'important');\n      child.style.setProperty('--tw-ring-shadow', '0 0 #0000', 'important');\n    });\n    mark(el);\n  };\n  const makeSearchPill = (el) => {\n    if (!el) return;\n    el.style.setProperty('background', 'var(--cgtds-search-background)', 'important');\n    el.style.setProperty('background-color', 'var(--cgtds-search-surface)', 'important');\n    el.style.setProperty('color', 'var(--text-primary)', 'important');\n    el.style.setProperty('border', '0 none transparent', 'important');\n    el.style.setProperty('border-color', 'transparent', 'important');\n    el.style.setProperty('border-style', 'none', 'important');\n    el.style.setProperty('border-width', '0px', 'important');\n    el.style.setProperty('border-radius', '999px', 'important');\n    el.style.setProperty('box-shadow', 'none', 'important');\n    el.style.setProperty('backdrop-filter', 'var(--cgtds-search-blur)', 'important');\n    el.style.setProperty('-webkit-backdrop-filter', 'var(--cgtds-search-blur)', 'important');\n    el.style.setProperty('outline', 'none', 'important');\n    el.style.setProperty('overflow', 'hidden', 'important');\n    el.querySelectorAll('input, textarea, [contenteditable], [role=\"textbox\"], [role=\"searchbox\"], span, div').forEach((child) => {\n      if (skip.has(child.tagName)) return;\n      child.style.setProperty('background', 'transparent', 'important');\n      child.style.setProperty('background-color', 'transparent', 'important');\n      child.style.setProperty('color', 'var(--text-primary)', 'important');\n      child.style.setProperty('border', '0 none transparent', 'important');\n      child.style.setProperty('box-shadow', 'none', 'important');\n      child.style.setProperty('outline', 'none', 'important');\n    });\n    mark(el);\n  };\n  const makePanelSurface = (el) => {\n    if (!el) return;\n    el.style.setProperty('background', panelBackgroundValue, 'important');\n    el.style.setProperty('background-position', 'center', 'important');\n    el.style.setProperty('background-repeat', 'no-repeat, no-repeat, no-repeat', 'important');\n    el.style.setProperty('background-size', 'cover, cover, auto', 'important');\n    el.style.setProperty('background-position', 'center, center, center', 'important');\n    el.style.setProperty('color', 'var(--text-primary)', 'important');\n    el.style.setProperty('border', '0 none transparent', 'important');\n    el.style.setProperty('border-color', 'transparent', 'important');\n    el.style.setProperty('border-style', 'none', 'important');\n    el.style.setProperty('border-width', '0px', 'important');\n    el.style.setProperty('border-right', '0 none transparent', 'important');\n    el.style.setProperty('border-left', '0 none transparent', 'important');\n    el.style.setProperty('border-radius', '0px', 'important');\n    el.style.setProperty('box-shadow', 'none', 'important');\n    el.style.setProperty('outline', 'none', 'important');\n    el.style.setProperty('backdrop-filter', 'var(--cgtds-panel-blur)', 'important');\n    el.style.setProperty('-webkit-backdrop-filter', 'var(--cgtds-panel-blur)', 'important');\n    el.querySelectorAll(':scope > div').forEach((child) => {\n      child.style.setProperty('background-color', 'transparent', 'important');\n      child.style.setProperty('background-image', 'none', 'important');\n      child.style.setProperty('box-shadow', 'none', 'important');\n      child.style.setProperty('border-color', 'transparent', 'important');\n      mark(child);\n    });\n    mark(el);\n  };\n  const sidebarRowSelector = 'aside a, aside button, aside [role=button], aside [role=link], nav a, nav button, nav [role=button], nav [role=link]';\n  const rowLooksSelected = (el) => {\n    if (!el) return false;\n    const classes = classText(el);\n    return el.hasAttribute('aria-current') ||\n      el.getAttribute('aria-selected') === 'true' ||\n      el.getAttribute('data-active') === 'true' ||\n      el.getAttribute('data-selected') === 'true' ||\n      /(selected|active|current|bg-token-sidebar-surface-secondary|bg-token-sidebar-surface-tertiary)/.test(classes);\n  };\n  const isSidebarActionButton = (el) => {\n    if (!el || !el.matches || !el.matches('button,[role=button]')) return false;\n    if (el.matches('[aria-label=\"Search chats\"]')) return false;\n    const rect = el.getBoundingClientRect();\n    const copy = (el.innerText || el.textContent || '').trim();\n    const hasIcon = !!el.querySelector('svg');\n    const compact = rect && rect.width > 0 && rect.height > 0 && rect.width <= 76 && rect.height <= 46;\n    const labelledIcon = !!(el.getAttribute('aria-label') || el.getAttribute('title')) && hasIcon;\n    return hasIcon && compact && (copy.length === 0 || labelledIcon);\n  };\n  const fixSidebarRows = () => {\n    let count = 0;\n    document.querySelectorAll(sidebarRowSelector).forEach((row) => {\n      const inSidebar = !!closestSidebarSurface(row);\n      if (!inSidebar) return;\n      const actionButton = isSidebarActionButton(row);\n      row.style.setProperty('border', '0 none transparent', 'important');\n      row.style.setProperty('border-color', 'transparent', 'important');\n      row.style.setProperty('box-shadow', 'none', 'important');\n      row.style.setProperty('outline', 'none', 'important');\n      row.style.setProperty('color', 'var(--text-primary)', 'important');\n      row.style.setProperty('pointer-events', 'auto', 'important');\n      row.style.setProperty('position', 'relative', 'important');\n      row.style.setProperty('z-index', '2', 'important');\n      row.style.setProperty('--tw-ring-color', 'transparent', 'important');\n      row.style.setProperty('--tw-ring-shadow', '0 0 #0000', 'important');\n      row.style.setProperty('--tw-shadow', '0 0 #0000', 'important');\n      row.style.setProperty('--tw-shadow-colored', '0 0 #0000', 'important');\n      if (actionButton) {\n        row.style.removeProperty('background');\n        row.style.removeProperty('background-color');\n        row.style.removeProperty('background-image');\n        row.style.setProperty('border-radius', '8px', 'important');\n        row.style.setProperty('isolation', 'isolate', 'important');\n      } else if (rowLooksSelected(row)) {\n        row.style.setProperty('background', 'var(--cgtds-sidebar-selected-background)', 'important');\n        row.style.setProperty('background-color', 'var(--cgtds-sidebar-selected-surface)', 'important');\n      } else {\n        row.style.removeProperty('background');\n        row.style.removeProperty('background-color');\n        row.style.removeProperty('background-image');\n      }\n      row.querySelectorAll('div, span, p').forEach((child) => {\n        if (skip.has(child.tagName)) return;\n        const childStyle = getComputedStyle(child);\n        const childClasses = classText(child);\n        if (nearDark(childStyle.backgroundColor) || /(bg-black|bg-gray-8|bg-gray-9|bg-neutral-8|bg-neutral-9|bg-\\[|dark:bg)/.test(childClasses)) {\n          child.style.setProperty('background', 'transparent', 'important');\n          child.style.setProperty('background-color', 'transparent', 'important');\n          child.style.setProperty('background-image', 'none', 'important');\n        }\n        child.style.setProperty('border-color', 'transparent', 'important');\n        child.style.setProperty('box-shadow', 'none', 'important');\n        child.style.setProperty('outline', 'none', 'important');\n        child.style.setProperty('color', 'var(--text-primary)', 'important');\n        child.style.setProperty('pointer-events', 'auto', 'important');\n      });\n      mark(row);\n      count++;\n    });\n    return count;\n  };\n  const flattenPillChildren = (pill) => {\n    if (!pill || !pill.querySelectorAll) return;\n    pill.querySelectorAll('div, span, label').forEach((child) => {\n      if (child === pill) return;\n      const cr = child.getBoundingClientRect();\n      if (!cr || cr.width <= 0 || cr.height <= 0) return;\n      child.style.setProperty('border', '0 none transparent', 'important');\n      child.style.setProperty('border-color', 'transparent', 'important');\n      child.style.setProperty('border-style', 'none', 'important');\n      child.style.setProperty('border-width', '0px', 'important');\n      child.style.setProperty('border-radius', '0px', 'important');\n      child.style.setProperty('box-shadow', 'none', 'important');\n      child.style.setProperty('outline', 'none', 'important');\n      child.style.setProperty('--tw-ring-color', 'transparent', 'important');\n      child.style.setProperty('--tw-ring-shadow', '0 0 #0000', 'important');\n      child.style.setProperty('--tw-shadow', '0 0 #0000', 'important');\n      child.style.setProperty('--tw-shadow-colored', '0 0 #0000', 'important');\n      child.style.setProperty('backdrop-filter', 'none', 'important');\n      child.style.setProperty('-webkit-backdrop-filter', 'none', 'important');\n      child.style.setProperty('background', 'transparent', 'important');\n      child.style.setProperty('background-color', 'transparent', 'important');\n      child.style.setProperty('background-image', 'none', 'important');\n      child.style.setProperty('color', 'var(--text-primary)', 'important');\n      mark(child);\n    });\n    pill.querySelectorAll(inputSelector).forEach(stripBox);\n  };\n  const skip = new Set(['SELECT','OPTION','SVG','PATH','IMG','VIDEO','CANVAS','PRE','CODE']);\n  const applySurfaceFixes = () => {\n  cleanedCount += clearFixes();\n  let fixed = 0;\n\n  document.querySelectorAll(sidebarSurfaceSelector).forEach((el) => {\n    if (isLikelySidebarSurface(el)) makePanelSurface(el);\n  });\n  fixed += fixSidebarRows();\n  document.querySelectorAll('footer, [class*=bottom-0], [class*=from-black], [class*=via-black], [class*=to-black], [class*=to-transparent], [class*=bg-gradient], [class*=gradient-to], [data-testid*=footer]').forEach((el) => {\n    flat(el);\n    el.style.setProperty('border-radius', '0px', 'important');\n    fixed++;\n  });\n\n  document.querySelectorAll('body *').forEach((el) => {\n    const rect = el.getBoundingClientRect();\n    if (!rect || rect.width <= 0 || rect.height <= 0) return;\n    const style = getComputedStyle(el);\n    const classes = classText(el);\n    const labelText = (el.getAttribute('aria-label') || el.getAttribute('placeholder') || '').trim().toLowerCase();\n    const ownText = (el.textContent || '').trim().toLowerCase();\n    const text = labelText || ownText.slice(0, 80);\n    const isInput = el.matches(inputSelector);\n    const inSidebar = !!closestSidebarSurface(el);\n    const hasInput = !!(el.querySelector && el.querySelector(inputSelector));\n    const bgImage = style.backgroundImage || '';\n    const classLooksLikeOverlay = /(bottom-0|bottom-\\[|from-black|via-black|to-black|to-transparent|bg-black|bg-gradient|bg-linear|gradient-to)/.test(classes);\n    const hasGradient = bgImage !== 'none' || classLooksLikeOverlay;\n    const isBottom = rect.bottom >= window.innerHeight - 8 || rect.top >= window.innerHeight - 190 || classes.indexOf('bottom-0') >= 0;\n    const isWideBottomBand = isBottom && rect.width >= Math.max(320, window.innerWidth * 0.36) && rect.height >= 18 && rect.height <= Math.max(220, window.innerHeight * 0.36);\n    const isFullWidthBottomBand = isBottom && rect.width >= window.innerWidth * 0.72 && rect.height <= 150;\n    const actualControl = el.matches('button, ' + inputSelector) || skip.has(el.tagName);\n    const editorChrome = el.matches('header, main, section, [role=main], [role=toolbar], [data-testid*=toolbar], [data-testid*=image], [data-testid*=canvas], [data-testid*=viewer]') || /(toolbar|workspace|viewer|canvas|editor|artifact|modal|popover|bg-token|bg-black|bg-neutral|bg-zinc|bg-stone|bg-gray|dark:bg|top-0|bottom-0)/.test(classes);\n    const isTopHeader = el.matches('header, [data-testid*=header]') || /(topbar|top-bar|top-0|sticky)/.test(classes) && rect.top <= 8 && rect.height <= 120;\n    if (inSidebar && !isInput) return;\n    if (isTopHeader) {\n      el.style.setProperty('pointer-events', 'auto', 'important');\n      return;\n    }\n    const largeChrome = !actualControl &&\n      !isInput &&\n      editorChrome &&\n      rect.width >= Math.max(360, window.innerWidth * 0.42) &&\n      rect.height >= Math.max(180, window.innerHeight * 0.28) &&\n      (maxRadius(style) > 28 || hasVisibleBorder(style) || /(rounded|workspace|viewer|canvas|editor|artifact)/.test(classes));\n\n    if (largeChrome) {\n      stripBox(el);\n      fixed++;\n      return;\n    }\n\n    if (!actualControl && isWideBottomBand && (isFullWidthBottomBand || hasInput || nearDark(style.backgroundColor) || hasGradient)) {\n      flat(el);\n      el.style.setProperty('border-radius', '0px', 'important');\n      fixed++;\n      return;\n    }\n\n    if (!actualControl && !hasInput && editorChrome && rect.width >= 36 && rect.height >= 24 && (nearDark(style.backgroundColor) || hasGradient)) {\n      flat(el);\n      if (rect.width > 180 && rect.height > 36) el.style.setProperty('border-radius', '0px', 'important');\n      fixed++;\n      return;\n    }\n\n    const exactSearch = text === 'search chats' || text === 'search' || labelText === 'search chats' || labelText === 'search';\n    const smallSearch = exactSearch && rect.width > 24 && rect.width < 430 && rect.height > 18 && rect.height <= 72;\n    if (smallSearch) {\n      let pill = el.closest('button,a,[role=button],[role=link],[aria-label=\"Search chats\"]') || el;\n      const pr = pill.getBoundingClientRect();\n      if (pr.height > 76 || pr.width > 430) pill = el;\n      for (let p = pill.parentElement, i = 0; p && i < 3; p = p.parentElement, i++) {\n        const ar = p.getBoundingClientRect();\n        if (ar.width < 430 && ar.height < 82 && !p.matches('aside,nav')) stripBox(p);\n      }\n      makeSearchPill(pill);\n      pill.querySelectorAll('*').forEach((child) => {\n        if (!skip.has(child.tagName)) stripBox(child);\n      });\n      fixed++;\n      return;\n    }\n\n    if (isInput && inSidebar) {\n      stripBox(el);\n      for (let p = el.parentElement, i = 0; p && i < 3; p = p.parentElement, i++) {\n        const pr = p.getBoundingClientRect();\n        if (pr.width < 430 && pr.height < 82 && !p.matches('aside,nav')) stripBox(p);\n      }\n      fixed++;\n      return;\n    }\n\n    if (isInput) {\n      stripBox(el);\n      let p = el.parentElement;\n      let pill = null;\n      const maxPillWidth = Math.min(window.innerWidth - 96, Math.max(520, rect.width + 360));\n      for (let i = 0; p && i < 8; i++, p = p.parentElement) {\n        if (p.matches('main,[role=main],body,html')) break;\n        const pr = p.getBoundingClientRect();\n        if (pr.width >= window.innerWidth * 0.72 && pr.height <= 150) {\n          stripBox(p);\n          continue;\n        }\n        if (pr.width > rect.width + 20 && pr.width <= maxPillWidth && pr.height > rect.height + 8 && pr.height <= 112) {\n          pill = p;\n          continue;\n        }\n        if (pr.height <= 112) stripBox(p);\n      }\n      if (pill) {\n        makePill(pill);\n        flattenPillChildren(pill);\n      }\n      fixed++;\n      return;\n    }\n\n    if (skip.has(el.tagName)) return;\n    if (rect.width < 120 || rect.height < 24) return;\n    if (hasInput && rect.height <= 130) return;\n    if (!editorChrome && !classLooksLikeOverlay && !/(bg-black|bg-gray|bg-neutral|bg-zinc|bg-stone|dark:bg|from-black|to-black|to-transparent)/.test(classes)) return;\n    if (!nearDark(style.backgroundColor) && !hasGradient) return;\n    flat(el);\n    fixed++;\n  });\n  return fixed;\n  };\n  const fixed = applySurfaceFixes();\n  if (window.__cgtdsResizeHandler) {\n    window.removeEventListener('resize', window.__cgtdsResizeHandler);\n  }\n  if (window.__cgtdsSidebarObserver) {\n    window.__cgtdsSidebarObserver.disconnect();\n  }\n  window.__cgtdsSidebarObserver = new MutationObserver(() => {\n    window.clearTimeout(window.__cgtdsSidebarTimer);\n    window.__cgtdsSidebarTimer = window.setTimeout(() => {\n      try {\n        applySurfaceFixes();\n      } catch (_) {\n      }\n    }, 180);\n  });\n  window.__cgtdsSidebarObserver.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class', 'aria-current', 'aria-selected', 'data-active', 'data-selected'] });\n  window.__cgtdsResizeHandler = () => {\n    window.clearTimeout(window.__cgtdsResizeTimer);\n    window.__cgtdsResizeTimer = window.setTimeout(() => {\n      try {\n        const liveStyle = document.getElementById(styleId);\n        if (liveStyle) liveStyle.textContent = css;\n      } catch (_) {\n      }\n    }, 220);\n  };\n  window.addEventListener('resize', window.__cgtdsResizeHandler, { passive: true });\n  return { ok: true, fixed, cleaned: cleanedCount, title: document.title, href: location.href };\n})()";
		return text.Replace("__CSS__", newValue).Replace("__STYLE_ID__", newValue2);
	}

	private string BuildCss(Theme theme, string layout, string backgroundMode, string backgroundValue, int transparency, string backgroundImageDataUrl, string panelImageDataUrl, bool glassSearch, string fontFamily)
	{
		string text = EnsureReadableText(theme.Text, theme.Bg);
		string hex = MixHex(text, theme.Panel, 0.38);
		string panel = theme.Panel;
		double num = (double)Math.Max(0, Math.Min(75, transparency)) / 75.0;
		double val = (string.Equals(theme.Bg, "#f4f7fb", StringComparison.OrdinalIgnoreCase) ? 0.94 : 0.84) - num * 0.72;
		double val2 = 0.96 - num * 0.48;
		double val3 = 0.92 - num * 0.68;
		double val4 = 0.88 - num * 0.58;
		double val5 = 0.94 - num * 0.7;
		val = Math.Max(0.36, val);
		val2 = Math.Max(0.78, val2);
		val3 = Math.Max(0.44, val3);
		val4 = Math.Max(0.42, val4);
		val5 = Math.Max(0.5, val5);
		double num2 = (glassSearch ? Math.Max(IsLightColor(theme.Bg) ? 0.78 : 0.62, val3 * 0.9) : val3);
		double alpha = (glassSearch ? 0.24 : 0.14);
		string text2 = (glassSearch ? ("linear-gradient(135deg, " + CssRgb(theme.Input, num2) + ", " + CssRgb(theme.Panel, Math.Max(0.34, num2 * 0.86)) + " 62%, " + CssRgb(theme.Accent, 0.1) + ")") : CssRgb(theme.Input, num2));
		string text3 = "none";
		string text4 = (glassSearch ? ("blur(" + Math.Max(12, (int)(num * 24.0)) + "px) saturate(1.35)") : "none");
		string text5 = "linear-gradient(135deg, " + CssRgb(theme.Input, val5) + ", " + CssRgb(theme.Panel, Math.Max(0.24, val5 * 0.72)) + " 52%, " + CssRgb(theme.Accent, 0.08) + ")";
		string text6 = ((num > 0.02) ? ("blur(" + Math.Max(10, (int)(num * 26.0)) + "px) saturate(1.25)") : "none");
		string text7 = "linear-gradient(90deg, " + CssRgb(theme.Accent, 0.18) + ", " + CssRgb(theme.Input, Math.Max(0.56, val4)) + ")";
		string text8 = CssRgb(theme.Panel, Math.Max(0.9, val2));
		string text9 = CssRgb(theme.Input, Math.Max(0.58, val4));
		string text10 = CssRgb(theme.Accent, IsLightColor(theme.Bg) ? 0.18 : 0.14);
		string text11 = CssRgb(theme.Accent, IsLightColor(theme.Bg) ? 0.26 : 0.22);
		string text12 = CssRgb(theme.Accent, IsLightColor(theme.Bg) ? 0.12 : 0.1);
		string text13 = BuildBackground(theme, backgroundMode, backgroundImageDataUrl, num);
		string text14 = BuildPanelBackground(theme, panel, val2, panelImageDataUrl);
		string value = BuildLayoutCss(layout);
		string text15 = BuildFontFamilyCss(fontFamily);
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine(":root, html, body {");
		stringBuilder.AppendLine("  --text-primary: " + CssRgb(text) + " !important;");
		stringBuilder.AppendLine("  --text-secondary: " + CssRgb(hex) + " !important;");
		stringBuilder.AppendLine("  --text-tertiary: " + CssRgb(hex, 0.82) + " !important;");
		stringBuilder.AppendLine("  --text-quaternary: " + CssRgb(hex, 0.7) + " !important;");
		stringBuilder.AppendLine("  --token-text-primary: " + CssRgb(text) + " !important;");
		stringBuilder.AppendLine("  --token-text-secondary: " + CssRgb(hex) + " !important;");
		stringBuilder.AppendLine("  --token-text-tertiary: " + CssRgb(hex, 0.82) + " !important;");
		stringBuilder.AppendLine("  --main-surface-primary: " + CssRgb(theme.Panel, val) + " !important;");
		stringBuilder.AppendLine("  --main-surface-secondary: " + CssRgb(theme.Input, val4) + " !important;");
		stringBuilder.AppendLine("  --main-surface-tertiary: " + CssRgb(theme.Bg, Math.Max(0.22, 0.82 - num * 0.5)) + " !important;");
		stringBuilder.AppendLine("  --sidebar-surface-primary: " + CssRgb(panel, val2) + " !important;");
		stringBuilder.AppendLine("  --cgtds-sidebar-cap-surface: " + CssRgb(panel, Math.Max(0.92, val2)) + " !important;");
		stringBuilder.AppendLine("  --cgtds-main-header-surface: " + text8 + " !important;");
		stringBuilder.AppendLine("  --sidebar-surface-secondary: " + CssRgb(theme.Panel, Math.Max(0.2, 0.86 - num * 0.48)) + " !important;");
		stringBuilder.AppendLine("  --sidebar-surface-tertiary: " + CssRgb(theme.Input, Math.Max(0.24, 0.88 - num * 0.5)) + " !important;");
		stringBuilder.AppendLine("  --message-surface: " + CssRgb(theme.User, 0.9) + " !important;");
		stringBuilder.AppendLine("  --composer-surface: " + CssRgb(theme.Input, val5) + " !important;");
		stringBuilder.AppendLine("  --cgtds-composer-background: " + text5 + " !important;");
		stringBuilder.AppendLine("  --cgtds-composer-blur: " + text6 + " !important;");
		stringBuilder.AppendLine("  --cgtds-panel-background: " + text14 + " !important;");
		stringBuilder.AppendLine("  --cgtds-panel-blur: blur(" + (int)(num * 20.0) + "px) saturate(1.2) !important;");
		stringBuilder.AppendLine("  --border-light: " + CssRgb(theme.Border, 0.42) + " !important;");
		stringBuilder.AppendLine("  --border-medium: " + CssRgb(theme.Border, 0.62) + " !important;");
		stringBuilder.AppendLine("  --border-heavy: " + CssRgb(theme.Border, 0.78) + " !important;");
		stringBuilder.AppendLine("  --cgtds-search-surface: " + CssRgb(theme.Input, num2) + " !important;");
		stringBuilder.AppendLine("  --cgtds-search-background: " + text2 + " !important;");
		stringBuilder.AppendLine("  --cgtds-search-border: " + CssRgb(theme.Border, alpha) + " !important;");
		stringBuilder.AppendLine("  --cgtds-search-shadow: " + text3 + " !important;");
		stringBuilder.AppendLine("  --cgtds-search-blur: " + text4 + " !important;");
		stringBuilder.AppendLine("  --cgtds-glass-strength: " + num.ToString("0.###", CultureInfo.InvariantCulture) + " !important;");
		stringBuilder.AppendLine("  --cgtds-button-background: " + text9 + " !important;");
		stringBuilder.AppendLine("  --cgtds-button-hover-background: " + text10 + " !important;");
		stringBuilder.AppendLine("  --cgtds-button-active-background: " + text11 + " !important;");
		stringBuilder.AppendLine("  --cgtds-sidebar-action-hover: " + text12 + " !important;");
		stringBuilder.AppendLine("  --cgtds-sidebar-selected-background: " + text7 + " !important;");
		stringBuilder.AppendLine("  --cgtds-sidebar-selected-surface: " + CssRgb(theme.Input, Math.Max(0.56, val4)) + " !important;");
		stringBuilder.AppendLine("  --link: " + CssRgb(theme.Accent) + " !important;");
		stringBuilder.AppendLine("  --link-hover: " + CssRgb(theme.Accent, 0.88) + " !important;");
		if (!string.IsNullOrEmpty(text15))
		{
			stringBuilder.AppendLine("  --cgtds-font-family: " + text15 + " !important;");
		}
		stringBuilder.AppendLine("}");
		stringBuilder.AppendLine("html, body { background: " + text13 + " !important; background-attachment: fixed !important; background-position: center !important; background-repeat: no-repeat !important; background-size: cover !important; color: " + CssRgb(text) + " !important; }");
		if (!string.IsNullOrEmpty(text15))
		{
			stringBuilder.AppendLine("body, body button, body input, body textarea, body select, body [contenteditable='true'], body [role='textbox'], body [role='searchbox'], body [data-message-author-role], body main, body aside, body nav { font-family: var(--cgtds-font-family) !important; }");
			stringBuilder.AppendLine("body :not(svg):not(path):not(pre):not(code):not(kbd):not(samp) { font-family: var(--cgtds-font-family) !important; }");
			stringBuilder.AppendLine("body pre, body code, body kbd, body samp { font-family: ui-monospace, SFMono-Regular, Consolas, \"Liberation Mono\", Menlo, monospace !important; }");
		}
		stringBuilder.AppendLine("body > div, #__next, main, [role='main'], [class*='bg-token-main-surface-primary'] { background-color: transparent !important; }");
		stringBuilder.AppendLine("[role='toolbar'], [data-testid*='toolbar'], [data-testid*='image'], [data-testid*='canvas'], [data-testid*='viewer'], [class*='toolbar'], [class*='workspace'], [class*='viewer'], [class*='canvas'], [class*='editor'], [class*='artifact'], [class*='bg-neutral'], [class*='bg-zinc'], [class*='bg-stone'] { background: transparent !important; background-color: transparent !important; background-image: none !important; border-color: transparent !important; border-radius: 0 !important; box-shadow: none !important; outline: none !important; }");
		stringBuilder.AppendLine("header, #page-header, [data-testid*='header'], [class*='topbar'], [class*='top-bar'] { color: " + CssRgb(text) + " !important; pointer-events: auto !important; } #page-header, header#page-header { background: var(--cgtds-main-header-surface) !important; background-color: var(--cgtds-main-header-surface) !important; background-image: none !important; backdrop-filter: var(--cgtds-panel-blur) !important; -webkit-backdrop-filter: var(--cgtds-panel-blur) !important; } #stage-slideover-sidebar [class*='sticky'][class*='top-0'], #stage-slideover-sidebar [class*='min-h-header-height'], #stage-slideover-sidebar [class*='bg-(--sidebar-surface-primary)'][class*='z-30'], #stage-slideover-sidebar [class*='top-header-height'][class*='sticky'], aside [class*='sticky'][class*='top-0'], aside [class*='min-h-header-height'], aside [class*='bg-(--sidebar-surface-primary)'][class*='z-30'], aside [class*='top-header-height'][class*='sticky'] { background: var(--cgtds-sidebar-cap-surface) !important; background-color: var(--cgtds-sidebar-cap-surface) !important; background-image: none !important; color: " + CssRgb(text) + " !important; backdrop-filter: var(--cgtds-panel-blur) !important; -webkit-backdrop-filter: var(--cgtds-panel-blur) !important; } header *, #page-header *, [data-testid*='header'] *, [class*='topbar'] *, [class*='top-bar'] * { pointer-events: auto !important; }");
		stringBuilder.AppendLine("#stage-slideover-sidebar, aside { background: var(--cgtds-panel-background) !important; background-position: center, center, center !important; background-repeat: no-repeat, no-repeat, no-repeat !important; background-size: cover, cover, auto !important; color: " + CssRgb(text) + " !important; border-color: transparent !important; box-shadow: none !important; outline: none !important; backdrop-filter: var(--cgtds-panel-blur) !important; -webkit-backdrop-filter: var(--cgtds-panel-blur) !important; }");
		stringBuilder.AppendLine("#stage-slideover-sidebar::before, #stage-slideover-sidebar::after, aside::before, aside::after { background: transparent !important; background-image: none !important; border-color: transparent !important; box-shadow: none !important; pointer-events: none !important; }");
		stringBuilder.AppendLine("#stage-slideover-sidebar > div, aside > div { background-color: transparent !important; background-image: none !important; box-shadow: none !important; border-color: transparent !important; }");
		stringBuilder.AppendLine("footer, [class*='bottom-0'], [class*='bottom-['], [data-testid*='footer'], [class*='composer'], [class*='thread'], [class*='conversation'] { background: transparent !important; background-color: transparent !important; background-image: none !important; mask-image: none !important; -webkit-mask-image: none !important; border-color: transparent !important; box-shadow: none !important; }");
		stringBuilder.AppendLine("[class*='bg-token-main-surface-secondary'], [class*='bg-token-main-surface-tertiary'], [class*='bg-token-sidebar-surface-secondary'], [class*='bg-token-sidebar-surface-tertiary'] { background-color: " + CssRgb(theme.Panel, val) + " !important; }");
		stringBuilder.AppendLine("footer [class*='bg-token-main-surface'], form [class*='bg-token-main-surface'], [data-testid*='composer'] [class*='bg-token-main-surface'], [class*='bottom-0'][class*='bg-token-main-surface'], [class*='bottom-['][class*='bg-token-main-surface'] { background: transparent !important; background-color: transparent !important; background-image: none !important; }");
		stringBuilder.AppendLine("main [class*='bg-gray-']:not(button), main [class*='bg-black']:not(button), main [class*='bg-white']:not(button), main [class*='bg-[']:not(button), [role='main'] [class*='bg-gray-']:not(button), [role='main'] [class*='bg-black']:not(button), [role='main'] [class*='bg-white']:not(button), [role='main'] [class*='bg-[']:not(button) { background-color: transparent !important; background-image: none !important; box-shadow: none !important; }");
		stringBuilder.AppendLine("form, [data-testid*='composer'], [class*='composer'], [class*='bottom-0'], [class*='bottom-['], [class*='bg-gradient'], [class*='bg-linear'], [class*='gradient-to'], footer { background: transparent !important; background-image: none !important; mask-image: none !important; -webkit-mask-image: none !important; border: 0 none transparent !important; border-color: transparent !important; border-style: none !important; border-width: 0 !important; border-radius: 0 !important; box-shadow: none !important; outline: none !important; --tw-ring-color: transparent !important; --tw-ring-shadow: 0 0 #0000 !important; --tw-shadow: 0 0 #0000 !important; --tw-shadow-colored: 0 0 #0000 !important; }");
		stringBuilder.AppendLine("textarea, [contenteditable]:not([contenteditable='false']), input[type='text'], input[type='search'], [role='textbox'], [role='searchbox'] { background-color: transparent !important; color: " + CssRgb(text) + " !important; caret-color: " + CssRgb(text) + " !important; border: 0 none transparent !important; border-color: transparent !important; border-style: none !important; border-width: 0 !important; border-radius: 0 !important; box-shadow: none !important; outline: none !important; --tw-ring-color: transparent !important; --tw-ring-shadow: 0 0 #0000 !important; --tw-shadow: 0 0 #0000 !important; --tw-shadow-colored: 0 0 #0000 !important; }");
		stringBuilder.AppendLine("form textarea, [data-testid*='composer'] textarea, [class*='composer'] textarea, form [contenteditable]:not([contenteditable='false']), [data-testid*='composer'] [contenteditable]:not([contenteditable='false']), [class*='composer'] [contenteditable]:not([contenteditable='false']), form [role='textbox'], [data-testid*='composer'] [role='textbox'], [class*='composer'] [role='textbox'] { white-space: pre-wrap !important; overflow-wrap: break-word !important; word-break: normal !important; overflow-x: hidden !important; overflow-y: auto !important; min-width: 0 !important; max-width: 100% !important; }");
		stringBuilder.AppendLine("#prompt-textarea, #prompt-textarea *, form [role='textbox'], [data-testid*='composer'] [role='textbox'], [class*='composer'] [role='textbox'] { white-space: pre-wrap !important; overflow-wrap: anywhere !important; word-break: normal !important; min-width: 0 !important; max-width: 100% !important; }");
		stringBuilder.AppendLine("#prompt-textarea.ProseMirror, #prompt-textarea.ProseMirror *, #prompt-textarea.ProseMirror p, form .ProseMirror, form .ProseMirror *, [data-testid*='composer'] .ProseMirror, [data-testid*='composer'] .ProseMirror *, [class*='composer'] .ProseMirror, [class*='composer'] .ProseMirror * { white-space: pre-wrap !important; overflow-wrap: anywhere !important; word-break: break-word !important; min-width: 0 !important; max-width: 100% !important; } form:has(#prompt-textarea), form:has(.ProseMirror), form:has([role='textbox']), form:has([contenteditable]:not([contenteditable='false'])), form [class*='prosemirror-parent'], [data-testid*='composer']:has(#prompt-textarea), [data-testid*='composer']:has(.ProseMirror), [data-testid*='composer'] [class*='prosemirror-parent'], [class*='composer']:has(#prompt-textarea), [class*='composer']:has(.ProseMirror), [class*='composer'] [class*='prosemirror-parent'] { min-width: 0 !important; max-width: 100% !important; overflow-x: hidden !important; box-sizing: border-box !important; }");
		stringBuilder.AppendLine("form [contenteditable]:not([contenteditable='false']) *, [data-testid*='composer'] [contenteditable]:not([contenteditable='false']) *, [class*='composer'] [contenteditable]:not([contenteditable='false']) * { white-space: pre-wrap !important; overflow-wrap: break-word !important; word-break: normal !important; }");
		stringBuilder.AppendLine("textarea::placeholder, input::placeholder, [contenteditable]:empty::before, [role='textbox']::placeholder, [role='searchbox']::placeholder { color: " + CssRgb(hex, 0.9) + " !important; opacity: 1 !important; }");
		stringBuilder.AppendLine("form:has(textarea), form:has([contenteditable]:not([contenteditable='false'])), [data-testid*='composer']:has(textarea), [data-testid*='composer']:has([contenteditable]:not([contenteditable='false'])), [class*='composer']:has(textarea), [class*='composer']:has([contenteditable]:not([contenteditable='false'])) { background: transparent !important; border: 0 none transparent !important; border-color: transparent !important; border-style: none !important; border-width: 0 !important; border-radius: 0 !important; box-shadow: none !important; outline: none !important; --tw-ring-color: transparent !important; --tw-ring-shadow: 0 0 #0000 !important; --tw-shadow: 0 0 #0000 !important; --tw-shadow-colored: 0 0 #0000 !important; }");
		stringBuilder.AppendLine("form textarea, form [contenteditable]:not([contenteditable='false']), [data-testid*='composer'] textarea, [data-testid*='composer'] [contenteditable]:not([contenteditable='false']), [class*='composer'] textarea, [class*='composer'] [contenteditable]:not([contenteditable='false']) { background-color: transparent !important; border: 0 none transparent !important; border-color: transparent !important; border-style: none !important; border-width: 0 !important; box-shadow: none !important; outline: none !important; }");
		stringBuilder.AppendLine("form button, [data-testid*='composer'] button, [class*='composer'] button { background-color: transparent !important; border: 0 none transparent !important; border-color: transparent !important; border-style: none !important; box-shadow: none !important; outline: none !important; }");
		stringBuilder.AppendLine("header button, [role='toolbar'] button, [data-testid*='toolbar'] button, [class*='toolbar'] button, button[aria-haspopup='menu'] { background-color: var(--cgtds-button-background) !important; color: " + CssRgb(text) + " !important; border-color: transparent !important; box-shadow: none !important; outline: none !important; }");
		stringBuilder.AppendLine("footer::before, footer::after, [class*='bottom-0']::before, [class*='bottom-0']::after, [class*='bottom-[']::before, [class*='bottom-[']::after, [class*='composer']::before, [class*='composer']::after, form :has(textarea)::before, form :has(textarea)::after, form :has([contenteditable]:not([contenteditable='false']))::before, form :has([contenteditable]:not([contenteditable='false']))::after { background: transparent !important; background-image: none !important; mask-image: none !important; -webkit-mask-image: none !important; border: 0 none transparent !important; border-color: transparent !important; border-style: none !important; border-width: 0 !important; box-shadow: none !important; outline: none !important; }");
		stringBuilder.AppendLine("form > div:has(textarea):not(:has(form)), form > div:has([contenteditable]:not([contenteditable='false'])):not(:has(form)), [data-testid*='composer'] > div:has(textarea):not(:has(form)), [data-testid*='composer'] > div:has([contenteditable]:not([contenteditable='false'])):not(:has(form)), [class*='composer'] > div:has(textarea):not(:has(form)), [class*='composer'] > div:has([contenteditable]:not([contenteditable='false'])):not(:has(form)) { background: var(--cgtds-composer-background) !important; background-color: var(--composer-surface) !important; border: 0 none transparent !important; border-color: transparent !important; border-style: none !important; border-width: 0 !important; border-radius: 28px !important; box-shadow: none !important; outline: none !important; overflow: visible !important; --tw-ring-color: transparent !important; --tw-ring-shadow: 0 0 #0000 !important; --tw-shadow: 0 0 #0000 !important; --tw-shadow-colored: 0 0 #0000 !important; backdrop-filter: var(--cgtds-composer-blur) !important; -webkit-backdrop-filter: var(--cgtds-composer-blur) !important; }");
		stringBuilder.AppendLine("form div:has(> textarea), form div:has(> [contenteditable]:not([contenteditable='false'])), form span:has(> textarea), form span:has(> [contenteditable]:not([contenteditable='false'])), form label:has(> textarea), form label:has(> [contenteditable]:not([contenteditable='false'])), [data-testid*='composer'] div:has(> textarea), [data-testid*='composer'] div:has(> [contenteditable]:not([contenteditable='false'])), [data-testid*='composer'] span:has(> textarea), [data-testid*='composer'] span:has(> [contenteditable]:not([contenteditable='false'])), [class*='composer'] div:has(> textarea), [class*='composer'] div:has(> [contenteditable]:not([contenteditable='false'])), [class*='composer'] span:has(> textarea), [class*='composer'] span:has(> [contenteditable]:not([contenteditable='false'])) { background: transparent !important; background-color: transparent !important; border: 0 none transparent !important; border-color: transparent !important; border-style: none !important; border-width: 0 !important; border-radius: 0 !important; box-shadow: none !important; outline: none !important; --tw-ring-color: transparent !important; --tw-ring-shadow: 0 0 #0000 !important; --tw-shadow: 0 0 #0000 !important; --tw-shadow-colored: 0 0 #0000 !important; backdrop-filter: none !important; -webkit-backdrop-filter: none !important; }");
		stringBuilder.AppendLine("aside input, aside input[type='search'], aside [role='searchbox'], aside [placeholder*='Search'], aside [data-testid*='search'] { background-color: transparent !important; color: " + CssRgb(text) + " !important; border: 0 !important; box-shadow: none !important; outline: none !important; }");
		stringBuilder.AppendLine("aside button, aside [role='button'], aside [role='link'], aside a, nav button, nav [role='button'], nav [role='link'], nav a { color: " + CssRgb(text) + " !important; background-color: transparent !important; background-image: none !important; border: 0 solid transparent !important; box-shadow: none !important; outline: none !important; pointer-events: auto !important; position: relative !important; z-index: 2 !important; } aside button *, aside [role='button'] *, aside [role='link'] *, aside a *, nav button *, nav [role='button'] *, nav [role='link'] *, nav a * { color: " + CssRgb(text) + " !important; border-color: transparent !important; box-shadow: none !important; outline: none !important; pointer-events: auto !important; } aside button:hover, aside [role='button']:hover, aside [role='link']:hover, aside a:hover, nav button:hover, nav [role='button']:hover, nav [role='link']:hover, nav a:hover { background: var(--cgtds-sidebar-action-hover) !important; background-color: var(--cgtds-sidebar-action-hover) !important; background-image: none !important; }");
		stringBuilder.AppendLine("aside [aria-current], aside [aria-selected='true'], aside [data-active='true'], aside [data-selected='true'], aside a[aria-current], aside button[aria-current], aside [role='button'][aria-current], aside [role='link'][aria-current], aside [class*='bg-token-sidebar-surface-secondary'], aside [class*='bg-token-sidebar-surface-tertiary'] { background: var(--cgtds-sidebar-selected-background) !important; background-color: var(--cgtds-sidebar-selected-surface) !important; color: " + CssRgb(text) + " !important; border-color: transparent !important; box-shadow: none !important; outline: none !important; }");
		stringBuilder.AppendLine("aside [aria-current] *, aside [aria-selected='true'] *, aside [data-active='true'] *, aside [data-selected='true'] *, aside a[aria-current] *, aside button[aria-current] *, aside [role='button'][aria-current] *, aside [role='link'][aria-current] *, aside [class*='bg-token-sidebar-surface-secondary'] *, aside [class*='bg-token-sidebar-surface-tertiary'] * { background-color: transparent !important; background-image: none !important; color: " + CssRgb(text) + " !important; border-color: transparent !important; box-shadow: none !important; outline: none !important; }");
		stringBuilder.AppendLine("aside button[aria-label]:not([aria-label='Search chats']), aside [role='button'][aria-label]:not([aria-label='Search chats']), aside a button, aside a [role='button'], aside button:has(svg):not(:has(span)):not([aria-label='Search chats']), aside [role='button']:has(svg):not(:has(span)):not([aria-label='Search chats']) { background: transparent !important; background-color: transparent !important; background-image: none !important; border: 0 none transparent !important; box-shadow: none !important; outline: none !important; border-radius: 8px !important; --tw-bg-opacity: 0 !important; --tw-ring-color: transparent !important; --tw-ring-shadow: 0 0 #0000 !important; --tw-shadow: 0 0 #0000 !important; --tw-shadow-colored: 0 0 #0000 !important; }");
		stringBuilder.AppendLine("aside button[aria-label]:not([aria-label='Search chats']):hover, aside [role='button'][aria-label]:not([aria-label='Search chats']):hover, aside a button:hover, aside a [role='button']:hover, aside button:has(svg):not(:has(span)):not([aria-label='Search chats']):hover, aside [role='button']:has(svg):not(:has(span)):not([aria-label='Search chats']):hover { background: var(--cgtds-sidebar-action-hover) !important; background-color: var(--cgtds-sidebar-action-hover) !important; background-image: none !important; }");
		stringBuilder.AppendLine("aside [aria-label='Search chats'], aside button[aria-label='Search chats'], aside a[href*='search'] { background: var(--cgtds-search-background) !important; background-color: var(--cgtds-search-surface) !important; color: " + CssRgb(text) + " !important; border: 0 none transparent !important; border-color: transparent !important; border-width: 0 !important; border-radius: 999px !important; box-shadow: none !important; backdrop-filter: var(--cgtds-search-blur) !important; -webkit-backdrop-filter: var(--cgtds-search-blur) !important; box-sizing: border-box !important; max-width: calc(100% - 8px) !important; min-height: 28px !important; overflow: hidden !important; }");
		stringBuilder.AppendLine("aside [aria-label='Search chats'] *, aside button[aria-label='Search chats'] *, aside a[href*='search'] * { background: transparent !important; color: " + CssRgb(text) + " !important; border: 0 !important; box-shadow: none !important; outline: none !important; }");
		stringBuilder.AppendLine("[data-message-author-role='user'] [class*='bg-'], [data-message-author-role='user'] [class*='rounded-'] { background-color: " + CssRgb(theme.User, 0.9) + " !important; }");
		stringBuilder.AppendLine("main [data-message-author-role], [role='main'] [data-message-author-role], main [data-message-author-role] *, [role='main'] [data-message-author-role] *, main .markdown, main .markdown * { min-width: 0 !important; max-width: 100% !important; overflow-wrap: anywhere !important; word-break: normal !important; }");
		stringBuilder.AppendLine("main [class*='workspace'], main [class*='viewer'], main [class*='canvas'], main [class*='editor'], main [class*='artifact'], [role='main'] [class*='workspace'], [role='main'] [class*='viewer'], [role='main'] [class*='canvas'], [role='main'] [class*='editor'], [role='main'] [class*='artifact'] { border-radius: 0 !important; border-color: transparent !important; box-shadow: none !important; outline: none !important; }");
		stringBuilder.AppendLine("button, [role='button'] { color: " + CssRgb(text) + " !important; border-color: transparent !important; box-shadow: none !important; outline: none !important; }");
		stringBuilder.AppendLine("header button:hover, header button:focus-visible, [role='toolbar'] button:hover, [role='toolbar'] button:focus-visible, [data-testid*='toolbar'] button:hover, [data-testid*='toolbar'] button:focus-visible, [class*='toolbar'] button:hover, [class*='toolbar'] button:focus-visible, button[aria-haspopup='menu']:hover, button[aria-haspopup='menu']:focus-visible { background-color: var(--cgtds-button-hover-background) !important; color: " + CssRgb(text) + " !important; }");
		stringBuilder.AppendLine("button[aria-pressed='true'], button[aria-selected='true'], button[data-active='true'], [role='button'][aria-pressed='true'], [role='button'][aria-selected='true'], [role='button'][data-active='true'] { background-color: var(--cgtds-button-active-background) !important; color: " + CssRgb(text) + " !important; border-color: transparent !important; box-shadow: none !important; outline: none !important; }");
		stringBuilder.AppendLine("button:hover, button:focus, button:focus-visible, [role='button']:hover, [role='button']:focus, [role='button']:focus-visible, a:hover, a:focus, a:focus-visible { border-color: transparent !important; outline: none !important; box-shadow: none !important; }");
		stringBuilder.AppendLine("a, [class*='text-token-text-secondary'], [class*='text-token-text-tertiary'], [class*='text-gray-'] { color: " + CssRgb(theme.Accent) + " !important; }");
		stringBuilder.AppendLine("[class*='border-token'], [class*='border-gray-'], [class*='divide-token'], [class*='divide-gray-'] { border-color: " + CssRgb(theme.Border, 0.62) + " !important; }");
		stringBuilder.AppendLine("form, form *, [data-testid*='composer'], [data-testid*='composer'] *, [class*='composer'], [class*='composer'] *, textarea, [contenteditable]:not([contenteditable='false']), [role='textbox'], [role='searchbox'], input[type='text'], input[type='search'] { outline: none !important; box-shadow: none !important; --tw-ring-color: transparent !important; --tw-ring-shadow: 0 0 #0000 !important; --tw-shadow: 0 0 #0000 !important; --tw-shadow-colored: 0 0 #0000 !important; }");
		stringBuilder.AppendLine("form :has(textarea), form :has([contenteditable]:not([contenteditable='false'])), [data-testid*='composer'] :has(textarea), [data-testid*='composer'] :has([contenteditable]:not([contenteditable='false'])), [class*='composer'] :has(textarea), [class*='composer'] :has([contenteditable]:not([contenteditable='false'])), aside [aria-label='Search chats'], aside [aria-label='Search chats'] *, aside button[aria-label='Search chats'], aside button[aria-label='Search chats'] *, aside a[href*='search'], aside a[href*='search'] * { border: 0 none transparent !important; border-color: transparent !important; border-style: none !important; border-width: 0 !important; box-shadow: none !important; outline: none !important; }");
		stringBuilder.AppendLine("[data-message-author-role], [data-message-author-role] *, main article, main article *, [role='main'] article, [role='main'] article *, main .markdown, main .markdown *, [class*='text-token-text-primary'], [class*='text-token-text-secondary'], [class*='prose'], [class*='prose'] * { color: " + CssRgb(text) + " !important; }");
		stringBuilder.AppendLine("[data-message-author-role] a, main article a, [role='main'] article a, main .markdown a { color: " + CssRgb(theme.Accent) + " !important; }");
		stringBuilder.AppendLine("::selection { background: " + CssRgb(theme.Accent, 0.38) + " !important; color: " + CssRgb(text) + " !important; }");
		stringBuilder.AppendLine(value);
		return stringBuilder.ToString();
	}

	private string BuildBackground(Theme theme, string mode, string imageDataUrl, double glass)
	{
		List<string> list = new List<string>();
		bool flag = mode == "file" && !string.IsNullOrEmpty(imageDataUrl);
		bool flag2 = IsLightColor(theme.Bg);
		double num = (flag ? Math.Max(0.16, (flag2 ? 0.28 : 0.38) - glass * 0.12) : Math.Max(0.3, (flag2 ? 0.48 : 0.7) - glass * 0.45));
		double alpha = ((!flag) ? (flag2 ? 0.09 : 0.1) : (flag2 ? 0.07 : 0.08));
		list.Add("linear-gradient(135deg, " + CssRgb(theme.Bg, num) + ", " + CssRgb(theme.Accent, alpha) + " 52%, " + CssRgb(theme.Bg, Math.Max(0.08, num * 0.62)) + ")");
		if (flag)
		{
			list.Add("url(\"" + CssUrl(imageDataUrl) + "\")");
		}
		else if (mode == "pattern")
		{
			list.Add(theme.Pattern);
		}
		list.Add(CssRgb(theme.Bg));
		return string.Join(", ", list.ToArray());
	}

	private string BuildPanelBackground(Theme theme, string sidebar, double sidebarAlpha, string panelImageDataUrl)
	{
		if (string.IsNullOrEmpty(panelImageDataUrl))
		{
			return CssRgb(sidebar, sidebarAlpha);
		}
		List<string> list = new List<string>();
		double alpha = Math.Max(0.18, Math.Min(0.46, sidebarAlpha * 0.52));
		list.Add("linear-gradient(" + CssRgb(sidebar, alpha) + ", " + CssRgb(sidebar, alpha) + ")");
		list.Add("url(\"" + CssUrl(panelImageDataUrl) + "\") center center / cover no-repeat");
		list.Add(CssRgb(sidebar));
		return string.Join(", ", list.ToArray());
	}

	private string ResolvePanelImageDataUrl(string backgroundImageDataUrl, List<string> warnings)
	{
		string text = SelectedPanelImageMode();
		if (text == "same")
		{
			return backgroundImageDataUrl;
		}
		if (text == "file")
		{
			return ResolveImageDataUrl("Panel image", text, (_panelImageTextBox == null) ? "" : _panelImageTextBox.Text.Trim(), warnings);
		}
		return "";
	}

	private string ResolveBackgroundImageDataUrl(string mode, string value, List<string> warnings)
	{
		return ResolveImageDataUrl("Background image", mode, value, warnings);
	}

	private string ResolveImageDataUrl(string label, string mode, string value, List<string> warnings)
	{
		if (mode != "file")
		{
			return "";
		}
		if (string.IsNullOrWhiteSpace(value))
		{
			return "";
		}
		if (!TryGetValidatedLocalImageFile(value, out var info, out var mimeType, out var error))
		{
			warnings?.Add(label + ": " + error);
			AppendDiagnosticLog("image", label + " fallback: " + error, null);
			return "";
		}
		byte[] array = File.ReadAllBytes(info.FullName);
		if (array.Length > 8388608)
		{
			string text = "Please choose an image smaller than 8 MB.";
			warnings?.Add(label + ": " + text);
			AppendDiagnosticLog("image", label + " fallback: " + text, null);
			return "";
		}
		return "data:" + mimeType + ";base64," + Convert.ToBase64String(array);
	}

	private static bool TryGetValidatedLocalImageFile(string value, out FileInfo info, out string mimeType, out string error)
	{
		info = null;
		mimeType = "";
		error = "";
		if (string.IsNullOrWhiteSpace(value))
		{
			error = "Choose an image file first.";
			return false;
		}
		string fullPath;
		try
		{
			fullPath = Path.GetFullPath(value);
		}
		catch
		{
			error = "Choose a valid local image path.";
			return false;
		}
		if (!File.Exists(fullPath))
		{
			error = "Image file was not found.";
			return false;
		}
		try
		{
			info = new FileInfo(fullPath);
		}
		catch
		{
			error = "Image file could not be opened.";
			return false;
		}
		if (info.Length > 8388608)
		{
			error = "Please choose an image smaller than 8 MB.";
			return false;
		}
		mimeType = MimeTypeFromExtension(info.Extension);
		if (string.IsNullOrEmpty(mimeType) || !mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
		{
			error = "Choose a PNG, JPG, WebP, GIF, or BMP image file.";
			return false;
		}
		if (string.Equals(mimeType, "image/svg+xml", StringComparison.OrdinalIgnoreCase))
		{
			error = "SVG backgrounds are not supported. Choose a PNG, JPG, WebP, GIF, or BMP image.";
			return false;
		}
		return true;
	}

	private static string MimeTypeFromExtension(string extension)
	{
		switch ((extension ?? "").ToLowerInvariant())
		{
		case ".jpg":
		case ".jpeg":
			return "image/jpeg";
		case ".png":
			return "image/png";
		case ".gif":
			return "image/gif";
		case ".webp":
			return "image/webp";
		case ".bmp":
			return "image/bmp";
		case ".svg":
			return "image/svg+xml";
		default:
			return "application/octet-stream";
		}
	}

	private string BuildLayoutCss(string layout)
	{
		return layout switch
		{
			"wide" => "main [data-message-author-role], [role='main'] [data-message-author-role] { padding-left: clamp(12px, 3vw, 32px) !important; padding-right: clamp(12px, 3vw, 32px) !important; }", 
			"compact" => "[data-message-author-role] { padding-top: 0.28rem !important; padding-bottom: 0.28rem !important; margin-top: 0 !important; margin-bottom: 0 !important; } main article, [role='main'] article { gap: 0.38rem !important; } form > div:has(textarea):not(:has(form)), form > div:has([contenteditable]:not([contenteditable='false'])):not(:has(form)), [data-testid*='composer'] > div:has(textarea):not(:has(form)), [data-testid*='composer'] > div:has([contenteditable]:not([contenteditable='false'])):not(:has(form)) { border-radius: 22px !important; }", 
			"focus" => "#stage-slideover-sidebar, aside { opacity: 0.36 !important; transition: opacity 160ms ease, filter 160ms ease !important; } #stage-slideover-sidebar:hover, #stage-slideover-sidebar:focus-within, aside:hover, aside:focus-within { opacity: 1 !important; filter: none !important; } main [data-message-author-role], [role='main'] [data-message-author-role] { padding-top: 0.45rem !important; padding-bottom: 0.45rem !important; }", 
			_ => "", 
		};
	}

	private Theme CurrentTheme()
	{
		return _themes.ContainsKey(_themeId) ? _themes[_themeId] : _themes["plum"];
	}

	private string SelectedLayout()
	{
		return (_layoutCombo.SelectedItem == null) ? "standard" : _layoutCombo.SelectedItem.ToString().ToLowerInvariant();
	}

	private string SelectedFontFamily()
	{
		string text = ((_fontCombo == null) ? "" : _fontCombo.Text.Trim());
		return string.IsNullOrEmpty(text) ? "Default" : text;
	}

	private string SelectedBackgroundMode()
	{
		return ((_backgroundModeCombo != null) ? _backgroundModeCombo.SelectedIndex : 0) switch
		{
			0 => "solid", 
			1 => "pattern", 
			2 => "file", 
			_ => "solid", 
		};
	}

	private string SelectedPanelImageMode()
	{
		return ((_panelImageModeCombo != null) ? _panelImageModeCombo.SelectedIndex : 0) switch
		{
			1 => "same", 
			2 => "file", 
			_ => "off", 
		};
	}

	private int BackgroundModeToIndex(string mode)
	{
		return mode switch
		{
			"solid" => 0, 
			"pattern" => 1, 
			"file" => 2, 
			_ => 0, 
		};
	}

	private int PanelImageModeToIndex(string mode)
	{
		if (mode == "same")
		{
			return 1;
		}
		if (mode == "file")
		{
			return 2;
		}
		return 0;
	}

	private int SelectedPort()
	{
		int result;
		return (int.TryParse((_portTextBox == null) ? "" : _portTextBox.Text.Trim(), out result) && result > 0 && result <= 65535) ? result : 9322;
	}

	private int SelectedTransparency()
	{
		if (_transparencyTrackBar == null)
		{
			return _transparency;
		}
		_transparency = _transparencyTrackBar.Value;
		return _transparency;
	}

	private bool SelectedGlassSearch()
	{
		if (_glassSearchCheckBox == null)
		{
			return _glassSearch;
		}
		_glassSearch = _glassSearchCheckBox.Checked;
		return _glassSearch;
	}

	private void UpdateTransparencyLabel()
	{
		if (_transparencyValueLabel != null)
		{
			_transparencyValueLabel.Text = SelectedTransparency() + "% transparent";
		}
	}

	private static string SafeExceptionMessage(Exception ex)
	{
		if (ex == null || string.IsNullOrWhiteSpace(ex.Message))
		{
			return "The operation failed.";
		}
		string message = ex.Message;
		message = Regex.Replace(message, "[A-Za-z]:\\\\[^\\r\\n\"']+", "[local path]");
		message = Regex.Replace(message, "\\\\\\\\[^\\\\\\s]+\\\\[^\\r\\n\"']+", "[network path]");
		if (message.Length > 260)
		{
			message = message.Substring(0, 257) + "...";
		}
		return message;
	}

	private void SetStatus(string message, bool isError)
	{
		if (_statusLabel != null)
		{
			_statusLabel.Text = message;
			_statusLabel.ForeColor = (isError ? Color.FromArgb(255, 105, 97) : AppleMuted);
		}
	}

	private static void AppendDiagnosticLog(string area, string message, Exception ex)
	{
		try
		{
			string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
			string text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + LimitThemeText(area ?? "diagnostic", 32) + "] " + LimitThemeText(message ?? "No message.", 240);
			if (ex != null)
			{
				text = text + " " + SafeExceptionMessage(ex);
			}
			File.AppendAllText(path, text + Environment.NewLine, Encoding.UTF8);
		}
		catch
		{
		}
	}

	private void UpdateActiveThemeLabel()
	{
		if (_activeThemeLabel != null)
		{
			_activeThemeLabel.Text = "Active: " + ActiveThemeDisplayName();
		}
	}

	private string ActiveThemeDisplayName()
	{
		if (!_activeThemeEnabled || _activeTheme == null)
		{
			return "Default";
		}
		return NonEmpty(_activeTheme.ThemeName, ThemeNameForId(_activeTheme.ThemeId));
	}

	private static string GetString(Dictionary<string, object> map, string key)
	{
		return (map.ContainsKey(key) && map[key] != null) ? Convert.ToString(map[key]) : "";
	}

	private static string NonEmpty(string value, string fallback)
	{
		return string.IsNullOrEmpty(value) ? fallback : value;
	}

	private static void SetComboText(ComboBox combo, string value)
	{
		if (combo == null)
		{
			return;
		}
		for (int i = 0; i < combo.Items.Count; i++)
		{
			if (string.Equals(Convert.ToString(combo.Items[i]), value, StringComparison.OrdinalIgnoreCase))
			{
				combo.SelectedIndex = i;
				return;
			}
		}
		if (combo.DropDownStyle == ComboBoxStyle.DropDownList)
		{
			if (combo.Items.Count > 0)
			{
				combo.SelectedIndex = 0;
			}
		}
		else
		{
			combo.Text = value;
		}
	}

	private static Color ColorFromHex(string hex)
	{
		try
		{
			return ColorTranslator.FromHtml(hex);
		}
		catch
		{
			return Color.FromArgb(17, 24, 39);
		}
	}

	private static Color ReadableTextColor(Color color)
	{
		int num = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
		return (num >= 140) ? Color.Black : Color.White;
	}

	private static string EnsureReadableText(string textHex, string backgroundHex)
	{
		Color first = ColorFromHex(textHex);
		Color color = ColorFromHex(backgroundHex);
		double num = ContrastRatio(first, color);
		if (num >= 4.5)
		{
			return NormalizeHex(textHex);
		}
		return (RelativeLuma(color) > 0.5) ? "#172033" : "#F8FBFF";
	}

	private static bool IsLightColor(string hex)
	{
		return RelativeLuma(ColorFromHex(hex)) > 0.55;
	}

	private static double RelativeLuma(Color color)
	{
		return 0.2126 * ChannelLuma(color.R) + 0.7152 * ChannelLuma(color.G) + 0.0722 * ChannelLuma(color.B);
	}

	private static double ChannelLuma(int channel)
	{
		double num = (double)channel / 255.0;
		return (num <= 0.03928) ? (num / 12.92) : Math.Pow((num + 0.055) / 1.055, 2.4);
	}

	private static double ContrastRatio(Color first, Color second)
	{
		double val = RelativeLuma(first) + 0.05;
		double val2 = RelativeLuma(second) + 0.05;
		return Math.Max(val, val2) / Math.Min(val, val2);
	}

	private static string NormalizeHex(string hex)
	{
		Color color = ColorFromHex(hex);
		return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
	}

	private static string CssUrl(string value)
	{
		return value.Replace("\\", "\\\\").Replace("\"", "").Replace("\r", "")
			.Replace("\n", "");
	}

	private static string CssRgb(string hex)
	{
		return CssRgb(hex, 1.0);
	}

	private static string CssRgb(string hex, double alpha)
	{
		Color color = ColorFromHex(hex);
		if (alpha >= 1.0)
		{
			return $"rgb({color.R}, {color.G}, {color.B})";
		}
		return string.Format(CultureInfo.InvariantCulture, "rgba({0}, {1}, {2}, {3:0.###})", color.R, color.G, color.B, alpha);
	}

	private static string BuildFontFamilyCss(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "";
		}
		value = value.Trim();
		if (string.Equals(value, "Default", StringComparison.OrdinalIgnoreCase))
		{
			return "";
		}
		if (string.Equals(value, "System UI", StringComparison.OrdinalIgnoreCase))
		{
			return "system-ui, -apple-system, BlinkMacSystemFont, \"Segoe UI\", sans-serif";
		}
		List<string> list = new List<string>();
		string[] array = value.Split(',');
		foreach (string value2 in array)
		{
			string text = SanitizeFontName(value2);
			if (!string.IsNullOrEmpty(text))
			{
				list.Add(FontNeedsQuotes(text) ? ("\"" + text.Replace("\"", "\\\"") + "\"") : text.ToLowerInvariant());
			}
		}
		if (list.Count == 0)
		{
			return "";
		}
		if (!list.Any(IsGenericFontFamily))
		{
			list.Add("sans-serif");
		}
		return string.Join(", ", list.ToArray());
	}

	private static string SanitizeFontName(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "";
		}
		value = value.Trim().Trim('"', '\'');
		StringBuilder stringBuilder = new StringBuilder();
		string text = value;
		foreach (char c in text)
		{
			if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '.')
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString().Trim();
	}

	private static bool FontNeedsQuotes(string font)
	{
		return !IsGenericFontFamily(font);
	}

	private static bool IsGenericFontFamily(string font)
	{
		if (string.IsNullOrWhiteSpace(font))
		{
			return false;
		}
		string text = font.Trim().ToLowerInvariant();
		int result;
		switch (text)
		{
		default:
			result = ((text == "ui-monospace") ? 1 : 0);
			break;
		case "serif":
		case "sans-serif":
		case "monospace":
		case "cursive":
		case "fantasy":
		case "system-ui":
		case "ui-sans-serif":
		case "ui-serif":
			result = 1;
			break;
		}
		return (byte)result != 0;
	}

	private static string MixHex(string first, string second, double secondWeight)
	{
		Color color = ColorFromHex(first);
		Color color2 = ColorFromHex(second);
		double num = Math.Max(0.0, Math.Min(1.0, secondWeight));
		int num2 = (int)Math.Round((double)(int)color.R * (1.0 - num) + (double)(int)color2.R * num);
		int num3 = (int)Math.Round((double)(int)color.G * (1.0 - num) + (double)(int)color2.G * num);
		int num4 = (int)Math.Round((double)(int)color.B * (1.0 - num) + (double)(int)color2.B * num);
		return $"#{num2:x2}{num3:x2}{num4:x2}";
	}
}
