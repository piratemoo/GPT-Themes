using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ChatGptDesktopSkinner;

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
		using Mutex obj = new Mutex(initiallyOwned: true, "Local\\ChatGptDesktopSkinner.SingleInstance", out createdNew);
		if (!createdNew)
		{
			FocusExistingInstance();
			return;
		}
		RunApplication();
		GC.KeepAlive(obj);
	}

	private static void RunApplication()
	{
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
		{
			ShowFatalError(e.Exception);
		};
		AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
		{
			Exception ex2 = e.ExceptionObject as Exception;
			ShowFatalError(ex2 ?? new Exception(Convert.ToString(e.ExceptionObject)));
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
			Process currentProcess = Process.GetCurrentProcess();
			Process[] processesByName = Process.GetProcessesByName(currentProcess.ProcessName);
			foreach (Process process in processesByName)
			{
				if (process.Id != currentProcess.Id)
				{
					IntPtr mainWindowHandle = process.MainWindowHandle;
					if (!(mainWindowHandle == IntPtr.Zero))
					{
						ShowWindow(mainWindowHandle, 9);
						SetForegroundWindow(mainWindowHandle);
						break;
					}
				}
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
			string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-error.log");
			File.AppendAllText(path, string.Concat(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Environment.NewLine, ex, Environment.NewLine, Environment.NewLine));
		}
		catch
		{
		}
		try
		{
			MessageBox.Show("GPT Themes hit an error while opening." + Environment.NewLine + Environment.NewLine + "A local startup-error.log file was written next to the EXE with details.", "GPT Themes", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		catch
		{
		}
	}
}
