using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace TodoistWidget;

public static class Program
{
    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
        [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        uint dwClsContext,
        uint flags,
        out uint lpdwRegister);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint dwRegister);

    private const uint CLSCTX_LOCAL_SERVER = 0x4;
    private const uint REGCLS_MULTIPLEUSE = 0x1;

    [MTAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            ComWrappersSupport.InitializeComWrappers();

            uint cookie;
            var clsid = Guid.Parse(TodoistWidgetProvider.ClsId);
            int hr = CoRegisterClassObject(
                clsid,
                new WidgetProviderFactory<TodoistWidgetProvider>(),
                CLSCTX_LOCAL_SERVER,
                REGCLS_MULTIPLEUSE,
                out cookie);

            if (hr != 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            var exitEvent = new ManualResetEvent(false);
            exitEvent.WaitOne();

            CoRevokeClassObject(cookie);
        }
    }
}
