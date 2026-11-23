using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace MeyertecPrint;

/// <summary>
/// GoDEX EZio 1.0.0.11 native SDK. The SDK has one active connection per process.
/// See https://github.com/godexsw/EZPL_DLL/tree/4c4f76df8d2f7d67e81c1c76bc8f40d51a0e904c.
/// </summary>
internal sealed class GodexWindowsSdk : IGodexPrintTarget
{
    private static readonly SemaphoreSlim Gate = new(1);
    private readonly nint _library;
    private readonly SendBuffer _send;
    private readonly ClosePort _close;

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int OpenPort([MarshalAs(UnmanagedType.LPStr)] string port);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int OpenNetwork([MarshalAs(UnmanagedType.LPStr)] string ip, [MarshalAs(UnmanagedType.LPStr)] string port);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void SetBaudRate(int rate);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SendBuffer(nint bytes, int length);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void ClosePort();

    private static string LibraryPath
    {
        get
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException(L10n.T("Godex.SdkWindows"));
            var name = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "EZio64.dll",
                Architecture.X86 => "EZio32.dll",
                _ => throw new PlatformNotSupportedException(L10n.T("Godex.SdkWindows"))
            };
            return Path.Combine(AppContext.BaseDirectory, name);
        }
    }

    internal static void EnsureAvailable()
    {
        var path = LibraryPath;
        if (!File.Exists(path))
            throw new FileNotFoundException(string.Format(L10n.T("Godex.SdkMissing"), Path.GetFileName(path)));
    }

    private GodexWindowsSdk(nint library, SendBuffer send, ClosePort close)
    {
        _library = library;
        _send = send;
        _close = close;
    }

    public static async Task<IGodexPrintTarget> OpenAsync(PrintSettings settings, CancellationToken token)
    {
        string? ip = null;
        if (settings.ConnectionKind == "ethernet")
        {
            // The documented OpenNet contract accepts an IPv4 address.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                var all = await Dns.GetHostAddressesAsync(settings.NetworkHost!.Trim(), AddressFamily.InterNetwork, timeout.Token)
                    .ConfigureAwait(false);
                ip = all.FirstOrDefault()?.ToString();
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { throw new TimeoutException(L10n.T("Godex.Timeout")); }
            if (ip is null) throw new InvalidDataException(L10n.T("Godex.SdkIpv4"));
        }

        await Gate.WaitAsync(token).ConfigureAwait(false);
        nint library = 0;
        try
        {
            token.ThrowIfCancellationRequested();
            library = NativeLibrary.Load(LibraryPath);
            var send = Export<SendBuffer>(library, "sendbuf");
            var close = Export<ClosePort>(library, "closeport");
            int result;
            switch (settings.ConnectionKind)
            {
                case "ethernet":
                    result = Export<OpenNetwork>(library, "OpenNet")(ip!, GodexPrintJob.NetworkPort.ToString(CultureInfo.InvariantCulture));
                    break;
                case "serial":
                    var setBaudRate = Export<SetBaudRate>(library, "setbaudrate");
                    result = Export<OpenPort>(library, "openport")(settings.SerialPort!.Trim()[3..]);
                    if (result == 1) setBaudRate(settings.SerialBaudRate);
                    break;
                default:
                    result = string.IsNullOrWhiteSpace(settings.UsbDevice)
                        ? Export<OpenPort>(library, "OpenDriver")(settings.PrinterName!)
                        : Export<OpenPort>(library, "OpenUSB")(settings.UsbDevice.Trim());
                    break;
            }
            if (result != 1)
                throw new IOException(L10n.T("Godex.SdkConnection"));
            return new GodexWindowsSdk(library, send, close);
        }
        catch
        {
            if (library != 0) NativeLibrary.Free(library);
            Gate.Release();
            throw;
        }
    }

    private static T Export<T>(nint library, string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    public ValueTask WritePageAsync(byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Tested EZio 1.0.0.11 returns 1 when it sends the whole buffer, although the
        // manual describes a byte count. Treat either success form as one complete write.
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var result = _send(pinned.AddrOfPinnedObject(), bytes.Length);
            if (result != 1 && result != bytes.Length)
                throw new IOException(L10n.T("Godex.SdkShortWrite"));
        }
        finally { pinned.Free(); }
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        try { _close(); }
        finally
        {
            NativeLibrary.Free(_library);
            Gate.Release();
        }
    }
}
