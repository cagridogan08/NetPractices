using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

namespace TestHwndHost
{
  /// <summary>
  /// MainWindow.xaml 的交互逻辑
  /// </summary>
  public partial class MainWindow : Window
  {
    public MainWindow()
    {
      InitializeComponent();
    }
  }

  public class MyHwndHost : HwndHost
  {
    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
      Process process = new Process();
      process.StartInfo.FileName = "notepad.exe";
      process.StartInfo.UseShellExecute = false;
      process.Start();
      process.WaitForInputIdle();

      Win32Native.SetWindowLong(process.MainWindowHandle, Win32Native.GWL_STYLE,
       new IntPtr((Win32Native.GetWindowLong(hwndParent.Handle, Win32Native.GWL_STYLE)
        | Win32Native.WS_CHILD)&~Win32Native.WS_CAPTION) );

      Win32Native.SetParent(process.MainWindowHandle, hwndParent.Handle);

      return new HandleRef(this, process.MainWindowHandle);
    }
    protected override void DestroyWindowCore(HandleRef hwnd)
    {
      Win32Native.DestroyWindow(hwnd.Handle);
    }
  }
}
