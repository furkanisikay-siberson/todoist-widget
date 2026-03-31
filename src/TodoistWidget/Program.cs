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
        // VS F5 ile calistirinca arguman gelmeyebilir - loglayalim
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TodoistWidget", "debug.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
            try { File.AppendAllText(logPath, line + Environment.NewLine); } catch { }
            Console.WriteLine(line);
        }

        try
        {
            Log($"Baslatildi. Args: [{string.Join(", ", args)}]");

            if (args.Length == 0 || args[0] != "-RegisterProcessAsComServer")
            {
                Log("UYARI: -RegisterProcessAsComServer argumani yok. Widget Board bu uygulamayi COM server olarak baslatir.");
                Log("Bu uygulama dogrudan calistirilmak icin degildir.");
                Log("MSIX paketini deploy edip Widget Board'dan pin edin.");

                // VS debug icin: arguman olmasa bile COM server olarak basla
                // Boylece F5 ile test edilebilir
                Log("Debug modu: COM server olarak baslatiliyor...");
            }

            Log("ComWrappersSupport.InitializeComWrappers() cagriliyor...");
            ComWrappersSupport.InitializeComWrappers();
            Log("ComWrappers basariyla initialize edildi.");

            uint cookie;
            var clsid = Guid.Parse(TodoistWidgetProvider.ClsId);
            Log($"CoRegisterClassObject cagriliyor, CLSID: {clsid}");

            int hr = CoRegisterClassObject(
                clsid,
                new WidgetProviderFactory<TodoistWidgetProvider>(),
                CLSCTX_LOCAL_SERVER,
                REGCLS_MULTIPLEUSE,
                out cookie);

            if (hr != 0)
            {
                Log($"CoRegisterClassObject basarisiz! HRESULT: 0x{hr:X8}");
                Marshal.ThrowExceptionForHR(hr);
            }

            Log($"COM server basariyla kaydedildi. Cookie: {cookie}");
            Log("Widget Board'dan etkilesim bekleniyor...");

            var exitEvent = new ManualResetEvent(false);
            exitEvent.WaitOne();

            CoRevokeClassObject(cookie);
        }
        catch (Exception ex)
        {
            Log($"HATA: {ex.GetType().Name}: {ex.Message}");
            Log($"StackTrace: {ex.StackTrace}");

            if (ex.InnerException != null)
                Log($"InnerException: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");

            // Debug build'de hata mesajini gostermek icin bekle
#if DEBUG
            Console.Error.WriteLine($"\nHATA: {ex.Message}");
            Console.Error.WriteLine("Devam etmek icin bir tusa basin...");
            // WinExe oldugu icin console gormeyebilir, log dosyasina yazdik
#endif
        }
    }
}
