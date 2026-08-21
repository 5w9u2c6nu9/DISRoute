using System.Runtime.InteropServices;

namespace DISRoute.Server.Operational;

/// <summary>
/// Minimal native Windows Service Control Manager lifetime so DISRoute.Server can run as a real
/// Windows service without adding an OS-specific package to the cross-platform server build.
/// The installer registers the published executable with --windows-service.
/// </summary>
public static class WindowsServiceHost
{
    private const int ServiceWin32OwnProcess = 0x00000010;
    private const int ServiceStartPending = 0x00000002;
    private const int ServiceStopPending = 0x00000003;
    private const int ServiceRunning = 0x00000004;
    private const int ServiceStopped = 0x00000001;
    private const int ServiceAcceptStop = 0x00000001;
    private const int ServiceAcceptShutdown = 0x00000004;
    private const int ServiceControlStop = 0x00000001;
    private const int ServiceControlShutdown = 0x00000005;
    private const string ServiceName = "DISRouteServer";

    private static WebApplication? _application;
    private static ServiceMainDelegate? _serviceMain;
    private static HandlerExDelegate? _handler;
    private static IntPtr _statusHandle;
    private static readonly ManualResetEventSlim StopCompleted = new(false);

    public static Task RunAsync(WebApplication application)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows service mode is only valid on Windows.");
        _application = application;
        _serviceMain = ServiceMain;
        _handler = Handler;
        var table = new[]
        {
            new ServiceTableEntry { ServiceName = ServiceName, ServiceMain = Marshal.GetFunctionPointerForDelegate(_serviceMain) },
            new ServiceTableEntry { ServiceName = null, ServiceMain = IntPtr.Zero }
        };
        if (!StartServiceCtrlDispatcher(table))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "DISRoute.Server could not connect to the Windows Service Control Manager. Start normally without --windows-service for console hosting.");
        return Task.CompletedTask;
    }

    private static void ServiceMain(int argc, IntPtr argv)
    {
        var app = _application ?? throw new InvalidOperationException("Windows service application host was not initialized.");
        _statusHandle = RegisterServiceCtrlHandlerEx(ServiceName, _handler!, IntPtr.Zero);
        if (_statusHandle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

        Report(ServiceStartPending, 0, 30_000);
        try
        {
            app.StartAsync().GetAwaiter().GetResult();
            Report(ServiceRunning, ServiceAcceptStop | ServiceAcceptShutdown, 0);
            app.WaitForShutdownAsync().GetAwaiter().GetResult();
            StopCompleted.Set();
            Report(ServiceStopped, 0, 0);
        }
        catch
        {
            StopCompleted.Set();
            Report(ServiceStopped, 0, 0, 1);
            throw;
        }
    }

    private static int Handler(int control, int eventType, IntPtr eventData, IntPtr context)
    {
        if (control is not (ServiceControlStop or ServiceControlShutdown)) return 0;
        if (_application is null) return 0;
        Report(ServiceStopPending, 0, 45_000);
        _ = Task.Run(async () =>
        {
            try { await _application.StopAsync(TimeSpan.FromSeconds(40)); }
            finally { StopCompleted.Set(); }
        });
        return 0;
    }

    private static void Report(int state, int accepted, int waitHint, int win32ExitCode = 0)
    {
        if (_statusHandle == IntPtr.Zero) return;
        var status = new ServiceStatus
        {
            ServiceType = ServiceWin32OwnProcess,
            CurrentState = state,
            ControlsAccepted = accepted,
            Win32ExitCode = win32ExitCode,
            ServiceSpecificExitCode = 0,
            CheckPoint = state is ServiceStartPending or ServiceStopPending ? 1 : 0,
            WaitHint = waitHint
        };
        _ = SetServiceStatus(_statusHandle, ref status);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceTableEntry
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string? ServiceName;
        public IntPtr ServiceMain;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public int ServiceType;
        public int CurrentState;
        public int ControlsAccepted;
        public int Win32ExitCode;
        public int ServiceSpecificExitCode;
        public int CheckPoint;
        public int WaitHint;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ServiceMainDelegate(int argc, IntPtr argv);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int HandlerExDelegate(int control, int eventType, IntPtr eventData, IntPtr context);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartServiceCtrlDispatcher([In] ServiceTableEntry[] serviceTable);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr RegisterServiceCtrlHandlerEx(string serviceName, HandlerExDelegate handler, IntPtr context);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetServiceStatus(IntPtr serviceStatusHandle, ref ServiceStatus serviceStatus);
}
