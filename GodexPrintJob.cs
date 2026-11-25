using System.IO.Ports;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace MeyertecPrint;

/// <summary>A monochrome 300 dpi raster with the leftmost pixel in the most significant bit.</summary>
public sealed class GodexRaster
{
    private GodexRaster(int width, int height, byte[] bits)
    {
        Width = width;
        Height = height;
        Bits = bits;
    }

    public int Width { get; }
    public int Height { get; }
    internal int Stride => (Width + 7) / 8;
    internal byte[] Bits { get; }

    public static GodexRaster Create(int width, int height, Func<int, int, bool> isBlack)
    {
        ArgumentNullException.ThrowIfNull(isBlack);
        var raster = Blank(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            if (isBlack(x, y))
                raster.Bits[y * raster.Stride + x / 8] |= (byte)(0x80 >> (x % 8));

        return raster;
    }

    internal static GodexRaster Blank(int width, int height)
    {
        if (width is < 1 or > 1_394 || height is < 1 or > 9_000)
            throw new InvalidDataException(L10n.T("Godex.RasterSize"));
        return new GodexRaster(width, height, new byte[((width + 7) / 8) * height]);
    }
}

/// <summary>GoDEX G530 EZPL graphics output through the SDK or an explicitly selected direct connection.</summary>
public static class GodexPrintJob
{
    public const int Dpi = 300;
    public const string DriverBackend = "driver";
    public const string SdkBackend = "godex-sdk";
    public const string DirectBackend = "godex-direct";
    public const int NetworkPort = 9100;
    public static IReadOnlyList<int> BaudRates { get; } = Array.AsReadOnly(new[] { 4800, 9600, 19200, 38400, 57600, 115200 });
    private const int HeadDots = 1248;
    private const double HeadWidthMm = 105.7;
    private const int MaxPrintLengthUm = 762_000;

