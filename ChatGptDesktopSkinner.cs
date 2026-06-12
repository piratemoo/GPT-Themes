using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ChatGptDesktopSkinner
{
    internal static class Program
    {
        private const int SwRestore = 9;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, "Local\\ChatGptDesktopSkinner.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    FocusExistingInstance();
                    return;
                }

                RunApplication();
                GC.KeepAlive(mutex);
            }
        }

        private static void RunApplication()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { ShowFatalError(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                var ex = e.ExceptionObject as Exception;
                ShowFatalError(ex ?? new Exception(Convert.ToString(e.ExceptionObject)));
            };

            try
            {
                Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
                Application.Run(new SkinnerForm());
            }
            catch (Exception ex)
            {
                ShowFatalError(ex);
            }
        }

        private static void FocusExistingInstance()
        {
            try
            {
                var current = Process.GetCurrentProcess();
                foreach (var process in Process.GetProcessesByName(current.ProcessName))
                {
                    if (process.Id == current.Id) continue;
                    IntPtr handle = process.MainWindowHandle;
                    if (handle == IntPtr.Zero) continue;
                    ShowWindow(handle, SwRestore);
                    SetForegroundWindow(handle);
                    return;
                }
            }
            catch
            {
            }
        }

        private static void ShowFatalError(Exception ex)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-error.log");
                File.AppendAllText(
                    logPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine +
                    ex + Environment.NewLine + Environment.NewLine);
            }
            catch
            {
            }

            try
            {
                MessageBox.Show(
                    "GPT Themes hit an error while opening." + Environment.NewLine + Environment.NewLine +
                    "A local startup-error.log file was written next to the EXE with details.",
                    "GPT Themes",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch
            {
            }
        }
    }

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
            return new Theme
            {
                Id = id,
                Name = name,
                Bg = Bg,
                Panel = Panel,
                Input = Input,
                Text = Text,
                Accent = Accent,
                Border = Border,
                User = User,
                Pattern = Pattern
            };
        }
    }

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

    internal sealed class AppliedThemeSnapshot
    {
        public string ThemeId { get; set; }
        public string ThemeName { get; set; }
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
    }

    internal sealed class CdpTarget
    {
        public string TargetId;
        public string Type;
        public string Title;
        public string Url;
    }

    internal sealed class CdpPipeConnection : IDisposable
    {
        private const int StdInputHandle = -10;
        private const int StdOutputHandle = -11;
        private const int StdErrorHandle = -12;
        private const int StartfUseStdHandles = 0x00000100;
        private const byte CrtFOpenPipe = 0x09;
        private const uint DuplicateSameAccess = 0x00000002;

        private readonly JavaScriptSerializer _json;
        private readonly AnonymousPipeServerStream _toChild;
        private readonly AnonymousPipeServerStream _fromChild;
        private readonly Process _process;
        private readonly Dictionary<int, TaskCompletionSource<Dictionary<string, object>>> _pending =
            new Dictionary<int, TaskCompletionSource<Dictionary<string, object>>>();
        private readonly object _pendingLock = new object();
        private readonly SemaphoreSlim _writeGate = new SemaphoreSlim(1, 1);
        private readonly Task _readerTask;
        private int _nextId;
        private bool _disposed;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcess(
            string lpApplicationName,
            StringBuilder lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref StartupInfo lpStartupInfo,
            out ProcessInformation lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DuplicateHandle(
            IntPtr hSourceProcessHandle,
            IntPtr hSourceHandle,
            IntPtr hTargetProcessHandle,
            out SafeFileHandle lpTargetHandle,
            uint dwDesiredAccess,
            bool bInheritHandle,
            uint dwOptions);

        private CdpPipeConnection(Process process, AnonymousPipeServerStream toChild, AnonymousPipeServerStream fromChild, JavaScriptSerializer json)
        {
            _process = process;
            _toChild = toChild;
            _fromChild = fromChild;
            _json = json;
            _readerTask = Task.Run((Func<Task>)ReadLoopAsync);
        }

        public bool IsUsable
        {
            get
            {
                if (_disposed || _process == null) return false;
                try
                {
                    return !_process.HasExited && _toChild.IsConnected && _fromChild.IsConnected;
                }
                catch
                {
                    return false;
                }
            }
        }

        public int ProcessId
        {
            get { return _process == null ? 0 : _process.Id; }
        }

        public static CdpPipeConnection Start(string exePath, string workingDirectory, JavaScriptSerializer json)
        {
            if (String.IsNullOrWhiteSpace(exePath))
            {
                throw new InvalidOperationException("ChatGPT.exe was not found.");
            }

            var toChild = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
            var fromChild = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
            var inherited = new List<SafeFileHandle>();
            var processInfo = new ProcessInformation();
            bool started = false;

            try
            {
                SafeFileHandle childRead = DuplicateInheritableHandle(toChild.ClientSafePipeHandle);
                SafeFileHandle childWrite = DuplicateInheritableHandle(fromChild.ClientSafePipeHandle);
                SafeFileHandle stdIn = DuplicateStdHandle(StdInputHandle);
                SafeFileHandle stdOut = DuplicateStdHandle(StdOutputHandle);
                SafeFileHandle stdErr = DuplicateStdHandle(StdErrorHandle);
                inherited.Add(childRead);
                inherited.Add(childWrite);
                if (stdIn != null) inherited.Add(stdIn);
                if (stdOut != null) inherited.Add(stdOut);
                if (stdErr != null) inherited.Add(stdErr);

                byte[] crtBlock = BuildCrtHandleBlock(stdIn, stdOut, stdErr, childRead, childWrite);
                GCHandle pinnedBlock = GCHandle.Alloc(crtBlock, GCHandleType.Pinned);
                try
                {
                    var startupInfo = new StartupInfo();
                    startupInfo.cb = Marshal.SizeOf(typeof(StartupInfo));
                    startupInfo.dwFlags = StartfUseStdHandles;
                    startupInfo.cbReserved2 = checked((short)crtBlock.Length);
                    startupInfo.lpReserved2 = pinnedBlock.AddrOfPinnedObject();
                    startupInfo.hStdInput = SafeHandleValue(stdIn);
                    startupInfo.hStdOutput = SafeHandleValue(stdOut);
                    startupInfo.hStdError = SafeHandleValue(stdErr);

                    string commandLine = QuoteCommandLineArgument(exePath) + " --remote-debugging-pipe";
                    var mutableCommandLine = new StringBuilder(commandLine);
                    string cwd = String.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory)
                        ? Path.GetDirectoryName(exePath)
                        : workingDirectory;

                    if (!CreateProcess(
                        exePath,
                        mutableCommandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        true,
                        0,
                        IntPtr.Zero,
                        cwd,
                        ref startupInfo,
                        out processInfo))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start ChatGPT with remote-debugging-pipe.");
                    }
                    started = true;
                }
                finally
                {
                    pinnedBlock.Free();
                }

                toChild.DisposeLocalCopyOfClientHandle();
                fromChild.DisposeLocalCopyOfClientHandle();

                Process process = Process.GetProcessById(processInfo.dwProcessId);
                return new CdpPipeConnection(process, toChild, fromChild, json);
            }
            finally
            {
                foreach (SafeFileHandle handle in inherited)
                {
                    if (handle != null) handle.Dispose();
                }
                if (processInfo.hThread != IntPtr.Zero) CloseHandle(processInfo.hThread);
                if (processInfo.hProcess != IntPtr.Zero) CloseHandle(processInfo.hProcess);
                if (!started)
                {
                    try { toChild.Dispose(); } catch { }
                    try { fromChild.Dispose(); } catch { }
                }
            }
        }

        public async Task<List<CdpTarget>> GetTargetsAsync()
        {
            Dictionary<string, object> response = await SendCommandAsync("Target.getTargets", null, null, 8);
            var result = new List<CdpTarget>();
            var resultMap = response.ContainsKey("result") ? response["result"] as Dictionary<string, object> : null;
            var targetInfos = resultMap != null && resultMap.ContainsKey("targetInfos") ? resultMap["targetInfos"] as object[] : null;
            if (targetInfos == null) return result;

            foreach (object item in targetInfos)
            {
                var map = item as Dictionary<string, object>;
                if (map == null) continue;
                result.Add(new CdpTarget
                {
                    TargetId = GetString(map, "targetId"),
                    Type = GetString(map, "type"),
                    Title = GetString(map, "title"),
                    Url = GetString(map, "url")
                });
            }
            return result;
        }

        public async Task<Dictionary<string, object>> EvaluateAsync(string targetId, string expression)
        {
            if (String.IsNullOrWhiteSpace(targetId))
            {
                throw new InvalidOperationException("ChatGPT target was missing its DevTools target id.");
            }

            string sessionId = "";
            var attachParams = new Dictionary<string, object>();
            attachParams["targetId"] = targetId;
            attachParams["flatten"] = true;
            Dictionary<string, object> attachResponse = await SendCommandAsync("Target.attachToTarget", attachParams, null, 8);
            var attachResult = attachResponse.ContainsKey("result") ? attachResponse["result"] as Dictionary<string, object> : null;
            if (attachResult != null) sessionId = GetString(attachResult, "sessionId");
            if (String.IsNullOrWhiteSpace(sessionId))
            {
                throw new InvalidOperationException("ChatGPT did not return a DevTools session id.");
            }

            Dictionary<string, object> response = null;
            Exception evaluateError = null;
            try
            {
                var evaluateParams = new Dictionary<string, object>();
                evaluateParams["expression"] = expression;
                evaluateParams["returnByValue"] = true;
                evaluateParams["awaitPromise"] = false;
                response = await SendCommandAsync("Runtime.evaluate", evaluateParams, sessionId, 8);
            }
            catch (Exception ex)
            {
                evaluateError = ex;
            }

            try
            {
                var detachParams = new Dictionary<string, object>();
                detachParams["sessionId"] = sessionId;
                await SendCommandAsync("Target.detachFromTarget", detachParams, null, 3);
            }
            catch
            {
            }

            if (evaluateError != null) throw evaluateError;
            return response;
        }

        private async Task<Dictionary<string, object>> SendCommandAsync(string method, Dictionary<string, object> parameters, string sessionId, int timeoutSeconds)
        {
            if (!IsUsable)
            {
                throw new InvalidOperationException("ChatGPT is not running on the private DevTools pipe.");
            }

            int id = Interlocked.Increment(ref _nextId);
            var payload = new Dictionary<string, object>();
            payload["id"] = id;
            payload["method"] = method;
            if (parameters != null) payload["params"] = parameters;
            if (!String.IsNullOrEmpty(sessionId)) payload["sessionId"] = sessionId;

            var completion = new TaskCompletionSource<Dictionary<string, object>>();
            lock (_pendingLock)
            {
                _pending[id] = completion;
            }

            try
            {
                byte[] messageBytes = Encoding.UTF8.GetBytes(_json.Serialize(payload));
                await _writeGate.WaitAsync();
                try
                {
                    await _toChild.WriteAsync(messageBytes, 0, messageBytes.Length);
                    _toChild.WriteByte(0);
                    await _toChild.FlushAsync();
                }
                finally
                {
                    _writeGate.Release();
                }

                Task timeout = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
                Task finished = await Task.WhenAny(completion.Task, timeout);
                if (finished != completion.Task)
                {
                    lock (_pendingLock)
                    {
                        _pending.Remove(id);
                    }
                    throw new TimeoutException("Timed out waiting for ChatGPT DevTools pipe response.");
                }

                Dictionary<string, object> response = await completion.Task;
                ThrowIfProtocolCommandFailed(response);
                return response;
            }
            catch
            {
                lock (_pendingLock)
                {
                    _pending.Remove(id);
                }
                throw;
            }
        }

        private async Task ReadLoopAsync()
        {
            var current = new MemoryStream();
            var buffer = new byte[8192];
            try
            {
                while (!_disposed)
                {
                    int count = await _fromChild.ReadAsync(buffer, 0, buffer.Length);
                    if (count <= 0) break;
                    for (int i = 0; i < count; i++)
                    {
                        byte value = buffer[i];
                        if (value == 0)
                        {
                            DispatchMessage(current.ToArray());
                            current.SetLength(0);
                        }
                        else
                        {
                            current.WriteByte(value);
                        }
                    }
                }
                FailPending(new InvalidOperationException("ChatGPT closed the private DevTools pipe."));
            }
            catch (Exception ex)
            {
                if (!_disposed) FailPending(ex);
            }
            finally
            {
                current.Dispose();
            }
        }

        private void DispatchMessage(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return;
            Dictionary<string, object> message;
            try
            {
                message = _json.DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>;
            }
            catch
            {
                return;
            }
            if (message == null || !message.ContainsKey("id")) return;

            int id;
            try
            {
                id = Convert.ToInt32(message["id"]);
            }
            catch
            {
                return;
            }

            TaskCompletionSource<Dictionary<string, object>> completion = null;
            lock (_pendingLock)
            {
                if (_pending.TryGetValue(id, out completion))
                {
                    _pending.Remove(id);
                }
            }
            if (completion != null) completion.TrySetResult(message);
        }

        private void FailPending(Exception ex)
        {
            List<TaskCompletionSource<Dictionary<string, object>>> pending;
            lock (_pendingLock)
            {
                pending = _pending.Values.ToList();
                _pending.Clear();
            }
            foreach (var completion in pending)
            {
                completion.TrySetException(ex);
            }
        }

        private static void ThrowIfProtocolCommandFailed(Dictionary<string, object> response)
        {
            if (response == null || !response.ContainsKey("error")) return;
            var error = response["error"] as Dictionary<string, object>;
            string message = error == null ? Convert.ToString(response["error"]) : GetString(error, "message");
            throw new InvalidOperationException("ChatGPT rejected the DevTools command: " + (String.IsNullOrWhiteSpace(message) ? "unknown pipe error" : message));
        }

        private static SafeFileHandle DuplicateStdHandle(int stdHandleId)
        {
            IntPtr handle = GetStdHandle(stdHandleId);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return null;
            try
            {
                return DuplicateInheritableHandle(handle);
            }
            catch
            {
                return null;
            }
        }

        private static SafeFileHandle DuplicateInheritableHandle(SafeHandle source)
        {
            return DuplicateInheritableHandle(source.DangerousGetHandle());
        }

        private static SafeFileHandle DuplicateInheritableHandle(IntPtr source)
        {
            if (source == IntPtr.Zero || source == new IntPtr(-1))
            {
                throw new InvalidOperationException("Cannot inherit an invalid pipe handle.");
            }

            SafeFileHandle duplicate;
            IntPtr currentProcess = GetCurrentProcess();
            if (!DuplicateHandle(currentProcess, source, currentProcess, out duplicate, 0, true, DuplicateSameAccess))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not duplicate an inheritable pipe handle.");
            }
            return duplicate;
        }

        private static byte[] BuildCrtHandleBlock(SafeFileHandle stdIn, SafeFileHandle stdOut, SafeFileHandle stdErr, SafeFileHandle childRead, SafeFileHandle childWrite)
        {
            int count = 5;
            int handleOffset = sizeof(int) + count;
            byte[] block = new byte[handleOffset + count * IntPtr.Size];
            BitConverter.GetBytes(count).CopyTo(block, 0);
            WriteCrtHandle(block, 0, stdIn, false);
            WriteCrtHandle(block, 1, stdOut, false);
            WriteCrtHandle(block, 2, stdErr, false);
            WriteCrtHandle(block, 3, childRead, true);
            WriteCrtHandle(block, 4, childWrite, true);
            return block;
        }

        private static void WriteCrtHandle(byte[] block, int index, SafeFileHandle handle, bool pipe)
        {
            int flagsOffset = sizeof(int) + index;
            int handleOffset = sizeof(int) + 5 + index * IntPtr.Size;
            IntPtr value = SafeHandleValue(handle);
            if (value == IntPtr.Zero || value == new IntPtr(-1))
            {
                block[flagsOffset] = 0;
                WriteIntPtr(block, handleOffset, IntPtr.Zero);
                return;
            }

            block[flagsOffset] = pipe ? CrtFOpenPipe : (byte)0x01;
            WriteIntPtr(block, handleOffset, value);
        }

        private static void WriteIntPtr(byte[] block, int offset, IntPtr value)
        {
            byte[] bytes = IntPtr.Size == 8
                ? BitConverter.GetBytes(value.ToInt64())
                : BitConverter.GetBytes(value.ToInt32());
            Buffer.BlockCopy(bytes, 0, block, offset, bytes.Length);
        }

        private static IntPtr SafeHandleValue(SafeHandle handle)
        {
            if (handle == null || handle.IsInvalid || handle.IsClosed) return IntPtr.Zero;
            return handle.DangerousGetHandle();
        }

        private static string QuoteCommandLineArgument(string value)
        {
            if (String.IsNullOrEmpty(value)) return "\"\"";
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string GetString(Dictionary<string, object> map, string key)
        {
            if (map == null || !map.ContainsKey(key) || map[key] == null) return "";
            return Convert.ToString(map[key]);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            FailPending(new ObjectDisposedException("CdpPipeConnection"));
            try { _toChild.Dispose(); } catch { }
            try { _fromChild.Dispose(); } catch { }
            try { _writeGate.Dispose(); } catch { }
            GC.KeepAlive(_readerTask);
        }
    }

    internal sealed class ThemeDocument
    {
        public string Format { get; set; }
        public int Version { get; set; }
        public Theme Theme { get; set; }
        public SkinnerSettings Settings { get; set; }
    }

    internal sealed class ChatGptDetectionResult
    {
        public bool Found { get; set; }
        public string ExePath { get; set; }
        public string AppUserModelId { get; set; }
        public string Method { get; set; }
        public List<string> Checks { get; private set; }

        public ChatGptDetectionResult()
        {
            Checks = new List<string>();
        }

        public string Summary
        {
            get
            {
                if (Found)
                {
                    return "ChatGPT Found" + (String.IsNullOrEmpty(Method) ? "" : " via " + Method) + ".";
                }
                return "ChatGPT Not Found.";
            }
        }

        public string Diagnostics
        {
            get { return Checks.Count == 0 ? "No detection checks have run yet." : String.Join(" ", Checks.ToArray()); }
        }
    }

    internal sealed class PreviewThemeTokens
    {
        public Color Bg;
        public Color Panel;
        public Color Sidebar;
        public Color Input;
        public Color User;
        public Color Composer;
        public Color Text;
        public Color Accent;
        public Color Border;
        public Color Hover;
        public Color Active;
        public Color Selected;
        public int PanelAlpha;
        public int SidebarAlpha;
        public int InputAlpha;
        public int ComposerAlpha;
        public int SearchPanelAlpha;
        public int SearchAccentAlpha;
        public int ComposerPanelAlpha;
        public int ComposerAccentAlpha;
        public int CardAlpha;
        public int OverlayAlpha;
        public int BackgroundAccentAlpha;
        public int BackgroundEndAlpha;
        public int PanelImageShadeAlpha;
        public int RowAlpha;
        public int HoverAlpha;
        public double Glass;
        public string Layout;
        public bool GlassSearch;
    }

    internal static class UiShape
    {
        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int r = Math.Max(1, radius);
            int d = r * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Color SurfaceBackColor(Control control, Color fallback)
        {
            for (Control current = control == null ? null : control.Parent; current != null; current = current.Parent)
            {
                if (current.BackColor.A > 0 && current.BackColor != Color.Transparent)
                {
                    return current.BackColor;
                }
            }
            return fallback;
        }
    }

    internal sealed class RoundedPanel : Panel
    {
        public int Radius { get; set; }
        public Color BorderColor { get; set; }
        public Color GradientTop { get; set; }
        public Color GradientBottom { get; set; }
        public bool ClipToRadius { get; set; }

        public RoundedPanel()
        {
            Radius = 8;
            BorderColor = Color.Transparent;
            GradientTop = Color.Empty;
            GradientBottom = Color.Empty;
            ClipToRadius = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            if (ClipToRadius)
            {
                UpdateRegion();
            }
            else if (Region != null)
            {
                Region = null;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(UiShape.SurfaceBackColor(this, Color.FromArgb(17, 20, 31)));
            using (var path = UiShape.RoundedRect(new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), Radius))
            using (var brush = GradientTop == Color.Empty || GradientBottom == Color.Empty
                ? (Brush)new SolidBrush(BackColor)
                : new LinearGradientBrush(ClientRectangle, GradientTop, GradientBottom, LinearGradientMode.Vertical))
            {
                e.Graphics.FillPath(brush, path);
                if (BorderColor.A > 0)
                {
                    using (var border = new Pen(BorderColor, 1f))
                    {
                        e.Graphics.DrawPath(border, path);
                    }
                }
            }
            base.OnPaint(e);
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (var path = UiShape.RoundedRect(new Rectangle(0, 0, Width, Height), Radius))
            {
                Region = new Region(path);
            }
        }
    }

    internal class HiddenScrollPanel : Panel
    {
        private const int SbBoth = 3;

        [DllImport("user32.dll")]
        private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

        public HiddenScrollPanel()
        {
            AutoScroll = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            HideBars();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            HideBars();
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            HideBars();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            HideBars();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
        }

        protected void HideBars()
        {
            if (IsHandleCreated)
            {
                ShowScrollBar(Handle, SbBoth, false);
            }
        }
    }

    internal sealed class HiddenScrollFlowLayoutPanel : FlowLayoutPanel
    {
        private const int SbBoth = 3;

        [DllImport("user32.dll")]
        private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

        public HiddenScrollFlowLayoutPanel()
        {
            AutoScroll = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            HideBars();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            HideBars();
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            HideBars();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            HideBars();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
        }

        private void HideBars()
        {
            if (IsHandleCreated)
            {
                ShowScrollBar(Handle, SbBoth, false);
            }
        }
    }

    internal sealed class ResizeAwareTableLayoutPanel : TableLayoutPanel
    {
        public Func<Point, IntPtr> ResizeHitTest { get; set; }

        public ResizeAwareTableLayoutPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84 && ResizeHitTest != null)
            {
                IntPtr result = ResizeHitTest(ScreenPointFromLParam(m.LParam));
                if (result != IntPtr.Zero)
                {
                    m.Result = result;
                    return;
                }
            }

            base.WndProc(ref m);
        }

        private static Point ScreenPointFromLParam(IntPtr lParam)
        {
            long value = lParam.ToInt64();
            int x = (short)(value & 0xffff);
            int y = (short)((value >> 16) & 0xffff);
            return new Point(x, y);
        }
    }

    internal sealed class ThemedComboBox : ComboBox
    {
        private const int CbGetComboBoxInfo = 0x0164;
        private const int GclStyle = -26;
        private const int CsDropShadow = 0x00020000;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;

        public Color ThemeBackColor { get; set; }
        public Color ThemeForeColor { get; set; }
        public Color ThemeAccentColor { get; set; }

        [StructLayout(LayoutKind.Sequential)]
        private struct ComboBoxInfo
        {
            public int cbSize;
            public Rectangle rcItem;
            public Rectangle rcButton;
            public int stateButton;
            public IntPtr hwndCombo;
            public IntPtr hwndItem;
            public IntPtr hwndList;
        }

        [DllImport("user32.dll", EntryPoint = "SendMessage")]
        private static extern IntPtr SendComboMessage(IntPtr hWnd, int msg, IntPtr wParam, ref ComboBoxInfo lParam);

        [DllImport("user32.dll", EntryPoint = "GetClassLong")]
        private static extern int GetClassLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetClassLongPtr")]
        private static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetClassLong")]
        private static extern int SetClassLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetClassLongPtr")]
        private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        public ThemedComboBox()
        {
            ThemeBackColor = Color.FromArgb(14, 17, 27);
            ThemeForeColor = Color.FromArgb(245, 247, 251);
            ThemeAccentColor = Color.FromArgb(60, 68, 86);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (DropDownStyle != ComboBoxStyle.DropDownList) return;
            if (m.Msg == 0x000F || m.Msg == 0x0085 || m.Msg == 0x0014)
            {
                PaintShell();
            }
        }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            base.OnSelectedIndexChanged(e);
            Invalidate();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnDropDown(EventArgs e)
        {
            base.OnDropDown(e);
            RemoveDropDownShadow();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        private void PaintShell()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0) return;
            using (Graphics g = Graphics.FromHwnd(Handle))
            using (var back = new SolidBrush(Enabled ? ThemeBackColor : Color.FromArgb(21, 24, 33)))
            using (var arrowBack = new SolidBrush(Enabled ? ThemeBackColor : Color.FromArgb(21, 24, 33)))
            using (var arrow = new SolidBrush(Enabled ? ThemeForeColor : Color.FromArgb(92, 98, 112)))
            {
                Rectangle bounds = new Rectangle(0, 0, Width, Height);
                g.FillRectangle(back, bounds);
                Rectangle arrowRect = new Rectangle(Math.Max(0, Width - 32), 0, 32, Height);
                g.FillRectangle(arrowBack, arrowRect);

                Rectangle textRect = new Rectangle(9, 0, Math.Max(1, Width - 50), Height);
                TextRenderer.DrawText(
                    g,
                    Text,
                    Font,
                    textRect,
                    Enabled ? ThemeForeColor : Color.FromArgb(92, 98, 112),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

                int cx = arrowRect.Left + (arrowRect.Width / 2);
                int cy = arrowRect.Top + (arrowRect.Height / 2) + 1;
                Point[] points =
                {
                    new Point(cx - 4, cy - 2),
                    new Point(cx + 4, cy - 2),
                    new Point(cx, cy + 3)
                };
                g.FillPolygon(arrow, points);
            }
        }

        private void RemoveDropDownShadow()
        {
            if (!IsHandleCreated) return;
            try
            {
                var info = new ComboBoxInfo();
                info.cbSize = Marshal.SizeOf(typeof(ComboBoxInfo));
                if (SendComboMessage(Handle, CbGetComboBoxInfo, IntPtr.Zero, ref info) == IntPtr.Zero || info.hwndList == IntPtr.Zero)
                {
                    return;
                }

                IntPtr stylePtr = GetClassStyle(info.hwndList);
                long style = stylePtr.ToInt64();
                if ((style & CsDropShadow) == 0) return;

                SetClassStyle(info.hwndList, new IntPtr(style & ~CsDropShadow));
                SetWindowPos(info.hwndList, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
            }
            catch
            {
            }
        }

        private static IntPtr GetClassStyle(IntPtr hWnd)
        {
            return IntPtr.Size == 8
                ? GetClassLongPtr64(hWnd, GclStyle)
                : new IntPtr(GetClassLong32(hWnd, GclStyle));
        }

        private static void SetClassStyle(IntPtr hWnd, IntPtr value)
        {
            if (IntPtr.Size == 8)
            {
                SetClassLongPtr64(hWnd, GclStyle, value);
            }
            else
            {
                SetClassLong32(hWnd, GclStyle, value.ToInt32());
            }
        }
    }

    internal sealed class ThemedTextBox : TextBox
    {
        private const int EmSetRect = 0xB3;
        private bool _normalizing;

        [StructLayout(LayoutKind.Sequential)]
        private struct TextRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref TextRect lParam);

        public ThemedTextBox()
        {
            AutoSize = false;
            BorderStyle = BorderStyle.FixedSingle;
            Multiline = true;
            WordWrap = false;
            ScrollBars = ScrollBars.None;
            ShortcutsEnabled = true;
            AcceptsReturn = false;
            AcceptsTab = false;
            Height = 30;
            MinimumSize = new Size(0, 30);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyTextRect();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyTextRect();
        }

        protected override void OnEnter(EventArgs e)
        {
            base.OnEnter(e);
            QueueTextRect();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            QueueTextRect();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            QueueTextRect();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            QueueTextRect();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            ApplyTextRect();
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (e.KeyChar == '\r' || e.KeyChar == '\n')
            {
                e.Handled = true;
                return;
            }
            base.OnKeyPress(e);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            if (!_normalizing && (Text.IndexOf('\r') >= 0 || Text.IndexOf('\n') >= 0))
            {
                _normalizing = true;
                int selection = SelectionStart;
                Text = Text.Replace("\r", "").Replace("\n", "");
                SelectionStart = Math.Min(selection, TextLength);
                _normalizing = false;
            }
            base.OnTextChanged(e);
            ApplyTextRect();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (Enabled && !ReadOnly && (keyData == Keys.Back || keyData == Keys.Delete))
            {
                DeleteText(keyData == Keys.Back);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void DeleteText(bool backwards)
        {
            int selection = SelectionStart;
            if (SelectionLength > 0)
            {
                SelectedText = "";
                return;
            }

            if (backwards)
            {
                if (selection <= 0) return;
                Text = Text.Remove(selection - 1, 1);
                SelectionStart = selection - 1;
                return;
            }

            if (selection >= TextLength) return;
            Text = Text.Remove(selection, 1);
            SelectionStart = selection;
        }

        private void ApplyTextRect()
        {
            if (!IsHandleCreated || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            int textHeight = TextRenderer.MeasureText("Ag", Font).Height;
            int top = Math.Max(0, (ClientSize.Height - textHeight) / 2);
            var rect = new TextRect
            {
                Left = 8,
                Top = top,
                Right = Math.Max(9, ClientSize.Width - 8),
                Bottom = Math.Min(ClientSize.Height, top + textHeight + 5)
            };
            SendMessage(Handle, EmSetRect, IntPtr.Zero, ref rect);
            Invalidate();
        }

        private void QueueTextRect()
        {
            ApplyTextRect();
            if (!IsHandleCreated || IsDisposed) return;
            try
            {
                BeginInvoke((MethodInvoker)ApplyTextRect);
            }
            catch
            {
            }
        }

        public void RefreshTextLayout()
        {
            ApplyTextRect();
        }
    }

    internal sealed class CenteredTextInput : Control
    {
        private int _caretIndex;
        private int _selectionStart;
        private int _selectionLength;
        private string _textValue = "";

        public int MaxLength { get; set; }
        public Func<char, bool> CharacterFilter { get; set; }
        public bool SelectAllOnFocus { get; set; }
        public Color BorderColor { get; set; }
        public int Radius { get; set; }
        public HorizontalAlignment TextAlignment { get; set; }

        public override string Text
        {
            get { return _textValue; }
            set
            {
                SetTextValue(value ?? "", (value ?? "").Length);
            }
        }

        public CenteredTextInput()
        {
            MaxLength = 5;
            CharacterFilter = delegate(char c) { return true; };
            SelectAllOnFocus = false;
            TextAlignment = HorizontalAlignment.Center;
            Height = 30;
            MinimumSize = new Size(0, 30);
            TabStop = true;
            Cursor = Cursors.IBeam;
            BackColor = Color.FromArgb(14, 17, 27);
            ForeColor = Color.FromArgb(245, 247, 251);
            BorderColor = Color.Transparent;
            Radius = 0;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Left || key == Keys.Right || key == Keys.Home || key == Keys.End || key == Keys.Back || key == Keys.Delete)
            {
                return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            _caretIndex = Math.Max(0, Math.Min(_caretIndex, _textValue.Length));
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            bool hadFocus = Focused;
            Focus();
            if ((SelectAllOnFocus && !hadFocus) || e.Clicks > 1)
            {
                SelectAllText();
            }
            else
            {
                _caretIndex = CaretIndexFromX(e.X);
                ClearSelection();
                Invalidate();
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            if (SelectAllOnFocus)
            {
                SelectAllText();
            }
            else
            {
                _caretIndex = Text.Length;
                ClearSelection();
                Invalidate();
            }
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.A)
            {
                SelectAllText();
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Control && e.KeyCode == Keys.C)
            {
                CopySelection();
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Control && e.KeyCode == Keys.X)
            {
                CutSelection();
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Control && e.KeyCode == Keys.V)
            {
                PasteAllowedText();
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Back)
            {
                if (!DeleteSelection() && _caretIndex > 0 && Text.Length > 0)
                {
                    int nextCaret = _caretIndex - 1;
                    SetTextValue(Text.Remove(nextCaret, 1), nextCaret);
                }
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Delete)
            {
                if (!DeleteSelection() && _caretIndex < _textValue.Length)
                {
                    SetTextValue(_textValue.Remove(_caretIndex, 1), _caretIndex);
                }
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Left)
            {
                _caretIndex = Math.Max(0, _caretIndex - 1);
                ClearSelection();
                Invalidate();
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Right)
            {
                _caretIndex = Math.Min(_textValue.Length, _caretIndex + 1);
                ClearSelection();
                Invalidate();
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Home)
            {
                _caretIndex = 0;
                ClearSelection();
                Invalidate();
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.End)
            {
                _caretIndex = _textValue.Length;
                ClearSelection();
                Invalidate();
                e.SuppressKeyPress = true;
                return;
            }

            base.OnKeyDown(e);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (!Char.IsControl(e.KeyChar) && CharacterFilter != null && CharacterFilter(e.KeyChar))
            {
                InsertText(e.KeyChar.ToString());
                e.Handled = true;
                return;
            }

            if (!Char.IsControl(e.KeyChar))
            {
                e.Handled = true;
                return;
            }

            base.OnKeyPress(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(UiShape.SurfaceBackColor(this, BackColor));
            Rectangle bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            if (Radius > 0)
            {
                using (var path = UiShape.RoundedRect(bounds, Radius))
                using (var fill = new SolidBrush(BackColor))
                {
                    e.Graphics.FillPath(fill, path);
                    if (BorderColor.A > 0)
                    {
                        using (var pen = new Pen(BorderColor, 1f))
                        {
                            e.Graphics.DrawPath(pen, path);
                        }
                    }
                }
            }
            else
            {
                using (var fill = new SolidBrush(BackColor))
                {
                    e.Graphics.FillRectangle(fill, bounds);
                }
            }
            Rectangle textRect = new Rectangle(8, 0, Math.Max(1, Width - 16), Height);
            DrawSelection(e.Graphics, textRect);
            TextFormatFlags align = TextAlignment == HorizontalAlignment.Left
                ? TextFormatFlags.Left
                : (TextAlignment == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(
                e.Graphics,
                _textValue,
                Font,
                textRect,
                ForeColor,
                align | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            if (Focused && !HasSelection)
            {
                DrawCaret(e.Graphics, textRect);
            }
        }

        private void PasteAllowedText()
        {
            try
            {
                if (!Clipboard.ContainsText()) return;
                string allowed = new string(Clipboard.GetText().Where(delegate(char c)
                {
                    return CharacterFilter == null || CharacterFilter(c);
                }).ToArray());
                InsertText(allowed);
            }
            catch
            {
            }
        }

        private void InsertText(string value)
        {
            if (String.IsNullOrEmpty(value)) return;
            int selectionLength = HasSelection ? _selectionLength : 0;
            int start = HasSelection ? _selectionStart : _caretIndex;
            string current = selectionLength > 0 ? _textValue.Remove(start, selectionLength) : _textValue;
            int available = Math.Max(0, MaxLength - current.Length);
            if (available == 0) return;
            string insert = value.Length > available ? value.Substring(0, available) : value;
            SetTextValue(current.Insert(start, insert), start + insert.Length);
        }

        private void SetTextValue(string next, int caretIndex)
        {
            next = next ?? "";
            if (MaxLength > 0 && next.Length > MaxLength)
            {
                next = next.Substring(0, MaxLength);
            }

            bool changed = !String.Equals(_textValue, next, StringComparison.Ordinal);
            _textValue = next;
            _caretIndex = Math.Max(0, Math.Min(caretIndex, _textValue.Length));
            ClearSelection();
            if (changed)
            {
                base.OnTextChanged(EventArgs.Empty);
            }
            Invalidate();
        }

        private bool HasSelection
        {
            get { return _selectionLength > 0 && _selectionStart >= 0 && _selectionStart < _textValue.Length; }
        }

        private void ClearSelection()
        {
            _selectionStart = _caretIndex;
            _selectionLength = 0;
        }

        private void SelectAllText()
        {
            _selectionStart = 0;
            _selectionLength = _textValue.Length;
            _caretIndex = _textValue.Length;
            Invalidate();
        }

        private bool DeleteSelection()
        {
            if (!HasSelection) return false;
            int start = _selectionStart;
            SetTextValue(_textValue.Remove(_selectionStart, _selectionLength), start);
            return true;
        }

        private void CopySelection()
        {
            if (!HasSelection) return;
            try
            {
                Clipboard.SetText(_textValue.Substring(_selectionStart, _selectionLength));
            }
            catch
            {
            }
        }

        private void CutSelection()
        {
            if (!HasSelection) return;
            CopySelection();
            DeleteSelection();
        }

        private void DrawSelection(Graphics graphics, Rectangle textRect)
        {
            if (!Focused || !HasSelection) return;
            int start = Math.Max(0, Math.Min(_selectionStart, _textValue.Length));
            int length = Math.Max(0, Math.Min(_selectionLength, _textValue.Length - start));
            if (length == 0) return;

            Size fullSize = MeasureText(graphics, _textValue);
            Size beforeSize = MeasureText(graphics, start == 0 ? "" : _textValue.Substring(0, start));
            Size selectedSize = MeasureText(graphics, _textValue.Substring(start, length));
            int textLeft = TextLeft(textRect, fullSize.Width);
            int selectionHeight = Math.Min(20, Math.Max(14, Height - 8));
            int selectionTop = (Height - selectionHeight) / 2;
            var selectionRect = new Rectangle(
                Math.Max(textRect.Left, textLeft + beforeSize.Width),
                selectionTop,
                Math.Max(4, selectedSize.Width + 3),
                selectionHeight);
            using (var brush = new SolidBrush(Color.FromArgb(92, 10, 132, 255)))
            {
                graphics.FillRectangle(brush, selectionRect);
            }
        }

        private void DrawCaret(Graphics graphics, Rectangle textRect)
        {
            string beforeCaret = _caretIndex <= 0 ? "" : _textValue.Substring(0, Math.Min(_caretIndex, _textValue.Length));
            Size fullSize = MeasureText(graphics, _textValue);
            Size caretSize = MeasureText(graphics, beforeCaret);
            int textLeft = TextLeft(textRect, fullSize.Width);
            int caretX = Math.Max(textRect.Left + 2, Math.Min(textRect.Right - 2, textLeft + caretSize.Width + 1));
            int caretHeight = Math.Min(18, Math.Max(12, Height - 10));
            int caretY = (Height - caretHeight) / 2;
            using (var pen = new Pen(ForeColor, 1f))
            {
                graphics.DrawLine(pen, caretX, caretY, caretX, caretY + caretHeight);
            }
        }

        private Size MeasureText(Graphics graphics, string value)
        {
            if (String.IsNullOrEmpty(value)) return Size.Empty;
            return TextRenderer.MeasureText(
                graphics,
                value,
                Font,
                new Size(Int32.MaxValue, Height),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        private int TextLeft(Rectangle textRect, int textWidth)
        {
            if (TextAlignment == HorizontalAlignment.Left) return textRect.Left;
            if (TextAlignment == HorizontalAlignment.Right) return textRect.Right - textWidth;
            return textRect.Left + ((textRect.Width - textWidth) / 2);
        }

        private int CaretIndexFromX(int x)
        {
            if (String.IsNullOrEmpty(_textValue)) return 0;
            using (Graphics graphics = CreateGraphics())
            {
                Rectangle textRect = new Rectangle(8, 0, Math.Max(1, Width - 16), Height);
                int textLeft = TextLeft(textRect, MeasureText(graphics, _textValue).Width);
                for (int i = 0; i <= _textValue.Length; i++)
                {
                    string left = i <= 0 ? "" : _textValue.Substring(0, i);
                    int leftWidth = MeasureText(graphics, left).Width;
                    if (x <= textLeft + leftWidth + 3)
                    {
                        return i;
                    }
                }
            }
            return _textValue.Length;
        }
    }

    internal sealed class ThemedCheckBox : CheckBox
    {
        public Color BoxBackColor { get; set; }
        public Color BoxBorderColor { get; set; }
        public Color CheckedBackColor { get; set; }
        public Color CheckMarkColor { get; set; }
        public Color TextColor { get; set; }

        public ThemedCheckBox()
        {
            BoxBackColor = Color.FromArgb(18, 21, 31);
            BoxBorderColor = Color.FromArgb(78, 86, 108);
            CheckedBackColor = Color.FromArgb(255, 126, 188);
            CheckMarkColor = Color.White;
            TextColor = Color.FromArgb(245, 247, 251);
            Cursor = Cursors.Hand;
            TabStop = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color clear = BackColor.A > 0 && BackColor != Color.Transparent
                ? BackColor
                : UiShape.SurfaceBackColor(this, Color.FromArgb(17, 20, 31));
            e.Graphics.Clear(clear);
            int boxSize = 16;
            Rectangle box = new Rectangle(0, Math.Max(0, (Height - boxSize) / 2), boxSize, boxSize);
            Color fill = Checked ? CheckedBackColor : BoxBackColor;
            Color borderColor = Checked ? CheckedBackColor : BoxBorderColor;
            Color textColor = Enabled ? TextColor : Color.FromArgb(110, 116, 130);

            using (var path = UiShape.RoundedRect(box, 4))
            using (var brush = new SolidBrush(Enabled ? fill : Color.FromArgb(28, 31, 42)))
            {
                e.Graphics.FillPath(brush, path);
                if (borderColor.A > 0)
                {
                    using (var border = new Pen(Enabled ? borderColor : Color.FromArgb(52, 58, 72), 1.2f))
                    {
                        e.Graphics.DrawPath(border, path);
                    }
                }
            }

            if (Checked)
            {
                using (var pen = new Pen(CheckMarkColor, 1.8f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    e.Graphics.DrawLine(pen, box.Left + 4, box.Top + 8, box.Left + 7, box.Top + 11);
                    e.Graphics.DrawLine(pen, box.Left + 7, box.Top + 11, box.Right - 4, box.Top + 5);
                }
            }

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                new Rectangle(box.Right + 8, 0, Math.Max(1, Width - box.Right - 8), Height),
                textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    internal enum ButtonIconKind
    {
        None,
        Eye,
        Trash,
        Eraser,
        Check,
        External,
        WindowMinimize,
        WindowMaximize,
        WindowClose
    }

    internal sealed class RoundedButton : Button
    {
        private bool _hovered;
        private bool _pressed;

        public int Radius { get; set; }
        public Color DisabledBackColor { get; set; }
        public Color DisabledForeColor { get; set; }
        public ButtonIconKind IconKind { get; set; }

        public RoundedButton()
        {
            Radius = 8;
            IconKind = ButtonIconKind.None;
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            TabStop = false;
            FlatAppearance.BorderSize = 0;
            DisabledBackColor = Color.FromArgb(25, 28, 38);
            DisabledForeColor = Color.FromArgb(92, 98, 112);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
        }

        protected override bool ShowFocusCues
        {
            get { return false; }
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(UiShape.SurfaceBackColor(this, Color.FromArgb(17, 20, 31)));
            Rectangle bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            Color fill = Enabled ? StateBackColor(BackColor) : DisabledBackColor;
            Color text = Enabled ? ForeColor : DisabledForeColor;
            using (var path = UiShape.RoundedRect(bounds, Radius))
            using (var brush = new SolidBrush(fill))
            {
                e.Graphics.FillPath(brush, path);
            }

            if (Image != null)
            {
                int imageSize = Math.Max(20, Math.Min(26, bounds.Height - 8));
                int imageGap = String.IsNullOrEmpty(Text) ? 0 : 10;
                Size imageTextSize = TextRenderer.MeasureText(e.Graphics, Text, Font, new Size(Math.Max(1, Width - 44), Height), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                int imageGroupWidth = String.IsNullOrEmpty(Text)
                    ? imageSize
                    : Math.Min(bounds.Width - 18, imageSize + imageGap + imageTextSize.Width);
                int imageX = bounds.Left + Math.Max(10, (bounds.Width - imageGroupWidth) / 2);
                int imageY = bounds.Top + ((bounds.Height - imageSize) / 2);
                e.Graphics.DrawImage(Image, new Rectangle(imageX, imageY, imageSize, imageSize));
                if (String.IsNullOrEmpty(Text)) return;

                Rectangle imageTextRect = new Rectangle(imageX + imageSize + imageGap, bounds.Top, Math.Max(1, bounds.Right - imageX - imageSize - imageGap - 8), bounds.Height);
                TextRenderer.DrawText(
                    e.Graphics,
                    Text,
                    Font,
                    imageTextRect,
                    text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                return;
            }

            if (IconKind == ButtonIconKind.None)
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    Text,
                    Font,
                    bounds,
                    text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                return;
            }

            int iconSize = 16;
            int gap = 9;
            Size textSize = TextRenderer.MeasureText(e.Graphics, Text, Font, new Size(Math.Max(1, Width - 36), Height), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            int groupWidth = Math.Min(bounds.Width - 18, iconSize + gap + textSize.Width);
            int iconX = bounds.Left + Math.Max(10, (bounds.Width - groupWidth) / 2);
            int iconY = bounds.Top + ((bounds.Height - iconSize) / 2);
            Rectangle iconRect = new Rectangle(iconX, iconY, iconSize, iconSize);
            Rectangle textRect = new Rectangle(iconRect.Right + gap, bounds.Top, Math.Max(1, bounds.Right - iconRect.Right - gap - 8), bounds.Height);

            if (String.IsNullOrEmpty(Text))
            {
                iconRect = new Rectangle(bounds.Left + ((bounds.Width - iconSize) / 2), bounds.Top + ((bounds.Height - iconSize) / 2), iconSize, iconSize);
                DrawButtonIcon(e.Graphics, iconRect, text);
                return;
            }

            DrawButtonIcon(e.Graphics, iconRect, text);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                textRect,
                text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            if (mevent.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRegion();
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (var path = UiShape.RoundedRect(new Rectangle(0, 0, Width, Height), Radius))
            {
                Region = new Region(path);
            }
        }

        private Color StateBackColor(Color baseColor)
        {
            if (_pressed && FlatAppearance.MouseDownBackColor != Color.Empty)
            {
                return FlatAppearance.MouseDownBackColor;
            }
            if (_hovered && FlatAppearance.MouseOverBackColor != Color.Empty)
            {
                return FlatAppearance.MouseOverBackColor;
            }
            if (_pressed) return Blend(baseColor, Color.Black, IsLight(baseColor) ? 0.12 : 0.06);
            if (_hovered) return Blend(baseColor, Color.White, IsLight(baseColor) ? 0.05 : 0.08);
            return baseColor;
        }

        private static bool IsLight(Color color)
        {
            return ((color.R * 299) + (color.G * 587) + (color.B * 114)) / 1000 > 160;
        }

        private static Color Blend(Color first, Color second, double amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromArgb(
                first.A,
                (int)(first.R + ((second.R - first.R) * amount)),
                (int)(first.G + ((second.G - first.G) * amount)),
                (int)(first.B + ((second.B - first.B) * amount)));
        }

        private void DrawButtonIcon(Graphics g, Rectangle r, Color color)
        {
            using (var pen = new Pen(color, 1.65f))
            using (var brush = new SolidBrush(color))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                if (IconKind == ButtonIconKind.Eye)
                {
                    using (var path = new GraphicsPath())
                    {
                        path.AddBezier(r.Left + 1, r.Top + r.Height / 2, r.Left + 5, r.Top + 3, r.Right - 5, r.Top + 3, r.Right - 1, r.Top + r.Height / 2);
                        path.AddBezier(r.Right - 1, r.Top + r.Height / 2, r.Right - 5, r.Bottom - 3, r.Left + 5, r.Bottom - 3, r.Left + 1, r.Top + r.Height / 2);
                        g.DrawPath(pen, path);
                    }
                    g.FillEllipse(brush, r.Left + 6, r.Top + 6, 4, 4);
                    return;
                }

                if (IconKind == ButtonIconKind.Trash)
                {
                    g.DrawLine(pen, r.Left + 4, r.Top + 5, r.Right - 4, r.Top + 5);
                    g.DrawLine(pen, r.Left + 6, r.Top + 5, r.Left + 7, r.Bottom - 2);
                    g.DrawLine(pen, r.Right - 6, r.Top + 5, r.Right - 7, r.Bottom - 2);
                    g.DrawLine(pen, r.Left + 7, r.Bottom - 2, r.Right - 7, r.Bottom - 2);
                    g.DrawLine(pen, r.Left + 7, r.Top + 3, r.Right - 7, r.Top + 3);
                    g.DrawLine(pen, r.Left + 8, r.Top + 8, r.Left + 8, r.Bottom - 5);
                    g.DrawLine(pen, r.Right - 8, r.Top + 8, r.Right - 8, r.Bottom - 5);
                    return;
                }

                if (IconKind == ButtonIconKind.Eraser)
                {
                    using (var body = new GraphicsPath())
                    {
                        body.AddPolygon(new[]
                        {
                            new Point(r.Left + 5, r.Top + 11),
                            new Point(r.Left + 10, r.Top + 5),
                            new Point(r.Right - 3, r.Top + 10),
                            new Point(r.Right - 8, r.Bottom - 2),
                            new Point(r.Left + 5, r.Bottom - 6)
                        });
                        g.DrawPath(pen, body);
                    }
                    g.DrawLine(pen, r.Left + 8, r.Bottom - 5, r.Right - 5, r.Bottom - 5);
                    g.DrawLine(pen, r.Left + 10, r.Top + 7, r.Right - 6, r.Top + 12);
                    return;
                }

                if (IconKind == ButtonIconKind.Check)
                {
                    g.DrawLine(pen, r.Left + 3, r.Top + 8, r.Left + 7, r.Top + 12);
                    g.DrawLine(pen, r.Left + 7, r.Top + 12, r.Right - 3, r.Top + 4);
                    return;
                }

                if (IconKind == ButtonIconKind.External)
                {
                    g.DrawRectangle(pen, r.Left + 3, r.Top + 6, r.Width - 9, r.Height - 9);
                    g.DrawLine(pen, r.Left + 8, r.Top + 8, r.Right - 3, r.Top + 8);
                    g.DrawLine(pen, r.Right - 3, r.Top + 8, r.Right - 3, r.Top + 13);
                    g.DrawLine(pen, r.Left + 9, r.Bottom - 9, r.Right - 3, r.Top + 8);
                    return;
                }

                if (IconKind == ButtonIconKind.WindowMinimize)
                {
                    g.DrawLine(pen, r.Left + 3, r.Top + r.Height / 2, r.Right - 3, r.Top + r.Height / 2);
                    return;
                }

                if (IconKind == ButtonIconKind.WindowMaximize)
                {
                    g.DrawRectangle(pen, r.Left + 4, r.Top + 4, r.Width - 8, r.Height - 8);
                    return;
                }

                if (IconKind == ButtonIconKind.WindowClose)
                {
                    g.DrawLine(pen, r.Left + 4, r.Top + 4, r.Right - 4, r.Bottom - 4);
                    g.DrawLine(pen, r.Right - 4, r.Top + 4, r.Left + 4, r.Bottom - 4);
                }
            }
        }
    }

    internal sealed class ThemedSlider : Control
    {
        private int _minimum;
        private int _maximum = 100;
        private int _value;
        private bool _dragging;

        public event EventHandler ValueChanged;

        public int Minimum
        {
            get { return _minimum; }
            set
            {
                _minimum = value;
                if (_maximum < _minimum) _maximum = _minimum;
                Value = _value;
                Invalidate();
            }
        }

        public int Maximum
        {
            get { return _maximum; }
            set
            {
                _maximum = Math.Max(_minimum, value);
                Value = _value;
                Invalidate();
            }
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int next = Math.Max(_minimum, Math.Min(_maximum, value));
                if (_value == next) return;
                _value = next;
                Invalidate();
                var handler = ValueChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        public Color ActiveColor { get; set; }
        public Color TrackColor { get; set; }
        public Color ThumbColor { get; set; }

        public ThemedSlider()
        {
            Height = 28;
            ActiveColor = Color.FromArgb(255, 126, 188);
            TrackColor = Color.FromArgb(72, 78, 96);
            ThumbColor = Color.FromArgb(255, 126, 188);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color clear = BackColor.A > 0 && BackColor != Color.Transparent
                ? BackColor
                : UiShape.SurfaceBackColor(this, Color.FromArgb(17, 20, 31));
            e.Graphics.Clear(clear);
            int thumb = 14;
            int left = thumb / 2;
            int right = Math.Max(left + 1, Width - (thumb / 2) - 1);
            int y = Height / 2;
            float ratio = _maximum == _minimum ? 0f : (_value - _minimum) / (float)(_maximum - _minimum);
            int x = left + (int)Math.Round((right - left) * ratio);

            using (var track = new Pen(TrackColor, 4f))
            using (var active = new Pen(ActiveColor, 4f))
            {
                track.StartCap = LineCap.Round;
                track.EndCap = LineCap.Round;
                active.StartCap = LineCap.Round;
                active.EndCap = LineCap.Round;
                e.Graphics.DrawLine(track, left, y, right, y);
                e.Graphics.DrawLine(active, left, y, x, y);
            }

            using (var brush = new SolidBrush(ThumbColor))
            {
                e.Graphics.FillEllipse(brush, x - (thumb / 2), y - (thumb / 2), thumb, thumb);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            Capture = true;
            SetValueFromPoint(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) SetValueFromPoint(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Capture = false;
        }

        private void SetValueFromPoint(int x)
        {
            int thumb = 14;
            int left = thumb / 2;
            int right = Math.Max(left + 1, Width - (thumb / 2) - 1);
            double ratio = (Math.Max(left, Math.Min(right, x)) - left) / (double)(right - left);
            Value = _minimum + (int)Math.Round((_maximum - _minimum) * ratio);
        }
    }

    internal sealed class ColorSwatchButton : Button
    {
        public Color SwatchColor { get; set; }
        public string LabelText { get; set; }
        public string HexText { get; set; }

        public ColorSwatchButton()
        {
            SwatchColor = Color.FromArgb(31, 35, 49);
            LabelText = "";
            HexText = "";
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            FlatAppearance.BorderSize = 0;
            TabStop = false;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
        }

        protected override bool ShowFocusCues
        {
            get { return false; }
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(UiShape.SurfaceBackColor(this, Color.FromArgb(17, 20, 31)));
            Rectangle tile = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (var tilePath = UiShape.RoundedRect(tile, 8))
            using (var tileBrush = new SolidBrush(Color.FromArgb(18, 21, 32)))
            {
                e.Graphics.FillPath(tileBrush, tilePath);
            }

            int swatchSize = Math.Min(28, Math.Max(22, Width - 30));
            Rectangle swatch = new Rectangle((Width - swatchSize) / 2, 4, swatchSize, swatchSize);
            using (var path = UiShape.RoundedRect(swatch, 5))
            using (var brush = new SolidBrush(SwatchColor))
            {
                e.Graphics.FillPath(brush, path);
                if (IsVeryLight(SwatchColor) || IsVeryDark(SwatchColor))
                {
                    using (var border = new Pen(Color.FromArgb(42, 132, 143, 165), 1f))
                    {
                        e.Graphics.DrawPath(border, path);
                    }
                }
            }

            TextRenderer.DrawText(e.Graphics, LabelText, Font, new Rectangle(0, swatch.Bottom, Width, 14), Color.FromArgb(245, 247, 251), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            using (var hexFont = new Font(Font.FontFamily, 6.6f))
            {
                TextRenderer.DrawText(e.Graphics, HexText, hexFont, new Rectangle(0, swatch.Bottom + 13, Width, 12), Color.FromArgb(156, 164, 181), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
        }

        private static bool IsVeryLight(Color color)
        {
            return ((color.R * 299) + (color.G * 587) + (color.B * 114)) / 1000 > 232;
        }

        private static bool IsVeryDark(Color color)
        {
            return ((color.R * 299) + (color.G * 587) + (color.B * 114)) / 1000 < 28;
        }
    }

    internal sealed class ThemedColorDialog : Form
    {
        private const int WmNclButtonDown = 0xA1;
        private const int HtCaption = 0x2;

        private readonly ThemedSlider _red;
        private readonly ThemedSlider _green;
        private readonly ThemedSlider _blue;
        private readonly CenteredTextInput _redBox;
        private readonly CenteredTextInput _greenBox;
        private readonly CenteredTextInput _blueBox;
        private readonly CenteredTextInput _hexBox;
        private readonly Panel _preview;
        private bool _updating;
        private bool _normalizingHexText;

        private static readonly Color DialogBg = Color.FromArgb(18, 20, 29);
        private static readonly Color DialogControl = Color.FromArgb(31, 35, 49);
        private static readonly Color DialogText = Color.FromArgb(245, 247, 251);
        private static readonly Color DialogMuted = Color.FromArgb(156, 164, 181);
        private static readonly Color DialogAccent = Color.FromArgb(255, 126, 188);
        private static readonly Color DialogBorder = Color.FromArgb(47, 54, 72);

        public Color SelectedColor { get; private set; }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        public ThemedColorDialog(string title, Color initial, IEnumerable<Color> palette)
        {
            Text = title;
            SelectedColor = initial;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            MinimumSize = new Size(420, 304);
            ClientSize = new Size(430, 306);
            Padding = new Padding(0);
            BackColor = DialogBg;
            ForeColor = DialogText;
            Font = new Font("Segoe UI", 9.25f);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(16, 12, 16, 14);
            root.BackColor = DialogBg;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            Controls.Add(root);

            var headingRow = new TableLayoutPanel();
            headingRow.Dock = DockStyle.Fill;
            headingRow.BackColor = Color.Transparent;
            headingRow.ColumnCount = 2;
            headingRow.RowCount = 1;
            headingRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            headingRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
            headingRow.MouseDown += DragDialog;
            root.Controls.Add(headingRow, 0, 0);

            var heading = new Label();
            heading.Text = String.Equals(title, "Base color", StringComparison.OrdinalIgnoreCase) ? "BASE COLOR" : title;
            heading.Dock = DockStyle.Fill;
            heading.ForeColor = DialogText;
            heading.Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold);
            heading.TextAlign = ContentAlignment.MiddleLeft;
            heading.MouseDown += DragDialog;
            headingRow.Controls.Add(heading, 0, 0);

            var close = DialogButton("", Color.FromArgb(26, 30, 42), DialogText);
            close.Dock = DockStyle.None;
            close.Anchor = AnchorStyles.None;
            close.Size = new Size(24, 24);
            close.Margin = new Padding(4, 0, 0, 0);
            var closeRounded = close as RoundedButton;
            if (closeRounded != null)
            {
                closeRounded.IconKind = ButtonIconKind.WindowClose;
                closeRounded.Radius = 12;
            }
            close.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            headingRow.Controls.Add(close, 1, 0);

            var swatches = new FlowLayoutPanel();
            swatches.Dock = DockStyle.Fill;
            swatches.BackColor = Color.Transparent;
            swatches.WrapContents = true;
            swatches.AutoScroll = false;
            swatches.Padding = new Padding(0, 5, 0, 0);
            root.Controls.Add(swatches, 0, 1);

            foreach (Color color in palette)
            {
                var swatch = new Button();
                swatch.Width = 24;
                swatch.Height = 24;
                swatch.Margin = new Padding(0, 0, 6, 5);
                swatch.BackColor = color;
                swatch.FlatStyle = FlatStyle.Flat;
                swatch.FlatAppearance.BorderSize = 0;
                swatch.Cursor = Cursors.Hand;
                swatch.Click += delegate { SetSelectedColor(((Button)swatch).BackColor); };
                swatches.Controls.Add(swatch);
            }

            var sliders = new TableLayoutPanel();
            sliders.Dock = DockStyle.Fill;
            sliders.BackColor = Color.Transparent;
            sliders.ColumnCount = 3;
            sliders.RowCount = 3;
            sliders.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
            sliders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sliders.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
            sliders.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            sliders.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            sliders.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            root.Controls.Add(sliders, 0, 2);

            _red = AddColorSlider(sliders, 0, "R");
            _green = AddColorSlider(sliders, 1, "G");
            _blue = AddColorSlider(sliders, 2, "B");
            // RGB and HEX are intentionally editable; sliders and typed values stay synchronized.
            _redBox = AddRgbInput(sliders, 0, "RValue");
            _greenBox = AddRgbInput(sliders, 1, "GValue");
            _blueBox = AddRgbInput(sliders, 2, "BValue");

            var lower = new TableLayoutPanel();
            lower.Dock = DockStyle.Fill;
            lower.BackColor = Color.Transparent;
            lower.ColumnCount = 3;
            lower.RowCount = 1;
            lower.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
            lower.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 162));
            lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.Controls.Add(lower, 0, 3);

            _preview = new Panel();
            _preview.Dock = DockStyle.Fill;
            _preview.Margin = new Padding(0, 5, 10, 5);
            lower.Controls.Add(_preview, 0, 0);

            _hexBox = new CenteredTextInput();
            _hexBox.Dock = DockStyle.Fill;
            _hexBox.Margin = new Padding(0, 5, 10, 5);
            _hexBox.BackColor = Color.FromArgb(24, 28, 40);
            _hexBox.ForeColor = DialogText;
            _hexBox.Font = Font;
            _hexBox.Height = 26;
            _hexBox.MaxLength = 7;
            _hexBox.TextAlignment = HorizontalAlignment.Center;
            _hexBox.SelectAllOnFocus = false;
            _hexBox.CharacterFilter = IsHexInputCharacter;
            _hexBox.TextChanged += delegate
            {
                if (_updating || _normalizingHexText) return;
                string cleaned = CleanHexInput(_hexBox.Text);
                if (!String.Equals(cleaned, _hexBox.Text, StringComparison.Ordinal))
                {
                    _normalizingHexText = true;
                    _hexBox.Text = cleaned;
                    _normalizingHexText = false;
                }

                Color parsed;
                if (TryParseHex(_hexBox.Text, out parsed))
                {
                    SetSelectedColorFromHexInput(parsed);
                }
            };
            _hexBox.Leave += delegate
            {
                Color parsed;
                if (TryParseHex(_hexBox.Text, out parsed))
                {
                    _hexBox.Text = ToHex(parsed);
                }
            };
            lower.Controls.Add(_hexBox, 1, 0);

            var spacer = new Panel();
            spacer.Dock = DockStyle.Fill;
            spacer.BackColor = Color.Transparent;
            lower.Controls.Add(spacer, 2, 0);

            var buttons = new TableLayoutPanel();
            buttons.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            buttons.BackColor = Color.Transparent;
            buttons.Size = new Size(210, 30);
            buttons.Width = 210;
            buttons.ColumnCount = 2;
            buttons.RowCount = 1;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            root.Controls.Add(buttons, 0, 4);

            var ok = DialogButton("Apply", DialogAccent, Color.White);
            ok.DialogResult = DialogResult.OK;
            ok.Margin = new Padding(0, 0, 8, 0);
            buttons.Controls.Add(ok, 0, 0);

            var cancel = DialogButton("Cancel", DialogControl, DialogText);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Margin = new Padding(0);
            buttons.Controls.Add(cancel, 1, 0);
            AcceptButton = ok;
            CancelButton = cancel;

            SetSelectedColor(initial);
            UpdateDialogRegion();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateDialogRegion();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(DialogBorder, 1f))
            {
                e.Graphics.DrawLine(pen, 0, 0, Width, 0);
                e.Graphics.DrawLine(pen, 0, 42, Width, 42);
            }
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            Invalidate(true);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Invalidate(true);
        }

        private void UpdateDialogRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (var path = UiShape.RoundedRect(new Rectangle(0, 0, Width, Height), 12))
            {
                Region = new Region(path);
            }
        }

        private void DragDialog(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WmNclButtonDown, HtCaption, 0);
        }

        private ThemedSlider AddColorSlider(TableLayoutPanel grid, int row, string name)
        {
            var label = new Label();
            label.Text = name;
            label.Dock = DockStyle.Fill;
            label.ForeColor = DialogText;
            label.TextAlign = ContentAlignment.MiddleLeft;
            grid.Controls.Add(label, 0, row);

            var slider = new ThemedSlider();
            slider.Minimum = 0;
            slider.Maximum = 255;
            slider.ActiveColor = DialogAccent;
            slider.TrackColor = Color.FromArgb(72, 78, 96);
            slider.ThumbColor = DialogAccent;
            slider.BackColor = DialogBg;
            slider.Dock = DockStyle.Fill;
            slider.Margin = new Padding(0, 7, 10, 7);
            slider.ValueChanged += delegate
            {
                if (_updating) return;
                SetSelectedColor(Color.FromArgb(_red.Value, _green.Value, _blue.Value));
            };
            grid.Controls.Add(slider, 1, row);

            return slider;
        }

        private CenteredTextInput AddRgbInput(TableLayoutPanel grid, int row, string name)
        {
            var input = new CenteredTextInput();
            input.Name = name;
            input.Dock = DockStyle.Fill;
            input.Margin = new Padding(4, 5, 0, 5);
            input.BackColor = Color.FromArgb(24, 28, 40);
            input.ForeColor = DialogMuted;
            input.Font = Font;
            input.MaxLength = 3;
            input.TextAlignment = HorizontalAlignment.Center;
            input.CharacterFilter = Char.IsDigit;
            input.TextChanged += delegate
            {
                if (_updating) return;
                int value;
                if (!TryParseRgbInput(input.Text, out value)) return;
                if (value > 255)
                {
                    _updating = true;
                    input.Text = "255";
                    _updating = false;
                    value = 255;
                }
                SetSelectedColorFromRgbInput(name, value);
            };
            input.LostFocus += delegate
            {
                int value;
                if (!TryParseRgbInput(input.Text, out value))
                {
                    value = name == "RValue" ? SelectedColor.R : (name == "GValue" ? SelectedColor.G : SelectedColor.B);
                }
                value = Math.Max(0, Math.Min(255, value));
                input.Text = value.ToString();
            };
            grid.Controls.Add(input, 2, row);
            return input;
        }

        private Button DialogButton(string text, Color back, Color fore)
        {
            var button = new RoundedButton();
            button.Text = text;
            button.Dock = DockStyle.Fill;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = back;
            button.ForeColor = fore;
            button.Cursor = Cursors.Hand;
            var rounded = button as RoundedButton;
            if (rounded != null)
            {
                rounded.Radius = 10;
                rounded.IconKind = text == "Apply" ? ButtonIconKind.Check : ButtonIconKind.None;
            }
            return button;
        }

        private void SetSelectedColor(Color color)
        {
            SelectedColor = Color.FromArgb(color.R, color.G, color.B);
            _updating = true;
            _red.Value = SelectedColor.R;
            _green.Value = SelectedColor.G;
            _blue.Value = SelectedColor.B;
            _preview.BackColor = SelectedColor;
            _hexBox.Text = ToHex(SelectedColor);
            UpdateRgbInputs();
            _updating = false;
        }

        private void SetSelectedColorFromHexInput(Color color)
        {
            SelectedColor = Color.FromArgb(color.R, color.G, color.B);
            _updating = true;
            _red.Value = SelectedColor.R;
            _green.Value = SelectedColor.G;
            _blue.Value = SelectedColor.B;
            _preview.BackColor = SelectedColor;
            UpdateRgbInputs();
            _updating = false;
        }

        private void SetSelectedColorFromRgbInput(string inputName, int value)
        {
            int r = inputName == "RValue" ? value : SelectedColor.R;
            int g = inputName == "GValue" ? value : SelectedColor.G;
            int b = inputName == "BValue" ? value : SelectedColor.B;
            SelectedColor = Color.FromArgb(r, g, b);
            _updating = true;
            _red.Value = SelectedColor.R;
            _green.Value = SelectedColor.G;
            _blue.Value = SelectedColor.B;
            _preview.BackColor = SelectedColor;
            _hexBox.Text = ToHex(SelectedColor);
            UpdateRgbInputsExcept(inputName);
            _updating = false;
        }

        private void UpdateRgbInputs()
        {
            if (_redBox != null) _redBox.Text = SelectedColor.R.ToString();
            if (_greenBox != null) _greenBox.Text = SelectedColor.G.ToString();
            if (_blueBox != null) _blueBox.Text = SelectedColor.B.ToString();
        }

        private void UpdateRgbInputsExcept(string inputName)
        {
            if (_redBox != null && inputName != "RValue") _redBox.Text = SelectedColor.R.ToString();
            if (_greenBox != null && inputName != "GValue") _greenBox.Text = SelectedColor.G.ToString();
            if (_blueBox != null && inputName != "BValue") _blueBox.Text = SelectedColor.B.ToString();
        }

        private static bool TryParseRgbInput(string value, out int parsed)
        {
            parsed = 0;
            if (String.IsNullOrWhiteSpace(value)) return false;
            return Int32.TryParse(value.Trim(), out parsed);
        }

        private static bool TryParseHex(string value, out Color color)
        {
            color = Color.Empty;
            if (String.IsNullOrWhiteSpace(value)) return false;
            value = value.Trim();
            if (!value.StartsWith("#")) value = "#" + value;
            if (value.Length != 7) return false;
            try
            {
                color = ColorTranslator.FromHtml(value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string CleanHexInput(string value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            var sb = new StringBuilder();
            foreach (char c in value.Trim())
            {
                if (c == '#')
                {
                    if (sb.Length == 0)
                    {
                        sb.Append(c);
                    }
                    continue;
                }

                if (!IsHexInputCharacter(c) || c == '#')
                {
                    continue;
                }

                int max = sb.Length > 0 && sb[0] == '#' ? 7 : 6;
                if (sb.Length >= max) break;
                sb.Append(Char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }

        private static bool IsHexInputCharacter(char c)
        {
            return c == '#' || (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        public static string ToHex(Color color)
        {
            return String.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }
    }

    internal sealed class ThemeCardButton : Button
    {
        public Theme ThemeData { get; set; }
        public bool SelectedTheme { get; set; }
        public int Radius { get; set; }
        public string DisplayName { get; set; }

        public ThemeCardButton()
        {
            Radius = 8;
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            TabStop = false;
            FlatAppearance.BorderSize = 0;
            TextAlign = ContentAlignment.MiddleLeft;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.Selectable, false);
        }

        protected override bool ShowFocusCues
        {
            get { return false; }
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(UiShape.SurfaceBackColor(this, Color.FromArgb(11, 13, 23)));
            Rectangle bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            Theme theme = ThemeData;
            Color panel = theme == null ? Color.FromArgb(28, 32, 44) : ParseColor(theme.Panel, Color.FromArgb(28, 32, 44));
            Color accent = theme == null ? Color.FromArgb(235, 91, 166) : ParseColor(theme.Accent, Color.FromArgb(235, 91, 166));
            Color text = Color.FromArgb(246, 248, 252);
            Color bg = theme == null ? Color.FromArgb(10, 14, 25) : ParseColor(theme.Bg, Color.FromArgb(10, 14, 25));

            Color fill = SelectedTheme ? Blend(panel, accent, 0.22) : Color.FromArgb(18, 22, 34);
            using (var path = UiShape.RoundedRect(bounds, Radius))
            using (var brush = new SolidBrush(fill))
            {
                e.Graphics.FillPath(brush, path);
            }

            int thumbSize = Math.Max(31, Math.Min(41, Math.Min(Width - 22, Height - 36)));
            Rectangle thumb = new Rectangle((Width - thumbSize) / 2, 6, thumbSize, thumbSize);
            using (var thumbPath = UiShape.RoundedRect(thumb, 6))
            using (var thumbBrush = new LinearGradientBrush(thumb, bg, accent, LinearGradientMode.ForwardDiagonal))
            {
                e.Graphics.FillPath(thumbBrush, thumbPath);
                using (var shineBrush = new LinearGradientBrush(
                    new Rectangle(thumb.Left, thumb.Top, thumb.Width, Math.Max(1, thumb.Height / 2)),
                    Color.FromArgb(86, Color.White),
                    Color.FromArgb(0, Color.White),
                    LinearGradientMode.Vertical))
                {
                    e.Graphics.FillPath(shineBrush, thumbPath);
                }
                using (var linePen = new Pen(Color.FromArgb(55, Color.White), 1f))
                {
                    e.Graphics.DrawLine(linePen, thumb.Left + 6, thumb.Bottom - 8, thumb.Right - 6, thumb.Top + 8);
                    e.Graphics.DrawLine(linePen, thumb.Left + 16, thumb.Bottom - 7, thumb.Right - 5, thumb.Top + 17);
                }
            }

            string label = !String.IsNullOrEmpty(DisplayName) ? DisplayName : (theme == null ? "" : theme.Name);
            Rectangle labelRect = new Rectangle(5, thumb.Bottom + 4, Width - 10, Height - thumb.Bottom - 5);
            Font labelFont = Font;
            bool disposeLabelFont = false;
            if (label.IndexOf(' ') < 0 && TextRenderer.MeasureText(e.Graphics, label, Font, labelRect.Size, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width > labelRect.Width)
            {
                labelFont = new Font(Font.FontFamily, Math.Max(7.2f, Font.Size - 1.1f), Font.Style);
                disposeLabelFont = true;
            }
            TextRenderer.DrawText(e.Graphics, label, labelFont, labelRect, text, TextFormatFlags.Top | TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (disposeLabelFont) labelFont.Dispose();

            if (SelectedTheme)
            {
                Rectangle check = new Rectangle(Width - 22, 6, 17, 17);
                using (var brush = new SolidBrush(accent))
                {
                    e.Graphics.FillEllipse(brush, check);
                }
                using (var checkFont = new Font(Font.FontFamily, 8.5f, FontStyle.Bold))
                {
                    TextRenderer.DrawText(e.Graphics, "\u2713", checkFont, check, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Region = null;
        }

        private static Color ParseColor(string hex, Color fallback)
        {
            try
            {
                return ColorTranslator.FromHtml(hex);
            }
            catch
            {
                return fallback;
            }
        }

        private static Color Blend(Color a, Color b, double amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromArgb(
                (int)(a.R + ((b.R - a.R) * amount)),
                (int)(a.G + ((b.G - a.G) * amount)),
                (int)(a.B + ((b.B - a.B) * amount)));
        }

    }

    internal sealed class ThemedMenuColorTable : ProfessionalColorTable
    {
        private readonly Color _back;
        private readonly Color _panel;
        private readonly Color _hover;
        private readonly Color _border;

        public ThemedMenuColorTable(Color back, Color panel, Color hover, Color border)
        {
            _back = back;
            _panel = panel;
            _hover = hover;
            _border = border;
        }

        public override Color ToolStripDropDownBackground { get { return _panel; } }
        public override Color ImageMarginGradientBegin { get { return _panel; } }
        public override Color ImageMarginGradientMiddle { get { return _panel; } }
        public override Color ImageMarginGradientEnd { get { return _panel; } }
        public override Color MenuBorder { get { return _panel; } }
        public override Color MenuItemBorder { get { return _panel; } }
        public override Color MenuItemSelected { get { return _hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return _hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return _hover; } }
        public override Color MenuItemPressedGradientBegin { get { return _back; } }
        public override Color MenuItemPressedGradientMiddle { get { return _panel; } }
        public override Color MenuItemPressedGradientEnd { get { return _panel; } }
        public override Color SeparatorDark { get { return _border; } }
        public override Color SeparatorLight { get { return _border; } }
        public override Color ToolStripBorder { get { return _panel; } }
        public override Color ToolStripGradientBegin { get { return _back; } }
        public override Color ToolStripGradientMiddle { get { return _back; } }
        public override Color ToolStripGradientEnd { get { return _back; } }
    }

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
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            Color fill = e.ToolStrip is MenuStrip ? _back : _panel;
            using (var brush = new SolidBrush(fill))
            {
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            bool topLevel = e.Item.Owner is MenuStrip;
            bool active = e.Item.Selected || (e.Item is ToolStripMenuItem && ((ToolStripMenuItem)e.Item).Pressed);
            Color fill = topLevel
                ? (active ? _hover : _back)
                : (active ? _hover : _panel);

            using (var brush = new SolidBrush(fill))
            {
                e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
            }

        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? _text : _muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.Enabled ? _text : _muted;
            base.OnRenderArrow(e);
        }
    }

    internal sealed class SkinnerForm : Form
    {
        private const string StyleId = "chatgpt-desktop-skinner-style";
        private const int WmNclButtonDown = 0xA1;
        private const int WmNcHitTest = 0x84;
        private const int WmNcCalcSize = 0x83;
        private const int WmNcActivate = 0x86;
        private const int WmLButtonDown = 0x201;
        private const int WmEnterSizeMove = 0x231;
        private const int WmExitSizeMove = 0x232;
        private const int HtClient = 0x1;
        private const int HtCaption = 0x2;
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
        private const int WsThickFrame = 0x00040000;
        private const int WsMinimizeBox = 0x00020000;
        private const int WsMaximizeBox = 0x00010000;
        private const int WsSysMenu = 0x00080000;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;
        private const string ChatGptPackageName = "OpenAI.ChatGPT-Desktop";
        private const string ChatGptPublisherId = "2p2nqsd0c76g0";

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
        [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IApplicationActivationManager
        {
            [PreserveSig]
            int ActivateApplication(
                [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
                [MarshalAs(UnmanagedType.LPWStr)] string arguments,
                ActivateOptions options,
                out int processId);

            [PreserveSig]
            int ActivateForFile(
                [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
                IntPtr itemArray,
                [MarshalAs(UnmanagedType.LPWStr)] string verb,
                out int processId);

            [PreserveSig]
            int ActivateForProtocol(
                [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
                IntPtr itemArray,
                out int processId);
        }

        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly Dictionary<string, Theme> _themes = new Dictionary<string, Theme>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Button> _themeButtons = new List<Button>();
        private readonly System.Windows.Forms.Timer _watchTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer _previewRefreshTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer _resizeSettleTimer = new System.Windows.Forms.Timer();
        private readonly ToolTip _statusToolTip = new ToolTip();

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
        private const int SectionHeaderHeight = 28;
        private const float SectionHeaderFontSize = 10.5f;
        private const int MaxImageBytes = 8 * 1024 * 1024;
        private const int MaxThemeFileBytes = 1024 * 1024;
        private const int MaxThemeTextLength = 512;
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
        private WindowChromeMessageFilter _chromeMessageFilter;
        private CdpPipeConnection _pipeConnection;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                if (!UseNativeWindowChrome)
                {
                    cp.Style |= WsThickFrame | WsMinimizeBox | WsMaximizeBox | WsSysMenu;
                }
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (!UseNativeWindowChrome && m.Msg == WmNcCalcSize && m.WParam != IntPtr.Zero)
            {
                m.Result = IntPtr.Zero;
                return;
            }
            if (!UseNativeWindowChrome && m.Msg == WmNcActivate)
            {
                m.Result = (IntPtr)1;
                return;
            }

            if (m.Msg == WmEnterSizeMove)
            {
                _inSystemSizeMove = true;
                BeginLiveResize();
            }
            else if (m.Msg == WmExitSizeMove)
            {
                _inSystemSizeMove = false;
                EndLiveResize();
            }

            if (m.Msg == WmNcHitTest)
            {
                base.WndProc(ref m);
                if (UseNativeWindowChrome)
                {
                    if (m.Result == (IntPtr)HtClient)
                    {
                        Point screenPoint = ScreenPointFromLParam(m.LParam);
                        Point clientPoint = PointToClient(screenPoint);
                        Control target = DeepChildAtPoint(this, clientPoint);
                        if (IsCaptionDragPoint(clientPoint, target))
                        {
                            m.Result = (IntPtr)HtCaption;
                        }
                    }
                }
                else
                {
                    Point screenPoint = ScreenPointFromLParam(m.LParam);
                    IntPtr hit = ResolveResizeHitTest(screenPoint);
                    if (hit != IntPtr.Zero) m.Result = hit;
                    else
                    {
                        Point clientPoint = PointToClient(screenPoint);
                        Control target = DeepChildAtPoint(this, clientPoint);
                        if (IsCaptionDragPoint(clientPoint, target))
                        {
                            m.Result = (IntPtr)HtCaption;
                        }
                    }
                }
                return;
            }

            base.WndProc(ref m);
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
            Invalidate(true);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            ApplyNativeWindowTheme();
            Invalidate(true);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            RemoveChromeMessageFilter();
            base.OnHandleDestroyed(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.N))
            {
                NewTheme();
                return true;
            }
            if (keyData == (Keys.Control | Keys.S))
            {
                SaveTheme(false);
                return true;
            }
            if (keyData == (Keys.Control | Keys.Shift | Keys.S))
            {
                SaveTheme(true);
                return true;
            }
            if (keyData == (Keys.Control | Keys.O))
            {
                ImportTheme();
                return true;
            }
            if (keyData == Keys.F1)
            {
                OpenHelpDocumentation();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private IntPtr ResolveResizeHitTest(Point screenPoint)
        {
            if (WindowState == FormWindowState.Maximized) return IntPtr.Zero;
            Point p = PointToClient(screenPoint);
            if (p.X < 0 || p.Y < 0 || p.X > ClientSize.Width || p.Y > ClientSize.Height) return IntPtr.Zero;
            bool left = p.X <= ChromeResizeGrip;
            bool right = p.X >= ClientSize.Width - ChromeResizeGrip;
            bool top = p.Y <= ChromeTopResizeGrip;
            bool bottom = p.Y >= ClientSize.Height - ChromeResizeGrip;

            if (left && top) return (IntPtr)HtTopLeft;
            if (right && top) return (IntPtr)HtTopRight;
            if (left && bottom) return (IntPtr)HtBottomLeft;
            if (right && bottom) return (IntPtr)HtBottomRight;
            if (left) return (IntPtr)HtLeft;
            if (right) return (IntPtr)HtRight;
            if (top) return (IntPtr)HtTop;
            if (bottom) return (IntPtr)HtBottom;
            return IntPtr.Zero;
        }

        private void InstallChromeMessageFilter()
        {
            if (UseNativeWindowChrome) return;
            if (_chromeMessageFilter != null) return;
            _chromeMessageFilter = new WindowChromeMessageFilter(this);
            Application.AddMessageFilter(_chromeMessageFilter);
        }

        private void RemoveChromeMessageFilter()
        {
            if (_chromeMessageFilter == null) return;
            Application.RemoveMessageFilter(_chromeMessageFilter);
            _chromeMessageFilter = null;
        }

        private void BeginSystemMoveOrResize(int hitTest)
        {
            if (!IsHandleCreated || IsDisposed) return;
            if (WindowState == FormWindowState.Maximized && hitTest != HtCaption) return;
            ReleaseCapture();
            SendMessage(Handle, WmNclButtonDown, hitTest, 0);
        }

        private bool IsCaptionDragPoint(Point clientPoint, Control target)
        {
            if (WindowState == FormWindowState.Maximized) return false;
            if (clientPoint.Y < ChromeTopResizeGrip || clientPoint.Y >= TitleBarHeight) return false;
            if (clientPoint.X < 0 || clientPoint.X >= ClientSize.Width) return false;
            if (_menuStrip != null)
            {
                Point menuPoint = _menuStrip.PointToClient(PointToScreen(clientPoint));
                if (_menuStrip.ClientRectangle.Contains(menuPoint))
                {
                    return _menuStrip.GetItemAt(menuPoint) == null;
                }
            }
            return !IsInteractiveChromeControl(target);
        }

        private bool IsOwnedMessageTarget(Control target)
        {
            if (target == null) return true;
            Form form = target.FindForm();
            return form == null || form == this;
        }

        private static Control DeepChildAtPoint(Control root, Point point)
        {
            if (root == null) return null;
            Control current = root;
            Point local = point;
            while (current != null)
            {
                Control child = current.GetChildAtPoint(local, GetChildAtPointSkip.Invisible);
                if (child == null) return current;
                local = child.PointToClient(current.PointToScreen(local));
                current = child;
            }

            return root;
        }

        private static bool IsInteractiveChromeControl(Control control)
        {
            for (Control current = control; current != null; current = current.Parent)
            {
                if (current is MenuStrip ||
                    current is ToolStrip ||
                    current is Button ||
                    current is ComboBox ||
                    current is TextBox ||
                    current is ThemedSlider ||
                    current is CheckBox ||
                    current is LinkLabel ||
                    current is ColorSwatchButton ||
                    current is CenteredTextInput)
                {
                    return true;
                }
            }
            return false;
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

                if (m.Msg != WmLButtonDown || _owner == null || _owner.IsDisposed || !_owner.Visible)
                {
                    return false;
                }

                Control target = Control.FromHandle(m.HWnd);
                if (!_owner.IsOwnedMessageTarget(target))
                {
                    return false;
                }

                Point screen = Control.MousePosition;
                Point client = _owner.PointToClient(screen);
                if (!_owner.ClientRectangle.Contains(client))
                {
                    return false;
                }

                if (!UseNativeWindowChrome)
                {
                    IntPtr resizeHit = _owner.ResolveResizeHitTest(screen);
                    if (resizeHit != IntPtr.Zero)
                    {
                        _owner.BeginSystemMoveOrResize(resizeHit.ToInt32());
                        return true;
                    }
                }

                if (_owner.IsCaptionDragPoint(client, target))
                {
                    _owner.BeginSystemMoveOrResize(HtCaption);
                    return true;
                }

                return false;
            }
        }

        private static Point ScreenPointFromLParam(IntPtr lParam)
        {
            long value = lParam.ToInt64();
            int x = (short)(value & 0xffff);
            int y = (short)((value >> 16) & 0xffff);
            return new Point(x, y);
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
            if (!_uiBuilt || !IsHandleCreated || IsDisposed || Disposing || WindowState == FormWindowState.Minimized) return;
            BeginLiveResize();
            if (_inSystemSizeMove) return;
            _resizeSettleTimer.Stop();
            _resizeSettleTimer.Start();
        }

        private void BeginLiveResize()
        {
            if (_isLiveResizing) return;
            _isLiveResizing = true;
            SetResizeRedrawSuspended(true);
            if (_previewPanel != null) _previewPanel.Invalidate();
        }

        private void EndLiveResize()
        {
            _resizeSettleTimer.Stop();
            if (!_isLiveResizing) return;
            _isLiveResizing = false;
            ResizeThemeButtons();
            SetResizeRedrawSuspended(false);
            if (_previewPanel != null) _previewPanel.Invalidate();
        }

        private void SetResizeRedrawSuspended(bool suspended)
        {
            if (_resizeRedrawSuspended == suspended) return;
            _resizeRedrawSuspended = suspended;

            if (!suspended)
            {
                Control root = Controls.Count > 0 ? Controls[0] : null;
                Control[] controls = new Control[] { root, _themePanel, _settingsScrollHost, _previewPanel };
                foreach (Control control in controls)
                {
                    if (control == null || control.IsDisposed || !control.IsHandleCreated) continue;
                    control.Invalidate(true);
                }
                Invalidate(true);
            }
        }

        public SkinnerForm()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            _json.MaxJsonLength = 1024 * 1024 * 16;
            _programLogo = LoadProgramLogo();
            LoadThemes();
            LoadSettings();

            _watchTimer.Interval = 2500;
            _watchTimer.Tick += async delegate { await ApplySkinAsync(true); };
            _previewRefreshTimer.Interval = 80;
            _previewRefreshTimer.Tick += delegate
            {
                _previewRefreshTimer.Stop();
                RefreshSkinnedPreview();
            };
            _resizeSettleTimer.Interval = 160;
            _resizeSettleTimer.Tick += delegate { EndLiveResize(); };

            BuildUi();
            ApplyUiState();
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            await RefreshChatGptDetectionStatusAsync(false);
            await RestoreActiveThemeOnStartupAsync();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ClosePipeConnection();
            RemoveChromeMessageFilter();
            SetResizeRedrawSuspended(false);
            _resizeSettleTimer.Stop();
            _resizeSettleTimer.Dispose();
            _previewRefreshTimer.Stop();
            _previewRefreshTimer.Dispose();
            _watchTimer.Stop();
            _watchTimer.Dispose();
            _statusToolTip.Dispose();
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
            AddTheme("midnight", "Midnight", "#0c111d", "#151c2b", "#101826", "#eff6ff", "#7cc7ff", "#6f86a5", "#244a69",
                "radial-gradient(circle at 12% 8%, rgb(124 199 255 / 0.2), transparent 32%), radial-gradient(circle at 85% 18%, rgb(142 240 176 / 0.14), transparent 30%)");
            AddTheme("plum", "Plum", "#160b22", "#2a1740", "#211031", "#fff6ff", "#f4b8ff", "#b985d0", "#5c2c78",
                "radial-gradient(circle at 18% 14%, rgb(244 184 255 / 0.28), transparent 30%), radial-gradient(circle at 82% 18%, rgb(124 199 255 / 0.16), transparent 30%), linear-gradient(135deg, rgb(244 184 255 / 0.08), transparent 38%)");
            AddTheme("sakura", "Sakura", "#1c0f1b", "#3a1c32", "#2b1426", "#fff4fb", "#ff9fd5", "#e88abf", "#7d315b",
                "radial-gradient(circle at 14% 12%, rgb(255 159 213 / 0.28), transparent 30%), radial-gradient(circle at 82% 18%, rgb(255 226 169 / 0.18), transparent 26%), linear-gradient(145deg, rgb(255 255 255 / 0.08), transparent 40%)");
            AddTheme("bubblegum", "Bubblegum", "#171129", "#2f1f58", "#251848", "#fff7ff", "#ffa7f3", "#93dbff", "#683b8f",
                "radial-gradient(circle at 15% 16%, rgb(255 167 243 / 0.3), transparent 28%), radial-gradient(circle at 88% 16%, rgb(147 219 255 / 0.22), transparent 30%), radial-gradient(circle at 50% 88%, rgb(255 235 153 / 0.12), transparent 32%)");
            AddTheme("seafoam", "Seafoam", "#061817", "#123b38", "#0d2a28", "#f1fffc", "#98ffe0", "#72c9bd", "#1d6d68",
                "radial-gradient(circle at 14% 12%, rgb(152 255 224 / 0.25), transparent 30%), radial-gradient(circle at 85% 20%, rgb(139 200 255 / 0.18), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.06), transparent 42%)");
            AddTheme("sunset", "Sunset Pop", "#211019", "#4a2033", "#351726", "#fff8f1", "#ffca7a", "#ff8fb8", "#823858",
                "radial-gradient(circle at 18% 14%, rgb(255 202 122 / 0.3), transparent 30%), radial-gradient(circle at 82% 16%, rgb(255 143 184 / 0.22), transparent 30%), linear-gradient(150deg, rgb(255 255 255 / 0.07), transparent 42%)");
            AddTheme("jade", "Jade", "#091512", "#10231e", "#0c1b17", "#effcf8", "#7af2c4", "#5c9584", "#1b5a4a",
                "radial-gradient(circle at 14% 10%, rgb(122 242 196 / 0.2), transparent 32%), radial-gradient(circle at 82% 22%, rgb(126 199 255 / 0.12), transparent 28%)");
            AddTheme("ember", "Ember", "#17120f", "#261a14", "#1d130f", "#fff6ed", "#ffb36b", "#a97858", "#66391f",
                "radial-gradient(circle at 20% 12%, rgb(255 179 107 / 0.24), transparent 30%), radial-gradient(circle at 80% 18%, rgb(255 118 118 / 0.14), transparent 28%)");
            AddTheme("graphite", "Graphite", "#101214", "#1e2226", "#171a1e", "#f1f4f6", "#d3dde8", "#7b858f", "#343c46",
                "linear-gradient(135deg, rgb(255 255 255 / 0.06), transparent 42%), radial-gradient(circle at 88% 12%, rgb(211 221 232 / 0.1), transparent 30%)");
            AddTheme("daylight", "Daylight", "#f4f7fb", "#ffffff", "#eef3f8", "#172033", "#3267d6", "#9ba9bd", "#dbe9ff",
                "radial-gradient(circle at 12% 8%, rgb(50 103 214 / 0.12), transparent 30%), radial-gradient(circle at 88% 14%, rgb(20 142 106 / 0.1), transparent 28%)");
            AddTheme("pearl", "Pearl", "#f7f9fe", "#ffffff", "#eef2fb", "#17213a", "#6d7dff", "#c4cad8", "#e5e9f6",
                "radial-gradient(circle at 12% 10%, rgb(109 125 255 / 0.14), transparent 28%), radial-gradient(circle at 86% 18%, rgb(126 211 255 / 0.14), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.7), transparent 45%)");
            AddTheme("cloudberry", "Cloudberry", "#fff6fb", "#ffffff", "#f6eefa", "#261729", "#ff6fb7", "#ddb6d0", "#ffe2f1",
                "radial-gradient(circle at 14% 12%, rgb(255 111 183 / 0.18), transparent 30%), radial-gradient(circle at 84% 18%, rgb(150 193 255 / 0.14), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.66), transparent 42%)");
            AddTheme("mintlight", "Mint Light", "#f3fffb", "#ffffff", "#eafff7", "#10251f", "#16b884", "#aad9cd", "#dbfff2",
                "radial-gradient(circle at 15% 12%, rgb(22 184 132 / 0.15), transparent 30%), radial-gradient(circle at 86% 18%, rgb(94 234 212 / 0.12), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.68), transparent 42%)");
            AddTheme("skyglass", "Sky Glass", "#f1f8ff", "#ffffff", "#e8f3ff", "#10223a", "#1884ff", "#aecce9", "#d8ecff",
                "radial-gradient(circle at 12% 10%, rgb(24 132 255 / 0.15), transparent 30%), radial-gradient(circle at 86% 20%, rgb(80 220 255 / 0.13), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.7), transparent 42%)");
            AddTheme("lilacmist", "Lilac Mist", "#faf7ff", "#ffffff", "#f0eaff", "#241a38", "#9b6dff", "#cfc2e8", "#eadfff",
                "radial-gradient(circle at 14% 12%, rgb(155 109 255 / 0.15), transparent 30%), radial-gradient(circle at 84% 18%, rgb(255 140 213 / 0.12), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.68), transparent 42%)");
            AddTheme("morning", "Morning", "#f5fbff", "#ffffff", "#edf7ff", "#1b2433", "#ff8a4d", "#b9cce0", "#dff2ff",
                "radial-gradient(circle at 13% 12%, rgb(255 138 77 / 0.14), transparent 28%), radial-gradient(circle at 84% 18%, rgb(83 190 255 / 0.14), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.7), transparent 42%)");
            AddTheme("cupertino", "Cupertino", "#0a0d13", "#171b24", "#202634", "#f7f8fb", "#0a84ff", "#7d8596", "#d9ecff",
                "linear-gradient(145deg, rgb(255 255 255 / 0.08), transparent 40%), radial-gradient(circle at 18% 12%, rgb(10 132 255 / 0.18), transparent 28%), radial-gradient(circle at 84% 20%, rgb(255 255 255 / 0.09), transparent 26%)");
            AddTheme("aurora", "Aurora", "#071318", "#102833", "#0b1d27", "#eefcff", "#68f7d2", "#6aaee8", "#1d536a",
                "radial-gradient(circle at 16% 12%, rgb(104 247 210 / 0.24), transparent 30%), radial-gradient(circle at 84% 16%, rgb(106 174 232 / 0.18), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.05), transparent 42%)");
            AddTheme("lavender", "Lavender", "#181224", "#2c2241", "#211a35", "#fbf7ff", "#c7a8ff", "#9f8ad8", "#5c4a88",
                "radial-gradient(circle at 16% 14%, rgb(199 168 255 / 0.28), transparent 30%), radial-gradient(circle at 82% 18%, rgb(255 170 232 / 0.14), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.06), transparent 42%)");
            AddTheme("rosequartz", "Rose Quartz", "#1c1117", "#3a202d", "#2a1722", "#fff5f8", "#ffacc7", "#e48aa8", "#7f3a55",
                "radial-gradient(circle at 18% 12%, rgb(255 172 199 / 0.28), transparent 30%), radial-gradient(circle at 82% 18%, rgb(255 223 238 / 0.14), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.07), transparent 42%)");
            AddTheme("cottoncandy", "Cotton Candy", "#15142a", "#2d2755", "#221e44", "#fff8ff", "#ff9ee8", "#8fd7ff", "#644caa",
                "radial-gradient(circle at 15% 16%, rgb(255 158 232 / 0.3), transparent 28%), radial-gradient(circle at 86% 16%, rgb(143 215 255 / 0.24), transparent 30%), radial-gradient(circle at 55% 92%, rgb(255 241 179 / 0.13), transparent 32%)");
            AddTheme("blueberry", "Blueberry", "#091226", "#14244a", "#0e1b39", "#f2f7ff", "#82b7ff", "#6f8bd8", "#284f91",
                "radial-gradient(circle at 14% 12%, rgb(130 183 255 / 0.24), transparent 32%), radial-gradient(circle at 84% 20%, rgb(167 139 250 / 0.16), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.05), transparent 42%)");
            AddTheme("mintchip", "Mint Chip", "#081512", "#163128", "#0f241e", "#f3fffb", "#8cffc7", "#79d5ac", "#275f4c",
                "radial-gradient(circle at 14% 12%, rgb(140 255 199 / 0.26), transparent 30%), radial-gradient(circle at 84% 20%, rgb(226 255 247 / 0.1), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.05), transparent 42%)");
            AddTheme("peachfizz", "Peach Fizz", "#211318", "#442636", "#321b29", "#fff8f2", "#ffc08a", "#ff8fb3", "#8b4a63",
                "radial-gradient(circle at 16% 12%, rgb(255 192 138 / 0.28), transparent 30%), radial-gradient(circle at 84% 18%, rgb(255 143 179 / 0.22), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.07), transparent 42%)");
            AddTheme("honeycomb", "Honeycomb", "#17130b", "#30230e", "#241a0b", "#fff7dc", "#ffd166", "#c6933d", "#6a4a14",
                "radial-gradient(circle at 16% 12%, rgb(255 209 102 / 0.26), transparent 30%), radial-gradient(circle at 84% 18%, rgb(255 159 28 / 0.14), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.06), transparent 42%)");
            AddTheme("lagoon", "Lagoon", "#06151d", "#113341", "#0c2532", "#f0fcff", "#5eead4", "#67c7f2", "#1c6175",
                "radial-gradient(circle at 15% 12%, rgb(94 234 212 / 0.24), transparent 30%), radial-gradient(circle at 84% 20%, rgb(103 199 242 / 0.2), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.05), transparent 42%)");
            AddTheme("orchid", "Orchid", "#190f25", "#33204c", "#25183a", "#fff6ff", "#e8a7ff", "#b481e2", "#70439a",
                "radial-gradient(circle at 18% 14%, rgb(232 167 255 / 0.3), transparent 30%), radial-gradient(circle at 84% 18%, rgb(180 129 226 / 0.2), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.06), transparent 42%)");
            AddTheme("raspberry", "Raspberry", "#210d18", "#41172e", "#301123", "#fff5fa", "#ff75b7", "#d65d95", "#8e285b",
                "radial-gradient(circle at 16% 14%, rgb(255 117 183 / 0.3), transparent 30%), radial-gradient(circle at 84% 18%, rgb(214 93 149 / 0.18), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.06), transparent 42%)");
            AddTheme("frostbite", "Frostbite", "#07111d", "#142236", "#0e192a", "#f5fbff", "#9be7ff", "#7ea6c8", "#244a68",
                "radial-gradient(circle at 12% 10%, rgb(155 231 255 / 0.24), transparent 32%), radial-gradient(circle at 84% 16%, rgb(255 255 255 / 0.12), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.05), transparent 42%)");
            AddTheme("icepop", "Ice Pop", "#0a1421", "#172d46", "#10243a", "#f4fbff", "#76e4ff", "#a6b6ff", "#2b5b82",
                "radial-gradient(circle at 14% 12%, rgb(118 228 255 / 0.26), transparent 30%), radial-gradient(circle at 84% 18%, rgb(166 182 255 / 0.2), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.05), transparent 42%)");
            AddTheme("starlight", "Starlight", "#0b1020", "#1a2240", "#121934", "#f8fbff", "#ffe082", "#94a3ff", "#3b4a8f",
                "radial-gradient(circle at 16% 12%, rgb(255 224 130 / 0.18), transparent 26%), radial-gradient(circle at 84% 18%, rgb(148 163 255 / 0.2), transparent 30%), radial-gradient(circle at 50% 80%, rgb(255 255 255 / 0.08), transparent 34%)");
            AddTheme("galaxy", "Galaxy", "#100d22", "#211844", "#171233", "#f7f3ff", "#b388ff", "#ff7ac8", "#5a3fa0",
                "radial-gradient(circle at 16% 14%, rgb(179 136 255 / 0.28), transparent 30%), radial-gradient(circle at 84% 18%, rgb(255 122 200 / 0.2), transparent 30%), radial-gradient(circle at 50% 92%, rgb(108 92 231 / 0.14), transparent 32%)");
            AddTheme("cyberlime", "Cyberlime", "#07140b", "#102415", "#0b1a10", "#f2fff2", "#b6ff4d", "#57d87a", "#2f7a34",
                "radial-gradient(circle at 16% 14%, rgb(182 255 77 / 0.28), transparent 30%), radial-gradient(circle at 84% 20%, rgb(87 216 122 / 0.18), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.05), transparent 42%)");
            AddTheme("neonnoir", "Neon Noir", "#080a12", "#151927", "#10131f", "#f8fbff", "#ff4fd8", "#4df3ff", "#3b2c66",
                "radial-gradient(circle at 15% 12%, rgb(255 79 216 / 0.26), transparent 28%), radial-gradient(circle at 86% 18%, rgb(77 243 255 / 0.2), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.04), transparent 42%)");
            AddTheme("watermelon", "Watermelon", "#10170f", "#23321f", "#172415", "#f9fff4", "#ff6f91", "#7fe3a1", "#4d7d43",
                "radial-gradient(circle at 16% 14%, rgb(255 111 145 / 0.24), transparent 30%), radial-gradient(circle at 84% 20%, rgb(127 227 161 / 0.18), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.05), transparent 42%)");
            AddTheme("cherrycola", "Cherry Cola", "#1b0c12", "#361722", "#271019", "#fff4f7", "#ff5f85", "#b9657a", "#6c2234",
                "radial-gradient(circle at 16% 12%, rgb(255 95 133 / 0.28), transparent 30%), radial-gradient(circle at 84% 18%, rgb(185 101 122 / 0.18), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.05), transparent 42%)");
            AddTheme("moonstone", "Moonstone", "#0d1117", "#1a2230", "#121923", "#f4f8fb", "#c8d9e8", "#8da2b8", "#3d4f66",
                "radial-gradient(circle at 16% 12%, rgb(200 217 232 / 0.18), transparent 30%), radial-gradient(circle at 84% 18%, rgb(141 162 184 / 0.14), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.06), transparent 42%)");
            AddTheme("solarflare", "Solar Flare", "#1c100a", "#3a1e10", "#2a160c", "#fff6ea", "#ff9f1c", "#ff5d73", "#84451f",
                "radial-gradient(circle at 16% 12%, rgb(255 159 28 / 0.3), transparent 30%), radial-gradient(circle at 84% 18%, rgb(255 93 115 / 0.2), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.06), transparent 42%)");
            AddTheme("prism", "Prism", "#10111f", "#20243a", "#171b2d", "#fbfbff", "#a78bfa", "#60a5fa", "#3b2d6e",
                "radial-gradient(circle at 12% 12%, rgb(255 121 198 / 0.24), transparent 28%), radial-gradient(circle at 88% 16%, rgb(96 165 250 / 0.2), transparent 30%), radial-gradient(circle at 52% 88%, rgb(52 211 153 / 0.14), transparent 32%)");
            AddTheme("matcha", "Matcha", "#0b140d", "#182718", "#111e12", "#f6fff0", "#b7f57a", "#8eb36d", "#405f2d",
                "radial-gradient(circle at 16% 12%, rgb(183 245 122 / 0.22), transparent 30%), radial-gradient(circle at 84% 18%, rgb(142 179 109 / 0.14), transparent 28%), linear-gradient(145deg, rgb(255 255 255 / 0.05), transparent 42%)");
            AddTheme("lilacdream", "Lilac Dream", "#171125", "#302346", "#241a36", "#fff8ff", "#d8b4fe", "#f0abfc", "#6e4c91",
                "radial-gradient(circle at 16% 14%, rgb(216 180 254 / 0.28), transparent 30%), radial-gradient(circle at 84% 18%, rgb(240 171 252 / 0.18), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.06), transparent 42%)");
            AddTheme("tropical", "Tropical", "#071610", "#123520", "#0c2517", "#f4fff8", "#4ade80", "#22d3ee", "#206a45",
                "radial-gradient(circle at 16% 12%, rgb(74 222 128 / 0.24), transparent 30%), radial-gradient(circle at 84% 18%, rgb(34 211 238 / 0.18), transparent 30%), linear-gradient(145deg, rgb(255 255 255 / 0.05), transparent 42%)");

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
            var settings = ReadSettings();
            if (settings == null)
            {
                _themeId = "plum";
                ApplyStartupSnapshot(DefaultStartupSnapshot());
                return;
            }

            _activeTheme = SanitizeAppliedThemeSnapshot(settings.ActiveTheme);
            _activeThemeEnabled = settings.ActiveThemeEnabled && _activeTheme != null;

            AppliedThemeSnapshot startup = _activeThemeEnabled
                ? _activeTheme
                : SnapshotFromSettings(settings);
            ApplyStartupSnapshot(startup);

            _manualChatGptExePath = IsChatGptExecutablePath(settings.ManualChatGptExePath) ? settings.ManualChatGptExePath : "";
            _currentThemeFilePath = String.IsNullOrWhiteSpace(settings.ThemeFilePath) ? "" : settings.ThemeFilePath;
        }

        private AppliedThemeSnapshot DefaultStartupSnapshot()
        {
            return new AppliedThemeSnapshot
            {
                ThemeId = "plum",
                ThemeName = "Plum",
                Layout = "Standard",
                BackgroundMode = "solid",
                BackgroundValue = "",
                Port = 9322,
                CustomBg = _customTheme.Bg,
                CustomPanel = _customTheme.Panel,
                CustomInput = _customTheme.Input,
                CustomText = _customTheme.Text,
                CustomAccent = _customTheme.Accent,
                CustomBorder = _customTheme.Border,
                CustomUser = _customTheme.User,
                Transparency = _transparency,
                PanelImage = false,
                PanelImageMode = "off",
                PanelImageValue = "",
                GlassSearch = _glassSearch,
                FontFamily = "Default"
            };
        }

        private AppliedThemeSnapshot SnapshotFromSettings(SkinnerSettings settings)
        {
            if (settings == null) return DefaultStartupSnapshot();
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
            if (snapshot == null) snapshot = DefaultStartupSnapshot();

            if (!String.IsNullOrEmpty(snapshot.ThemeId) && _themes.ContainsKey(snapshot.ThemeId))
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
            _loadedBackgroundMode = NormalizeChoice(snapshot.BackgroundMode, new[] { "solid", "pattern", "file" }, "solid");
            _loadedBackgroundValue = NonEmpty(snapshot.BackgroundValue, "");
            _loadedPanelImageMode = NormalizeChoice(snapshot.PanelImageMode, new[] { "off", "same", "file" }, snapshot.PanelImage ? "same" : "off");
            _loadedPanelImageValue = NonEmpty(snapshot.PanelImageValue, "");
            _loadedPort = snapshot.Port > 0 && snapshot.Port <= 65535 ? snapshot.Port : 9322;
        }

        private AppliedThemeSnapshot SanitizeAppliedThemeSnapshot(AppliedThemeSnapshot snapshot)
        {
            if (snapshot == null) return null;
            string themeId = NonEmpty(snapshot.ThemeId, "custom");
            if (!_themes.ContainsKey(themeId)) themeId = "custom";

            return new AppliedThemeSnapshot
            {
                ThemeId = themeId,
                ThemeName = NonEmpty(snapshot.ThemeName, ThemeNameForId(themeId)),
                Layout = NormalizeChoice(snapshot.Layout, new[] { "standard", "wide", "compact", "focus" }, "standard"),
                BackgroundMode = NormalizeChoice(snapshot.BackgroundMode, new[] { "solid", "pattern", "file" }, "solid"),
                BackgroundValue = LimitThemeText(snapshot.BackgroundValue ?? "", MaxThemeTextLength),
                Port = snapshot.Port > 0 && snapshot.Port <= 65535 ? snapshot.Port : 9322,
                CustomBg = NormalizeHex(snapshot.CustomBg),
                CustomPanel = NormalizeHex(snapshot.CustomPanel),
                CustomInput = NormalizeHex(snapshot.CustomInput),
                CustomText = NormalizeHex(snapshot.CustomText),
                CustomAccent = NormalizeHex(snapshot.CustomAccent),
                CustomBorder = NormalizeHex(snapshot.CustomBorder),
                CustomUser = NormalizeHex(snapshot.CustomUser),
                Transparency = Math.Max(0, Math.Min(75, snapshot.Transparency)),
                PanelImage = snapshot.PanelImage,
                PanelImageMode = NormalizeChoice(snapshot.PanelImageMode, new[] { "off", "same", "file" }, snapshot.PanelImage ? "same" : "off"),
                PanelImageValue = LimitThemeText(snapshot.PanelImageValue ?? "", MaxThemeTextLength),
                GlassSearch = snapshot.GlassSearch,
                FontFamily = NormalizeFontChoice(snapshot.FontFamily)
            };
        }

        private string ThemeNameForId(string themeId)
        {
            return !String.IsNullOrEmpty(themeId) && _themes.ContainsKey(themeId) ? _themes[themeId].Name : "Custom";
        }

        private static string LayoutDisplayName(string layout)
        {
            string normalized = NormalizeChoice(layout, new[] { "standard", "wide", "compact", "focus" }, "standard");
            return Char.ToUpperInvariant(normalized[0]) + normalized.Substring(1);
        }

        private SkinnerSettings ReadSettings()
        {
            try
            {
                string path = SettingsPath();
                if (!File.Exists(path)) return null;
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
            return new SkinnerSettings
            {
                ThemeId = _themeId,
                Layout = SelectedLayout(),
                BackgroundMode = SelectedBackgroundMode(),
                BackgroundValue = _backgroundTextBox == null ? "" : _backgroundTextBox.Text.Trim(),
                Port = SelectedPort(),
                CustomBg = _customTheme.Bg,
                CustomPanel = _customTheme.Panel,
                CustomInput = _customTheme.Input,
                CustomText = _customTheme.Text,
                CustomAccent = _customTheme.Accent,
                CustomBorder = _customTheme.Border,
                CustomUser = _customTheme.User,
                Transparency = SelectedTransparency(),
                PanelImage = SelectedPanelImageMode() != "off",
                PanelImageMode = SelectedPanelImageMode(),
                PanelImageValue = _panelImageTextBox == null ? "" : _panelImageTextBox.Text.Trim(),
                GlassSearch = SelectedGlassSearch(),
                FontFamily = SelectedFontFamily(),
                ManualChatGptExePath = _manualChatGptExePath,
                ThemeFilePath = _currentThemeFilePath,
                ActiveThemeEnabled = _activeThemeEnabled && _activeTheme != null,
                ActiveTheme = _activeThemeEnabled ? _activeTheme : null
            };
        }

        private AppliedThemeSnapshot CaptureCurrentAppliedTheme(Theme theme)
        {
            return SanitizeAppliedThemeSnapshot(new AppliedThemeSnapshot
            {
                ThemeId = _themeId,
                ThemeName = theme == null ? ThemeNameForId(_themeId) : theme.Name,
                Layout = SelectedLayout(),
                BackgroundMode = SelectedBackgroundMode(),
                BackgroundValue = _backgroundTextBox == null ? "" : _backgroundTextBox.Text.Trim(),
                Port = SelectedPort(),
                CustomBg = _customTheme.Bg,
                CustomPanel = _customTheme.Panel,
                CustomInput = _customTheme.Input,
                CustomText = _customTheme.Text,
                CustomAccent = _customTheme.Accent,
                CustomBorder = _customTheme.Border,
                CustomUser = _customTheme.User,
                Transparency = SelectedTransparency(),
                PanelImage = SelectedPanelImageMode() != "off",
                PanelImageMode = SelectedPanelImageMode(),
                PanelImageValue = _panelImageTextBox == null ? "" : _panelImageTextBox.Text.Trim(),
                GlassSearch = SelectedGlassSearch(),
                FontFamily = SelectedFontFamily()
            });
        }

        private static string SettingsPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ChatGPTDesktopSkinner",
                "settings.json");
        }

        private void BuildUi()
        {
            Text = "GPT Themes";
            LoadWindowIcon();
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1106, 414);
            Size = StartupSizeWithinWorkingArea();
            TopMost = false;
            ShowInTaskbar = true;
            Opacity = 1.0;
            BackColor = AppleBg;
            ForeColor = AppleText;
            Font = new Font("Segoe UI", 9.25f);
            FormBorderStyle = UseNativeWindowChrome ? FormBorderStyle.Sizable : FormBorderStyle.None;
            Padding = UseNativeWindowChrome ? new Padding(0) : new Padding(2);

            var root = new ResizeAwareTableLayoutPanel();
            root.ResizeHitTest = UseNativeWindowChrome ? null : (Func<Point, IntPtr>)ResolveResizeHitTest;
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 2;
            root.Padding = new Padding(0);
            root.BackColor = AppleBg;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleBarHeight));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            var titleBar = BuildTitleBar();
            root.Controls.Add(titleBar, 0, 0);

            var workspace = new RoundedPanel();
            workspace.Dock = DockStyle.Fill;
            workspace.Margin = new Padding(0);
            workspace.Padding = new Padding(4, 0, 4, 4);
            workspace.Radius = 0;
            workspace.BackColor = AppleBg;
            workspace.BorderColor = Color.Transparent;
            workspace.GradientTop = Color.Empty;
            workspace.GradientBottom = Color.Empty;
            root.Controls.Add(workspace, 0, 1);

            var body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.ColumnCount = 3;
            body.RowCount = 1;
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 357));
            body.BackColor = AppleCard;
            workspace.Controls.Add(body);

            var left = new Panel();
            left.Dock = DockStyle.Fill;
            left.Margin = new Padding(0, 0, 5, 0);
            left.Padding = new Padding(6, 3, 0, 0);
            left.BackColor = Color.Transparent;
            body.Controls.Add(left, 0, 0);

            var leftStack = new TableLayoutPanel();
            leftStack.Dock = DockStyle.Fill;
            leftStack.BackColor = Color.Transparent;
            leftStack.ColumnCount = 1;
            leftStack.RowCount = 2;
            leftStack.RowStyles.Add(new RowStyle(SizeType.Absolute, SectionHeaderHeight));
            leftStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.Controls.Add(leftStack);

            var title = SectionHeaderLabel("THEMES");
            leftStack.Controls.Add(title, 0, 0);

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
            leftStack.Controls.Add(_themePanel, 0, 1);

            foreach (var id in new[]
            {
                "cupertino", "plum", "sakura", "bubblegum", "cottoncandy", "rosequartz",
                "lavender", "lilacdream", "orchid", "raspberry", "cherrycola", "watermelon",
                "seafoam", "mintchip", "jade", "matcha", "tropical", "lagoon",
                "aurora", "icepop", "frostbite", "blueberry", "midnight", "starlight",
                "galaxy", "neonnoir", "cyberlime", "prism", "sunset", "peachfizz",
                "honeycomb", "solarflare", "ember", "moonstone", "graphite", "daylight",
                "pearl", "cloudberry", "mintlight", "skyglass", "lilacmist", "morning",
                "custom"
            })
            {
                AddThemeButton(id);
            }

            var center = new Panel();
            center.Dock = DockStyle.Fill;
            center.Margin = new Padding(0, 0, 6, 0);
            center.Padding = new Padding(0);
            center.BackColor = Color.Transparent;
            body.Controls.Add(center, 1, 0);

            var settingsCard = new Panel();
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

            var stack = new TableLayoutPanel();
            stack.Dock = DockStyle.Top;
            stack.AutoSize = true;
            stack.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            stack.RowCount = 4;
            stack.ColumnCount = 1;
            stack.BackColor = Color.Transparent;
            _settingsStack = stack;
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            _settingsScrollHost.Controls.Add(stack);

            stack.Controls.Add(BuildLayoutPanel(), 0, 0);
            stack.Controls.Add(BuildCustomColorPanel(), 0, 1);
            stack.Controls.Add(BuildConnectionPanel(), 0, 2);

            var rightStack = new TableLayoutPanel();
            rightStack.Dock = DockStyle.Fill;
            rightStack.BackColor = Color.Transparent;
            rightStack.Margin = new Padding(0);
            // Keep preview painting inside the right column so it cannot bleed into the glass controls.
            rightStack.Padding = new Padding(8, 0, 6, 0);
            rightStack.ColumnCount = 1;
            rightStack.RowCount = 3;
            rightStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 286));
            rightStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            rightStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.Controls.Add(rightStack, 2, 0);

            _previewPanel = BuildPreviewPanel();
            rightStack.Controls.Add(_previewPanel, 0, 0);
            rightStack.Controls.Add(BuildActionsPanel(), 0, 1);
            var rightFiller = new Panel();
            rightFiller.Dock = DockStyle.Fill;
            rightFiller.BackColor = Color.Transparent;
            rightStack.Controls.Add(rightFiller, 0, 2);

            _uiBuilt = true;
        }

        private Size StartupSizeWithinWorkingArea()
        {
            Rectangle workingArea = Screen.PrimaryScreen == null
                ? new Rectangle(0, 0, 1380, 720)
                : Screen.PrimaryScreen.WorkingArea;
            int width = Math.Min(1106, Math.Max(MinimumSize.Width, workingArea.Width - 16));
            int height = Math.Min(429, Math.Max(MinimumSize.Height, workingArea.Height - 16));
            return new Size(width, height);
        }

        private Control BuildTitleBar()
        {
            var bar = new Panel();
            bar.Dock = DockStyle.Fill;
            bar.BackColor = Color.Transparent;
            bar.MouseDown += DragWindow;
            bar.DoubleClick += delegate { ToggleWindowMaximized(); };

            if (_programLogo != null)
            {
                var logo = new PictureBox();
                logo.SetBounds(8, 3, 30, 30);
                logo.SizeMode = PictureBoxSizeMode.Zoom;
                logo.Image = _programLogo;
                logo.BackColor = Color.Transparent;
                logo.Margin = new Padding(0);
                logo.MouseDown += DragWindow;
                logo.DoubleClick += delegate { ToggleWindowMaximized(); };
                bar.Controls.Add(logo);
            }
            else
            {
                var logo = new Label();
                logo.SetBounds(8, 3, 30, 30);
                logo.Text = "GPT";
                logo.Font = new Font(Font.FontFamily, 7.5f, FontStyle.Bold);
                logo.ForeColor = Color.White;
                logo.BackColor = Color.Transparent;
                logo.TextAlign = ContentAlignment.MiddleCenter;
                logo.MouseDown += DragWindow;
                logo.DoubleClick += delegate { ToggleWindowMaximized(); };
                bar.Controls.Add(logo);
            }

            _titleLabel = Label("GPT Themes");
            _titleLabel.SetBounds(40, 0, 110, TitleBarHeight);
            _titleLabel.Font = new Font(Font.FontFamily, 12.5f, FontStyle.Bold);
            _titleLabel.ForeColor = AppleText;
            _titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            _titleLabel.MouseDown += DragWindow;
            _titleLabel.DoubleClick += delegate { ToggleWindowMaximized(); };
            bar.Controls.Add(_titleLabel);

            _menuStrip = BuildMenuStrip();
            _menuStrip.MouseDown += DragMenuStripEmptyArea;
            bar.Controls.Add(_menuStrip);
            MainMenuStrip = _menuStrip;

            if (UseNativeWindowChrome)
            {
                bar.Resize += delegate { LayoutTitleBar(bar, null, null, null); };
                LayoutTitleBar(bar, null, null, null);
            }
            else
            {
                var closeButton = WindowButton(ButtonIconKind.WindowClose);
                var maximizeButton = WindowButton(ButtonIconKind.WindowMaximize);
                var minimizeButton = WindowButton(ButtonIconKind.WindowMinimize);
                closeButton.Click += delegate { Close(); };
                maximizeButton.Click += delegate { ToggleWindowMaximized(); };
                minimizeButton.Click += delegate { WindowState = FormWindowState.Minimized; };
                bar.Controls.Add(closeButton);
                bar.Controls.Add(maximizeButton);
                bar.Controls.Add(minimizeButton);
                bar.Resize += delegate { LayoutTitleBar(bar, minimizeButton, maximizeButton, closeButton); };
                LayoutTitleBar(bar, minimizeButton, maximizeButton, closeButton);
            }

            return bar;
        }

        private MenuStrip BuildMenuStrip()
        {
            var menu = new MenuStrip();
            menu.Dock = DockStyle.None;
            menu.AutoSize = false;
            menu.BackColor = AppleBg;
            menu.ForeColor = AppleText;
            menu.Font = new Font(Font.FontFamily, 9.1f, FontStyle.Regular);
            menu.Padding = new Padding(0);
            menu.Margin = new Padding(0);
            menu.GripStyle = ToolStripGripStyle.Hidden;
            menu.RenderMode = ToolStripRenderMode.Professional;
            menu.Renderer = new ThemedMenuRenderer(AppleBg, AppleBg, AppleText, AppleMuted, AppleCard, AppleBorder);

            var file = new ToolStripMenuItem("File");
            file.DropDownItems.Add(MenuItem("New Theme", Keys.Control | Keys.N, delegate { NewTheme(); }));
            file.DropDownItems.Add(MenuItem("Save Theme", Keys.Control | Keys.S, delegate { SaveTheme(false); }));
            file.DropDownItems.Add(MenuItem("Save Theme As...", Keys.Control | Keys.Shift | Keys.S, delegate { SaveTheme(true); }));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(MenuItem("Import Theme", Keys.Control | Keys.O, delegate { ImportTheme(); }));
            file.DropDownItems.Add(MenuItem("Export Theme", Keys.None, delegate { SaveTheme(true); }));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(MenuItem("Exit", Keys.None, delegate { Close(); }));

            var help = new ToolStripMenuItem("Help");
            help.DropDownItems.Add(MenuItem("Help Documentation", Keys.F1, delegate { OpenHelpDocumentation(); }));
            help.DropDownItems.Add(MenuItem("Project Information", Keys.None, delegate { ShowProjectInformation(); }));
            help.DropDownItems.Add(MenuItem("Visit piratemoo.com", Keys.None, delegate { OpenUrl("https://piratemoo.com"); }));
            help.DropDownItems.Add(new ToolStripSeparator());
            help.DropDownItems.Add(MenuItem("About", Keys.None, delegate { ShowAboutDialog(); }));

            menu.Items.Add(file);
            menu.Items.Add(help);
            StyleMenuItems(menu.Items, true);
            return menu;
        }

        private ToolStripMenuItem MenuItem(string text, Keys shortcut, EventHandler click)
        {
            var item = new ToolStripMenuItem(text);
            item.ShortcutKeys = shortcut;
            item.ShowShortcutKeys = shortcut != Keys.None;
            item.Click += click;
            item.BackColor = AppleBg;
            item.ForeColor = AppleText;
            return item;
        }

        private void StyleMenuItems(ToolStripItemCollection items)
        {
            StyleMenuItems(items, false);
        }

        private void StyleMenuItems(ToolStripItemCollection items, bool topLevel)
        {
            if (items == null) return;
            Theme theme = CurrentTheme();
            Color text = ColorFromHex(EnsureReadableText(theme.Text, theme.Bg));
            Color muted = Blend(text, AppleBg, 0.42);
            foreach (ToolStripItem item in items)
            {
                item.BackColor = AppleBg;
                item.ForeColor = item.Enabled ? text : muted;
                if (_menuStrip != null)
                {
                    item.Font = _menuStrip.Font;
                }
                var menuItem = item as ToolStripMenuItem;
                if (menuItem != null)
                {
                    menuItem.DropDown.BackColor = AppleBg;
                    menuItem.DropDown.ForeColor = text;
                    StyleMenuItems(menuItem.DropDownItems, false);
                }
                var separator = item as ToolStripSeparator;
                if (separator != null)
                {
                    separator.BackColor = AppleBg;
                    separator.ForeColor = Blend(text, AppleBg, 0.45);
                }
            }
        }

        private void LoadWindowIcon()
        {
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gpt-themes.ico");
                if (File.Exists(iconPath))
                {
                    Icon = new Icon(iconPath);
                    return;
                }

                Icon extracted = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (extracted != null) Icon = extracted;
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
                    string sourceAsset = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "logo.png");
                    if (File.Exists(sourceAsset)) path = sourceAsset;
                }
                if (!File.Exists(path)) return null;

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var image = Image.FromStream(stream))
                {
                    return new Bitmap(image);
                }
            }
            catch
            {
                return null;
            }
        }

        private Button WindowButton(ButtonIconKind icon)
        {
            var button = Button("");
            var rounded = button as RoundedButton;
            if (rounded != null)
            {
                rounded.Radius = 1;
                rounded.IconKind = icon;
            }
            button.Font = new Font(Font.FontFamily, 9.5f, FontStyle.Regular);
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = AppleBg;
            button.ForeColor = AppleText;
            button.FlatAppearance.MouseOverBackColor = icon == ButtonIconKind.WindowClose
                ? Color.FromArgb(196, 54, 72)
                : Color.FromArgb(24, 28, 40);
            button.FlatAppearance.MouseDownBackColor = icon == ButtonIconKind.WindowClose
                ? Color.FromArgb(154, 37, 52)
                : Color.FromArgb(34, 39, 54);
            button.Width = 46;
            button.Height = TitleBarHeight;
            button.TabStop = false;
            return button;
        }

        private void LayoutTitleBar(Control bar, Button minimizeButton, Button maximizeButton, Button closeButton)
        {
            int buttonLeft = bar.Width;
            if (closeButton != null && maximizeButton != null && minimizeButton != null)
            {
                int top = 0;
                int width = 46;
                int height = TitleBarHeight;
                int gap = 0;
                int right = Math.Max(0, bar.Width - width);
                closeButton.SetBounds(right, top, width, height);
                maximizeButton.SetBounds(right - width - gap, top, width, height);
                minimizeButton.SetBounds(right - ((width + gap) * 2), top, width, height);
                buttonLeft = minimizeButton.Left;
            }
            if (_titleLabel != null)
            {
                _titleLabel.SetBounds(40, 0, 110, TitleBarHeight);
            }
            if (_menuStrip != null)
            {
                int menuLeft = 152;
                int menuWidth = UseNativeWindowChrome ? 96 : Math.Max(120, buttonLeft - 8 - menuLeft);
                _menuStrip.SetBounds(menuLeft, 3, menuWidth, 28);
            }
        }

        private void ToggleWindowMaximized()
        {
            if (WindowState == FormWindowState.Maximized)
            {
                WindowState = FormWindowState.Normal;
                return;
            }

            MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
            WindowState = FormWindowState.Maximized;
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WmNclButtonDown, HtCaption, 0);
        }

        private void DragMenuStripEmptyArea(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _menuStrip == null) return;
            if (_menuStrip.GetItemAt(e.Location) != null) return;
            DragWindow(sender, e);
        }

        private Control BuildLayoutPanel()
        {
            var panel = CardPanel();
            var grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.Padding = new Padding(14, 4, 14, 4);
            grid.BackColor = Color.Transparent;
            grid.ColumnCount = 1;
            grid.RowCount = 3;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, SectionHeaderHeight));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.Controls.Add(grid);

            var section = SectionHeaderLabel("APPEARANCE");
            grid.Controls.Add(section, 0, 0);

            var topRow = new TableLayoutPanel();
            topRow.Dock = DockStyle.Fill;
            topRow.BackColor = Color.Transparent;
            topRow.ColumnCount = 2;
            topRow.RowCount = 1;
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 244));
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            topRow.Margin = new Padding(0, 0, 0, 0);
            grid.Controls.Add(topRow, 0, 1);

            var layoutStack = new TableLayoutPanel();
            layoutStack.Dock = DockStyle.Fill;
            layoutStack.BackColor = Color.Transparent;
            layoutStack.ColumnCount = 2;
            layoutStack.RowCount = 3;
            layoutStack.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            layoutStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layoutStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layoutStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layoutStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layoutStack.Margin = new Padding(0, 0, 14, 0);
            topRow.Controls.Add(layoutStack, 0, 0);

            var layoutLabel = Label("Layout");
            layoutLabel.Dock = DockStyle.Fill;
            layoutLabel.TextAlign = ContentAlignment.MiddleLeft;
            layoutStack.Controls.Add(layoutLabel, 0, 0);

            _layoutCombo = new ThemedComboBox();
            _layoutCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            StyleCombo(_layoutCombo);
            _layoutCombo.Items.AddRange(new object[] { "Standard", "Wide", "Compact", "Focus" });
            _layoutCombo.Dock = DockStyle.Fill;
            _layoutCombo.Margin = new Padding(0, 3, 0, 3);
            _layoutCombo.SelectedIndexChanged += delegate { RefreshSkinnedPreview(); };
            layoutStack.Controls.Add(_layoutCombo, 1, 0);

            var fontLabel = Label("Font");
            fontLabel.Dock = DockStyle.Fill;
            fontLabel.TextAlign = ContentAlignment.MiddleLeft;
            layoutStack.Controls.Add(fontLabel, 0, 1);

            _fontCombo = new ThemedComboBox();
            _fontCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            StyleCombo(_fontCombo);
            _fontCombo.Items.AddRange(new object[]
            {
                "Default",
                "System UI",
                "Segoe UI",
                "Inter",
                "Arial",
                "Verdana",
                "Georgia",
                "Courier New",
                "Comic Sans MS",
                "Atkinson Hyperlegible",
                "OpenDyslexic"
            });
            _fontCombo.Dock = DockStyle.Fill;
            _fontCombo.Margin = new Padding(0, 3, 0, 3);
            _fontCombo.SelectedIndexChanged += delegate { RefreshSkinnedPreview(); };
            layoutStack.Controls.Add(_fontCombo, 1, 1);

            var glassStack = new TableLayoutPanel();
            glassStack.Dock = DockStyle.Fill;
            glassStack.BackColor = Color.Transparent;
            glassStack.ColumnCount = 1;
            glassStack.RowCount = 3;
            glassStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            glassStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            glassStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
            glassStack.Margin = new Padding(0, 0, 0, 0);
            topRow.Controls.Add(glassStack, 1, 0);

            var glassHeader = new TableLayoutPanel();
            glassHeader.Dock = DockStyle.Fill;
            glassHeader.BackColor = Color.Transparent;
            glassHeader.Margin = new Padding(0);
            glassHeader.Padding = new Padding(0);
            glassHeader.ColumnCount = 3;
            glassHeader.RowCount = 1;
            glassHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            glassHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            glassHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            glassStack.Controls.Add(glassHeader, 0, 0);

            var glassLabel = Label("Glass");
            glassLabel.Dock = DockStyle.Fill;
            glassLabel.TextAlign = ContentAlignment.MiddleLeft;
            glassHeader.Controls.Add(glassLabel, 0, 0);

            _glassSearchCheckBox = new ThemedCheckBox();
            _glassSearchCheckBox.Text = "Glass Search";
            _glassSearchCheckBox.Checked = _glassSearch;
            _glassSearchCheckBox.Dock = DockStyle.Fill;
            _glassSearchCheckBox.ForeColor = AppleText;
            _glassSearchCheckBox.BackColor = Color.Transparent;
            _glassSearchCheckBox.Font = Font;
            _glassSearchCheckBox.Margin = new Padding(0, 0, 0, 0);
            var themedGlassCheck = _glassSearchCheckBox as ThemedCheckBox;
            if (themedGlassCheck != null)
            {
                themedGlassCheck.BoxBorderColor = Color.Transparent;
                themedGlassCheck.BoxBackColor = Color.FromArgb(14, 17, 27);
                themedGlassCheck.CheckedBackColor = ApplePink;
                themedGlassCheck.CheckMarkColor = Color.White;
                themedGlassCheck.TextColor = AppleText;
            }
            _glassSearchCheckBox.CheckedChanged += delegate
            {
                _glassSearch = _glassSearchCheckBox.Checked;
                RefreshSkinnedPreview();
            };
            glassHeader.Controls.Add(_glassSearchCheckBox, 1, 0);

            var glassRow = new TableLayoutPanel();
            glassRow.Dock = DockStyle.Fill;
            glassRow.BackColor = Color.Transparent;
            glassRow.Margin = new Padding(0);
            glassRow.Padding = new Padding(0);
            glassRow.ColumnCount = 3;
            glassRow.RowCount = 1;
            glassRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
            glassRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            glassRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            glassStack.Controls.Add(glassRow, 0, 1);

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
            };
            glassRow.Controls.Add(_transparencyTrackBar, 0, 0);

            _transparencyValueLabel = Label("");
            StyleMuted(_transparencyValueLabel);
            _transparencyValueLabel.Dock = DockStyle.Fill;
            _transparencyValueLabel.TextAlign = ContentAlignment.MiddleLeft;
            glassRow.Controls.Add(_transparencyValueLabel, 1, 0);

            var sliderCaptions = new TableLayoutPanel();
            sliderCaptions.Dock = DockStyle.Fill;
            sliderCaptions.BackColor = Color.Transparent;
            sliderCaptions.Margin = new Padding(0);
            sliderCaptions.Padding = new Padding(0);
            sliderCaptions.ColumnCount = 3;
            sliderCaptions.RowCount = 1;
            sliderCaptions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
            sliderCaptions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            sliderCaptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            glassStack.Controls.Add(sliderCaptions, 0, 2);

            var trackCaptions = new TableLayoutPanel();
            trackCaptions.Dock = DockStyle.Fill;
            trackCaptions.BackColor = Color.Transparent;
            trackCaptions.ColumnCount = 2;
            trackCaptions.RowCount = 1;
            trackCaptions.Margin = new Padding(0, 0, 10, 0);
            trackCaptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            trackCaptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            sliderCaptions.Controls.Add(trackCaptions, 0, 0);

            var opaqueLabel = Label("More Opaque");
            StyleMuted(opaqueLabel);
            opaqueLabel.Dock = DockStyle.Fill;
            opaqueLabel.Font = new Font(Font.FontFamily, 7.8f);
            opaqueLabel.TextAlign = ContentAlignment.MiddleLeft;
            trackCaptions.Controls.Add(opaqueLabel, 0, 0);

            var glassMoreLabel = Label("More Glass");
            StyleMuted(glassMoreLabel);
            glassMoreLabel.Dock = DockStyle.Fill;
            glassMoreLabel.Font = new Font(Font.FontFamily, 7.8f);
            glassMoreLabel.TextAlign = ContentAlignment.MiddleRight;
            trackCaptions.Controls.Add(glassMoreLabel, 1, 0);

            var backgroundFrame = new RoundedPanel();
            backgroundFrame.Dock = DockStyle.Fill;
            backgroundFrame.Margin = new Padding(0, 0, 0, 0);
            backgroundFrame.Padding = new Padding(0);
            backgroundFrame.Radius = 12;
            backgroundFrame.BackColor = AppleCard;
            backgroundFrame.BorderColor = Color.Transparent;
            backgroundFrame.GradientTop = Color.Empty;
            backgroundFrame.GradientBottom = Color.Empty;
            grid.Controls.Add(backgroundFrame, 0, 2);

            var backgroundGrid = new TableLayoutPanel();
            backgroundGrid.Dock = DockStyle.Fill;
            backgroundGrid.BackColor = Color.Transparent;
            backgroundGrid.ColumnCount = 7;
            backgroundGrid.RowCount = 5;
            backgroundGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            backgroundGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144));
            backgroundGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
            backgroundGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            backgroundGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
            backgroundGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
            backgroundGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            backgroundGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            backgroundGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 2));
            backgroundGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            backgroundGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 2));
            backgroundGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            backgroundFrame.Controls.Add(backgroundGrid);

            var backgroundLabel = Label("Background");
            backgroundLabel.Dock = DockStyle.Fill;
            backgroundLabel.TextAlign = ContentAlignment.MiddleLeft;
            backgroundGrid.Controls.Add(backgroundLabel, 0, 0);

            _backgroundModeCombo = new ThemedComboBox();
            _backgroundModeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            StyleCombo(_backgroundModeCombo);
            _backgroundModeCombo.Items.AddRange(new object[] { "Base Color", "Theme Pattern", "Image File" });
            _backgroundModeCombo.Dock = DockStyle.Fill;
            _backgroundModeCombo.Margin = new Padding(0, 2, 0, 2);
            _backgroundModeCombo.SelectedIndexChanged += delegate
            {
                UpdateBackgroundInputState();
                RefreshBackgroundPreview();
                RefreshSkinnedPreview();
            };
            backgroundGrid.Controls.Add(_backgroundModeCombo, 1, 0);

            _backgroundPreviewPanel = new RoundedPanel();
            _backgroundPreviewPanel.Dock = DockStyle.Fill;
            _backgroundPreviewPanel.Margin = new Padding(8, 0, 8, 0);
            _backgroundPreviewPanel.Radius = 8;
            _backgroundPreviewPanel.BackColor = AppleCard;
            _backgroundPreviewPanel.BorderColor = Color.FromArgb(42, 48, 64);
            _backgroundPreviewPanel.Paint += PaintBackgroundPreviewPanel;
            backgroundGrid.Controls.Add(_backgroundPreviewPanel, 2, 0);

            _backgroundTextBox = new ThemedTextBox();
            StyleTextBox(_backgroundTextBox);
            _backgroundTextBox.Dock = DockStyle.Fill;
            _backgroundTextBox.Margin = new Padding(0, 2, 10, 2);
            _backgroundTextBox.TextChanged += delegate
            {
                RefreshBackgroundPreview();
                RefreshSkinnedPreview();
            };
            backgroundGrid.Controls.Add(_backgroundTextBox, 3, 0);

            _browseButton = Button("Browse");
            _browseButton.Dock = DockStyle.Fill;
            _browseButton.Margin = new Padding(0, 2, 0, 2);
            _browseButton.Click += BrowseBackground;
            backgroundGrid.Controls.Add(_browseButton, 4, 0);

            _backgroundOkButton = Button("OK");
            _backgroundOkButton.Dock = DockStyle.Fill;
            _backgroundOkButton.Margin = new Padding(4, 2, 0, 2);
            _backgroundOkButton.Click += async delegate { await ApplySkinAsync(false); };
            backgroundGrid.Controls.Add(_backgroundOkButton, 5, 0);

            var panelImageLabel = Label("Panel");
            panelImageLabel.Dock = DockStyle.Fill;
            panelImageLabel.Margin = new Padding(1, 0, 0, 0);
            panelImageLabel.TextAlign = ContentAlignment.MiddleLeft;
            backgroundGrid.Controls.Add(panelImageLabel, 0, 2);

            _panelImageModeCombo = new ThemedComboBox();
            _panelImageModeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            StyleCombo(_panelImageModeCombo);
            _panelImageModeCombo.Items.AddRange(new object[] { "Base Color", "Same Image", "Image File" });
            _panelImageModeCombo.Dock = DockStyle.Fill;
            _panelImageModeCombo.Margin = new Padding(0, 2, 0, 2);
            _panelImageModeCombo.SelectedIndexChanged += delegate
            {
                UpdatePanelImageInputState();
                RefreshPanelImagePreview();
                RefreshSkinnedPreview();
            };
            backgroundGrid.Controls.Add(_panelImageModeCombo, 1, 2);

            _panelImagePreviewPanel = new RoundedPanel();
            _panelImagePreviewPanel.Dock = DockStyle.Fill;
            _panelImagePreviewPanel.Margin = new Padding(8, 0, 8, 0);
            _panelImagePreviewPanel.Radius = 8;
            _panelImagePreviewPanel.BackColor = AppleCard;
            _panelImagePreviewPanel.BorderColor = Color.FromArgb(42, 48, 64);
            _panelImagePreviewPanel.Paint += PaintPanelImagePreviewPanel;
            backgroundGrid.Controls.Add(_panelImagePreviewPanel, 2, 2);

            _panelImageTextBox = new ThemedTextBox();
            StyleTextBox(_panelImageTextBox);
            _panelImageTextBox.Dock = DockStyle.Fill;
            _panelImageTextBox.Margin = new Padding(0, 2, 10, 2);
            _panelImageTextBox.TextChanged += delegate
            {
                RefreshPanelImagePreview();
                RefreshSkinnedPreview();
            };
            backgroundGrid.Controls.Add(_panelImageTextBox, 3, 2);

            _panelImageBrowseButton = Button("Browse");
            _panelImageBrowseButton.Dock = DockStyle.Fill;
            _panelImageBrowseButton.Margin = new Padding(0, 2, 0, 2);
            _panelImageBrowseButton.Click += BrowsePanelImage;
            backgroundGrid.Controls.Add(_panelImageBrowseButton, 4, 2);

            _imageOkButton = Button("OK");
            _imageOkButton.Dock = DockStyle.Fill;
            _imageOkButton.Margin = new Padding(4, 2, 0, 2);
            _imageOkButton.Click += async delegate { await ApplySkinAsync(false); };
            backgroundGrid.Controls.Add(_imageOkButton, 5, 2);

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

        private void PaintPanelImagePreviewPanel(object sender, PaintEventArgs e)
        {
            var control = sender as Control;
            if (control == null) return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(UiShape.SurfaceBackColor(control, AppleCard));
            Rectangle bounds = new Rectangle(0, 0, Math.Max(1, control.Width - 1), Math.Max(1, control.Height - 1));
            Theme theme = CurrentTheme();
            Color panel = ColorFromHex(theme.Panel);
            Color accent = ColorFromHex(theme.Accent);
            string mode = SelectedPanelImageMode();

            using (var path = UiShape.RoundedRect(bounds, 8))
            {
                Image image = LoadPreviewPanelImage();
                if (image != null)
                {
                    Region oldClip = e.Graphics.Clip;
                    e.Graphics.SetClip(path);
                    e.Graphics.DrawImage(image, CoverRectangle(image.Size, bounds));
                    using (var tint = new SolidBrush(Color.FromArgb(76, panel)))
                    {
                        e.Graphics.FillRectangle(tint, bounds);
                    }
                    e.Graphics.Clip = oldClip;
                }
                else
                {
                    using (var brush = new LinearGradientBrush(bounds, Blend(panel, accent, mode == "off" ? 0.05 : 0.12), Blend(panel, Color.Black, 0.10), LinearGradientMode.ForwardDiagonal))
                    {
                        e.Graphics.FillPath(brush, path);
                    }
                }

                using (var shine = new LinearGradientBrush(bounds, Color.FromArgb(42, Color.White), Color.FromArgb(0, Color.White), LinearGradientMode.ForwardDiagonal))
                using (var border = new Pen(Color.FromArgb(58, 66, 84), 1f))
                {
                    e.Graphics.FillPath(shine, path);
                    e.Graphics.DrawPath(border, path);
                }
            }
        }

        private void PaintBackgroundPreviewPanel(object sender, PaintEventArgs e)
        {
            var control = sender as Control;
            if (control == null) return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(UiShape.SurfaceBackColor(control, AppleCard));
            Rectangle bounds = new Rectangle(0, 0, Math.Max(1, control.Width - 1), Math.Max(1, control.Height - 1));
            Theme theme = CurrentTheme();
            Color bg = ColorFromHex(theme.Bg);
            Color accent = ColorFromHex(theme.Accent);
            string mode = SelectedBackgroundMode();

            using (var path = UiShape.RoundedRect(bounds, 8))
            {
                if (mode == "file")
                {
                    Image image = LoadPreviewBackgroundImage();
                    if (image != null)
                    {
                        Region oldClip = e.Graphics.Clip;
                        e.Graphics.SetClip(path);
                        e.Graphics.DrawImage(image, CoverRectangle(image.Size, bounds));
                        e.Graphics.Clip = oldClip;
                    }
                    else
                    {
                        using (var brush = new LinearGradientBrush(bounds, Blend(bg, accent, 0.14), Blend(bg, Color.Black, 0.10), LinearGradientMode.ForwardDiagonal))
                        {
                            e.Graphics.FillPath(brush, path);
                        }
                    }
                }
                else if (mode == "solid")
                {
                    using (var brush = new SolidBrush(bg))
                    {
                        e.Graphics.FillPath(brush, path);
                    }
                }
                else
                {
                    using (var brush = new LinearGradientBrush(bounds, Blend(bg, accent, 0.12), Blend(bg, Color.Black, 0.10), LinearGradientMode.ForwardDiagonal))
                    {
                        e.Graphics.FillPath(brush, path);
                    }
                }

                using (var shine = new LinearGradientBrush(bounds, Color.FromArgb(58, Color.White), Color.FromArgb(0, Color.White), LinearGradientMode.ForwardDiagonal))
                using (var border = new Pen(Color.FromArgb(58, 66, 84), 1f))
                {
                    e.Graphics.FillPath(shine, path);
                    e.Graphics.DrawPath(border, path);
                }
            }
        }

        private Control BuildCustomColorPanel()
        {
            var panel = CardPanel();
            var grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.Padding = new Padding(104, 0, 14, 0);
            grid.BackColor = Color.Transparent;
            grid.ColumnCount = 1;
            grid.RowCount = 1;
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.Controls.Add(grid);

            _baseColorButton = ColorButton("Base", delegate { return _customTheme.Bg; }, delegate(string v) { _customTheme.Bg = v; });
            _panelColorButton = ColorButton("Panel", delegate { return _customTheme.Panel; }, delegate(string v) { _customTheme.Panel = v; });
            _inputColorButton = ColorButton("Input", delegate { return _customTheme.Input; }, delegate(string v) { _customTheme.Input = v; });
            _textColorButton = ColorButton("Text", delegate { return _customTheme.Text; }, delegate(string v) { _customTheme.Text = v; });
            _accentColorButton = ColorButton("Accent", delegate { return _customTheme.Accent; }, delegate(string v) { _customTheme.Accent = v; });
            _borderColorButton = ColorButton("Border", delegate { return _customTheme.Border; }, delegate(string v) { _customTheme.Border = v; });
            _userColorButton = ColorButton("User", delegate { return _customTheme.User; }, delegate(string v) { _customTheme.User = v; });

            var flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.BackColor = Color.Transparent;
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.WrapContents = true;
            flow.AutoScroll = false;
            flow.Padding = new Padding(0, 2, 0, 0);
            grid.Controls.Add(flow, 0, 0);

            var buttons = new[] { _baseColorButton, _panelColorButton, _inputColorButton, _textColorButton, _accentColorButton, _borderColorButton, _userColorButton };
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].Size = new Size(52, 58);
                buttons[i].Margin = new Padding(0, 0, 4, 0);
                buttons[i].Padding = new Padding(0);
                var rounded = buttons[i] as RoundedButton;
                if (rounded != null) rounded.Radius = 8;
                flow.Controls.Add(buttons[i]);
            }

            return panel;
        }

        private Control BuildConnectionPanel()
        {
            var panel = CardPanel();
            var grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.Padding = new Padding(14, 3, 14, 3);
            grid.BackColor = Color.Transparent;
            grid.ColumnCount = 4;
            grid.RowCount = 4;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            panel.Controls.Add(grid);

            var headerRow = new TableLayoutPanel();
            headerRow.Dock = DockStyle.Fill;
            headerRow.BackColor = Color.Transparent;
            headerRow.Margin = new Padding(0);
            headerRow.Padding = new Padding(0);
            headerRow.ColumnCount = 3;
            headerRow.RowCount = 1;
            headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
            headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.Controls.Add(headerRow, 0, 0);
            grid.SetColumnSpan(headerRow, 4);

            var label = Label("CONNECTION  -");
            label.Dock = DockStyle.Fill;
            label.Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Margin = new Padding(0);
            headerRow.Controls.Add(label, 0, 0);

            _chatGptStatusLabel = Label("ChatGPT Not Checked");
            _chatGptStatusLabel.Dock = DockStyle.Fill;
            _chatGptStatusLabel.Font = new Font(Font.FontFamily, 9.1f, FontStyle.Bold);
            _chatGptStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _chatGptStatusLabel.Margin = new Padding(0);
            headerRow.Controls.Add(_chatGptStatusLabel, 1, 0);

            _chatGptDiagnosticsLabel = Label("Detection checks running process, saved executable, local install, and Windows app package.");
            StyleMuted(_chatGptDiagnosticsLabel);
            _chatGptDiagnosticsLabel.Dock = DockStyle.Fill;
            _chatGptDiagnosticsLabel.TextAlign = ContentAlignment.MiddleLeft;
            _chatGptDiagnosticsLabel.AutoEllipsis = true;
            _chatGptDiagnosticsLabel.Margin = new Padding(4, 0, 0, 0);
            headerRow.Controls.Add(_chatGptDiagnosticsLabel, 2, 0);

            var portGroup = new TableLayoutPanel();
            portGroup.Dock = DockStyle.Fill;
            portGroup.BackColor = Color.Transparent;
            portGroup.ColumnCount = 2;
            portGroup.RowCount = 1;
            portGroup.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
            portGroup.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            portGroup.Margin = new Padding(0, 1, 8, 1);
            grid.Controls.Add(portGroup, 0, 2);

            var portLabel = Label("Pipe");
            portLabel.Dock = DockStyle.Fill;
            portLabel.TextAlign = ContentAlignment.MiddleLeft;
            portGroup.Controls.Add(portLabel, 0, 0);

            _portTextBox = new CenteredTextInput();
            _portTextBox.Text = "Private";
            _portTextBox.MaxLength = 16;
            _portTextBox.SelectAllOnFocus = false;
            _portTextBox.CharacterFilter = delegate(char c) { return false; };
            _portTextBox.TextAlignment = HorizontalAlignment.Center;
            _portTextBox.Dock = DockStyle.Fill;
            _portTextBox.Margin = new Padding(0, 0, 0, 0);
            _portTextBox.Font = Font;
            _portTextBox.TabStop = false;
            _portTextBox.Enabled = false;
            portGroup.Controls.Add(_portTextBox, 1, 0);

            var note = Label("Uses a private DevTools pipe; no localhost debug port is opened.");
            StyleMuted(note);
            note.Dock = DockStyle.Fill;
            note.TextAlign = ContentAlignment.MiddleLeft;
            note.AutoEllipsis = true;
            note.Margin = new Padding(0, 0, 0, 0);
            grid.Controls.Add(note, 1, 2);
            grid.SetColumnSpan(note, 3);

            _testPortButton = Button("Test Pipe");
            _testPortButton.Dock = DockStyle.Top;
            _testPortButton.Height = 22;
            _testPortButton.Margin = new Padding(0, 5, 8, 0);
            _testPortButton.Click += async delegate { await TestPipeAsync(false); };
            grid.Controls.Add(_testPortButton, 0, 3);

            _relaunchButton = Button("Relaunch with Pipe");
            _relaunchButton.Dock = DockStyle.Top;
            _relaunchButton.Height = 22;
            _relaunchButton.Margin = new Padding(0, 5, 8, 0);
            _relaunchButton.Click += async delegate { await RelaunchChatGptWithPipeAsync(); };
            grid.Controls.Add(_relaunchButton, 1, 3);

            _chooseChatGptButton = Button("Choose ChatGPT.exe");
            _chooseChatGptButton.Dock = DockStyle.Top;
            _chooseChatGptButton.Height = 22;
            _chooseChatGptButton.Margin = new Padding(0, 5, 0, 0);
            _chooseChatGptButton.Click += ChooseChatGptExe;
            grid.Controls.Add(_chooseChatGptButton, 2, 3);
            return panel;
        }

        private RoundedPanel BuildPreviewPanel()
        {
            var panel = new RoundedPanel();
            panel.Dock = DockStyle.Fill;
            panel.Height = 380;
            panel.Radius = 1;
            panel.Margin = new Padding(0);
            panel.BackColor = Color.Transparent;
            panel.BorderColor = Color.Transparent;
            panel.GradientTop = Color.Empty;
            panel.GradientBottom = Color.Empty;
            panel.Paint += PaintPreviewPanel;

            return panel;
        }

        private void PaintPreviewPanel(object sender, PaintEventArgs e)
        {
            var panel = sender as Control;
            if (panel == null) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(UiShape.SurfaceBackColor(panel, AppleCard));
            if (_isLiveResizing)
            {
                PaintFastPreviewPanel(panel, e.Graphics);
                return;
            }

            PreviewThemeTokens tokens = CurrentPreviewTokens();
            Color bg = tokens.Bg;
            Color card = tokens.Panel;
            Color sidebar = tokens.Sidebar;
            Color user = tokens.User;
            Color input = tokens.Input;
            Color text = tokens.Text;
            Color accent = tokens.Accent;
            string layout = tokens.Layout;
            double glass = tokens.Glass;
            int sidebarAlpha = tokens.SidebarAlpha;
            int inputAlpha = tokens.InputAlpha;
            int composerAlpha = tokens.ComposerAlpha;
            int cardAlpha = tokens.CardAlpha;
            int overlayAlpha = tokens.OverlayAlpha;

            Rectangle r = panel.ClientRectangle;
            using (var titleFont = new Font(Font.FontFamily, 10.5f, FontStyle.Bold))
            {
                TextRenderer.DrawText(e.Graphics, "LIVE PREVIEW", titleFont, new Rectangle(14, 4, 150, SectionHeaderHeight), AppleText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }

            int screenHeight = Math.Max(188, r.Height - 48);
            int screenWidth = Math.Max(80, Math.Min(r.Width - 28, 640));
            Rectangle screen = new Rectangle(Math.Max(14, (r.Width - screenWidth) / 2), 40, screenWidth, screenHeight);
            using (var screenPath = UiShape.RoundedRect(screen, 12))
            {
                using (var bgBrush = new SolidBrush(bg))
                {
                    e.Graphics.FillPath(bgBrush, screenPath);
                }
                Image image = _previewSkinEnabled ? LoadPreviewBackgroundImage() : null;
                if (image != null)
                {
                    GraphicsState imageClip = e.Graphics.Save();
                    e.Graphics.SetClip(screenPath);
                    e.Graphics.DrawImage(image, CoverRectangle(image.Size, screen));
                    e.Graphics.Restore(imageClip);
                }
                using (var overlay = new LinearGradientBrush(
                    screen,
                    Color.FromArgb(overlayAlpha, bg),
                    Color.FromArgb(tokens.BackgroundEndAlpha, bg),
                    LinearGradientMode.ForwardDiagonal))
                {
                    var blend = new ColorBlend(3);
                    blend.Positions = new[] { 0f, 0.52f, 1f };
                    blend.Colors = new[]
                    {
                        Color.FromArgb(overlayAlpha, bg),
                        Color.FromArgb(tokens.BackgroundAccentAlpha, accent),
                        Color.FromArgb(tokens.BackgroundEndAlpha, bg)
                    };
                    overlay.InterpolationColors = blend;
                    e.Graphics.FillPath(overlay, screenPath);
                }
            }

            GraphicsState previewClip = e.Graphics.Save();
            using (var clipPath = UiShape.RoundedRect(screen, 12))
            {
                e.Graphics.SetClip(clipPath);
            }

            int sideWidth;
            if (layout == "focus")
            {
                sideWidth = Math.Max(76, Math.Min(128, screen.Width / 4));
            }
            else if (layout == "compact")
            {
                sideWidth = Math.Max(96, Math.Min(132, screen.Width / 3));
            }
            else if (layout == "wide")
            {
                sideWidth = Math.Max(104, Math.Min(176, screen.Width / 3));
            }
            else
            {
                sideWidth = Math.Max(94, Math.Min(152, screen.Width / 3));
            }
            sideWidth = Math.Min(sideWidth, Math.Max(70, screen.Width - 120));
            Rectangle side = new Rectangle(screen.Left, screen.Top, sideWidth, screen.Height);
            Image panelImage = _previewSkinEnabled ? LoadPreviewPanelImage() : null;
            int sideFillAlpha = panelImage == null ? sidebarAlpha : 255;
            using (var sideBrush = new SolidBrush(Color.FromArgb(sideFillAlpha, sidebar)))
            {
                e.Graphics.FillRectangle(sideBrush, side);
            }
            if (panelImage != null)
            {
                GraphicsState panelClip = e.Graphics.Save();
                e.Graphics.SetClip(side);
                e.Graphics.DrawImage(panelImage, CoverRectangle(panelImage.Size, side));
                using (var tint = new SolidBrush(Color.FromArgb(tokens.PanelImageShadeAlpha, sidebar)))
                {
                    e.Graphics.FillRectangle(tint, side);
                }
                e.Graphics.Restore(panelClip);
            }

            if (layout == "focus")
            {
                using (var sideTitleFont = new Font(Font.FontFamily, 8.6f, FontStyle.Bold))
                {
                    TextRenderer.DrawText(e.Graphics, "ChatGPT", sideTitleFont, new Rectangle(side.Left + 14, side.Top + 20, side.Width - 24, 22), accent, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
                DrawPreviewRow(e.Graphics, side.Left + 12, side.Top + 62, side.Width - 22, "New", PreviewTextForSurface(accent, tokens.Selected), tokens.Selected, tokens.RowAlpha);
                DrawPreviewRow(e.Graphics, side.Left + 12, side.Top + 101, side.Width - 22, "Search", PreviewTextForSurface(text, tokens.Hover), tokens.Hover, tokens.HoverAlpha);
                DrawPreviewRow(e.Graphics, side.Left + 12, side.Top + 140, side.Width - 22, "Library", text, Color.Empty, 0);
            }
            else
            {
                using (var sideTitleFont = new Font(Font.FontFamily, 10f, FontStyle.Bold))
                {
                    TextRenderer.DrawText(e.Graphics, "ChatGPT", sideTitleFont, new Rectangle(side.Left + 18, side.Top + 20, side.Width - 30, 24), accent, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
                string newLabel = layout == "compact" ? "New" : "New chat";
                string gptsLabel = layout == "compact" ? "GPTs" : "GPTs";
                DrawPreviewRow(e.Graphics, side.Left + 18, side.Top + 68, side.Width - 30, newLabel, PreviewTextForSurface(accent, tokens.Selected), tokens.Selected, tokens.RowAlpha);
                DrawPreviewRow(e.Graphics, side.Left + 18, side.Top + 110, side.Width - 30, "Search", PreviewTextForSurface(text, tokens.Hover), tokens.Hover, tokens.HoverAlpha);
                DrawPreviewRow(e.Graphics, side.Left + 18, side.Top + 152, side.Width - 30, "Library", text, Color.Empty, 0);
                if (screen.Height > 252)
                {
                    DrawPreviewRow(e.Graphics, side.Left + 18, side.Top + 194, side.Width - 30, gptsLabel, text, Color.Empty, 0);
                }
                TextRenderer.DrawText(e.Graphics, "Settings", Font, new Rectangle(side.Left + 22, side.Bottom - 42, side.Width - 36, 24), text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            Rectangle content = new Rectangle(side.Right + 1, screen.Top, screen.Width - side.Width - 1, screen.Height);
            using (var sheen = new LinearGradientBrush(content, Color.FromArgb((int)(18 + (glass * 42)), accent), Color.FromArgb(0, bg), LinearGradientMode.Horizontal))
            {
                e.Graphics.FillRectangle(sheen, content);
            }
            int pillWidth = Math.Max(58, Math.Min(92, content.Width - 24));
            DrawPreviewPill(e.Graphics, new Rectangle(content.Right - pillWidth - 20, content.Top + 14, pillWidth, 24), _previewSkinEnabled ? layout : "Unskinned", input, text, inputAlpha);

            int contentPad = Math.Max(12, Math.Min(24, content.Width / 10));
            int availableContentWidth = Math.Max(68, content.Width - (contentPad * 2));
            int maxMessageWidth = layout == "wide" ? availableContentWidth : Math.Min(layout == "compact" ? 190 : 235, availableContentWidth);
            int userWidth = Math.Max(68, maxMessageWidth);
            DrawBubble(e.Graphics, new Rectangle(content.Right - userWidth - contentPad, content.Top + 48, userWidth, 38), "Hello!", user, text, 230);

            int assistantWidth = Math.Max(80, layout == "wide" ? availableContentWidth : Math.Min(layout == "compact" ? 205 : 250, availableContentWidth));
            int assistantHeight = layout == "compact" ? 62 : Math.Max(78, Math.Min(108, content.Height - 172));
            DrawBubble(e.Graphics, new Rectangle(content.Left + contentPad, content.Top + 100, assistantWidth, assistantHeight), _previewSkinEnabled ? "Preview updates as you customize." : "Skin cleared. ChatGPT is back to its default look.", card, text, cardAlpha);

            int composerWidth = layout == "compact" ? Math.Min(availableContentWidth, 310) : availableContentWidth;
            int composerLeft = layout == "compact" ? content.Left + ((content.Width - composerWidth) / 2) : content.Left + contentPad;
            DrawGradientBubble(
                e.Graphics,
                new Rectangle(composerLeft, content.Bottom - 64, composerWidth, 46),
                "Message ChatGPT...",
                input,
                composerAlpha,
                card,
                tokens.ComposerPanelAlpha,
                accent,
                tokens.ComposerAccentAlpha,
                Color.FromArgb(180, text));

            e.Graphics.Restore(previewClip);
        }

        private PreviewThemeTokens CurrentPreviewTokens()
        {
            Theme theme = _previewSkinEnabled ? CurrentTheme() : new Theme
            {
                Id = "default",
                Name = "Default",
                Bg = "#202123",
                Panel = "#171717",
                Input = "#2f2f2f",
                Text = "#ececf1",
                Accent = "#ececf1",
                Border = "#4a4a4a",
                User = "#2f2f2f",
                Pattern = ""
            };

            string effectiveText = EnsureReadableText(theme.Text, theme.Bg);
            // Keep Background and Panel independent. The preview must not blend Bg into Panel.
            string sidebarHex = theme.Panel;
            Color bg = ColorFromHex(theme.Bg);
            Color panel = ColorFromHex(theme.Panel);
            Color sidebar = ColorFromHex(sidebarHex);
            Color input = ColorFromHex(theme.Input);
            Color user = ColorFromHex(theme.User);
            Color text = ColorFromHex(effectiveText);
            Color accent = ColorFromHex(theme.Accent);
            Color border = ColorFromHex(theme.Border);
            string layout = _previewSkinEnabled ? SelectedLayout() : "standard";
            int transparency = _previewSkinEnabled ? SelectedTransparency() : 0;
            double glass = Math.Max(0, Math.Min(75, transparency)) / 75.0;
            bool glassSearch = _previewSkinEnabled && SelectedGlassSearch();
            bool light = IsLightColor(theme.Bg);
            double panelAlphaValue = (String.Equals(theme.Bg, "#f4f7fb", StringComparison.OrdinalIgnoreCase) ? 0.94 : 0.84) - (glass * 0.72);
            double sidebarAlphaValue = 0.96 - (glass * 0.72);
            double inputAlphaValue = 0.92 - (glass * 0.68);
            double tokenAlphaValue = 0.88 - (glass * 0.58);
            double composerAlphaValue = 0.94 - (glass * 0.7);
            panelAlphaValue = Math.Max(0.12, panelAlphaValue);
            sidebarAlphaValue = Math.Max(0.16, sidebarAlphaValue);
            inputAlphaValue = Math.Max(0.16, inputAlphaValue);
            tokenAlphaValue = Math.Max(0.2, tokenAlphaValue);
            composerAlphaValue = Math.Max(0.18, composerAlphaValue);
            double searchAlphaValue = glassSearch
                ? Math.Max(light ? 0.78 : 0.56, inputAlphaValue * 0.78)
                : inputAlphaValue;

            bool hasBackgroundImage = false;
            if (_previewSkinEnabled && String.Equals(SelectedBackgroundMode(), "file", StringComparison.OrdinalIgnoreCase))
            {
                string backgroundPath = _backgroundTextBox == null ? "" : _backgroundTextBox.Text.Trim();
                try
                {
                    hasBackgroundImage = !String.IsNullOrWhiteSpace(backgroundPath) && File.Exists(Path.GetFullPath(backgroundPath));
                }
                catch
                {
                    hasBackgroundImage = false;
                }
            }
            double imageShade = hasBackgroundImage
                ? Math.Max(0.16, (light ? 0.28 : 0.38) - (glass * 0.12))
                : Math.Max(0.30, (light ? 0.48 : 0.70) - (glass * 0.45));
            double accentShade = hasBackgroundImage ? (light ? 0.07 : 0.08) : (light ? 0.09 : 0.10);
            Color composer = Blend(Blend(input, panel, 0.28), accent, 0.08);

            return new PreviewThemeTokens
            {
                Bg = bg,
                Panel = panel,
                Sidebar = sidebar,
                Input = input,
                User = user,
                Composer = composer,
                Text = text,
                Accent = accent,
                Border = border,
                Hover = accent,
                Active = Blend(input, accent, light ? 0.26 : 0.22),
                Selected = Blend(input, accent, 0.18),
                PanelAlpha = AlphaByte(panelAlphaValue),
                SidebarAlpha = AlphaByte(sidebarAlphaValue),
                InputAlpha = AlphaByte(searchAlphaValue),
                ComposerAlpha = AlphaByte(composerAlphaValue),
                SearchPanelAlpha = AlphaByte(Math.Max(0.34, searchAlphaValue * 0.86)),
                SearchAccentAlpha = AlphaByte(0.10),
                ComposerPanelAlpha = AlphaByte(Math.Max(0.24, composerAlphaValue * 0.72)),
                ComposerAccentAlpha = AlphaByte(0.08),
                CardAlpha = AlphaByte(panelAlphaValue),
                OverlayAlpha = AlphaByte(imageShade),
                BackgroundAccentAlpha = AlphaByte(accentShade),
                BackgroundEndAlpha = AlphaByte(Math.Max(0.08, imageShade * 0.62)),
                PanelImageShadeAlpha = AlphaByte(Math.Max(0.18, Math.Min(0.46, sidebarAlphaValue * 0.52))),
                RowAlpha = AlphaByte(Math.Max(0.56, tokenAlphaValue)),
                HoverAlpha = AlphaByte(light ? 0.12 : 0.10),
                Glass = glass,
                Layout = layout,
                GlassSearch = glassSearch
            };
        }

        private static int AlphaByte(double alpha)
        {
            return Math.Max(0, Math.Min(255, (int)Math.Round(alpha * 255)));
        }

        private void PaintFastPreviewPanel(Control panel, Graphics g)
        {
            PreviewThemeTokens tokens = CurrentPreviewTokens();
            Color bg = tokens.Bg;
            Color card = tokens.Panel;
            Color sidebar = tokens.Sidebar;
            Color text = tokens.Text;
            Color accent = tokens.Accent;

            Rectangle r = panel.ClientRectangle;
            using (var titleFont = new Font(Font.FontFamily, 10.5f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, "LIVE PREVIEW", titleFont, new Rectangle(14, 4, 150, SectionHeaderHeight), AppleText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
            int screenWidth = Math.Max(80, Math.Min(r.Width - 28, 640));
            Rectangle screen = new Rectangle(Math.Max(14, (r.Width - screenWidth) / 2), 40, screenWidth, Math.Max(170, r.Height - 54));
            using (var screenPath = UiShape.RoundedRect(screen, 12))
            {
                using (var bgBrush = new SolidBrush(bg))
                {
                    g.FillPath(bgBrush, screenPath);
                }
                using (var overlay = new LinearGradientBrush(
                    screen,
                    Color.FromArgb(tokens.OverlayAlpha, bg),
                    Color.FromArgb(tokens.BackgroundEndAlpha, bg),
                    LinearGradientMode.ForwardDiagonal))
                {
                    var blend = new ColorBlend(3);
                    blend.Positions = new[] { 0f, 0.52f, 1f };
                    blend.Colors = new[]
                    {
                        Color.FromArgb(tokens.OverlayAlpha, bg),
                        Color.FromArgb(tokens.BackgroundAccentAlpha, accent),
                        Color.FromArgb(tokens.BackgroundEndAlpha, bg)
                    };
                    overlay.InterpolationColors = blend;
                    g.FillPath(overlay, screenPath);
                }
                int sideWidth = Math.Max(72, Math.Min(132, screen.Width / 4));
                using (var sideBrush = new SolidBrush(Color.FromArgb(tokens.SidebarAlpha, sidebar)))
                {
                    g.FillRectangle(sideBrush, new Rectangle(screen.Left, screen.Top, sideWidth, screen.Height));
                }
                using (var inputBrush = new SolidBrush(Color.FromArgb(tokens.ComposerAlpha, tokens.Composer)))
                {
                    Rectangle composer = new Rectangle(screen.Left + sideWidth + 28, screen.Bottom - 62, Math.Max(80, screen.Width - sideWidth - 56), 42);
                    using (var composerPath = UiShape.RoundedRect(composer, 21))
                    {
                        g.FillPath(inputBrush, composerPath);
                    }
                }
                using (var bubbleBrush = new SolidBrush(Color.FromArgb(tokens.CardAlpha, card)))
                {
                    Rectangle bubble = new Rectangle(screen.Left + sideWidth + 34, screen.Top + 88, Math.Max(110, Math.Min(260, screen.Width - sideWidth - 72)), 72);
                    using (var bubblePath = UiShape.RoundedRect(bubble, 14))
                    {
                        g.FillPath(bubbleBrush, bubblePath);
                    }
                }
            }

            TextRenderer.DrawText(g, "Preview refreshes after resizing", Font, new Rectangle(screen.Left, screen.Bottom - 26, screen.Width, 18), Color.FromArgb(185, text), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private Image LoadPreviewBackgroundImage()
        {
            if (!String.Equals(SelectedBackgroundMode(), "file", StringComparison.OrdinalIgnoreCase))
            {
                ClearCachedImage(ref _cachedBackgroundPreviewImage, ref _cachedBackgroundPreviewKey);
                return null;
            }

            return LoadCachedFilePreviewImage(
                ref _cachedBackgroundPreviewImage,
                ref _cachedBackgroundPreviewKey,
                _backgroundTextBox == null ? "" : _backgroundTextBox.Text.Trim());
        }

        private Image LoadPreviewPanelImage()
        {
            string mode = SelectedPanelImageMode();
            if (mode == "same")
            {
                ClearCachedImage(ref _cachedPanelPreviewImage, ref _cachedPanelPreviewKey);
                return LoadPreviewBackgroundImage();
            }

            if (mode != "file")
            {
                ClearCachedImage(ref _cachedPanelPreviewImage, ref _cachedPanelPreviewKey);
                return null;
            }

            return LoadCachedFilePreviewImage(
                ref _cachedPanelPreviewImage,
                ref _cachedPanelPreviewKey,
                _panelImageTextBox == null ? "" : _panelImageTextBox.Text.Trim());
        }

        private Image LoadCachedFilePreviewImage(ref Image cachedImage, ref string cachedKey, string path)
        {
            FileInfo info;
            string mimeType;
            string error;
            if (!TryGetValidatedLocalImageFile(path, out info, out mimeType, out error))
            {
                ClearCachedImage(ref cachedImage, ref cachedKey);
                return null;
            }

            string key = PreviewImageKey(info);
            if (cachedImage != null && String.Equals(cachedKey, key, StringComparison.OrdinalIgnoreCase))
            {
                return cachedImage;
            }

            ClearCachedImage(ref cachedImage, ref cachedKey);
            try
            {
                using (var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var image = Image.FromStream(stream))
                {
                    cachedImage = new Bitmap(image);
                    cachedKey = key;
                    return cachedImage;
                }
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
            if (imageSize.Width <= 0 || imageSize.Height <= 0) return dest;
            double scale = Math.Max(dest.Width / (double)imageSize.Width, dest.Height / (double)imageSize.Height);
            int width = (int)Math.Ceiling(imageSize.Width * scale);
            int height = (int)Math.Ceiling(imageSize.Height * scale);
            return new Rectangle(
                dest.Left + ((dest.Width - width) / 2),
                dest.Top + ((dest.Height - height) / 2),
                width,
                height);
        }

        private void DrawPreviewRow(Graphics g, int x, int y, int w, string text, Color textColor, Color fillColor, int fillAlpha)
        {
            Rectangle row = new Rectangle(x, y, w, 32);
            if (fillAlpha > 0 && fillColor != Color.Empty)
            {
                using (var path = UiShape.RoundedRect(row, 8))
                using (var brush = new SolidBrush(Color.FromArgb(Math.Max(24, Math.Min(220, fillAlpha)), fillColor)))
                {
                    g.FillPath(brush, path);
                }
            }
            using (Font previewFont = PreviewChatFont(Font.Size, FontStyle.Regular))
            {
                TextRenderer.DrawText(g, text, previewFont, new Rectangle(x + 14, y, Math.Max(1, w - 18), 32), textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
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
            using (var path = UiShape.RoundedRect(rect, 12))
            using (var brush = new SolidBrush(Color.FromArgb(Math.Max(42, Math.Min(230, alpha)), fill)))
            {
                g.FillPath(brush, path);
            }
            using (var pillFont = new Font(Font.FontFamily, 7.8f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, copy, pillFont, rect, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        private void DrawBubble(Graphics g, Rectangle rect, string copy, Color fill, Color text, int alpha)
        {
            using (var path = UiShape.RoundedRect(rect, 18))
            using (var brush = new SolidBrush(Color.FromArgb(Math.Max(42, Math.Min(230, alpha)), fill)))
            {
                g.FillPath(brush, path);
            }
            using (Font previewFont = PreviewChatFont(Font.Size, FontStyle.Regular))
            {
                TextRenderer.DrawText(g, copy, previewFont, new Rectangle(rect.Left + 20, rect.Top + 10, Math.Max(1, rect.Width - 34), Math.Max(1, rect.Height - 18)), text, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            }
        }

        private void DrawGradientBubble(Graphics g, Rectangle rect, string copy, Color first, int firstAlpha, Color middle, int middleAlpha, Color last, int lastAlpha, Color text)
        {
            using (var path = UiShape.RoundedRect(rect, 18))
            using (var brush = new LinearGradientBrush(
                rect,
                Color.FromArgb(Math.Max(42, Math.Min(240, firstAlpha)), first),
                Color.FromArgb(Math.Max(18, Math.Min(240, lastAlpha)), last),
                LinearGradientMode.ForwardDiagonal))
            {
                var blend = new ColorBlend(3);
                blend.Positions = new[] { 0f, 0.52f, 1f };
                blend.Colors = new[]
                {
                    Color.FromArgb(Math.Max(42, Math.Min(240, firstAlpha)), first),
                    Color.FromArgb(Math.Max(18, Math.Min(240, middleAlpha)), middle),
                    Color.FromArgb(Math.Max(18, Math.Min(240, lastAlpha)), last)
                };
                brush.InterpolationColors = blend;
                g.FillPath(brush, path);
            }
            using (Font previewFont = PreviewChatFont(Font.Size, FontStyle.Regular))
            {
                TextRenderer.DrawText(g, copy, previewFont, new Rectangle(rect.Left + 20, rect.Top + 10, Math.Max(1, rect.Width - 34), Math.Max(1, rect.Height - 18)), text, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            }
        }

        private Font PreviewChatFont(float size, FontStyle style)
        {
            string font = SelectedFontFamily();
            if (String.IsNullOrWhiteSpace(font) || String.Equals(font, "Default", StringComparison.OrdinalIgnoreCase) || String.Equals(font, "System UI", StringComparison.OrdinalIgnoreCase))
            {
                return new Font(Font.FontFamily, size, style);
            }

            string first = SanitizeFontName(font.Split(',')[0]);
            if (String.IsNullOrEmpty(first))
            {
                return new Font(Font.FontFamily, size, style);
            }

            try
            {
                return new Font(first, size, style);
            }
            catch
            {
                return new Font(Font.FontFamily, size, style);
            }
        }

        private static Color Blend(Color a, Color b, double amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromArgb(
                (int)(a.R + ((b.R - a.R) * amount)),
                (int)(a.G + ((b.G - a.G) * amount)),
                (int)(a.B + ((b.B - a.B) * amount)));
        }

        private Control BuildActionsPanel()
        {
            var panel = new RoundedPanel();
            panel.Dock = DockStyle.Fill;
            panel.Margin = new Padding(0, 0, 0, 0);
            panel.Padding = new Padding(6, 0, 6, 0);
            panel.Radius = 1;
            panel.BackColor = Color.Transparent;
            panel.BorderColor = Color.Transparent;
            panel.GradientTop = Color.Empty;
            panel.GradientBottom = Color.Empty;

            var bar = new TableLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.BackColor = Color.Transparent;
            bar.ColumnCount = 1;
            bar.RowCount = 3;
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
            bar.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.Controls.Add(bar);

            var statusRow = new TableLayoutPanel();
            statusRow.Dock = DockStyle.Fill;
            statusRow.BackColor = Color.Transparent;
            statusRow.Margin = new Padding(0);
            statusRow.Padding = new Padding(0);
            statusRow.ColumnCount = 1;
            statusRow.RowCount = 1;
            statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.Controls.Add(statusRow, 0, 0);

            _statusLabel = Label("Ready.");
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.ForeColor = AppleMuted;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.Margin = new Padding(0, 0, 6, 1);
            _statusLabel.Padding = new Padding(0, 0, 0, 4);
            _statusLabel.AutoEllipsis = true;
            _statusToolTip.SetToolTip(_statusLabel, "Ready.");
            statusRow.Controls.Add(_statusLabel, 0, 0);

            _activeThemeLabel = Label("");
            _activeThemeLabel.Visible = false;

            var utilityRow = new TableLayoutPanel();
            utilityRow.Dock = DockStyle.Fill;
            utilityRow.BackColor = Color.Transparent;
            utilityRow.Margin = new Padding(0);
            utilityRow.Padding = new Padding(0);
            utilityRow.ColumnCount = 2;
            utilityRow.RowCount = 1;
            utilityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            utilityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            _watchButton = Button("Watch: Off");
            SetButtonIcon(_watchButton, ButtonIconKind.Eye);
            _watchButton.Dock = DockStyle.Fill;
            _watchButton.Margin = new Padding(0, 0, 8, 0);
            _watchButton.Click += ToggleWatch;
            utilityRow.Controls.Add(_watchButton, 0, 0);

            _clearButton = Button("Clear Theme");
            // Keep Clear Theme text-only; do not replace it with emoji or decorative symbols.
            SetButtonIcon(_clearButton, ButtonIconKind.None);
            _clearButton.Dock = DockStyle.Fill;
            _clearButton.Margin = new Padding(0);
            _clearButton.Click += async delegate { await ClearSkinAsync(); };
            utilityRow.Controls.Add(_clearButton, 1, 0);
            bar.Controls.Add(utilityRow, 0, 1);

            var primaryRow = new TableLayoutPanel();
            primaryRow.Dock = DockStyle.Fill;
            primaryRow.BackColor = Color.Transparent;
            primaryRow.ColumnCount = 1;
            primaryRow.RowCount = 1;
            primaryRow.Margin = new Padding(0, 2, 0, 0);
            primaryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.Controls.Add(primaryRow, 0, 2);

            _applyButton = Button("Apply");
            _applyButton.Text = "Apply Theme";
            SetButtonIcon(_applyButton, ButtonIconKind.Check);
            _applyButton.Dock = DockStyle.Fill;
            _applyButton.Margin = new Padding(0);
            _applyButton.BackColor = ApplePink;
            _applyButton.ForeColor = Color.White;
            _applyButton.FlatAppearance.MouseOverBackColor = ApplePink;
            _applyButton.FlatAppearance.MouseDownBackColor = ApplePink;
            var applyRounded = _applyButton as RoundedButton;
            if (applyRounded != null)
            {
                applyRounded.DisabledBackColor = Blend(ApplePink, AppleControl, 0.45);
                applyRounded.DisabledForeColor = Color.White;
            }
            _applyButton.Click += async delegate { await ApplySkinAsync(false); };
            primaryRow.Controls.Add(_applyButton, 0, 0);
            return panel;
        }

        private Panel CardPanel()
        {
            var panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.Margin = new Padding(0);
            panel.BackColor = Color.Transparent;
            return panel;
        }

        private Label Label(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                ForeColor = AppleText,
                BackColor = Color.Transparent
            };
        }

        private Label SectionHeaderLabel(string text)
        {
            var label = Label(text);
            label.Dock = DockStyle.Fill;
            label.Height = SectionHeaderHeight;
            label.Font = new Font(Font.FontFamily, SectionHeaderFontSize, FontStyle.Bold);
            label.ForeColor = AppleText;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Margin = new Padding(0);
            label.Padding = new Padding(0);
            return label;
        }

        private void StyleCombo(ComboBox combo)
        {
            if (combo == null) return;
            combo.FlatStyle = FlatStyle.Flat;
            combo.BackColor = Color.FromArgb(14, 17, 27);
            combo.ForeColor = AppleText;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.ItemHeight = 24;
            combo.Height = 30;
            combo.MinimumSize = new Size(0, 30);
            combo.DropDownHeight = 320;
            combo.MaxDropDownItems = 16;
            var themed = combo as ThemedComboBox;
            if (themed != null)
            {
                themed.ThemeBackColor = Color.FromArgb(14, 17, 27);
                themed.ThemeForeColor = AppleText;
                themed.ThemeAccentColor = Color.FromArgb(60, 68, 86);
            }
            combo.DrawItem -= DrawComboItem;
            combo.DrawItem += DrawComboItem;
        }

        private void DrawComboItem(object sender, DrawItemEventArgs e)
        {
            var combo = sender as ComboBox;
            if (combo == null || e.Bounds.Width <= 0 || e.Bounds.Height <= 0) return;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var themed = combo as ThemedComboBox;
            Color normalBack = themed == null ? Color.FromArgb(14, 17, 27) : themed.ThemeBackColor;
            Color selectedBack = themed == null ? Color.FromArgb(31, 35, 49) : themed.ThemeAccentColor;
            Color fore = themed == null ? AppleText : themed.ThemeForeColor;
            Color back = selected ? selectedBack : normalBack;
            using (var brush = new SolidBrush(back))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            string text = e.Index >= 0 && e.Index < combo.Items.Count ? Convert.ToString(combo.Items[e.Index]) : combo.Text;
            TextRenderer.DrawText(
                e.Graphics,
                text,
                Font,
                new Rectangle(e.Bounds.Left + 9, e.Bounds.Top, Math.Max(1, e.Bounds.Width - 18), e.Bounds.Height),
                fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        private void StyleTextBox(TextBox textBox)
        {
            if (textBox == null) return;
            textBox.BackColor = AppleInput;
            textBox.ForeColor = AppleText;
            textBox.Font = Font;
            textBox.AutoSize = false;
            textBox.Height = 30;
            textBox.MinimumSize = new Size(0, 30);
            textBox.BorderStyle = BorderStyle.None;
            var themed = textBox as ThemedTextBox;
            if (themed != null) themed.RefreshTextLayout();
        }

        private void StyleMuted(Label label)
        {
            if (label == null) return;
            label.ForeColor = AppleMuted;
        }

        private Button Button(string text)
        {
            var button = new RoundedButton();
            button.Radius = 12;
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = AppleControl;
            button.ForeColor = AppleText;
            button.FlatAppearance.BorderColor = button.BackColor;
            button.FlatAppearance.MouseOverBackColor = AppleControl;
            button.FlatAppearance.MouseDownBackColor = AppleControl;
            button.Cursor = Cursors.Hand;
            return button;
        }

        private void SetButtonIcon(Button button, ButtonIconKind icon)
        {
            var rounded = button as RoundedButton;
            if (rounded == null) return;
            rounded.IconKind = icon;
            rounded.Invalidate();
        }

        private void NewTheme()
        {
            Theme source = _themes.ContainsKey("plum") ? _themes["plum"] : CurrentTheme();
            _customTheme = source.Clone("custom", "Custom");
            _themes["custom"] = _customTheme;
            _themeId = "custom";
            _currentThemeFilePath = "";
            if (_backgroundModeCombo != null) _backgroundModeCombo.SelectedIndex = 0;
            if (_backgroundTextBox != null) _backgroundTextBox.Text = "";
            if (_panelImageModeCombo != null) _panelImageModeCombo.SelectedIndex = 0;
            if (_panelImageTextBox != null) _panelImageTextBox.Text = "";
            _previewSkinEnabled = true;
            ApplyUiState();
            SaveSettings();
            SetStatus("Started a new custom theme.", false);
        }

        private void SaveTheme(bool saveAs)
        {
            string path = _currentThemeFilePath;
            if (saveAs || String.IsNullOrWhiteSpace(path))
            {
                using (var dialog = new SaveFileDialog())
                {
                    dialog.Filter = "GPT Themes files|*.gpttheme|JSON files|*.json|All files|*.*";
                    dialog.DefaultExt = "gpttheme";
                    dialog.FileName = SafeFileName(CurrentTheme().Name) + ".gpttheme";
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    path = dialog.FileName;
                }
            }

            try
            {
                _currentThemeFilePath = path;
                SaveThemeDocument(path);
                SaveSettings();
                SetStatus("Saved theme to " + Path.GetFileName(path) + ".", false);
            }
            catch
            {
                SetStatus("Could not save theme. Choose a folder you can write to.", true);
            }
        }

        private void SaveThemeDocument(string path)
        {
            var theme = CurrentTheme().Clone(CurrentTheme().Id, CurrentTheme().Name);
            var doc = new ThemeDocument
            {
                Format = "gpt-themes.theme",
                Version = 1,
                Theme = theme,
                Settings = ExportSettingsSnapshot()
            };
            string folder = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(path, _json.Serialize(doc), Encoding.UTF8);
        }

        private SkinnerSettings ExportSettingsSnapshot()
        {
            SkinnerSettings settings = CurrentSettingsSnapshot();
            settings.ManualChatGptExePath = "";
            settings.ThemeFilePath = "";
            if (String.Equals(settings.BackgroundMode, "file", StringComparison.OrdinalIgnoreCase))
            {
                settings.BackgroundMode = "solid";
                settings.BackgroundValue = "";
            }
            if (String.Equals(settings.PanelImageMode, "file", StringComparison.OrdinalIgnoreCase))
            {
                settings.PanelImageMode = "off";
                settings.PanelImageValue = "";
                settings.PanelImage = false;
            }
            return settings;
        }

        private void ImportTheme()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "GPT Themes files|*.gpttheme;*.json|All files|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var info = new FileInfo(dialog.FileName);
                    if (info.Length > MaxThemeFileBytes)
                    {
                        throw new InvalidOperationException("Theme files must be 1 MB or smaller.");
                    }
                    string json = File.ReadAllText(info.FullName, Encoding.UTF8);
                    var doc = _json.Deserialize<ThemeDocument>(json);
                    if (doc == null || doc.Settings == null)
                    {
                        throw new InvalidOperationException("The selected file is not a GPT Themes theme file.");
                    }
                    NormalizeImportedThemeDocument(doc);
                    ApplyImportedTheme(doc, info.FullName);
                    SetStatus("Imported theme from " + Path.GetFileName(info.FullName) + ".", false);
                }
                catch (Exception ex)
                {
                    SetStatus("Could not import theme: " + SafeExceptionMessage(ex), true);
                }
            }
        }

        private void NormalizeImportedThemeDocument(ThemeDocument doc)
        {
            if (!String.IsNullOrEmpty(doc.Format) && !String.Equals(doc.Format, "gpt-themes.theme", StringComparison.OrdinalIgnoreCase))
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
            settings.ThemeId = _themes.ContainsKey(settings.ThemeId ?? "") ? settings.ThemeId : "custom";
            settings.Layout = NormalizeChoice(settings.Layout, new[] { "standard", "wide", "compact", "focus" }, "standard");
            settings.BackgroundMode = NormalizeChoice(settings.BackgroundMode, new[] { "solid", "pattern", "file" }, "solid");
            settings.PanelImageMode = NormalizeChoice(settings.PanelImageMode, new[] { "off", "same", "file" }, "off");
            settings.FontFamily = NormalizeFontChoice(settings.FontFamily);
            settings.Port = settings.Port > 0 && settings.Port <= 65535 ? settings.Port : 9322;
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

            if (settings.BackgroundMode == "file")
            {
                settings.BackgroundMode = "solid";
                settings.BackgroundValue = "";
            }
            else
            {
                settings.BackgroundValue = "";
            }

            if (settings.PanelImageMode == "file")
            {
                settings.PanelImageMode = "off";
                settings.PanelImageValue = "";
                settings.PanelImage = false;
            }
            else
            {
                settings.PanelImageValue = "";
                settings.PanelImage = settings.PanelImageMode == "same";
            }
        }

        private static string LimitThemeText(string value, int maxLength)
        {
            if (String.IsNullOrEmpty(value)) return "";
            value = value.Trim();
            if (value.Length > maxLength) value = value.Substring(0, maxLength);
            var sb = new StringBuilder();
            foreach (char c in value)
            {
                if (!Char.IsControl(c))
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static string SanitizeThemePattern(string pattern)
        {
            if (String.IsNullOrWhiteSpace(pattern)) return "";
            pattern = LimitThemeText(pattern, MaxThemeTextLength);
            string lower = pattern.ToLowerInvariant();
            if (lower.IndexOf("url(", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("@import", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("expression", StringComparison.Ordinal) >= 0 ||
                pattern.IndexOf(';') >= 0 ||
                pattern.IndexOf('{') >= 0 ||
                pattern.IndexOf('}') >= 0 ||
                pattern.IndexOf('<') >= 0 ||
                pattern.IndexOf('>') >= 0)
            {
                return "";
            }

            string[] layers = pattern.Split(',');
            foreach (string layer in layers)
            {
                string trimmed = layer.TrimStart().ToLowerInvariant();
                if (!(trimmed.StartsWith("linear-gradient(", StringComparison.Ordinal) ||
                    trimmed.StartsWith("radial-gradient(", StringComparison.Ordinal) ||
                    trimmed.StartsWith("transparent", StringComparison.Ordinal) ||
                    trimmed.StartsWith("rgb(", StringComparison.Ordinal)))
                {
                    return "";
                }
            }
            return pattern;
        }

        private static string NormalizeChoice(string value, IEnumerable<string> allowed, string fallback)
        {
            string normalized = (value ?? "").Trim().ToLowerInvariant();
            foreach (string item in allowed)
            {
                if (String.Equals(normalized, item, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }
            return fallback;
        }

        private static string NormalizeFontChoice(string value)
        {
            value = LimitThemeText(value, 64);
            if (String.IsNullOrWhiteSpace(value)) return "Default";
            foreach (string known in new[]
            {
                "Default", "System UI", "Segoe UI", "Inter", "Arial", "Verdana", "Georgia",
                "Courier New", "Comic Sans MS", "Atkinson Hyperlegible", "OpenDyslexic"
            })
            {
                if (String.Equals(value, known, StringComparison.OrdinalIgnoreCase))
                {
                    return known;
                }
            }
            string sanitized = SanitizeFontName(value);
            return String.IsNullOrEmpty(sanitized) ? "Default" : sanitized;
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
            else if (!String.IsNullOrEmpty(settings.ThemeId) && _themes.ContainsKey(settings.ThemeId))
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
            _manualChatGptExePath = IsChatGptExecutablePath(settings.ManualChatGptExePath) ? settings.ManualChatGptExePath : _manualChatGptExePath;
            _currentThemeFilePath = filePath;

            SetComboText(_layoutCombo, String.IsNullOrEmpty(settings.Layout) ? "Standard" : settings.Layout);
            SetComboText(_fontCombo, NonEmpty(settings.FontFamily, "Default"));
            if (_transparencyTrackBar != null) _transparencyTrackBar.Value = _transparency;
            if (_glassSearchCheckBox != null) _glassSearchCheckBox.Checked = _glassSearch;
            if (_backgroundModeCombo != null) _backgroundModeCombo.SelectedIndex = BackgroundModeToIndex(NonEmpty(settings.BackgroundMode, "solid"));
            if (_backgroundTextBox != null) _backgroundTextBox.Text = NonEmpty(settings.BackgroundValue, "");
            if (_panelImageModeCombo != null) _panelImageModeCombo.SelectedIndex = PanelImageModeToIndex(NonEmpty(settings.PanelImageMode, settings.PanelImage ? "same" : "off"));
            if (_panelImageTextBox != null) _panelImageTextBox.Text = NonEmpty(settings.PanelImageValue, "");
            if (_portTextBox != null) _portTextBox.Text = "Private";
            _previewSkinEnabled = true;
            ApplyUiState();
            SaveSettings();
        }

        private void OpenHelpDocumentation()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "README.md");
            if (!File.Exists(path))
            {
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs", "README.md");
            }
            if (File.Exists(path))
            {
                OpenPath(path);
                return;
            }
            ShowThemedInfoDialog("Help Documentation", "The local README.md help file was not found next to the application.", null);
        }

        private void ShowProjectInformation()
        {
            ShowThemedInfoDialog(
                "Project Information",
                "GPT Themes lets you customize the appearance of the ChatGPT Desktop application with ready-made or custom themes. Create a theme, adjust colors, glass, layout, fonts, and images, preview the look live, then apply it when you are happy with it." + Environment.NewLine + Environment.NewLine +
                "You can save themes for later, import themes from other people, and export your own themes to share.",
                new[] { "https://github.com/piratemoo/gpt-themes", "https://piratemoo.com" });
        }

        private void ShowAboutDialog()
        {
            string version = String.IsNullOrWhiteSpace(Application.ProductVersion) ? "0.1.0" : Application.ProductVersion;
            ShowThemedInfoDialog(
                "About GPT Themes",
                "GPT Themes" + Environment.NewLine +
                "Version " + version + Environment.NewLine + Environment.NewLine +
                "Created by @piratemoo" + Environment.NewLine +
                "Copyright 2026",
                new[] { "https://piratemoo.com", "https://github.com/piratemoo/gpt-themes" });
        }

        private void ShowThemedInfoDialog(string title, string body, IEnumerable<string> links)
        {
            Theme theme = CurrentTheme();
            Color bg = ColorFromHex(theme.Bg);
            Color panel = ColorFromHex(theme.Panel);
            Color input = ColorFromHex(theme.Input);
            Color text = ColorFromHex(EnsureReadableText(theme.Text, theme.Bg));
            Color accent = ColorFromHex(theme.Accent);
            using (var form = new Form())
            {
                form.Text = title;
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.None;
                form.ClientSize = new Size(500, links == null ? 220 : 316);
                form.MinimumSize = new Size(440, 220);
                form.BackColor = bg;
                form.ForeColor = text;
                form.Font = Font;
                form.KeyPreview = true;
                form.KeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Escape) form.Close();
                };

                var root = new TableLayoutPanel();
                root.Dock = DockStyle.Fill;
                root.Padding = new Padding(18, 14, 18, 16);
                root.BackColor = panel;
                root.ColumnCount = 1;
                root.RowCount = 4;
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, links == null ? 0 : 54));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
                form.Controls.Add(root);

                var heading = Label(title);
                heading.Dock = DockStyle.Fill;
                heading.Font = new Font(Font.FontFamily, 12f, FontStyle.Bold);
                heading.ForeColor = text;
                heading.TextAlign = ContentAlignment.MiddleLeft;
                root.Controls.Add(heading, 0, 0);

                var message = Label(body);
                message.Dock = DockStyle.Fill;
                message.ForeColor = text;
                message.TextAlign = ContentAlignment.TopLeft;
                message.Padding = new Padding(0, 8, 0, 0);
                root.Controls.Add(message, 0, 1);

                var linkPanel = new FlowLayoutPanel();
                linkPanel.Dock = DockStyle.Fill;
                linkPanel.BackColor = Color.Transparent;
                linkPanel.FlowDirection = FlowDirection.TopDown;
                linkPanel.WrapContents = false;
                root.Controls.Add(linkPanel, 0, 2);
                if (links != null)
                {
                    foreach (string url in links)
                    {
                        var link = new LinkLabel();
                        link.Text = url;
                        link.AutoSize = true;
                        link.LinkColor = accent;
                        link.ActiveLinkColor = accent;
                        link.VisitedLinkColor = accent;
                        link.BackColor = Color.Transparent;
                        link.Margin = new Padding(0, 2, 0, 0);
                        link.Click += delegate { OpenUrl(url); };
                        linkPanel.Controls.Add(link);
                    }
                }

                var ok = Button("OK");
                ok.Dock = DockStyle.Right;
                ok.Width = 112;
                ok.BackColor = input;
                ok.ForeColor = text;
                ok.Click += delegate { form.Close(); };
                root.Controls.Add(ok, 0, 3);
                form.ShowDialog(this);
            }
        }

        private static string SafeFileName(string value)
        {
            value = String.IsNullOrWhiteSpace(value) ? "theme" : value.Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(c, '-');
            }
            return value;
        }

        private void OpenPath(string path)
        {
            try
            {
                var info = new ProcessStartInfo();
                info.FileName = path;
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch (Exception ex)
            {
                SetStatus("Could not open file: " + SafeExceptionMessage(ex), true);
            }
        }

        private void OpenUrl(string url)
        {
            try
            {
                var info = new ProcessStartInfo();
                info.FileName = url;
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch (Exception ex)
            {
                SetStatus("Could not open link: " + SafeExceptionMessage(ex), true);
            }
        }

        private Button ColorButton(string label, Func<string> getColor, Action<string> setColor)
        {
            var button = new ColorSwatchButton();
            button.LabelText = label;
            button.Text = label;
            button.Font = new Font(Font.FontFamily, 8.4f);
            button.BackColor = Color.Transparent;
            button.ForeColor = AppleText;
            button.Click += delegate
            {
                using (var dialog = new ThemedColorDialog(label + " color", ColorFromHex(getColor()), PaletteColors()))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        setColor(ThemedColorDialog.ToHex(dialog.SelectedColor));
                        _themeId = "custom";
                        _previewSkinEnabled = true;
                        ApplyUiState();
                    }
                }
            };
            return button;
        }

        private IEnumerable<Color> PaletteColors()
        {
            var colors = new List<Color>();
            AddPaletteColor(colors, CurrentTheme().Bg);
            AddPaletteColor(colors, CurrentTheme().Panel);
            AddPaletteColor(colors, CurrentTheme().Input);
            AddPaletteColor(colors, CurrentTheme().Text);
            AddPaletteColor(colors, CurrentTheme().Accent);
            AddPaletteColor(colors, CurrentTheme().Border);
            AddPaletteColor(colors, CurrentTheme().User);
            AddPaletteColor(colors, "#0B0F19");
            AddPaletteColor(colors, "#141824");
            AddPaletteColor(colors, "#F8FAFC");
            AddPaletteColor(colors, "#60A5FA");
            AddPaletteColor(colors, "#8B5CF6");
            AddPaletteColor(colors, "#F472B6");
            AddPaletteColor(colors, "#34D399");
            AddPaletteColor(colors, "#F59E0B");
            AddPaletteColor(colors, "#EF4444");
            return colors;
        }

        private void AddPaletteColor(List<Color> colors, string hex)
        {
            Color color = ColorFromHex(hex);
            foreach (Color existing in colors)
            {
                if (existing.ToArgb() == color.ToArgb()) return;
            }
            colors.Add(color);
        }

        private void AddThemeButton(string id)
        {
            Theme theme = _themes[id];
            var button = new ThemeCardButton();
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
            button.Click += delegate { SelectTheme((string)button.Tag); };
            button.MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && button.ClientRectangle.Contains(e.Location))
                {
                    SelectTheme((string)button.Tag);
                }
            };
            button.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
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
            if (String.IsNullOrWhiteSpace(id) || !_themes.ContainsKey(id)) return;

            long now = DateTime.UtcNow.Ticks;
            if (String.Equals(id, _lastThemeActivationId, StringComparison.OrdinalIgnoreCase) &&
                now - _lastThemeActivationTicks < TimeSpan.TicksPerMillisecond * 180)
            {
                return;
            }

            _lastThemeActivationId = id;
            _lastThemeActivationTicks = now;
            _previewSkinEnabled = true;
            _themeId = id;
            if (_themeId != "custom")
            {
                _customTheme = _themes[_themeId].Clone("custom", "Custom");
                _themes["custom"] = _customTheme;
            }
            ApplyUiState();
        }

        private int ThemeButtonWidth()
        {
            if (_themePanel == null || _themePanel.ClientSize.Width <= 0) return 69;
            int available = Math.Max(70, _themePanel.ClientSize.Width - 1);
            int columns = available >= 132 ? 2 : 1;
            int rightMarginPerTile = 4;
            int width = (available - (columns * rightMarginPerTile)) / columns;
            return Math.Max(61, Math.Min(71, width));
        }

        private void ResizeThemeButtons()
        {
            if (_themePanel == null) return;
            int width = ThemeButtonWidth();
            if (width == _lastThemeButtonWidth) return;
            _lastThemeButtonWidth = width;
            foreach (Button button in _themeButtons)
            {
                button.Width = width;
                button.Height = width;
                button.Margin = new Padding(0, 0, 4, 4);
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
            foreach (var button in _themeButtons)
            {
                string id = (string)button.Tag;
                bool selected = String.Equals(id, _themeId, StringComparison.OrdinalIgnoreCase);
                Theme theme = _themes[id];
                var card = button as ThemeCardButton;
                if (card != null)
                {
                    card.ThemeData = theme;
                    card.SelectedTheme = selected;
                    card.Invalidate();
                }
                button.FlatAppearance.BorderSize = 0;
                button.BackColor = Color.FromArgb(18, 22, 34);
                button.ForeColor = AppleText;
                button.FlatAppearance.BorderColor = button.BackColor;
                button.FlatAppearance.MouseOverBackColor = button.BackColor;
                button.FlatAppearance.MouseDownBackColor = button.BackColor;
            }

            if (_layoutCombo.SelectedIndex < 0)
            {
                SetComboText(_layoutCombo, _loadedLayout);
            }
            if (_fontCombo != null && String.IsNullOrWhiteSpace(_fontCombo.Text))
            {
                SetComboText(_fontCombo, _loadedFontFamily);
            }
            if (_backgroundModeCombo.SelectedIndex < 0)
            {
                _backgroundModeCombo.SelectedIndex = BackgroundModeToIndex(_loadedBackgroundMode);
                _backgroundTextBox.Text = _loadedBackgroundValue;
                _portTextBox.Text = "Private";
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
            if (_previewPanel != null) _previewPanel.Invalidate();
            UpdateActiveThemeLabel();
            SetStatus("Ready. Apply targets ChatGPT Desktop through a private pipe.", false);
        }

        private void ApplyChromeTheme()
        {
            Theme theme = CurrentTheme();
            Color bg = ColorFromHex(theme.Bg);
            Color panel = ColorFromHex(theme.Panel);
            Color input = ColorFromHex(theme.Input);
            Color accent = ColorFromHex(theme.Accent);
            Color border = ColorFromHex(theme.Border);
            Color text = ColorFromHex(EnsureReadableText(theme.Text, theme.Bg));
            Color muted = Blend(text, panel, 0.45);
            Color hover = Blend(input, accent, IsLightColor(theme.Bg) ? 0.18 : 0.14);

            if (_titleLabel != null)
            {
                _titleLabel.ForeColor = text;
            }
            if (_activeThemeLabel != null)
            {
                _activeThemeLabel.ForeColor = muted;
            }
            if (_menuStrip != null)
            {
                _menuStrip.BackColor = AppleBg;
                _menuStrip.ForeColor = text;
                _menuStrip.Renderer = new ThemedMenuRenderer(AppleBg, AppleBg, text, muted, AppleCard, border);
                StyleMenuItems(_menuStrip.Items, true);
                _menuStrip.Invalidate();
            }

            foreach (ComboBox combo in new[] { _layoutCombo, _fontCombo, _backgroundModeCombo, _panelImageModeCombo })
            {
                if (combo == null) continue;
                combo.BackColor = AppleInput;
                combo.ForeColor = text;
                var themed = combo as ThemedComboBox;
                if (themed != null)
                {
                    themed.ThemeBackColor = AppleInput;
                    themed.ThemeForeColor = text;
                    themed.ThemeAccentColor = hover;
                    themed.Invalidate();
                }
            }

            foreach (TextBox textBox in new[] { _backgroundTextBox, _panelImageTextBox })
            {
                if (textBox == null) continue;
                textBox.BackColor = AppleInput;
                textBox.ForeColor = text;
            }

            if (_portTextBox != null)
            {
                _portTextBox.BackColor = AppleInput;
                _portTextBox.ForeColor = text;
            }
            if (_transparencyTrackBar != null)
            {
                _transparencyTrackBar.ActiveColor = accent;
                _transparencyTrackBar.ThumbColor = accent;
                _transparencyTrackBar.TrackColor = Color.FromArgb(72, 78, 96);
                _transparencyTrackBar.BackColor = Color.Transparent;
                _transparencyTrackBar.Invalidate();
            }
            if (_glassSearchCheckBox != null)
            {
                var themedCheck = _glassSearchCheckBox as ThemedCheckBox;
                if (themedCheck != null)
                {
                    themedCheck.CheckedBackColor = accent;
                    themedCheck.BoxBackColor = AppleInput;
                    themedCheck.BoxBorderColor = Color.Transparent;
                    themedCheck.TextColor = text;
                    themedCheck.Invalidate();
                }
            }

            foreach (Button button in new[] { _watchButton, _clearButton, _testPortButton, _relaunchButton, _browseButton, _backgroundOkButton, _imageOkButton, _panelImageBrowseButton, _chooseChatGptButton })
            {
                if (button == null) continue;
                button.BackColor = AppleControl;
                button.ForeColor = text;
                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.MouseOverBackColor = hover;
                button.FlatAppearance.MouseDownBackColor = Blend(input, accent, 0.24);
                button.Invalidate();
            }
            if (_applyButton != null)
            {
                _applyButton.BackColor = ApplePink;
                _applyButton.ForeColor = Color.White;
                _applyButton.FlatAppearance.MouseOverBackColor = ApplePink;
                _applyButton.FlatAppearance.MouseDownBackColor = ApplePink;
                var applyRounded = _applyButton as RoundedButton;
                if (applyRounded != null)
                {
                    applyRounded.DisabledBackColor = Blend(ApplePink, AppleControl, 0.45);
                    applyRounded.DisabledForeColor = Color.White;
                }
                _applyButton.Invalidate();
            }
            ApplyNativeWindowTheme();
        }

        private void ApplyNativeWindowTheme()
        {
            if (!IsHandleCreated) return;

            try
            {
                int enabled = 1;
                DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
                DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));

                int borderColor = ColorToColorRef(AppleBg);
                int captionColor = ColorToColorRef(AppleBg);
                int textColor = ColorToColorRef(AppleText);
                DwmSetWindowAttribute(Handle, 34, ref borderColor, sizeof(int));
                DwmSetWindowAttribute(Handle, 35, ref captionColor, sizeof(int));
                DwmSetWindowAttribute(Handle, 36, ref textColor, sizeof(int));
                SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
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
            if (button == null) return;
            button.Text = text;
            var swatch = button as ColorSwatchButton;
            if (swatch != null)
            {
                swatch.LabelText = text;
                swatch.HexText = NormalizeHex(color);
                swatch.SwatchColor = ColorFromHex(color);
                swatch.BackColor = Color.Transparent;
                swatch.ForeColor = AppleText;
                swatch.Invalidate();
                return;
            }
            button.BackColor = ColorFromHex(color);
            button.ForeColor = ReadableTextColor(button.BackColor);
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.BorderColor = button.BackColor;
            button.FlatAppearance.MouseOverBackColor = button.BackColor;
            button.FlatAppearance.MouseDownBackColor = button.BackColor;
        }

        private void UpdateBackgroundInputState()
        {
            string mode = SelectedBackgroundMode();
            bool needsText = mode == "file";
            Theme theme = CurrentTheme();
            Color themedText = ColorFromHex(EnsureReadableText(theme.Text, theme.Bg));
            Color themedMuted = Blend(themedText, ColorFromHex(theme.Panel), 0.45);
            if (!needsText && !String.IsNullOrEmpty(_backgroundTextBox.Text))
            {
                _backgroundTextBox.Text = "";
            }
            _backgroundTextBox.Enabled = true;
            _backgroundTextBox.ReadOnly = !needsText;
            _backgroundTextBox.BackColor = Color.FromArgb(14, 17, 27);
            _backgroundTextBox.ForeColor = needsText ? themedText : themedMuted;
            _browseButton.Enabled = true;
            _backgroundTextBox.PlaceholderTextCompat("Select a local background image");
            if (_backgroundOkButton != null) _backgroundOkButton.Enabled = true;
            RefreshBackgroundPreview();
        }

        private void UpdatePanelImageInputState()
        {
            string mode = SelectedPanelImageMode();
            bool needsText = mode == "file";
            Theme theme = CurrentTheme();
            Color themedText = ColorFromHex(EnsureReadableText(theme.Text, theme.Bg));
            Color themedMuted = Blend(themedText, ColorFromHex(theme.Panel), 0.45);
            if (_panelImageTextBox != null)
            {
                if (!needsText && !String.IsNullOrEmpty(_panelImageTextBox.Text))
                {
                    _panelImageTextBox.Text = "";
                }
                _panelImageTextBox.Enabled = true;
                _panelImageTextBox.ReadOnly = !needsText;
                _panelImageTextBox.BackColor = Color.FromArgb(14, 17, 27);
                _panelImageTextBox.ForeColor = needsText ? themedText : themedMuted;
            }
            if (_panelImageBrowseButton != null) _panelImageBrowseButton.Enabled = true;
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
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "Image files|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|All files|*.*";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _backgroundTextBox.Text = dialog.FileName;
                }
            }
        }

        private void BrowsePanelImage(object sender, EventArgs e)
        {
            if (_panelImageModeCombo != null && _panelImageModeCombo.SelectedIndex != 2)
            {
                _panelImageModeCombo.SelectedIndex = 2;
                UpdatePanelImageInputState();
            }
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "Image files|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|All files|*.*";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _panelImageTextBox.Text = dialog.FileName;
                }
            }
        }

        private async void ChooseChatGptExe(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Choose ChatGPT.exe";
                dialog.Filter = "ChatGPT executable|ChatGPT.exe|Executables|*.exe|All files|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!IsChatGptExecutablePath(dialog.FileName))
                {
                    SetStatus("That does not look like the official ChatGPT.exe. Choose the ChatGPT Desktop executable.", true);
                    return;
                }

                _manualChatGptExePath = dialog.FileName;
                SaveSettings();
                await RefreshChatGptDetectionStatusAsync(true);
                SetStatus("Saved manual ChatGPT.exe location.", false);
            }
        }

        private async Task RefreshChatGptDetectionStatusAsync(bool verbose)
        {
            var result = await Task.Run(delegate { return DetectChatGpt(); });
            UpdateChatGptDetectionUi(result);
            if (verbose)
            {
                SetStatus(result.Summary + " " + result.Diagnostics, !result.Found);
            }
        }

        private void UpdateChatGptDetectionUi(ChatGptDetectionResult result)
        {
            if (result == null) return;
            if (_chatGptStatusLabel != null)
            {
                _chatGptStatusLabel.Text = result.Found ? "ChatGPT Detected - Ready" : "ChatGPT Not Detected";
                _chatGptStatusLabel.ForeColor = result.Found ? Color.FromArgb(126, 242, 196) : Color.FromArgb(255, 105, 97);
            }
            if (_chatGptDiagnosticsLabel != null)
            {
                _chatGptDiagnosticsLabel.Text = result.Found ? "" : result.Diagnostics;
            }
        }

        private ChatGptDetectionResult DetectChatGpt()
        {
            var result = new ChatGptDetectionResult();

            string manual = _manualChatGptExePath;
            if (!String.IsNullOrWhiteSpace(manual))
            {
                if (IsChatGptExecutablePath(manual))
                {
                    result.Found = true;
                    result.ExePath = manual;
                    result.Method = "manual selection";
                    result.Checks.Add("Manual executable is valid.");
                    result.AppUserModelId = PreferredChatGptAppUserModelId(TryFindChatGptAppUserModelId());
                    return result;
                }
                result.Checks.Add("Manual executable is missing or invalid.");
            }
            else
            {
                result.Checks.Add("No manual executable has been selected.");
            }

            try
            {
                foreach (var process in Process.GetProcessesByName("ChatGPT"))
                {
                    try
                    {
                        string path = process.MainModule.FileName;
                        if (IsChatGptExecutablePath(path))
                        {
                            result.Found = true;
                            result.ExePath = path;
                            result.Method = "running ChatGPT process";
                            result.AppUserModelId = PreferredChatGptAppUserModelId(AppUserModelIdFromPath(path));
                            result.Checks.Add("Found a running ChatGPT process.");
                            return result;
                        }
                    }
                    catch
                    {
                        result.Checks.Add("Running process path could not be read.");
                    }
                }
                result.Checks.Add("No running ChatGPT process was found.");
            }
            catch
            {
                result.Checks.Add("Process check failed.");
            }

            string localPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ChatGPT", "ChatGPT.exe");
            if (IsChatGptExecutablePath(localPrograms))
            {
                result.Found = true;
                result.ExePath = localPrograms;
                result.Method = "LocalAppData install";
                result.AppUserModelId = PreferredChatGptAppUserModelId(TryFindChatGptAppUserModelId());
                result.Checks.Add("Found ChatGPT in the current user's local app folder.");
                return result;
            }
            result.Checks.Add("Local app folder did not contain ChatGPT.exe.");

            string windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            if (Directory.Exists(windowsApps))
            {
                try
                {
                    foreach (string folder in Directory.GetDirectories(windowsApps, "OpenAI.ChatGPT-Desktop_*").OrderByDescending(path => path))
                    {
                        string candidate = Path.Combine(folder, "app", "ChatGPT.exe");
                        if (IsChatGptExecutablePath(candidate))
                        {
                            result.Found = true;
                            result.ExePath = candidate;
                            result.Method = "installed Windows package";
                            result.AppUserModelId = PreferredChatGptAppUserModelId(AppUserModelIdFromPath(candidate));
                            result.Checks.Add("Found the packaged ChatGPT desktop app.");
                            return result;
                        }
                    }
                    result.Checks.Add("WindowsApps was checked, but no ChatGPT package executable was readable.");
                }
                catch
                {
                    result.Checks.Add("WindowsApps check was blocked.");
                }
            }
            else
            {
                result.Checks.Add("WindowsApps folder was not present.");
            }

            result.AppUserModelId = PreferredChatGptAppUserModelId(TryFindChatGptAppUserModelId());
            result.Checks.Add("Windows app activation can still be tried, but it may not accept debugging arguments.");
            return result;
        }

        private void ToggleWatch(object sender, EventArgs e)
        {
            if (_watchTimer.Enabled)
            {
                _watchTimer.Stop();
                _watchButton.Text = "Watch: Off";
                SetStatus("Watch stopped.", false);
            }
            else
            {
                SaveSettings();
                _watchTimer.Start();
                _watchButton.Text = "Watch: On";
                SetStatus("Watch is on. The skin will be reapplied automatically.", false);
            }
        }

        private async Task ApplySkinAsync(bool quiet)
        {
            if (_applying) return;
            _applying = true;
            try
            {
                SaveSettings();
                _previewSkinEnabled = true;
                if (_previewPanel != null) _previewPanel.Invalidate();
                Theme theme = CurrentTheme();
                string backgroundImageDataUrl = ResolveBackgroundImageDataUrl(SelectedBackgroundMode(), _backgroundTextBox.Text.Trim());
                string panelImageDataUrl = ResolvePanelImageDataUrl(backgroundImageDataUrl);
                string css = BuildCss(
                    theme,
                    SelectedLayout(),
                    SelectedBackgroundMode(),
                    _backgroundTextBox.Text.Trim(),
                    SelectedTransparency(),
                    backgroundImageDataUrl,
                    panelImageDataUrl,
                    SelectedGlassSearch(),
                    SelectedFontFamily());
                string expression = BuildInjectionExpression(css);
                List<CdpTarget> targets = null;
                try
                {
                    targets = await GetPipeTargetsAsync();
                }
                catch
                {
                    if (quiet) return;
                    targets = new List<CdpTarget>();
                }

                if (targets.Count == 0)
                {
                    if (quiet) return;
                    SetStatus("Opening ChatGPT with the private skinning pipe...", false);
                    targets = await StartOrRestartChatGptAndWaitAsync(true, "Opening ChatGPT with the private skinning pipe");
                    if (targets.Count == 0)
                    {
                        SetStatus("ChatGPT opened, but the private skinning pipe is not ready yet. Try Apply again after it finishes loading.", true);
                        return;
                    }
                }

                foreach (var target in targets)
                {
                    Dictionary<string, object> response = await _pipeConnection.EvaluateAsync(target.TargetId, expression);
                    ThrowIfEvaluateFailed(response);
                }

                _activeTheme = CaptureCurrentAppliedTheme(theme);
                _activeThemeEnabled = _activeTheme != null;
                SaveSettings();
                UpdateActiveThemeLabel();

                if (!quiet)
                {
                    SetStatus("Applied " + theme.Name + " to ChatGPT.", false);
                }
            }
            catch (Exception ex)
            {
                SetStatus(SafeExceptionMessage(ex), true);
            }
            finally
            {
                _applying = false;
            }
        }

        private async Task RestoreActiveThemeOnStartupAsync()
        {
            UpdateActiveThemeLabel();
            if (!_activeThemeEnabled || _activeTheme == null)
            {
                return;
            }

            SetStatus("Active theme loaded: " + ActiveThemeDisplayName() + ". Restoring ChatGPT theme...", false);
            await ApplySkinAsync(false);
        }

        private async Task TestPipeAsync(bool quiet)
        {
            try
            {
                if (_pipeConnection == null || !_pipeConnection.IsUsable)
                {
                    var detection = await Task.Run(delegate { return DetectChatGpt(); });
                    UpdateChatGptDetectionUi(detection);
                    if (detection.Found)
                    {
                        SetStatus("ChatGPT is open normally, not on GPT Themes' private pipe. Use Relaunch with Pipe.", true);
                    }
                    else
                    {
                        SetStatus("No private pipe is connected. Open ChatGPT with Relaunch with Pipe first.", true);
                    }
                    return;
                }

                var targets = await GetPipeTargetsAsync();
                if (targets.Count == 0)
                {
                    SetStatus("The private pipe is open, but no ChatGPT page target was found yet.", true);
                    return;
                }

                if (!quiet)
                {
                    SetStatus("Connected to ChatGPT through the private pipe (" + targets.Count + " target(s)).", false);
                }
            }
            catch (Exception ex)
            {
                SetStatus(SafeExceptionMessage(ex), true);
            }
        }

        private async Task OpenWithChatGptAsync()
        {
            try
            {
                SetStatus("Opening ChatGPT with the private theming pipe...", false);
                var targets = await GetPipeTargetsAsync();
                if (targets.Count > 0)
                {
                    FocusChatGptWindow();
                    if (_activeThemeEnabled && _activeTheme != null)
                    {
                        await ApplySkinAsync(false);
                    }
                    else
                    {
                        SetStatus("ChatGPT is open on the private pipe. Apply a theme when ready.", false);
                    }
                    return;
                }
            }
            catch
            {
            }

            try
            {
                var targets = await StartOrRestartChatGptAndWaitAsync(true, "Opening ChatGPT with the private theming pipe");
                FocusChatGptWindow();
                if (targets.Count == 0)
                {
                    SetStatus("ChatGPT opened, but the private theming pipe is not ready. Use Relaunch with Pipe if Apply cannot connect.", true);
                    return;
                }

                if (_activeThemeEnabled && _activeTheme != null)
                {
                    await ApplySkinAsync(false);
                    return;
                }

                SetStatus("ChatGPT is open on the private pipe. Apply a theme when ready.", false);
            }
            catch (Exception ex)
            {
                SetStatus("Could not open ChatGPT: " + SafeExceptionMessage(ex), true);
            }
        }

        private async Task RelaunchChatGptWithPipeAsync()
        {
            DialogResult answer = MessageBox.Show(
                this,
                "This will close the running ChatGPT desktop app and reopen it with a private skinning pipe.",
                "Relaunch ChatGPT?",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);

            if (answer != DialogResult.OK) return;

            try
            {
                var targets = await StartOrRestartChatGptAndWaitAsync(true, "Relaunching ChatGPT with the private skinning pipe");
                FocusChatGptWindow();
                SetStatus(targets.Count > 0
                    ? "ChatGPT is ready on the private pipe. Apply a skin when ready."
                    : "ChatGPT relaunched, but the private skinning pipe is not ready yet. Try Apply again after it finishes loading.", targets.Count == 0);
            }
            catch (Exception ex)
            {
                SetStatus("Could not relaunch ChatGPT: " + SafeExceptionMessage(ex), true);
            }
        }

        private async Task<List<CdpTarget>> StartOrRestartChatGptAndWaitAsync(bool restartRunning, string statusPrefix)
        {
            return await StartOrRestartChatGptAndWaitAsync(restartRunning, statusPrefix, 28);
        }

        private async Task<List<CdpTarget>> StartOrRestartChatGptAndWaitAsync(bool restartRunning, string statusPrefix, int waitAttempts)
        {
            ChatGptDetectionResult detection = DetectChatGpt();
            UpdateChatGptDetectionUi(detection);
            string preferredExePath = detection.Found ? detection.ExePath : TryFindChatGptExePath();
            bool chatGptRunning = CountValidatedChatGptProcesses() > 0;
            if (restartRunning && chatGptRunning)
            {
                SetStatus("Restarting ChatGPT with the private skinning pipe...", false);
                ClosePipeConnection();
                StopChatGptProcesses();
            }

            string launchMethod;
            StartChatGptWithPipe(preferredExePath, out launchMethod);
            SetStatus(statusPrefix + " using " + launchMethod + "...", false);

            for (int i = 0; i < waitAttempts; i++)
            {
                await Task.Delay(500);
                try
                {
                    var targets = await GetPipeTargetsAsync();
                    if (targets.Count > 0)
                    {
                        if (!FocusChatGptWindow())
                        {
                            await WaitForChatGptWindowAsync(12, 250);
                            FocusChatGptWindow();
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
            foreach (var process in Process.GetProcessesByName("ChatGPT"))
            {
                try
                {
                    bool validated = false;
                    try
                    {
                        validated = IsValidatedChatGptProcess(process);
                    }
                    catch
                    {
                    }

                    if (!validated && !String.Equals(process.ProcessName, "ChatGPT", StringComparison.OrdinalIgnoreCase)) continue;
                    process.Kill();
                    process.WaitForExit(2500);
                }
                catch
                {
                }
            }

            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (CountValidatedChatGptProcesses() == 0) return;
                }
                catch
                {
                    return;
                }
                Thread.Sleep(150);
            }
        }

        private static int CountValidatedChatGptProcesses()
        {
            int count = 0;
            foreach (var process in Process.GetProcessesByName("ChatGPT"))
            {
                try
                {
                    if (IsValidatedChatGptProcess(process)) count++;
                }
                catch
                {
                }
            }
            return count;
        }

        private static bool IsValidatedChatGptProcess(Process process)
        {
            if (process == null) return false;
            try
            {
                return IsChatGptExecutablePath(process.MainModule.FileName);
            }
            catch
            {
                return false;
            }
        }

        private void StartChatGptWithPipe(string preferredExePath, out string launchMethod)
        {
            ClosePipeConnection();

            var errors = new List<string>();
            string exePath = TryFindPipeLaunchableChatGptExePath(preferredExePath);
            try
            {
                if (String.IsNullOrEmpty(exePath))
                {
                    string fallback = FindChatGptExePath();
                    exePath = fallback;
                }
            }
            catch (Exception ex)
            {
                errors.Add("installed EXE lookup: " + ex.Message);
            }

            if (String.IsNullOrEmpty(exePath))
            {
                throw new InvalidOperationException("ChatGPT Desktop was not found. Install ChatGPT Desktop, open it once, or use Choose ChatGPT.exe to select the real executable.");
            }
            else
            {
                try
                {
                    string directory = Path.GetDirectoryName(exePath);
                    _pipeConnection = CdpPipeConnection.Start(exePath, directory, _json);
                    launchMethod = "ChatGPT.exe private pipe";
                    return;
                }
                catch (Exception ex)
                {
                    errors.Add("private pipe launch: " + ex.Message);
                    ClosePipeConnection();
                }
            }

            errors.Add("Windows app activation cannot carry inherited DevTools pipe handles.");
            errors.Add("chatgpt.exe app aliases cannot carry inherited DevTools pipe handles.");
            throw new InvalidOperationException("Could not start ChatGPT with a private DevTools pipe. " + String.Join(" | ", errors.ToArray()));
        }

        private string TryFindPipeLaunchableChatGptExePath(string preferredExePath)
        {
            if (IsChatGptExecutablePath(preferredExePath))
            {
                return preferredExePath;
            }

            if (IsChatGptExecutablePath(_manualChatGptExePath))
            {
                return _manualChatGptExePath;
            }

            string localPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ChatGPT", "ChatGPT.exe");
            if (IsChatGptExecutablePath(localPrograms)) return localPrograms;

            foreach (var process in Process.GetProcessesByName("ChatGPT"))
            {
                try
                {
                    string path = process.MainModule.FileName;
                    if (IsChatGptExecutablePath(path))
                    {
                        return path;
                    }
                }
                catch
                {
                }
            }

            return "";
        }

        private void ClosePipeConnection()
        {
            if (_pipeConnection == null) return;
            try
            {
                _pipeConnection.Dispose();
            }
            catch
            {
            }
            _pipeConnection = null;
        }

        private void OpenChatGptNormally(string preferredAppId, out string launchMethod)
        {
            var errors = new List<string>();
            if (TryActivateChatGptPackage(preferredAppId, "", "Windows app activation", errors, out launchMethod))
            {
                return;
            }

            try
            {
                var packageInfo = new ProcessStartInfo();
                packageInfo.FileName = "explorer.exe";
                packageInfo.Arguments = "shell:AppsFolder\\" + PreferredChatGptAppUserModelId(preferredAppId);
                packageInfo.UseShellExecute = false;
                Process.Start(packageInfo);
                launchMethod = "Windows app entry";
                return;
            }
            catch (Exception ex)
            {
                errors.Add("Windows app entry: " + ex.Message);
            }

            try
            {
                var aliasInfo = new ProcessStartInfo();
                aliasInfo.FileName = "chatgpt.exe";
                aliasInfo.UseShellExecute = true;
                Process.Start(aliasInfo);
                launchMethod = "chatgpt.exe alias";
                return;
            }
            catch (Exception ex)
            {
                errors.Add("app alias: " + ex.Message);
            }

            try
            {
                string exePath = TryFindChatGptExePath();
                if (!String.IsNullOrEmpty(exePath) && !IsWindowsAppsPath(exePath))
                {
                    var startInfo = new ProcessStartInfo();
                    startInfo.FileName = exePath;
                    startInfo.UseShellExecute = false;
                    string directory = Path.GetDirectoryName(exePath);
                    if (!String.IsNullOrEmpty(directory) && Directory.Exists(directory))
                    {
                        startInfo.WorkingDirectory = directory;
                    }
                    Process.Start(startInfo);
                    launchMethod = "ChatGPT.exe";
                    return;
                }
            }
            catch (Exception ex)
            {
                errors.Add("installed EXE: " + ex.Message);
            }

            throw new InvalidOperationException(String.Join(" | ", errors.ToArray()));
        }

        private bool TryActivateChatGptPackage(string preferredAppId, string arguments, string methodName, List<string> errors, out string launchMethod)
        {
            launchMethod = "";
            try
            {
                string appId = PreferredChatGptAppUserModelId(preferredAppId);
                var manager = (IApplicationActivationManager)new ApplicationActivationManager();
                int processId;
                int hr = manager.ActivateApplication(appId, arguments ?? "", ActivateOptions.None, out processId);
                if (hr >= 0)
                {
                    launchMethod = methodName;
                    return true;
                }

                errors.Add(methodName + ": activation failed 0x" + unchecked((uint)hr).ToString("X8"));
            }
            catch (Exception ex)
            {
                errors.Add(methodName + ": " + ex.Message);
            }

            return false;
        }

        private string PreferredChatGptAppUserModelId(string preferredAppId)
        {
            if (!String.IsNullOrWhiteSpace(preferredAppId)) return preferredAppId;
            return ChatGptPackageName + "_" + ChatGptPublisherId + "!ChatGPT";
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
                foreach (var process in Process.GetProcessesByName("ChatGPT"))
                {
                    try
                    {
                        process.Refresh();
                        IntPtr handle = process.MainWindowHandle;
                        if (handle == IntPtr.Zero) continue;
                        ShowWindow(handle, SwRestore);
                        SetForegroundWindow(handle);
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
                if (FocusChatGptWindow()) return true;
                await Task.Delay(delayMs);
            }

            return false;
        }

        private string FindChatGptAppUserModelId()
        {
            string packageFamily = ChatGptPackageName + "_" + ChatGptPublisherId;
            try
            {
                foreach (var process in Process.GetProcessesByName("ChatGPT"))
                {
                    try
                    {
                        string path = process.MainModule.FileName;
                        string parsed = PackageFamilyFromWindowsAppsPath(path);
                        if (!String.IsNullOrEmpty(parsed)) return parsed + "!ChatGPT";
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return packageFamily + "!ChatGPT";
        }

        private static string AppUserModelIdFromPath(string path)
        {
            string family = PackageFamilyFromWindowsAppsPath(path);
            return String.IsNullOrEmpty(family) ? "" : family + "!ChatGPT";
        }

        private static string PackageFamilyFromWindowsAppsPath(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return "";
            string normalized = path.Replace('/', '\\');
            string marker = "\\WindowsApps\\";
            int start = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return "";
            start += marker.Length;
            int end = normalized.IndexOf("\\", start, StringComparison.OrdinalIgnoreCase);
            if (end <= start) return "";
            string packageFullName = normalized.Substring(start, end - start);
            int firstUnderscore = packageFullName.IndexOf('_');
            int doubleUnderscore = packageFullName.LastIndexOf("__", StringComparison.Ordinal);
            if (firstUnderscore <= 0 || doubleUnderscore < 0 || doubleUnderscore + 2 >= packageFullName.Length) return "";
            return packageFullName.Substring(0, firstUnderscore) + "_" + packageFullName.Substring(doubleUnderscore + 2);
        }

        private string FindChatGptExePath()
        {
            if (IsChatGptExecutablePath(_manualChatGptExePath))
            {
                return _manualChatGptExePath;
            }

            foreach (var process in Process.GetProcessesByName("ChatGPT"))
            {
                try
                {
                    string path = process.MainModule.FileName;
                    if (IsChatGptExecutablePath(path))
                    {
                        return path;
                    }
                }
                catch
                {
                }
            }

            string windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            string localPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ChatGPT", "ChatGPT.exe");
            if (IsChatGptExecutablePath(localPrograms)) return localPrograms;

            if (Directory.Exists(windowsApps))
            {
                try
                {
                    var candidates = Directory.GetDirectories(windowsApps, "OpenAI.ChatGPT-Desktop_*")
                        .OrderByDescending(path => path)
                        .Select(path => Path.Combine(path, "app", "ChatGPT.exe"));

                    foreach (string candidate in candidates)
                    {
                        if (IsChatGptExecutablePath(candidate)) return candidate;
                    }
                }
                catch
                {
                }
            }

            throw new InvalidOperationException("Could not find a real ChatGPT.exe. Open ChatGPT from the Start menu once, then try Relaunch with Pipe again.");
        }

        private static bool IsChatGptExecutablePath(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return false;
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                return false;
            }

            if (!File.Exists(fullPath)) return false;

            string fileName = Path.GetFileName(fullPath);
            if (!String.Equals(fileName, "ChatGPT.exe", StringComparison.OrdinalIgnoreCase)) return false;

            string normalized = fullPath.Replace('/', '\\');
            if (normalized.IndexOf("OpenAI.Codex", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (normalized.IndexOf("\\Codex", StringComparison.OrdinalIgnoreCase) >= 0) return false;

            if (IsExpectedPackagedChatGptPath(normalized)) return true;
            return HasTrustedChatGptSignature(fullPath);
        }

        private static bool IsExpectedPackagedChatGptPath(string normalizedPath)
        {
            if (String.IsNullOrWhiteSpace(normalizedPath)) return false;
            return normalizedPath.IndexOf("\\WindowsApps\\OpenAI.ChatGPT-Desktop_", StringComparison.OrdinalIgnoreCase) >= 0
                && normalizedPath.EndsWith("\\app\\ChatGPT.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasTrustedChatGptSignature(string path)
        {
            try
            {
                var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
                var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                if (!chain.Build(cert)) return false;

                string identity = (cert.Subject + " " +
                    cert.GetNameInfo(X509NameType.SimpleName, false) + " " +
                    cert.GetNameInfo(X509NameType.DnsName, false)).ToLowerInvariant();
                return identity.IndexOf("openai", StringComparison.Ordinal) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsWindowsAppsPath(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return false;
            string normalized = path.Replace('/', '\\');
            return normalized.IndexOf("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private async Task ClearSkinAsync()
        {
            _activeThemeEnabled = false;
            _activeTheme = null;
            _previewSkinEnabled = false;
            SaveSettings();
            UpdateActiveThemeLabel();
            if (_previewPanel != null) _previewPanel.Invalidate();

            try
            {
                var targets = await GetPipeTargetsAsync();
                if (targets.Count == 0)
                {
                    SetStatus("Default ChatGPT theme restored. No running ChatGPT target needed clearing.", false);
                    return;
                }

                string styleLiteral = _json.Serialize(StyleId);
                string expression = "(() => { if (window.__cgtdsResizeHandler) window.removeEventListener('resize', window.__cgtdsResizeHandler); window.clearTimeout(window.__cgtdsResizeTimer); if (window.__cgtdsSidebarObserver) window.__cgtdsSidebarObserver.disconnect(); window.clearTimeout(window.__cgtdsSidebarTimer); const s = document.getElementById(" + styleLiteral + "); if (s) s.remove(); document.querySelectorAll('[data-cgtds-surface-fix]').forEach((el) => { ['background','background-color','background-image','background-position','background-repeat','background-size','border','border-color','border-style','border-width','border-radius','box-shadow','outline','overflow','color','pointer-events','position','isolation','z-index','mask-image','-webkit-mask-image','backdrop-filter','-webkit-backdrop-filter','--tw-ring-color','--tw-ring-shadow','--tw-shadow','--tw-shadow-colored'].forEach((name) => el.style.removeProperty(name)); el.removeAttribute('data-cgtds-surface-fix'); }); delete window.__cgtdsSidebarObserver; delete document.documentElement.dataset.chatgptDesktopSkinner; return true; })()";
                foreach (var target in targets)
                {
                    Dictionary<string, object> response = await _pipeConnection.EvaluateAsync(target.TargetId, expression);
                    ThrowIfEvaluateFailed(response);
                }
                SetStatus("Default ChatGPT theme restored.", false);
            }
            catch (Exception ex)
            {
                SetStatus("Default ChatGPT theme restored for future sessions. Current ChatGPT session was not reachable: " + SafeExceptionMessage(ex), false);
            }
        }

        private async Task<List<CdpTarget>> GetPipeTargetsAsync()
        {
            if (_pipeConnection == null || !_pipeConnection.IsUsable)
            {
                throw new InvalidOperationException("ChatGPT is not connected to GPT Themes through the private DevTools pipe. Use Relaunch with Pipe.");
            }

            var result = new List<CdpTarget>();
            List<CdpTarget> targets = await _pipeConnection.GetTargetsAsync();
            foreach (CdpTarget target in targets)
            {
                if (target == null) continue;
                string targetUrl = target.Url ?? "";
                if (targetUrl.StartsWith("devtools:", StringComparison.OrdinalIgnoreCase)) continue;
                if (target.Type != "page" && target.Type != "webview" && target.Type != "iframe") continue;
                if (!IsChatGptTargetUrl(targetUrl)) continue;
                if (String.IsNullOrWhiteSpace(target.TargetId)) continue;
                result.Add(target);
            }

            return result;
        }

        private static bool IsChatGptTargetUrl(string targetUrl)
        {
            if (String.IsNullOrWhiteSpace(targetUrl)) return false;
            Uri uri;
            if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out uri)) return false;
            if (!String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
            string host = uri.Host ?? "";
            return String.Equals(host, "chatgpt.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".chatgpt.com", StringComparison.OrdinalIgnoreCase);
        }

        private void ThrowIfEvaluateFailed(Dictionary<string, object> response)
        {
            if (response == null) return;
            if (response.ContainsKey("error"))
            {
                var error = response["error"] as Dictionary<string, object>;
                string message = error == null ? Convert.ToString(response["error"]) : GetString(error, "message");
                throw new InvalidOperationException("ChatGPT rejected the skin update: " + NonEmpty(message, "unknown DevTools error"));
            }

            var result = response.ContainsKey("result") ? response["result"] as Dictionary<string, object> : null;
            var exceptionDetails = result != null && result.ContainsKey("exceptionDetails")
                ? result["exceptionDetails"] as Dictionary<string, object>
                : null;
            if (exceptionDetails != null)
            {
                string text = GetString(exceptionDetails, "text");
                var exception = exceptionDetails.ContainsKey("exception")
                    ? exceptionDetails["exception"] as Dictionary<string, object>
                    : null;
                string description = exception == null ? "" : GetString(exception, "description");
                throw new InvalidOperationException("ChatGPT could not apply the skin: " + NonEmpty(description, NonEmpty(text, "JavaScript evaluation failed")));
            }
        }

        private string BuildInjectionExpression(string css)
        {
            string cssLiteral = _json.Serialize(css);
            string styleLiteral = _json.Serialize(StyleId);
            string script = @"
(() => {
  const css = __CSS__;
  const styleId = __STYLE_ID__;
  if (window.__cgtdsSidebarObserver) {
    window.__cgtdsSidebarObserver.disconnect();
  }
  window.clearTimeout(window.__cgtdsSidebarTimer);
  const clearFixes = () => {
    const cleaned = document.querySelectorAll('[data-cgtds-surface-fix]');
    cleaned.forEach((el) => {
      ['background','background-color','background-image','background-position','background-repeat','background-size','border','border-color','border-style','border-width','border-radius','box-shadow','outline','overflow','color','pointer-events','position','isolation','z-index','mask-image','-webkit-mask-image','backdrop-filter','-webkit-backdrop-filter','--tw-ring-color','--tw-ring-shadow','--tw-shadow','--tw-shadow-colored'].forEach((name) => el.style.removeProperty(name));
      el.removeAttribute('data-cgtds-surface-fix');
    });
    return cleaned.length;
  };
  let cleanedCount = 0;
  let style = document.getElementById(styleId);
  if (!style) {
    style = document.createElement('style');
    style.id = styleId;
    style.dataset.owner = 'chatgpt-desktop-skinner';
    (document.head || document.documentElement).appendChild(style);
  }
  style.textContent = css;
  document.documentElement.dataset.chatgptDesktopSkinner = 'enabled';
  cleanedCount += clearFixes();
  if (window.__cgtdsResizeHandler) {
    window.removeEventListener('resize', window.__cgtdsResizeHandler);
  }
  window.clearTimeout(window.__cgtdsResizeTimer);
  window.__cgtdsResizeHandler = () => {
    window.clearTimeout(window.__cgtdsResizeTimer);
    window.__cgtdsResizeTimer = window.setTimeout(() => {
      const currentStyle = document.getElementById(styleId);
      if (currentStyle && currentStyle.textContent !== css) currentStyle.textContent = css;
    }, 220);
  };
  window.addEventListener('resize', window.__cgtdsResizeHandler, { passive: true });
  return { ok: true, fixed: 0, cleaned: cleanedCount, title: document.title, href: location.href };
  const cssCustomValue = (name) => {
    const escapedName = name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const match = css.match(new RegExp(escapedName + '\\s*:\\s*([\\s\\S]*?)\\s*!important\\s*;'));
    return match ? match[1].trim() : '';
  };
  const panelBackgroundValue = cssCustomValue('--cgtds-panel-background') || 'var(--cgtds-panel-background)';

  const nearDark = (value) => {
    const m = String(value || '').match(/rgba?\((\d+),\s*(\d+),\s*(\d+)(?:,\s*([\d.]+))?\)/);
    if (!m) return false;
    const a = m[4] == null ? 1 : Number(m[4]);
    return a > 0.72 && Number(m[1]) < 48 && Number(m[2]) < 48 && Number(m[3]) < 48;
  };
  const maxRadius = (style) => Math.max(
    parseFloat(style.borderTopLeftRadius) || 0,
    parseFloat(style.borderTopRightRadius) || 0,
    parseFloat(style.borderBottomRightRadius) || 0,
    parseFloat(style.borderBottomLeftRadius) || 0
  );
  const hasVisibleBorder = (style) => (
    (parseFloat(style.borderTopWidth) || 0) +
    (parseFloat(style.borderRightWidth) || 0) +
    (parseFloat(style.borderBottomWidth) || 0) +
    (parseFloat(style.borderLeftWidth) || 0)
  ) > 0;
  const classText = (el) => String(el.className && typeof el.className === 'string' ? el.className : '').toLowerCase();
  const inputSelector = 'input, textarea, [contenteditable]:not([contenteditable=false]), [role=textbox], [role=searchbox]';
  const sidebarSurfaceSelector = 'aside, nav, [class*=sidebar], [class*=bg-token-sidebar]';
  const isLikelySidebarSurface = (el) => {
    if (!el || !el.matches) return false;
    const classes = classText(el);
    if (el.matches('aside, [class*=sidebar], [class*=bg-token-sidebar]')) return true;
    if (!el.matches('nav')) return false;
    const rect = el.getBoundingClientRect();
    if (!rect || rect.width <= 0 || rect.height <= 0) return false;
    const leftDocked = rect.left <= 4 || rect.right <= Math.min(420, window.innerWidth * 0.42);
    const tallEnough = rect.height >= Math.max(220, window.innerHeight * 0.45);
    return leftDocked && tallEnough && !/(toolbar|header|topbar|menubar|breadcrumb|tab)/.test(classes);
  };
  const closestSidebarSurface = (el) => {
    const surface = el && el.closest ? el.closest(sidebarSurfaceSelector) : null;
    return isLikelySidebarSurface(surface) ? surface : null;
  };
  const mark = (el) => { if (el) el.dataset.cgtdsSurfaceFix = 'true'; };
  const flat = (el) => {
    if (!el) return;
    el.style.setProperty('background', 'transparent', 'important');
    el.style.setProperty('background-color', 'transparent', 'important');
    el.style.setProperty('background-image', 'none', 'important');
    el.style.setProperty('mask-image', 'none', 'important');
    el.style.setProperty('-webkit-mask-image', 'none', 'important');
    el.style.setProperty('border', '0 none transparent', 'important');
    el.style.setProperty('border-color', 'transparent', 'important');
    el.style.setProperty('border-style', 'none', 'important');
    el.style.setProperty('border-width', '0px', 'important');
    el.style.setProperty('box-shadow', 'none', 'important');
    el.style.setProperty('outline', 'none', 'important');
    el.style.setProperty('--tw-ring-color', 'transparent', 'important');
    el.style.setProperty('--tw-ring-shadow', '0 0 #0000', 'important');
    el.style.setProperty('--tw-shadow', '0 0 #0000', 'important');
    el.style.setProperty('--tw-shadow-colored', '0 0 #0000', 'important');
    mark(el);
  };
  const stripBox = (el) => {
    if (!el) return;
    flat(el);
    el.style.setProperty('border', '0 none transparent', 'important');
    el.style.setProperty('border-color', 'transparent', 'important');
    el.style.setProperty('border-style', 'none', 'important');
    el.style.setProperty('border-width', '0px', 'important');
    el.style.setProperty('border-radius', '0px', 'important');
    el.style.setProperty('box-shadow', 'none', 'important');
    el.style.setProperty('outline', 'none', 'important');
  };
  const makePill = (el) => {
    if (!el) return;
    el.style.setProperty('background', 'var(--cgtds-composer-background)', 'important');
    el.style.setProperty('background-color', 'var(--composer-surface)', 'important');
    el.style.setProperty('color', 'var(--text-primary)', 'important');
    el.style.setProperty('border', '0 none transparent', 'important');
    el.style.setProperty('border-color', 'transparent', 'important');
    el.style.setProperty('border-style', 'none', 'important');
    el.style.setProperty('border-width', '0px', 'important');
    el.style.setProperty('border-radius', '999px', 'important');
    el.style.setProperty('box-shadow', 'none', 'important');
    el.style.setProperty('backdrop-filter', 'var(--cgtds-composer-blur)', 'important');
    el.style.setProperty('-webkit-backdrop-filter', 'var(--cgtds-composer-blur)', 'important');
    el.style.setProperty('outline', 'none', 'important');
    el.style.setProperty('overflow', 'hidden', 'important');
    el.querySelectorAll('button, [role=""button""], input, textarea, [contenteditable], [role=""textbox""], [role=""searchbox""], span, div, label').forEach((child) => {
      if (skip.has(child.tagName)) return;
      child.style.setProperty('background', 'transparent', 'important');
      child.style.setProperty('background-color', 'transparent', 'important');
      child.style.setProperty('background-image', 'none', 'important');
      child.style.setProperty('color', 'var(--text-primary)', 'important');
      child.style.setProperty('border', '0 none transparent', 'important');
      child.style.setProperty('box-shadow', 'none', 'important');
      child.style.setProperty('outline', 'none', 'important');
      child.style.setProperty('--tw-ring-color', 'transparent', 'important');
      child.style.setProperty('--tw-ring-shadow', '0 0 #0000', 'important');
    });
    mark(el);
  };
  const makeSearchPill = (el) => {
    if (!el) return;
    el.style.setProperty('background', 'var(--cgtds-search-background)', 'important');
    el.style.setProperty('background-color', 'var(--cgtds-search-surface)', 'important');
    el.style.setProperty('color', 'var(--text-primary)', 'important');
    el.style.setProperty('border', '0 none transparent', 'important');
    el.style.setProperty('border-color', 'transparent', 'important');
    el.style.setProperty('border-style', 'none', 'important');
    el.style.setProperty('border-width', '0px', 'important');
    el.style.setProperty('border-radius', '999px', 'important');
    el.style.setProperty('box-shadow', 'none', 'important');
    el.style.setProperty('backdrop-filter', 'var(--cgtds-search-blur)', 'important');
    el.style.setProperty('-webkit-backdrop-filter', 'var(--cgtds-search-blur)', 'important');
    el.style.setProperty('outline', 'none', 'important');
    el.style.setProperty('overflow', 'hidden', 'important');
    el.querySelectorAll('input, textarea, [contenteditable], [role=""textbox""], [role=""searchbox""], span, div').forEach((child) => {
      if (skip.has(child.tagName)) return;
      child.style.setProperty('background', 'transparent', 'important');
      child.style.setProperty('background-color', 'transparent', 'important');
      child.style.setProperty('color', 'var(--text-primary)', 'important');
      child.style.setProperty('border', '0 none transparent', 'important');
      child.style.setProperty('box-shadow', 'none', 'important');
      child.style.setProperty('outline', 'none', 'important');
    });
    mark(el);
  };
  const makePanelSurface = (el) => {
    if (!el) return;
    el.style.setProperty('background', panelBackgroundValue, 'important');
    el.style.setProperty('background-position', 'center', 'important');
    el.style.setProperty('background-repeat', 'no-repeat, no-repeat, no-repeat', 'important');
    el.style.setProperty('background-size', 'cover, cover, auto', 'important');
    el.style.setProperty('background-position', 'center, center, center', 'important');
    el.style.setProperty('color', 'var(--text-primary)', 'important');
    el.style.setProperty('border', '0 none transparent', 'important');
    el.style.setProperty('border-color', 'transparent', 'important');
    el.style.setProperty('border-style', 'none', 'important');
    el.style.setProperty('border-width', '0px', 'important');
    el.style.setProperty('border-right', '0 none transparent', 'important');
    el.style.setProperty('border-left', '0 none transparent', 'important');
    el.style.setProperty('border-radius', '0px', 'important');
    el.style.setProperty('box-shadow', 'none', 'important');
    el.style.setProperty('outline', 'none', 'important');
    el.style.setProperty('position', 'relative', 'important');
    el.style.setProperty('isolation', 'isolate', 'important');
    el.style.setProperty('backdrop-filter', 'var(--cgtds-panel-blur)', 'important');
    el.style.setProperty('-webkit-backdrop-filter', 'var(--cgtds-panel-blur)', 'important');
    el.querySelectorAll(':scope > div').forEach((child) => {
      child.style.setProperty('background-color', 'transparent', 'important');
      child.style.setProperty('background-image', 'none', 'important');
      child.style.setProperty('box-shadow', 'none', 'important');
      child.style.setProperty('border-color', 'transparent', 'important');
      child.style.setProperty('position', 'relative', 'important');
      child.style.setProperty('z-index', '1', 'important');
      mark(child);
    });
    mark(el);
  };
  const sidebarRowSelector = 'aside a, aside button, aside [role=button], aside [role=link], nav a, nav button, nav [role=button], nav [role=link]';
  const rowLooksSelected = (el) => {
    if (!el) return false;
    const classes = classText(el);
    return el.hasAttribute('aria-current') ||
      el.getAttribute('aria-selected') === 'true' ||
      el.getAttribute('data-active') === 'true' ||
      el.getAttribute('data-selected') === 'true' ||
      /(selected|active|current|bg-token-sidebar-surface-secondary|bg-token-sidebar-surface-tertiary)/.test(classes);
  };
  const isSidebarActionButton = (el) => {
    if (!el || !el.matches || !el.matches('button,[role=button]')) return false;
    if (el.matches('[aria-label=""Search chats""]')) return false;
    const rect = el.getBoundingClientRect();
    const copy = (el.innerText || el.textContent || '').trim();
    const hasIcon = !!el.querySelector('svg');
    const compact = rect && rect.width > 0 && rect.height > 0 && rect.width <= 76 && rect.height <= 46;
    const labelledIcon = !!(el.getAttribute('aria-label') || el.getAttribute('title')) && hasIcon;
    return hasIcon && compact && (copy.length === 0 || labelledIcon);
  };
  const fixSidebarRows = () => {
    let count = 0;
    document.querySelectorAll(sidebarRowSelector).forEach((row) => {
      const inSidebar = !!closestSidebarSurface(row);
      if (!inSidebar) return;
      const actionButton = isSidebarActionButton(row);
      row.style.setProperty('border', '0 none transparent', 'important');
      row.style.setProperty('border-color', 'transparent', 'important');
      row.style.setProperty('box-shadow', 'none', 'important');
      row.style.setProperty('outline', 'none', 'important');
      row.style.setProperty('color', 'var(--text-primary)', 'important');
      row.style.setProperty('pointer-events', 'auto', 'important');
      row.style.setProperty('position', 'relative', 'important');
      row.style.setProperty('z-index', '2', 'important');
      row.style.setProperty('--tw-ring-color', 'transparent', 'important');
      row.style.setProperty('--tw-ring-shadow', '0 0 #0000', 'important');
      row.style.setProperty('--tw-shadow', '0 0 #0000', 'important');
      row.style.setProperty('--tw-shadow-colored', '0 0 #0000', 'important');
      if (actionButton) {
        row.style.removeProperty('background');
        row.style.removeProperty('background-color');
        row.style.removeProperty('background-image');
        row.style.setProperty('border-radius', '8px', 'important');
        row.style.setProperty('isolation', 'isolate', 'important');
      } else if (rowLooksSelected(row)) {
        row.style.setProperty('background', 'var(--cgtds-sidebar-selected-background)', 'important');
        row.style.setProperty('background-color', 'var(--cgtds-sidebar-selected-surface)', 'important');
      } else {
        row.style.removeProperty('background');
        row.style.removeProperty('background-color');
        row.style.removeProperty('background-image');
      }
      row.querySelectorAll('div, span, p').forEach((child) => {
        if (skip.has(child.tagName)) return;
        const childStyle = getComputedStyle(child);
        const childClasses = classText(child);
        if (nearDark(childStyle.backgroundColor) || /(bg-black|bg-gray-8|bg-gray-9|bg-neutral-8|bg-neutral-9|bg-\[|dark:bg)/.test(childClasses)) {
          child.style.setProperty('background', 'transparent', 'important');
          child.style.setProperty('background-color', 'transparent', 'important');
          child.style.setProperty('background-image', 'none', 'important');
        }
        child.style.setProperty('border-color', 'transparent', 'important');
        child.style.setProperty('box-shadow', 'none', 'important');
        child.style.setProperty('outline', 'none', 'important');
        child.style.setProperty('color', 'var(--text-primary)', 'important');
        child.style.setProperty('pointer-events', 'auto', 'important');
      });
      mark(row);
      count++;
    });
    return count;
  };
  const flattenPillChildren = (pill) => {
    if (!pill || !pill.querySelectorAll) return;
    pill.querySelectorAll('div, span, label').forEach((child) => {
      if (child === pill) return;
      const cr = child.getBoundingClientRect();
      if (!cr || cr.width <= 0 || cr.height <= 0) return;
      child.style.setProperty('border', '0 none transparent', 'important');
      child.style.setProperty('border-color', 'transparent', 'important');
      child.style.setProperty('border-style', 'none', 'important');
      child.style.setProperty('border-width', '0px', 'important');
      child.style.setProperty('border-radius', '0px', 'important');
      child.style.setProperty('box-shadow', 'none', 'important');
      child.style.setProperty('outline', 'none', 'important');
      child.style.setProperty('--tw-ring-color', 'transparent', 'important');
      child.style.setProperty('--tw-ring-shadow', '0 0 #0000', 'important');
      child.style.setProperty('--tw-shadow', '0 0 #0000', 'important');
      child.style.setProperty('--tw-shadow-colored', '0 0 #0000', 'important');
      child.style.setProperty('backdrop-filter', 'none', 'important');
      child.style.setProperty('-webkit-backdrop-filter', 'none', 'important');
      child.style.setProperty('background', 'transparent', 'important');
      child.style.setProperty('background-color', 'transparent', 'important');
      child.style.setProperty('background-image', 'none', 'important');
      child.style.setProperty('color', 'var(--text-primary)', 'important');
      mark(child);
    });
    pill.querySelectorAll(inputSelector).forEach(stripBox);
  };
  const skip = new Set(['SELECT','OPTION','SVG','PATH','IMG','VIDEO','CANVAS','PRE','CODE']);
  const applySurfaceFixes = () => {
  cleanedCount += clearFixes();
  let fixed = 0;

  document.querySelectorAll(sidebarSurfaceSelector).forEach((el) => {
    if (isLikelySidebarSurface(el)) makePanelSurface(el);
  });
  fixed += fixSidebarRows();
  document.querySelectorAll('footer, [class*=bottom-0], [class*=from-black], [class*=via-black], [class*=to-black], [class*=to-transparent], [class*=bg-gradient], [class*=gradient-to], [data-testid*=footer]').forEach((el) => {
    flat(el);
    el.style.setProperty('border-radius', '0px', 'important');
    fixed++;
  });

  document.querySelectorAll('body *').forEach((el) => {
    const rect = el.getBoundingClientRect();
    if (!rect || rect.width <= 0 || rect.height <= 0) return;
    const style = getComputedStyle(el);
    const classes = classText(el);
    const labelText = (el.getAttribute('aria-label') || el.getAttribute('placeholder') || '').trim().toLowerCase();
    const ownText = (el.textContent || '').trim().toLowerCase();
    const text = labelText || ownText.slice(0, 80);
    const isInput = el.matches(inputSelector);
    const inSidebar = !!closestSidebarSurface(el);
    const hasInput = !!(el.querySelector && el.querySelector(inputSelector));
    const bgImage = style.backgroundImage || '';
    const classLooksLikeOverlay = /(bottom-0|bottom-\[|from-black|via-black|to-black|to-transparent|bg-black|bg-gradient|bg-linear|gradient-to)/.test(classes);
    const hasGradient = bgImage !== 'none' || classLooksLikeOverlay;
    const isBottom = rect.bottom >= window.innerHeight - 8 || rect.top >= window.innerHeight - 190 || classes.indexOf('bottom-0') >= 0;
    const isWideBottomBand = isBottom && rect.width >= Math.max(320, window.innerWidth * 0.36) && rect.height >= 18 && rect.height <= Math.max(220, window.innerHeight * 0.36);
    const isFullWidthBottomBand = isBottom && rect.width >= window.innerWidth * 0.72 && rect.height <= 150;
    const actualControl = el.matches('button, ' + inputSelector) || skip.has(el.tagName);
    const editorChrome = el.matches('header, main, section, [role=main], [role=toolbar], [data-testid*=toolbar], [data-testid*=image], [data-testid*=canvas], [data-testid*=viewer]') || /(toolbar|workspace|viewer|canvas|editor|artifact|modal|popover|bg-token|bg-black|bg-neutral|bg-zinc|bg-stone|bg-gray|dark:bg|top-0|bottom-0)/.test(classes);
    if (inSidebar && !isInput) return;
    const largeChrome = !actualControl &&
      !isInput &&
      editorChrome &&
      rect.width >= Math.max(360, window.innerWidth * 0.42) &&
      rect.height >= Math.max(180, window.innerHeight * 0.28) &&
      (maxRadius(style) > 28 || hasVisibleBorder(style) || /(rounded|workspace|viewer|canvas|editor|artifact)/.test(classes));

    if (largeChrome) {
      stripBox(el);
      fixed++;
      return;
    }

    if (!actualControl && isWideBottomBand && (isFullWidthBottomBand || hasInput || nearDark(style.backgroundColor) || hasGradient)) {
      flat(el);
      el.style.setProperty('border-radius', '0px', 'important');
      fixed++;
      return;
    }

    if (!actualControl && !hasInput && editorChrome && rect.width >= 36 && rect.height >= 24 && (nearDark(style.backgroundColor) || hasGradient)) {
      flat(el);
      if (rect.width > 180 && rect.height > 36) el.style.setProperty('border-radius', '0px', 'important');
      fixed++;
      return;
    }

    const exactSearch = text === 'search chats' || text === 'search' || labelText === 'search chats' || labelText === 'search';
    const smallSearch = exactSearch && rect.width > 24 && rect.width < 430 && rect.height > 18 && rect.height <= 72;
    if (smallSearch) {
      let pill = el.closest('button,a,[role=button],[role=link],[aria-label=""Search chats""]') || el;
      const pr = pill.getBoundingClientRect();
      if (pr.height > 76 || pr.width > 430) pill = el;
      for (let p = pill.parentElement, i = 0; p && i < 3; p = p.parentElement, i++) {
        const ar = p.getBoundingClientRect();
        if (ar.width < 430 && ar.height < 82 && !p.matches('aside,nav')) stripBox(p);
      }
      makeSearchPill(pill);
      pill.querySelectorAll('*').forEach((child) => {
        if (!skip.has(child.tagName)) stripBox(child);
      });
      fixed++;
      return;
    }

    if (isInput && inSidebar) {
      stripBox(el);
      for (let p = el.parentElement, i = 0; p && i < 3; p = p.parentElement, i++) {
        const pr = p.getBoundingClientRect();
        if (pr.width < 430 && pr.height < 82 && !p.matches('aside,nav')) stripBox(p);
      }
      fixed++;
      return;
    }

    if (isInput) {
      stripBox(el);
      let p = el.parentElement;
      let pill = null;
      const maxPillWidth = Math.min(window.innerWidth - 96, Math.max(520, rect.width + 360));
      for (let i = 0; p && i < 8; i++, p = p.parentElement) {
        if (p.matches('main,[role=main],body,html')) break;
        const pr = p.getBoundingClientRect();
        if (pr.width >= window.innerWidth * 0.72 && pr.height <= 150) {
          stripBox(p);
          continue;
        }
        if (pr.width > rect.width + 20 && pr.width <= maxPillWidth && pr.height > rect.height + 8 && pr.height <= 112) {
          pill = p;
          continue;
        }
        if (pr.height <= 112) stripBox(p);
      }
      if (pill) {
        makePill(pill);
        flattenPillChildren(pill);
      }
      fixed++;
      return;
    }

    if (skip.has(el.tagName)) return;
    if (rect.width < 120 || rect.height < 24) return;
    if (hasInput && rect.height <= 130) return;
    if (!editorChrome && !classLooksLikeOverlay && !/(bg-black|bg-gray|bg-neutral|bg-zinc|bg-stone|dark:bg|from-black|to-black|to-transparent)/.test(classes)) return;
    if (!nearDark(style.backgroundColor) && !hasGradient) return;
    flat(el);
    fixed++;
  });
  return fixed;
  };
  const fixed = applySurfaceFixes();
  if (window.__cgtdsResizeHandler) {
    window.removeEventListener('resize', window.__cgtdsResizeHandler);
  }
  if (window.__cgtdsSidebarObserver) {
    window.__cgtdsSidebarObserver.disconnect();
  }
  window.__cgtdsSidebarObserver = new MutationObserver(() => {
    window.clearTimeout(window.__cgtdsSidebarTimer);
    window.__cgtdsSidebarTimer = window.setTimeout(() => {
      try {
        fixSidebarRows();
      } catch (_) {
      }
    }, 180);
  });
  window.__cgtdsSidebarObserver.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class', 'aria-current', 'aria-selected', 'data-active', 'data-selected'] });
  window.__cgtdsResizeHandler = () => {
    window.clearTimeout(window.__cgtdsResizeTimer);
    window.__cgtdsResizeTimer = window.setTimeout(() => {
      try {
        const liveStyle = document.getElementById(styleId);
        if (liveStyle) liveStyle.textContent = css;
      } catch (_) {
      }
    }, 220);
  };
  window.addEventListener('resize', window.__cgtdsResizeHandler, { passive: true });
  return { ok: true, fixed, cleaned: cleanedCount, title: document.title, href: location.href };
})()";
            return script.Replace("__CSS__", cssLiteral).Replace("__STYLE_ID__", styleLiteral);
        }

        private string BuildCss(Theme theme, string layout, string backgroundMode, string backgroundValue, int transparency, string backgroundImageDataUrl, string panelImageDataUrl, bool glassSearch, string fontFamily)
        {
            string effectiveText = EnsureReadableText(theme.Text, theme.Bg);
            string muted = MixHex(effectiveText, theme.Panel, 0.38);
            // Keep Background and Panel independent. Bg owns page background; Panel owns sidebar/card surfaces.
            string sidebar = theme.Panel;
            double glass = Math.Max(0, Math.Min(75, transparency)) / 75.0;
            double panelAlpha = (String.Equals(theme.Bg, "#f4f7fb", StringComparison.OrdinalIgnoreCase) ? 0.94 : 0.84) - (glass * 0.72);
            double sidebarAlpha = 0.96 - (glass * 0.72);
            double inputAlpha = 0.92 - (glass * 0.68);
            double tokenAlpha = 0.88 - (glass * 0.58);
            double composerAlpha = 0.94 - (glass * 0.7);
            panelAlpha = Math.Max(0.12, panelAlpha);
            sidebarAlpha = Math.Max(0.16, sidebarAlpha);
            inputAlpha = Math.Max(0.16, inputAlpha);
            tokenAlpha = Math.Max(0.2, tokenAlpha);
            composerAlpha = Math.Max(0.18, composerAlpha);
            double searchAlpha = glassSearch
                ? Math.Max(IsLightColor(theme.Bg) ? 0.78 : 0.56, inputAlpha * 0.78)
                : inputAlpha;
            double searchBorderAlpha = glassSearch ? 0.24 : 0.14;
            string searchBackground = glassSearch
                ? "linear-gradient(135deg, " + CssRgb(theme.Input, searchAlpha) + ", " + CssRgb(theme.Panel, Math.Max(0.34, searchAlpha * 0.86)) + " 62%, " + CssRgb(theme.Accent, 0.10) + ")"
                : CssRgb(theme.Input, searchAlpha);
            string searchShadow = "none";
            string searchBlur = glassSearch ? "blur(" + Math.Max(12, (int)(glass * 24)) + "px) saturate(1.35)" : "none";
            string composerBackground = "linear-gradient(135deg, " + CssRgb(theme.Input, composerAlpha) + ", " + CssRgb(theme.Panel, Math.Max(0.24, composerAlpha * 0.72)) + " 52%, " + CssRgb(theme.Accent, 0.08) + ")";
            string composerBlur = glass > 0.02 ? "blur(" + Math.Max(10, (int)(glass * 26)) + "px) saturate(1.25)" : "none";
            string sidebarSelectedBackground = "linear-gradient(90deg, " + CssRgb(theme.Accent, 0.18) + ", " + CssRgb(theme.Input, Math.Max(0.56, tokenAlpha)) + ")";
            string buttonBackground = CssRgb(theme.Input, Math.Max(0.58, tokenAlpha));
            string buttonHoverBackground = CssRgb(theme.Accent, IsLightColor(theme.Bg) ? 0.18 : 0.14);
            string buttonActiveBackground = CssRgb(theme.Accent, IsLightColor(theme.Bg) ? 0.26 : 0.22);
            string sidebarActionHover = CssRgb(theme.Accent, IsLightColor(theme.Bg) ? 0.12 : 0.1);
            string background = BuildBackground(theme, backgroundMode, backgroundImageDataUrl, glass);
            string panelBackground = BuildPanelBackground(theme, sidebar, sidebarAlpha, panelImageDataUrl);
            string layoutCss = BuildLayoutCss(layout);
            string fontStack = BuildFontFamilyCss(fontFamily);

            var sb = new StringBuilder();
            sb.AppendLine(":root, html, body {");
            sb.AppendLine("  --text-primary: " + CssRgb(effectiveText) + " !important;");
            sb.AppendLine("  --text-secondary: " + CssRgb(muted) + " !important;");
            sb.AppendLine("  --text-tertiary: " + CssRgb(muted, 0.82) + " !important;");
            sb.AppendLine("  --text-quaternary: " + CssRgb(muted, 0.7) + " !important;");
            sb.AppendLine("  --token-text-primary: " + CssRgb(effectiveText) + " !important;");
            sb.AppendLine("  --token-text-secondary: " + CssRgb(muted) + " !important;");
            sb.AppendLine("  --token-text-tertiary: " + CssRgb(muted, 0.82) + " !important;");
            sb.AppendLine("  --main-surface-primary: " + CssRgb(theme.Panel, panelAlpha) + " !important;");
            sb.AppendLine("  --main-surface-secondary: " + CssRgb(theme.Input, tokenAlpha) + " !important;");
            sb.AppendLine("  --main-surface-tertiary: " + CssRgb(theme.Bg, Math.Max(0.22, 0.82 - (glass * 0.5))) + " !important;");
            sb.AppendLine("  --sidebar-surface-primary: " + CssRgb(sidebar, sidebarAlpha) + " !important;");
            sb.AppendLine("  --sidebar-surface-secondary: " + CssRgb(theme.Panel, Math.Max(0.2, 0.86 - (glass * 0.48))) + " !important;");
            sb.AppendLine("  --sidebar-surface-tertiary: " + CssRgb(theme.Input, Math.Max(0.24, 0.88 - (glass * 0.5))) + " !important;");
            sb.AppendLine("  --message-surface: " + CssRgb(theme.User, 0.9) + " !important;");
            sb.AppendLine("  --composer-surface: " + CssRgb(theme.Input, composerAlpha) + " !important;");
            sb.AppendLine("  --cgtds-composer-background: " + composerBackground + " !important;");
            sb.AppendLine("  --cgtds-composer-blur: " + composerBlur + " !important;");
            sb.AppendLine("  --cgtds-panel-background: " + panelBackground + " !important;");
            sb.AppendLine("  --cgtds-panel-blur: blur(" + (int)(glass * 20) + "px) saturate(1.2) !important;");
            sb.AppendLine("  --border-light: " + CssRgb(theme.Border, 0.42) + " !important;");
            sb.AppendLine("  --border-medium: " + CssRgb(theme.Border, 0.62) + " !important;");
            sb.AppendLine("  --border-heavy: " + CssRgb(theme.Border, 0.78) + " !important;");
            sb.AppendLine("  --cgtds-search-surface: " + CssRgb(theme.Input, searchAlpha) + " !important;");
            sb.AppendLine("  --cgtds-search-background: " + searchBackground + " !important;");
            sb.AppendLine("  --cgtds-search-border: " + CssRgb(theme.Border, searchBorderAlpha) + " !important;");
            sb.AppendLine("  --cgtds-search-shadow: " + searchShadow + " !important;");
            sb.AppendLine("  --cgtds-search-blur: " + searchBlur + " !important;");
            sb.AppendLine("  --cgtds-glass-strength: " + glass.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " !important;");
            sb.AppendLine("  --cgtds-button-background: " + buttonBackground + " !important;");
            sb.AppendLine("  --cgtds-button-hover-background: " + buttonHoverBackground + " !important;");
            sb.AppendLine("  --cgtds-button-active-background: " + buttonActiveBackground + " !important;");
            sb.AppendLine("  --cgtds-sidebar-action-hover: " + sidebarActionHover + " !important;");
            sb.AppendLine("  --cgtds-sidebar-selected-background: " + sidebarSelectedBackground + " !important;");
            sb.AppendLine("  --cgtds-sidebar-selected-surface: " + CssRgb(theme.Input, Math.Max(0.56, tokenAlpha)) + " !important;");
            sb.AppendLine("  --link: " + CssRgb(theme.Accent) + " !important;");
            sb.AppendLine("  --link-hover: " + CssRgb(theme.Accent, 0.88) + " !important;");
            if (!String.IsNullOrEmpty(fontStack))
            {
                sb.AppendLine("  --cgtds-font-family: " + fontStack + " !important;");
            }
            sb.AppendLine("}");
            sb.AppendLine("html, body { background: " + background + " !important; background-attachment: fixed !important; background-position: center !important; background-repeat: no-repeat !important; background-size: cover !important; color: " + CssRgb(effectiveText) + " !important; }");
            if (!String.IsNullOrEmpty(fontStack))
            {
                sb.AppendLine("body, body button, body input, body textarea, body select, body [contenteditable='true'], body [role='textbox'], body [role='searchbox'], body [data-message-author-role], body main, body aside, body nav { font-family: var(--cgtds-font-family) !important; }");
                sb.AppendLine("body :not(svg):not(path):not(pre):not(code):not(kbd):not(samp) { font-family: var(--cgtds-font-family) !important; }");
                sb.AppendLine("body pre, body code, body kbd, body samp { font-family: ui-monospace, SFMono-Regular, Consolas, \"Liberation Mono\", Menlo, monospace !important; }");
            }
            sb.AppendLine("body > div, #__next, main, [role='main'], [class*='bg-token-main-surface-primary'] { background-color: transparent !important; }");
            sb.AppendLine("header, [role='toolbar'], [data-testid*='toolbar'], [data-testid*='image'], [data-testid*='canvas'], [data-testid*='viewer'], [class*='toolbar'], [class*='workspace'], [class*='viewer'], [class*='canvas'], [class*='editor'], [class*='artifact'], [class*='bg-neutral'], [class*='bg-zinc'], [class*='bg-stone'] { background: transparent !important; background-color: transparent !important; background-image: none !important; border-color: transparent !important; border-radius: 0 !important; box-shadow: none !important; outline: none !important; }");
            sb.AppendLine("aside, nav[class*='sidebar'], nav[class*='bg-token-sidebar'], [class*='sidebar'], [class*='bg-token-sidebar'] { background: var(--cgtds-panel-background) !important; background-position: center !important; background-repeat: no-repeat !important; background-size: cover !important; color: " + CssRgb(effectiveText) + " !important; border: 0 none transparent !important; border-color: transparent !important; border-style: none !important; border-width: 0 !important; border-right: 0 none transparent !important; border-left: 0 none transparent !important; border-radius: 0 !important; box-shadow: none !important; outline: none !important; backdrop-filter: var(--cgtds-panel-blur) !important; -webkit-backdrop-filter: var(--cgtds-panel-blur) !important; }");
            sb.AppendLine("aside > div, nav[class*='sidebar'] > div, nav[class*='bg-token-sidebar'] > div, [class*='sidebar'] > div, [class*='bg-token-sidebar'] > div { background-color: transparent !important; background-image: none !important; box-shadow: none !important; border-color: transparent !important; }");
            sb.AppendLine("header, footer, [class*='sticky'], [class*='fixed'], [class*='top-0'], [class*='bottom-0'], [class*='bottom-['], [class*='from-black'], [class*='via-black'], [class*='to-black'], [class*='to-transparent'], [class*='bg-gradient'], [class*='bg-gradient-to'], [class*='bg-linear'], [class*='gradient-to'], [style*='linear-gradient'], [data-testid*='header'], [data-testid*='footer'], [class*='composer'], [class*='thread'], [class*='conversation'] { background: transparent !important; background-color: transparent !important; background-image: none !important; mask-image: none !important; -webkit-mask-image: none !important; border-color: transparent !important; box-shadow: none !important; }");
            sb.AppendLine("[class*='bg-token-main-surface-secondary'], [class*='bg-token-main-surface-tertiary'], [class*='bg-token-sidebar-surface-secondary'], [class*='bg-token-sidebar-surface-tertiary'] { background-color: " + CssRgb(theme.Panel, panelAlpha) + " !important; }");
            sb.AppendLine("footer [class*='bg-token-main-surface'], form [class*='bg-token-main-surface'], [data-testid*='composer'] [class*='bg-token-main-surface'], [class*='bottom-0'][class*='bg-token-main-surface'], [class*='bottom-['][class*='bg-token-main-surface'] { background: transparent !important; background-color: transparent !important; background-image: none !important; }");
            sb.AppendLine("main [class*='bg-gray-']:not(button), main [class*='bg-black']:not(button), main [class*='bg-white']:not(button), main [class*='bg-[']:not(button), [role='main'] [class*='bg-gray-']:not(button), [role='main'] [class*='bg-black']:not(button), [role='main'] [class*='bg-white']:not(button), [role='main'] [class*='bg-[']:not(button) { background-color: transparent !important; background-image: none !important; box-shadow: none !important; }");
            sb.AppendLine("form, [data-testid*='composer'], [class*='composer'], [class*='bottom-0'], [class*='bottom-['], [class*='bg-gradient'], [class*='bg-linear'], [class*='gradient-to'], footer { background: transparent !important; background-image: none !important; mask-image: none !important; -webkit-mask-image: none !important; border: 0 none transparent !important; border-color: transparent !important; border-style: none !important; border-width: 0 !important; border-radius: 0 !important; box-shadow: none !important; outline: none !important; --tw-ring-color: transparent !important; --tw-ring-shadow: 0 0 #0000 !important; --tw-shadow: 0 0 #0000 !important; --tw-shadow-colored: 0 0 #0000 !important; }");
            sb.AppendLine("textarea, [contenteditable]:not([contenteditable='false']), input[type='text'], input[type='search'], [role='textbox'], [role='searchbox'] { background-color: transparent !important; color: " + CssRgb(effectiveText) + " !important; caret-color: " + CssRgb(effectiveText) + " !important; border: 0 none transparent !important; border-color: transparent !important; border-style: none !important; border-width: 0 !important; border-radius: 0 !important; box-shadow: none !important; outline: none !important; line-height: 1.5 !important; min-height: 1.5em !important; overflow: visible !important; --tw-ring-color: transparent !important; --tw-ring-shadow: 0 0 #0000 !important; --tw-shadow: 0 0 #0000 !important; --tw-shadow-colored: 0 0 #0000 !important; }");
            sb.AppendLine("textarea::placeholder, input::placeholder, [contenteditable]:empty::before, [role='textbox']::placeholder, [role='searchbox']::placeholder { color: " + CssRgb(muted, 0.9) + " !important; opacity: 1 !important; }");
            sb.AppendLine("form:has(textarea), form:has([contenteditable]:not([contenteditable='false'])), [data-testid*='composer']:has(textarea), [data-testid*='composer']:has([contenteditable]:not([contenteditable='false'])), [class*='composer']:has(textarea), [class*='composer']:has([contenteditable]:not([contenteditable='false'])) { background: transparent !important; border: 0 none transparent !important; border-color: transparent !important; border-style: none !important; border-width: 0 !important; border-radius: 0 !important; box-shadow: none !important; outline: none !important; --tw-ring-color: transparent !important; --tw-ring-shadow: 0 0 #0000 !important; --tw-shadow: 0 0 #0000 !important; --tw-shadow-colored: 0 0 #0000 !important; }");
            sb.AppendLine("form textarea, form [contenteditable]:not([contenteditable='false']), [data-testid*='composer'] textarea, [data-testid*='composer'] [contenteditable]:not([contenteditable='false']), [class*='composer'] textarea, [class*='composer'] [contenteditable]:not([contenteditable='false']) { background-color: transparent !important; border: 0 none transparent !important; border-color: transparent !important; border-style: none !important; border-width: 0 !important; box-shadow: none !important; outline: none !important; }");
            sb.AppendLine("form button, [data-testid*='composer'] button, [class*='composer'] button { background-color: transparent !important; border: 0 none transparent !important; border-color: transparent !important; border-style: none !important; box-shadow: none !important; outline: none !important; }");
            sb.AppendLine("header button, [role='toolbar'] button, [data-testid*='toolbar'] button, [class*='toolbar'] button, button[aria-haspopup='menu'] { background-color: var(--cgtds-button-background) !important; color: " + CssRgb(effectiveText) + " !important; border-color: transparent !important; box-shadow: none !important; outline: none !important; }");
            sb.AppendLine("footer::before, footer::after, [class*='bottom-0']::before, [class*='bottom-0']::after, [class*='bottom-[']::before, [class*='bottom-[']::after, [class*='composer']::before, [class*='composer']::after, form :has(textarea)::before, form :has(textarea)::after, form :has([contenteditable]:not([contenteditable='false']))::before, form :has([contenteditable]:not([contenteditable='false']))::after { background: transparent !important; background-image: none !important; mask-image: none !important; -webkit-mask-image: none !important; border: 0 none transparent !important; border-color: transparent !important; border-style: none !important; border-width: 0 !important; box-shadow: none !important; outline: none !important; }");
            // Composer surfaces can be rounded, but must never clip typed text/carets.
            sb.AppendLine("form > div:has(textarea):not(:has(form)), form > div:has([contenteditable]:not([contenteditable='false'])):not(:has(form)), [data-testid*='composer'] > div:has(textarea):not(:has(form)), [data-testid*='composer'] > div:has([contenteditable]:not([contenteditable='false'])):not(:has(form)), [class*='composer'] > div:has(textarea):not(:has(form)), [class*='composer'] > div:has([contenteditable]:not([contenteditable='false'])):not(:has(form)) { background: var(--cgtds-composer-background) !important; background-color: var(--composer-surface) !important; border: 0 none transparent !important; border-color: transparent !important; border-style: none !important; border-width: 0 !important; border-radius: 28px !important; box-shadow: none !important; outline: none !important; overflow: visible !important; --tw-ring-color: transparent !important; --tw-ring-shadow: 0 0 #0000 !important; --tw-shadow: 0 0 #0000 !important; --tw-shadow-colored: 0 0 #0000 !important; backdrop-filter: var(--cgtds-composer-blur) !important; -webkit-backdrop-filter: var(--cgtds-composer-blur) !important; }");
            sb.AppendLine("form div:has(> textarea), form div:has(> [contenteditable]:not([contenteditable='false'])), form span:has(> textarea), form span:has(> [contenteditable]:not([contenteditable='false'])), form label:has(> textarea), form label:has(> [contenteditable]:not([contenteditable='false'])), [data-testid*='composer'] div:has(> textarea), [data-testid*='composer'] div:has(> [contenteditable]:not([contenteditable='false'])), [data-testid*='composer'] span:has(> textarea), [data-testid*='composer'] span:has(> [contenteditable]:not([contenteditable='false'])), [class*='composer'] div:has(> textarea), [class*='composer'] div:has(> [contenteditable]:not([contenteditable='false'])), [class*='composer'] span:has(> textarea), [class*='composer'] span:has(> [contenteditable]:not([contenteditable='false'])) { background: transparent !important; background-color: transparent !important; border: 0 none transparent !important; border-color: transparent !important; border-style: none !important; border-width: 0 !important; border-radius: 0 !important; box-shadow: none !important; outline: none !important; --tw-ring-color: transparent !important; --tw-ring-shadow: 0 0 #0000 !important; --tw-shadow: 0 0 #0000 !important; --tw-shadow-colored: 0 0 #0000 !important; backdrop-filter: none !important; -webkit-backdrop-filter: none !important; }");
            sb.AppendLine("aside input, aside input[type='search'], aside [role='searchbox'], aside [placeholder*='Search'], aside [data-testid*='search'] { background-color: transparent !important; color: " + CssRgb(effectiveText) + " !important; border: 0 !important; box-shadow: none !important; outline: none !important; }");
            sb.AppendLine("aside button, aside [role='button'], aside [role='link'], aside a { color: " + CssRgb(effectiveText) + " !important; background-color: transparent !important; background-image: none !important; border: 0 solid transparent !important; box-shadow: none !important; outline: none !important; pointer-events: auto !important; position: relative !important; z-index: 2 !important; } aside button *, aside [role='button'] *, aside [role='link'] *, aside a * { color: " + CssRgb(effectiveText) + " !important; border-color: transparent !important; box-shadow: none !important; outline: none !important; pointer-events: auto !important; } aside button:hover, aside [role='button']:hover, aside [role='link']:hover, aside a:hover { background-color: var(--cgtds-sidebar-action-hover) !important; }");
            sb.AppendLine("aside [aria-current], aside [aria-selected='true'], aside [data-active='true'], aside [data-selected='true'], aside a[aria-current], aside button[aria-current], aside [role='button'][aria-current], aside [role='link'][aria-current], aside [class*='bg-token-sidebar-surface-secondary'], aside [class*='bg-token-sidebar-surface-tertiary'] { background: var(--cgtds-sidebar-selected-background) !important; background-color: var(--cgtds-sidebar-selected-surface) !important; color: " + CssRgb(effectiveText) + " !important; border-color: transparent !important; box-shadow: none !important; outline: none !important; }");
            sb.AppendLine("aside [aria-current] *, aside [aria-selected='true'] *, aside [data-active='true'] *, aside [data-selected='true'] *, aside a[aria-current] *, aside button[aria-current] *, aside [role='button'][aria-current] *, aside [role='link'][aria-current] *, aside [class*='bg-token-sidebar-surface-secondary'] *, aside [class*='bg-token-sidebar-surface-tertiary'] * { background-color: transparent !important; background-image: none !important; color: " + CssRgb(effectiveText) + " !important; border-color: transparent !important; box-shadow: none !important; outline: none !important; }");
            sb.AppendLine("aside button[aria-label]:not([aria-label='Search chats']), aside [role='button'][aria-label]:not([aria-label='Search chats']), aside a button, aside a [role='button'], aside button:has(svg):not(:has(span)):not([aria-label='Search chats']), aside [role='button']:has(svg):not(:has(span)):not([aria-label='Search chats']) { background: transparent !important; background-color: transparent !important; background-image: none !important; border: 0 none transparent !important; box-shadow: none !important; outline: none !important; border-radius: 8px !important; --tw-bg-opacity: 0 !important; --tw-ring-color: transparent !important; --tw-ring-shadow: 0 0 #0000 !important; --tw-shadow: 0 0 #0000 !important; --tw-shadow-colored: 0 0 #0000 !important; }");
            sb.AppendLine("aside button[aria-label]:not([aria-label='Search chats']):hover, aside [role='button'][aria-label]:not([aria-label='Search chats']):hover, aside a button:hover, aside a [role='button']:hover, aside button:has(svg):not(:has(span)):not([aria-label='Search chats']):hover, aside [role='button']:has(svg):not(:has(span)):not([aria-label='Search chats']):hover { background: var(--cgtds-sidebar-action-hover) !important; background-color: var(--cgtds-sidebar-action-hover) !important; background-image: none !important; }");
            sb.AppendLine("aside [aria-label='Search chats'], aside button[aria-label='Search chats'], aside a[href*='search'] { background: var(--cgtds-search-background) !important; background-color: var(--cgtds-search-surface) !important; color: " + CssRgb(effectiveText) + " !important; border: 0 none transparent !important; border-color: transparent !important; border-width: 0 !important; border-radius: 999px !important; box-shadow: none !important; backdrop-filter: var(--cgtds-search-blur) !important; -webkit-backdrop-filter: var(--cgtds-search-blur) !important; box-sizing: border-box !important; max-width: calc(100% - 8px) !important; min-height: 28px !important; overflow: hidden !important; }");
            sb.AppendLine("aside [aria-label='Search chats'] *, aside button[aria-label='Search chats'] *, aside a[href*='search'] * { background: transparent !important; color: " + CssRgb(effectiveText) + " !important; border: 0 !important; box-shadow: none !important; outline: none !important; }");
            sb.AppendLine("[data-message-author-role='user'] [class*='bg-'], [data-message-author-role='user'] [class*='rounded-'] { background-color: " + CssRgb(theme.User, 0.9) + " !important; }");
            sb.AppendLine("main [class*='workspace'], main [class*='viewer'], main [class*='canvas'], main [class*='editor'], main [class*='artifact'], [role='main'] [class*='workspace'], [role='main'] [class*='viewer'], [role='main'] [class*='canvas'], [role='main'] [class*='editor'], [role='main'] [class*='artifact'] { border-radius: 0 !important; border-color: transparent !important; box-shadow: none !important; outline: none !important; }");
            sb.AppendLine("button, [role='button'] { color: " + CssRgb(effectiveText) + " !important; border-color: transparent !important; box-shadow: none !important; outline: none !important; }");
            sb.AppendLine("header button:hover, header button:focus-visible, [role='toolbar'] button:hover, [role='toolbar'] button:focus-visible, [data-testid*='toolbar'] button:hover, [data-testid*='toolbar'] button:focus-visible, [class*='toolbar'] button:hover, [class*='toolbar'] button:focus-visible, button[aria-haspopup='menu']:hover, button[aria-haspopup='menu']:focus-visible { background-color: var(--cgtds-button-hover-background) !important; color: " + CssRgb(effectiveText) + " !important; }");
            sb.AppendLine("button[aria-pressed='true'], button[aria-selected='true'], button[data-active='true'], [role='button'][aria-pressed='true'], [role='button'][aria-selected='true'], [role='button'][data-active='true'] { background-color: var(--cgtds-button-active-background) !important; color: " + CssRgb(effectiveText) + " !important; border-color: transparent !important; box-shadow: none !important; outline: none !important; }");
            sb.AppendLine("button:hover, button:focus, button:focus-visible, [role='button']:hover, [role='button']:focus, [role='button']:focus-visible, a:hover, a:focus, a:focus-visible { border-color: transparent !important; outline: none !important; box-shadow: none !important; }");
            sb.AppendLine("a, [class*='text-token-text-secondary'], [class*='text-token-text-tertiary'], [class*='text-gray-'] { color: " + CssRgb(theme.Accent) + " !important; }");
            sb.AppendLine("[class*='border-token'], [class*='border-gray-'], [class*='divide-token'], [class*='divide-gray-'] { border-color: " + CssRgb(theme.Border, 0.62) + " !important; }");
            sb.AppendLine("form, form *, [data-testid*='composer'], [data-testid*='composer'] *, [class*='composer'], [class*='composer'] *, textarea, [contenteditable]:not([contenteditable='false']), [role='textbox'], [role='searchbox'], input[type='text'], input[type='search'] { outline: none !important; box-shadow: none !important; --tw-ring-color: transparent !important; --tw-ring-shadow: 0 0 #0000 !important; --tw-shadow: 0 0 #0000 !important; --tw-shadow-colored: 0 0 #0000 !important; }");
            sb.AppendLine("form :has(textarea), form :has([contenteditable]:not([contenteditable='false'])), [data-testid*='composer'] :has(textarea), [data-testid*='composer'] :has([contenteditable]:not([contenteditable='false'])), [class*='composer'] :has(textarea), [class*='composer'] :has([contenteditable]:not([contenteditable='false'])), aside [aria-label='Search chats'], aside [aria-label='Search chats'] *, aside button[aria-label='Search chats'], aside button[aria-label='Search chats'] *, aside a[href*='search'], aside a[href*='search'] * { border: 0 none transparent !important; border-color: transparent !important; border-style: none !important; border-width: 0 !important; box-shadow: none !important; outline: none !important; }");
            sb.AppendLine("[data-message-author-role], [data-message-author-role] *, main article, main article *, [role='main'] article, [role='main'] article *, main .markdown, main .markdown *, [class*='text-token-text-primary'], [class*='text-token-text-secondary'], [class*='prose'], [class*='prose'] * { color: " + CssRgb(effectiveText) + " !important; }");
            sb.AppendLine("[data-message-author-role] a, main article a, [role='main'] article a, main .markdown a { color: " + CssRgb(theme.Accent) + " !important; }");
            sb.AppendLine("::selection { background: " + CssRgb(theme.Accent, 0.38) + " !important; color: " + CssRgb(effectiveText) + " !important; }");
            sb.AppendLine(layoutCss);
            return sb.ToString();
        }

        private string BuildBackground(Theme theme, string mode, string imageDataUrl, double glass)
        {
            var layers = new List<string>();
            bool hasImage = mode == "file" && !String.IsNullOrEmpty(imageDataUrl);
            bool lightTheme = IsLightColor(theme.Bg);
            double imageShade = hasImage
                ? Math.Max(0.16, (lightTheme ? 0.28 : 0.38) - (glass * 0.12))
                : Math.Max(0.30, (lightTheme ? 0.48 : 0.70) - (glass * 0.45));
            double accentShade = hasImage ? (lightTheme ? 0.07 : 0.08) : (lightTheme ? 0.09 : 0.10);
            layers.Add("linear-gradient(135deg, " + CssRgb(theme.Bg, imageShade) + ", " + CssRgb(theme.Accent, accentShade) + " 52%, " + CssRgb(theme.Bg, Math.Max(0.08, imageShade * 0.62)) + ")");

            if (hasImage)
            {
                layers.Add("url(\"" + CssUrl(imageDataUrl) + "\")");
            }
            else if (mode == "pattern")
            {
                layers.Add(theme.Pattern);
            }

            layers.Add(CssRgb(theme.Bg));
            return String.Join(", ", layers.ToArray());
        }

        private string BuildPanelBackground(Theme theme, string sidebar, double sidebarAlpha, string panelImageDataUrl)
        {
            if (String.IsNullOrEmpty(panelImageDataUrl))
            {
                return CssRgb(sidebar, sidebarAlpha);
            }

            var layers = new List<string>();
            double panelImageShade = Math.Max(0.18, Math.Min(0.46, sidebarAlpha * 0.52));
            layers.Add("linear-gradient(" + CssRgb(sidebar, panelImageShade) + ", " + CssRgb(sidebar, panelImageShade) + ")");
            layers.Add("url(\"" + CssUrl(panelImageDataUrl) + "\") center center / cover no-repeat");
            layers.Add(CssRgb(sidebar));
            return String.Join(", ", layers.ToArray());
        }

        private string ResolvePanelImageDataUrl(string backgroundImageDataUrl)
        {
            string mode = SelectedPanelImageMode();
            if (mode == "same") return backgroundImageDataUrl;
            if (mode == "file")
            {
                return ResolveBackgroundImageDataUrl(mode, _panelImageTextBox == null ? "" : _panelImageTextBox.Text.Trim());
            }
            return "";
        }

        private string ResolveBackgroundImageDataUrl(string mode, string value)
        {
            if (mode != "file") return "";
            if (String.IsNullOrWhiteSpace(value)) return "";
            FileInfo info;
            string mimeType;
            string error;
            if (!TryGetValidatedLocalImageFile(value, out info, out mimeType, out error))
            {
                throw new InvalidOperationException(error);
            }
            byte[] bytes = File.ReadAllBytes(info.FullName);

            if (bytes.Length > MaxImageBytes)
            {
                throw new InvalidOperationException("Please choose an image smaller than 8 MB.");
            }

            return "data:" + mimeType + ";base64," + Convert.ToBase64String(bytes);
        }

        private static bool TryGetValidatedLocalImageFile(string value, out FileInfo info, out string mimeType, out string error)
        {
            info = null;
            mimeType = "";
            error = "";
            if (String.IsNullOrWhiteSpace(value))
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

            if (info.Length > MaxImageBytes)
            {
                error = "Please choose an image smaller than 8 MB.";
                return false;
            }

            mimeType = MimeTypeFromExtension(info.Extension);
            if (String.IsNullOrEmpty(mimeType) || !mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                error = "Choose a PNG, JPG, WebP, GIF, or BMP image file.";
                return false;
            }
            if (String.Equals(mimeType, "image/svg+xml", StringComparison.OrdinalIgnoreCase))
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
            if (layout == "wide")
            {
                return "main article, [data-message-author-role] { max-width: min(1180px, calc(100vw - 48px)) !important; }";
            }
            if (layout == "compact")
            {
                return "[data-message-author-role] { padding-top: 0.35rem !important; padding-bottom: 0.35rem !important; } main article { gap: 0.45rem !important; }";
            }
            if (layout == "focus")
            {
                return "aside { opacity: 0.28 !important; transition: opacity 160ms ease !important; } aside:hover, aside:focus-within { opacity: 1 !important; } main article, [data-message-author-role] { max-width: min(860px, calc(100vw - 42px)) !important; }";
            }
            return "";
        }

        private Theme CurrentTheme()
        {
            return _themes.ContainsKey(_themeId) ? _themes[_themeId] : _themes["plum"];
        }

        private string SelectedLayout()
        {
            return _layoutCombo.SelectedItem == null ? "standard" : _layoutCombo.SelectedItem.ToString().ToLowerInvariant();
        }

        private string SelectedFontFamily()
        {
            string value = _fontCombo == null ? "" : _fontCombo.Text.Trim();
            return String.IsNullOrEmpty(value) ? "Default" : value;
        }

        private string SelectedBackgroundMode()
        {
            int index = _backgroundModeCombo == null ? 0 : _backgroundModeCombo.SelectedIndex;
            if (index == 0) return "solid";
            if (index == 1) return "pattern";
            if (index == 2) return "file";
            return "solid";
        }

        private string SelectedPanelImageMode()
        {
            int index = _panelImageModeCombo == null ? 0 : _panelImageModeCombo.SelectedIndex;
            if (index == 1) return "same";
            if (index == 2) return "file";
            return "off";
        }

        private int BackgroundModeToIndex(string mode)
        {
            if (mode == "solid") return 0;
            if (mode == "pattern") return 1;
            if (mode == "file") return 2;
            return 0;
        }

        private int PanelImageModeToIndex(string mode)
        {
            if (mode == "same") return 1;
            if (mode == "file") return 2;
            return 0;
        }

        private int SelectedPort()
        {
            int port;
            return Int32.TryParse(_portTextBox == null ? "" : _portTextBox.Text.Trim(), out port) && port > 0 && port <= 65535 ? port : 9322;
        }

        private int SelectedTransparency()
        {
            if (_transparencyTrackBar == null) return _transparency;
            _transparency = _transparencyTrackBar.Value;
            return _transparency;
        }

        private bool SelectedGlassSearch()
        {
            if (_glassSearchCheckBox == null) return _glassSearch;
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
            if (ex == null || String.IsNullOrWhiteSpace(ex.Message)) return "The operation failed.";
            string message = ex.Message;
            message = Regex.Replace(message, @"[A-Za-z]:\\[^\r\n""']+", "[local path]");
            message = Regex.Replace(message, @"\\\\[^\\\s]+\\[^\r\n""']+", "[network path]");
            if (message.Length > 260) message = message.Substring(0, 257) + "...";
            return message;
        }

        private void SetStatus(string message, bool isError)
        {
            if (_statusLabel == null) return;
            message = String.IsNullOrWhiteSpace(message) ? "Ready." : message;
            _statusLabel.Text = message;
            _statusLabel.ForeColor = isError ? Color.FromArgb(255, 105, 97) : AppleMuted;
            _statusToolTip.SetToolTip(_statusLabel, message);
        }

        private void UpdateActiveThemeLabel()
        {
            if (_activeThemeLabel == null) return;
            _activeThemeLabel.Text = "Active: " + ActiveThemeDisplayName();
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
            return map.ContainsKey(key) && map[key] != null ? Convert.ToString(map[key]) : "";
        }

        private static string NonEmpty(string value, string fallback)
        {
            return String.IsNullOrEmpty(value) ? fallback : value;
        }

        private static void SetComboText(ComboBox combo, string value)
        {
            if (combo == null) return;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (String.Equals(Convert.ToString(combo.Items[i]), value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            if (combo.DropDownStyle == ComboBoxStyle.DropDownList)
            {
                if (combo.Items.Count > 0) combo.SelectedIndex = 0;
                return;
            }
            combo.Text = value;
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
            int yiq = ((color.R * 299) + (color.G * 587) + (color.B * 114)) / 1000;
            return yiq >= 140 ? Color.Black : Color.White;
        }

        private static string EnsureReadableText(string textHex, string backgroundHex)
        {
            Color text = ColorFromHex(textHex);
            Color background = ColorFromHex(backgroundHex);
            double contrast = ContrastRatio(text, background);
            if (contrast >= 4.5) return NormalizeHex(textHex);
            return RelativeLuma(background) > 0.5 ? "#172033" : "#F8FBFF";
        }

        private static bool IsLightColor(string hex)
        {
            return RelativeLuma(ColorFromHex(hex)) > 0.55;
        }

        private static double RelativeLuma(Color color)
        {
            return ((0.2126 * ChannelLuma(color.R)) + (0.7152 * ChannelLuma(color.G)) + (0.0722 * ChannelLuma(color.B)));
        }

        private static double ChannelLuma(int channel)
        {
            double value = channel / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        private static double ContrastRatio(Color first, Color second)
        {
            double a = RelativeLuma(first) + 0.05;
            double b = RelativeLuma(second) + 0.05;
            return Math.Max(a, b) / Math.Min(a, b);
        }

        private static string NormalizeHex(string hex)
        {
            Color color = ColorFromHex(hex);
            return String.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        private static string CssUrl(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "").Replace("\r", "").Replace("\n", "");
        }

        private static string CssRgb(string hex)
        {
            return CssRgb(hex, 1);
        }

        private static string CssRgb(string hex, double alpha)
        {
            Color color = ColorFromHex(hex);
            if (alpha >= 1)
            {
                return String.Format("rgb({0}, {1}, {2})", color.R, color.G, color.B);
            }
            return String.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "rgba({0}, {1}, {2}, {3:0.###})",
                color.R,
                color.G,
                color.B,
                alpha);
        }

        private static string BuildFontFamilyCss(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "";
            value = value.Trim();
            if (String.Equals(value, "Default", StringComparison.OrdinalIgnoreCase)) return "";
            if (String.Equals(value, "System UI", StringComparison.OrdinalIgnoreCase))
            {
                return "system-ui, -apple-system, BlinkMacSystemFont, \"Segoe UI\", sans-serif";
            }

            var parts = new List<string>();
            foreach (string raw in value.Split(','))
            {
                string font = SanitizeFontName(raw);
                if (String.IsNullOrEmpty(font)) continue;
                parts.Add(FontNeedsQuotes(font) ? "\"" + font.Replace("\"", "\\\"") + "\"" : font.ToLowerInvariant());
            }

            if (parts.Count == 0) return "";
            bool hasGeneric = parts.Any(IsGenericFontFamily);
            if (!hasGeneric)
            {
                parts.Add("sans-serif");
            }
            return String.Join(", ", parts.ToArray());
        }

        private static string SanitizeFontName(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "";
            value = value.Trim().Trim('"', '\'');
            var sb = new StringBuilder();
            foreach (char c in value)
            {
                if (Char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '.')
                {
                    sb.Append(c);
                }
            }
            return sb.ToString().Trim();
        }

        private static bool FontNeedsQuotes(string font)
        {
            return !IsGenericFontFamily(font);
        }

        private static bool IsGenericFontFamily(string font)
        {
            if (String.IsNullOrWhiteSpace(font)) return false;
            string normalized = font.Trim().ToLowerInvariant();
            return normalized == "serif"
                || normalized == "sans-serif"
                || normalized == "monospace"
                || normalized == "cursive"
                || normalized == "fantasy"
                || normalized == "system-ui"
                || normalized == "ui-sans-serif"
                || normalized == "ui-serif"
                || normalized == "ui-monospace";
        }

        private static string MixHex(string first, string second, double secondWeight)
        {
            Color a = ColorFromHex(first);
            Color b = ColorFromHex(second);
            double w = Math.Max(0, Math.Min(1, secondWeight));
            int r = (int)Math.Round(a.R * (1 - w) + b.R * w);
            int g = (int)Math.Round(a.G * (1 - w) + b.G * w);
            int blue = (int)Math.Round(a.B * (1 - w) + b.B * w);
            return String.Format("#{0:x2}{1:x2}{2:x2}", r, g, blue);
        }
    }

    internal static class TextBoxExtensions
    {
        public static void PlaceholderTextCompat(this TextBox textBox, string text)
        {
            // PlaceholderText is not available on the .NET Framework WinForms TextBox.
            if (textBox.TextLength == 0)
            {
                textBox.Tag = text;
            }
        }
    }
}
