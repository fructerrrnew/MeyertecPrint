using System.Drawing.Printing;

namespace MeyertecPrint;

/// <summary>Preflight and spool on a dedicated STA thread; cancellation can leave a partial job.</summary>
public sealed class PrintProgressDialog : BrandForm
{
    private readonly IReadOnlyList<LabelLayout> _pages;
    private readonly int _start, _count;
    private readonly PrintSettings _settings;
    private readonly PrinterPlugin? _plugin;
    private readonly CancellationTokenSource _cancel = new();
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Top, Height = 24, Maximum = 1000 };
    private readonly Label _status = new() { Dock = DockStyle.Top, Height = 70, Padding = new Padding(15), AutoEllipsis = true };
    private readonly Button _cancelButton = new() { Width = 120 };
    private bool _done;
    public Exception? Error { get; private set; }
    public bool WasCancelled { get; private set; }

    public PrintProgressDialog(IReadOnlyList<LabelLayout> pages, int from, int to, PrintSettings settings, PrinterPlugin? plugin)
    {
        SuspendLayout();
        _pages = pages; _start = from - 1; _count = to - from + 1; _settings = settings; _plugin = plugin;
        Text = L10n.T("Print.ProgressTitle"); StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MinimizeBox = MaximizeBox = false;
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(550, 214); Font = Theme.UiFont(); BackColor = Theme.Surface;
        _status.Text = L10n.T("Print.Preparing");
        _progress.AccessibleName = Text;
        var warning = new Label { Dock = DockStyle.Fill, Padding = new Padding(15), ForeColor = Theme.SecondaryText,
            Text = L10n.T("Print.CancelWarning") };
        _cancelButton.Text = L10n.T("Dialog.Cancel"); _cancelButton.Click += (_, _) => RequestCancel();
        Controls.Add(warning); Controls.Add(_progress); Controls.Add(_status); Controls.Add(Theme.DialogFooter(_cancelButton));
        CancelButton = _cancelButton;
        Shown += (_, _) =>
        {
            var thread = new Thread(Work) { IsBackground = true, Name = "MeyertecPrint job" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        };
        ResumeLayout();
    }
    private void RequestCancel()
    {
        _cancel.Cancel(); _cancelButton.Enabled = false; _status.Text = L10n.T("Print.Cancelling");
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_done) { e.Cancel = true; RequestCancel(); }
        base.OnFormClosing(e);
    }
    private void Report(string key, long value, long maximum)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke((Action)(() =>
            {
                if (_cancel.IsCancellationRequested) return;
                _status.Text = string.Format(L10n.T(key), value, maximum);
                _progress.Value = maximum <= 0 ? 0 : (int)Math.Clamp(value * 1000L / maximum, 0, 1000);
            }));
        }
        catch (InvalidOperationException) { }
    }
    private void Work()
    {
        Exception? error = null; var cancelled = false;
        try
        {
            var token = _cancel.Token;
            using var document = new PrintDocument { OriginAtMargins = false, PrintController = new StandardPrintController() };
            document.PrinterSettings.PrinterName = _settings.PrinterName ?? string.Empty;
            if (!document.PrinterSettings.IsValid) throw new InvalidPrinterException(document.PrinterSettings);
            MainForm.SetLabelPaper(document, _pages[_start]);
            document.PrinterSettings.Copies = 1;
            var resolution = document.DefaultPageSettings.PrinterResolution;
            var dpiX = resolution.X >= 72 ? resolution.X : _plugin?.ResolutionDpi ?? 300;
            var dpiY = resolution.Y >= 72 ? resolution.Y : _plugin?.ResolutionDpi ?? 300;
            for (var i = 0; i < _count; i++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var page = _pages[_start + i];
                    if (_plugin is not null) LabelPrintLayout.EnsurePrintWidth(page, _plugin.MaxPrintWidthMm);
                    using var bitmap = LabelPrintRenderer.Render(page, _settings, dpiX, dpiY, validateForPrint: true);
                }
                catch (InvalidDataException ex)
                { throw new InvalidDataException(string.Format(L10n.T("Print.PageError"), _start + i + 1, ex.Message), ex); }
                Report("Print.CheckingProgress", i + 1, _count);
            }
            token.ThrowIfCancellationRequested();
            long index = 0, total = (long)_count * _settings.Copies;
            document.PrintPage += (_, e) =>
            {
                if (token.IsCancellationRequested) { e.Cancel = true; e.HasMorePages = false; return; }
                var graphics = e.Graphics ?? throw new InvalidDataException(L10n.T("Print.InvalidGraphics"));
                var page = _pages[_start + (int)(index % _count)];
                using var bitmap = LabelPrintRenderer.Render(page, _settings, graphics.DpiX, graphics.DpiY, validateForPrint: true);
                LabelPrintRenderer.DrawToDevice(graphics, bitmap, e.PageSettings.HardMarginX, e.PageSettings.HardMarginY);
                index++;
                Report("Print.SpoolProgress", index, total);
                e.HasMorePages = index < total;
            };
            document.Print();
            token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) { cancelled = true; }
        catch (Exception ex) { error = ex; AppLog.Write(ex, "print job"); }
        try
        {
            BeginInvoke((Action)(() =>
            {
                Error = error; WasCancelled = cancelled; _done = true;
                DialogResult = error is null && !cancelled ? DialogResult.OK : DialogResult.Cancel;
                Close();
            }));
        }
        catch (InvalidOperationException) { }
    }
    protected override void Dispose(bool disposing) { if (disposing) _cancel.Dispose(); base.Dispose(disposing); }
}
