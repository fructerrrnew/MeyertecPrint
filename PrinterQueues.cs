using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MeyertecPrint;

/// <summary>Чтение конфигурации очереди Windows без изменения драйверов или портов.</summary>
public static class PrinterQueues
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterInfo2
    {
        public IntPtr ServerName, PrinterName, ShareName, PortName, DriverName, Comment, Location,
            DevMode, SepFile, PrintProcessor, Datatype, Parameters, SecurityDescriptor;
        public uint Attributes, Priority, DefaultPriority, StartTime, UntilTime, Status, Jobs, AveragePpm;
    }
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string name, out IntPtr printer, IntPtr defaults);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetPrinter(IntPtr printer, int level, IntPtr buffer, int size, out int needed);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr printer);

    public static string GetPort(string printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName) || !OperatingSystem.IsWindows()) return string.Empty;
        if (!OpenPrinter(printerName, out var handle, IntPtr.Zero)) return string.Empty;
        try
        {
            GetPrinter(handle, 2, IntPtr.Zero, 0, out var needed);
            if (needed <= 0 || needed > 4_000_000) return string.Empty;
            var buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!GetPrinter(handle, 2, buffer, needed, out _)) return string.Empty;
                return Marshal.PtrToStringUni(Marshal.PtrToStructure<PrinterInfo2>(buffer).PortName) ?? string.Empty;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { ClosePrinter(handle); }
    }

    public static string GetNetworkHost(string port)
    {
        if (string.IsNullOrWhiteSpace(port) || !OperatingSystem.IsWindows() || port.Contains(',')) return string.Empty;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Monitors\Standard TCP/IP Port\Ports\" + port);
            var value = key?.GetValue("IPAddress") as string;
            if (string.IsNullOrWhiteSpace(value) || value == "0.0.0.0") value = key?.GetValue("HostName") as string;
            return value?.Trim() ?? string.Empty;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { return string.Empty; }
    }

    public enum HostMatch { Verified, Mismatch, Unknown }
    public static HostMatch CompareNetworkHost(string port, string? host)
    {
        var configured = GetNetworkHost(port);
        host = host?.Trim();
        if (string.IsNullOrWhiteSpace(configured) || string.IsNullOrWhiteSpace(host)) return HostMatch.Unknown;
        if (string.Equals(configured, host, StringComparison.OrdinalIgnoreCase)) return HostMatch.Verified;
        if (System.Net.IPAddress.TryParse(configured, out var a) && System.Net.IPAddress.TryParse(host, out var b))
            return a.Equals(b) ? HostMatch.Verified : HostMatch.Mismatch;
        // Different host names may still resolve to the same address; a shared queue may hide its port.
        return HostMatch.Unknown;
    }

    public static bool MatchesNetworkHost(string port, string host)
    {
        var configured = GetNetworkHost(port);
        return !string.IsNullOrWhiteSpace(configured) &&
               string.Equals(configured, host?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