    public static Task SendAsync(PrintSettings settings, IReadOnlyList<LabelLayout> pages,
        Func<LabelLayout, PrintSettings, int, GodexRaster> render,
        IProgress<(long Done, long Total)>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(render);
        return Task.Run(async () =>
        {
            ValidateConnection(settings);
            ValidateParameters(settings);
            if (pages.Count == 0 || settings.Copies is < 1 or > 500)
                throw new InvalidDataException(L10n.T("Godex.JobSize"));

            // Validate every selected record before opening the destination. Repeat rendering on demand
            // so a large Excel job does not hold a bitmap or encoded page per record in memory.
            foreach (var page in pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateMediaGap(page, settings);
                _ = RenderPage(page, settings, render);
            }

            using var target = await OpenTargetAsync(settings, cancellationToken).ConfigureAwait(false);
            var first = pages[0];
            var sectionsPerLabel = LabelPrintPages.PhysicalSectionsPerLabel(first);
            var total = (long)pages.Count * settings.Copies;
            if (settings.ConnectionKind != "serial" && sectionsPerLabel > 1 &&
                pages.All(page => page.AlternateNotch is not null &&
                page.MediaWidthUm == first.MediaWidthUm && page.MediaHeightUm == first.MediaHeightUm))
            {
                // One format per strip avoids a printer stop and restart at each perforation.
                var sectionsPerBlock = MaxPrintLengthUm / first.MediaHeightUm;
                var totalSections = total * sectionsPerLabel;
                for (long section = 0; section < totalSections;)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var count = (int)Math.Min(sectionsPerBlock, totalSections - section);
                    var command = EncodeContinuousBlock(pages, settings, render, section, count, cancellationToken);
                    await target.WritePageAsync(command, cancellationToken).ConfigureAwait(false);
                    section += count;
                    progress?.Report((section / sectionsPerLabel, total));
                }
                return;
            }

            long done = 0;
            for (var copy = 0; copy < settings.Copies; copy++)
            foreach (var page in pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var raster = RenderPage(page, settings, render);
                var command = EncodePage(page, settings, raster);
                await target.WritePageAsync(command, cancellationToken).ConfigureAwait(false);
                if (LabelPrintPages.PhysicalSectionsPerLabel(page) > 1)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var blank = GodexRaster.Blank(raster.Width, raster.Height);
                    await target.WritePageAsync(EncodePage(page, settings, blank), cancellationToken).ConfigureAwait(false);
                }
                progress?.Report((++done, total));
            }
        }, cancellationToken);
    }

    internal static byte[] EncodeContinuousBlock(IReadOnlyList<LabelLayout> pages, PrintSettings settings,
        Func<LabelLayout, PrintSettings, int, GodexRaster> render, long firstSection, int count,
        CancellationToken cancellationToken)
    {
        var first = pages[0];
        var sectionsPerLabel = LabelPrintPages.PhysicalSectionsPerLabel(first);
        var length = first.MediaHeightUm * count;
        var raster = GodexRaster.Blank(ToDots(first.MediaWidthUm), ToDots(length));
        for (var section = 0; section < count; section++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = firstSection + section;
            if (index % sectionsPerLabel != 0) continue;
            var page = pages[(int)(index / sectionsPerLabel % pages.Count)];
            var label = RenderPage(page, settings, render);
            var start = ToDots(section * first.MediaHeightUm);
            var end = ToDots((section + 1) * first.MediaHeightUm);
            CopySection(label, raster, start, end);
        }
        return EncodeRaster(first.MediaWidthUm, length, settings, raster, inlinePattern: true);
    }

    private static int ToDots(long microns) => (int)Math.Round(microns * Dpi / 25_400d);

    private static void CopySection(GodexRaster label, GodexRaster strip, int start, int end)
    {
        // Round the cumulative position so sub-dot differences cannot build up over the strip.
        var rows = end - start;
        for (var row = 0; row < rows; row++)
        {
            var source = (2 * row + 1) * label.Height / (2 * rows);
            label.Bits.AsSpan(source * label.Stride, label.Stride)
                .CopyTo(strip.Bits.AsSpan((start + row) * strip.Stride, strip.Stride));
        }
    }

    private static GodexRaster RenderPage(LabelLayout page, PrintSettings settings, Func<LabelLayout, PrintSettings, int, GodexRaster> render)
    {
        ValidateMedia(page);
        LabelPrintLayout.Create(page).EnsureFits(settings.OffsetXMm, settings.OffsetYMm, page.MediaWidthUm, page.MediaHeightUm);
        var raster = render(page, settings, Dpi);
        ValidateRaster(page, raster);
        return raster;
    }

    public static void ValidateConnection(PrintSettings settings)
    {
        var sdk = settings.PrintBackend == SdkBackend;
        if (!sdk && settings.PrintBackend != DirectBackend)
            throw new InvalidOperationException(L10n.T("Godex.BackendUnsupported"));
        if (sdk)
            GodexWindowsSdk.EnsureAvailable();

        switch (settings.ConnectionKind)
        {
            case "ethernet":
                if (string.IsNullOrWhiteSpace(settings.NetworkHost) ||
                    Uri.CheckHostName(settings.NetworkHost.Trim()) == UriHostNameType.Unknown)
                    throw new InvalidDataException(L10n.T("Setup.HostRequired"));
                break;
            case "serial":
                var serialPort = settings.SerialPort?.Trim() ?? string.Empty;
                if (!BaudRates.Contains(settings.SerialBaudRate))
                    throw new InvalidDataException(L10n.T("Godex.BaudInvalid"));
                if (sdk)
                {
                    if (!Regex.IsMatch(serialPort, "^COM[1-4]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                        throw new InvalidDataException(L10n.T("Godex.SdkSerialPort"));
                }
                else if (OperatingSystem.IsWindows()
                    ? !Regex.IsMatch(serialPort, "^COM[1-9][0-9]{0,2}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                    : !Regex.IsMatch(serialPort, "^/dev/(tty(S|USB|ACM)[0-9]+|serial/by-id/[A-Za-z0-9._:+-]+)$", RegexOptions.CultureInvariant))
                    throw new InvalidDataException(L10n.T("Godex.SerialRequired"));
                break;
            case "usb":
                if (sdk && string.IsNullOrWhiteSpace(settings.UsbDevice) && string.IsNullOrWhiteSpace(settings.PrinterName))
                    throw new InvalidDataException(L10n.T("Godex.UsbSdkRequired"));
                if (!sdk && (!OperatingSystem.IsLinux() ||
                    !Regex.IsMatch(settings.UsbDevice?.Trim() ?? string.Empty, "^/dev/(usb/lp|usblp)[0-9]+$", RegexOptions.CultureInvariant)))
                    throw new InvalidDataException(L10n.T("Godex.UsbDirectRequired"));
                break;
            default:
                throw new InvalidDataException(L10n.T("Setup.NoConnection"));
        }
    }

    public static void ValidateParameters(PrintSettings settings)
    {
        if (settings.Temperature is < 0 or > 19)
            throw new InvalidDataException(L10n.T("Godex.DarknessInvalid"));
        if (settings.CutOffsetMm is double stop && (!double.IsFinite(stop) || stop < 0 || stop > 40 || stop != Math.Truncate(stop)))
            throw new InvalidDataException(L10n.T("Godex.StopInvalid"));
        if (settings.MediaGapMm is < 0 or > 100)
            throw new InvalidDataException(L10n.T("Godex.GapInvalid"));
    }

    private static void ValidateMedia(LabelLayout page)
    {
        if (page.MediaWidthUm is < 25_400 or > 118_000 || page.MediaHeightUm is < 4_000 or > MaxPrintLengthUm)
            throw new InvalidDataException(string.Format(L10n.T("Setup.MediaLimits"), 25.4, 118, 4, 762));
    }

    internal static void ValidateMediaGap(LabelLayout page, PrintSettings settings)
    {
        if (page.AlternateNotch is not null && settings.MediaGapMm != 0)
            throw new InvalidDataException(L10n.T("Print.ContinuousMediaGap"));
    }

    private static void ValidateRaster(LabelLayout page, GodexRaster raster)
    {
        if (raster.Width != ToDots(page.MediaWidthUm) || raster.Height != ToDots(page.MediaHeightUm))
            throw new InvalidDataException(L10n.T("Godex.RasterSize"));

        // The G530 may feed wider material than it can address. Discard blank columns only.
        for (var y = 0; y < raster.Height; y++)
        for (var i = HeadDots / 8; i < raster.Stride; i++)
            if (raster.Bits[y * raster.Stride + i] != 0)
                throw new InvalidDataException(string.Format(L10n.T("Print.HeadWidth"), HeadWidthMm));
    }

    internal static byte[] EncodePage(LabelLayout page, PrintSettings settings, GodexRaster raster)
    {
        ValidateMedia(page);
        ValidateParameters(settings);
        ValidateMediaGap(page, settings);
        ValidateRaster(page, raster);
        return EncodeRaster(page.MediaWidthUm, page.MediaHeightUm, settings, raster, inlinePattern: false);
    }

    private static byte[] EncodeRaster(long widthUm, long lengthUm, PrintSettings settings, GodexRaster raster, bool inlinePattern)
    {
        var width = Math.Min(Units.UmToMm(widthUm), HeadWidthMm);
        var length = Units.UmToMm(lengthUm);
        var media = settings.MediaGapMm == 0
            ? FormattableString.Invariant($"^Q{length:0.###},0,0\r\n")
            : FormattableString.Invariant($"^Q{length:0.###},{settings.MediaGapMm:0.###}\r\n");
        var setup = new StringBuilder(media)
            .Append(FormattableString.Invariant($"^W{width:0.###}\r\n^P1\r\n^C1\r\n^R0\r\n"));
        if (settings.Temperature is int darkness)
            setup.Append(FormattableString.Invariant($"^H{darkness:00}\r\n"));
        if (settings.CutOffsetMm is double stop)
            setup.Append(FormattableString.Invariant($"^E{stop:0}\r\n"));

        var bytesPerRow = Math.Min(raster.Stride, HeadDots / 8);
        if (inlinePattern)
        {
            // Inline Q uses even-byte rows and CR directly before the binary bitmap.
            // This matches EZio64.dll putimage and the EZPL manual's Pattern command.
            var paddedStride = (bytesPerRow + 1) & ~1;
            setup.Append(FormattableString.Invariant($"^L\r\nQ0,0,{paddedStride},{raster.Height}\r"));
            var patternHeader = Encoding.ASCII.GetBytes(setup.ToString());
            using var pattern = new MemoryStream(patternHeader.Length + paddedStride * raster.Height + 3);
            pattern.Write(patternHeader);
            for (var y = 0; y < raster.Height; y++)
            {
                pattern.Write(raster.Bits, y * raster.Stride, bytesPerRow);
                if (paddedStride != bytesPerRow) pattern.WriteByte(0);
            }
            pattern.Write("E\r\n"u8);
            return pattern.ToArray();
        }

        setup.Append("~G\r\n");
        var header = Encoding.ASCII.GetBytes(setup.ToString());
        using var buffer = new MemoryStream(header.Length + raster.Height * (bytesPerRow + 2) + 3);
        buffer.Write(header);
        for (var y = 0; y < raster.Height; y++)
        {
            // GoDEX EZPL Programmer's Manual, Rev. J §4 Gwxxx and appendix 12.
            buffer.WriteByte((byte)'G');
            buffer.WriteByte((byte)bytesPerRow);
            buffer.Write(raster.Bits, y * raster.Stride, bytesPerRow);
        }
        buffer.Write("E\r\n"u8);
        return buffer.ToArray();
    }

    private static async Task<IGodexPrintTarget> OpenTargetAsync(PrintSettings settings, CancellationToken token)
    {
        if (settings.PrintBackend == SdkBackend)
            return await GodexWindowsSdk.OpenAsync(settings, token).ConfigureAwait(false);

        if (settings.ConnectionKind == "serial")
            return new SerialTarget(settings.SerialPort!.Trim(), settings.SerialBaudRate);

        if (settings.ConnectionKind == "usb")
            return new StreamTarget(new FileStream(settings.UsbDevice!.Trim(), FileMode.Open, FileAccess.Write, FileShare.None));

        var client = new TcpClient { NoDelay = true };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            try { await client.ConnectAsync(settings.NetworkHost!.Trim(), NetworkPort, timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { throw new TimeoutException(L10n.T("Godex.Timeout")); }
            return new StreamTarget(client.GetStream());
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private sealed class StreamTarget(Stream stream) : IGodexPrintTarget
    {
        public async ValueTask WritePageAsync(byte[] bytes, CancellationToken token)
        {
            // Cancelling a batch between labels keeps the printer's binary graphics mode in sync.
            token.ThrowIfCancellationRequested();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await stream.WriteAsync(bytes, timeout.Token).ConfigureAwait(false);
                await stream.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            { throw new TimeoutException(L10n.T("Godex.Timeout")); }
        }

        public void Dispose() => stream.Dispose();
    }

    private sealed class SerialTarget : IGodexPrintTarget
    {
        private readonly SerialPort _port;

        public SerialTarget(string port, int baud)
        {
            _port = new SerialPort(port, baud, Parity.None, 8, StopBits.One)
                { WriteTimeout = 8_000, Handshake = Handshake.RequestToSendXOnXOff };
            try { _port.Open(); }
            catch { _port.Dispose(); throw; }
        }

        public ValueTask WritePageAsync(byte[] bytes, CancellationToken token)
        {
            // Finish this label if cancellation arrives mid-frame, otherwise the next job can be
            // interpreted as its missing binary data by the printer.
            token.ThrowIfCancellationRequested();
            for (var offset = 0; offset < bytes.Length; offset += 2048)
                _port.Write(bytes, offset, Math.Min(2048, bytes.Length - offset));
            return ValueTask.CompletedTask;
        }

        public void Dispose() => _port.Dispose();
    }
}

internal interface IGodexPrintTarget : IDisposable
{
    ValueTask WritePageAsync(byte[] bytes, CancellationToken token);
}
