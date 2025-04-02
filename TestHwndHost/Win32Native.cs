using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace TestHwndHost
{
  public static class Win32Native
  {
    public const int GWL_STYLE = -16;
    public const int WS_CHILD = 0x40000000;
    public const int WS_VISIBLE = 0x10000000;
    public const int WS_CAPTION = 0x00C00000;
    public const int WS_BORDER = 0x00800000;
    public const int WS_THICKFRAME = 0x00040000;

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    
    [DllImport("user32.dll")]
    public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
    [DllImport("user32.dll", EntryPoint = "DestroyWindow", CharSet = CharSet.Unicode)]
    public static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "CreateWindowEx", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowEx(int dwExStyle,
                                                string lpszClassName,
                                                string lpszWindowName,
                                                int style,
                                                int x, int y,
                                                int width, int height,
                                                IntPtr hwndParent,
                                                IntPtr hMenu,
                                                IntPtr hInst,
                                                [MarshalAs(UnmanagedType.AsAny)] object pvParam);

    
  }
}
