using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace WindHost32Integration;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    void LaunchAndEmbedApp(string processName, ContentControl contentControl)
    {
        Process process = Process.Start(processName);
        process.WaitForInputIdle();

        IntPtr externalWindowHandle = process.MainWindowHandle;

        if (externalWindowHandle != IntPtr.Zero)
        {
            EmbedExternalApp(externalWindowHandle, contentControl);
        }
    }

    void EmbedExternalApp(IntPtr externalWindowHandle, ContentControl contentControl)
    {
        ExternalAppHost appHost = new ExternalAppHost(externalWindowHandle);
        contentControl.Content = appHost;
        appHost.BeginInit();

        appHost.EndInit();
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        //using (var mmf = MemoryMappedFile.CreateNew("SharedMemory", 1024))
        //{
        //    using (var accessor = mmf.CreateViewAccessor())
        //    {
        //        byte[] data = Encoding.UTF8.GetBytes("Hello from Main App");
        //        accessor.Write(0, (byte)data.Length);
        //        accessor.WriteArray(1, data, 0, data.Length);
        //    }

        //    Console.WriteLine("Message written to shared memory.");
        //    Console.ReadLine(); // Keep the process open
        //}
        LaunchAndEmbedApp(@"D:\\NetPractices\\WindHost32Integration\\HostedApp\\bin\\Debug\\net6.0-windows\\HostedApp.exe", ContentControlApp);
    }
}

class MyClass
{

}
class ExternalAppHost : HwndHost
{
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32")]
    private static extern IntPtr SetParent(IntPtr hWnd, IntPtr hWndParent);

    private IntPtr ChildHandle = IntPtr.Zero;
    private Process? _process;

    public ExternalAppHost(IntPtr handle)
    {
        this.ChildHandle = handle;
    }

    private static int GWL_STYLE = -16;
    private static int GWL_EXSTYLE = -20;

    private static UInt32 WS_CHILD = 0x40000000;
    private static UInt32 WS_POPUP = 0x80000000;
    private static UInt32 WS_CAPTION = 0x00C00000;
    private static UInt32 WS_THICKFRAME = 0x00040000;

    private static UInt32 WS_EX_DLGMODALFRAME = 0x00000001;
    private static UInt32 WS_EX_WINDOWEDGE = 0x00000100;
    private static UInt32 WS_EX_CLIENTEDGE = 0x00000200;
    private static UInt32 WS_EX_STATICEDGE = 0x00020000;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        int style = GetWindowLong(ChildHandle, GWL_STYLE);
        style = style & ~((int)WS_CAPTION) & ~((int)WS_THICKFRAME); // Removes Caption bar and the sizing border
        style |= ((int)WS_CHILD); // Must be a child window to be hosted

        SetWindowLong(ChildHandle, GWL_STYLE, style);
        SetParent(ChildHandle, hwndParent.Handle);

        this.InvalidateVisual();

        HandleRef hwnd = new HandleRef(this, ChildHandle);
        return hwnd;
    }

    protected override void DestroyWindowCore(System.Runtime.InteropServices.HandleRef hwnd)
    {
        // Cleanup code if needed
    }
}
