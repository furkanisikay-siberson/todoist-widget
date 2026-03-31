using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace TodoistWidget;

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("00000001-0000-0000-C000-000000000046")]
internal interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);
    [PreserveSig]
    int LockServer(bool fLock);
}

internal sealed class WidgetProviderFactory<T> : IClassFactory where T : IWidgetProvider, new()
{
    private const int CLASS_E_NOAGGREGATION = -2147221232;
    private const int E_NOINTERFACE = -2147467262;

    public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
    {
        ppvObject = IntPtr.Zero;

        if (pUnkOuter != IntPtr.Zero)
        {
            Marshal.ThrowExceptionForHR(CLASS_E_NOAGGREGATION);
        }

        if (riid == typeof(IWidgetProvider).GUID || riid == Guid.Parse("00000000-0000-0000-C000-000000000046"))
        {
            ppvObject = MarshalInspectable<IWidgetProvider>.FromManaged(new T());
        }
        else
        {
            Marshal.ThrowExceptionForHR(E_NOINTERFACE);
        }

        return 0;
    }

    public int LockServer(bool fLock) => 0;
}
