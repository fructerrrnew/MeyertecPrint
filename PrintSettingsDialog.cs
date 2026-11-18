using System.Drawing.Printing;
using System.Net.Sockets;

namespace MeyertecPrint;

/// <summary>Настройка задания в интерфейсе MEYERTEC. Передача — через установленный драйвер Windows.</summary>
public sealed class PrintSettingsDialog : BrandForm
{
    private readonly ComboBox _models = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _connection = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _queues = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _host = new() { Dock = DockStyle.Fill };
    private readonly Label _port = new() { Dock = DockStyle.Fill, ForeColor = Theme.SecondaryText, AutoEllipsis = true };
    private readonly Label _status = new() { Dock = DockStyle.Fill, ForeColor = Theme.SecondaryText };
    private readonly NumericUpDown _width = Number(1, 300, 60), _height = Number(1, 762, 30);
    private readonly NumericUpDown _offsetX = Number(-50, 50, 0), _offsetY = Number(-50, 50, 0);
    private readonly NumericUpDown _copies = Number(1, 500, 1, 0);
    private readonly CheckBox _imported = new() { AutoSize = true };
    private readonly NumericUpDown _from = Number(1, 1, 1, 0), _to = Number(1, 1, 1, 0);
    private readonly int _importedCount;
    private readonly Button _test = new() { Dock = DockStyle.Fill };
    private sealed record ConnectionOption(string Key, string Caption) { public override string ToString() => Caption; }
    private readonly bool _forPrint;
    private readonly PrintSettings _original;
    private bool _updating;
    public PrintSettings Result { get; private set; } = new();
    public string? PrinterModel => _models.SelectedItem as string;
    public long MediaWidthUm => Units.MmToUm((double)_width.Value);
    public long MediaHeightUm => Units.MmToUm((double)_height.Value);
    public bool IncludeImported => _imported.Checked;
    public int RangeFrom => (int)_from.Value;
    public int RangeTo => (int)_to.Value;

    public PrintSettingsDialog(ProjectFile project, LabelLayout layout, int importedCount, bool forPrint)
    {
        _forPrint = forPrint;
        _importedCount = importedCount;
        _original = project.Print ?? new PrintSettings();
        Text = L10n.T(forPrint ? "Setup.PrintTitle" : "PrintSettings.Title");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowIcon = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(660, 620);
        Font = Theme.UiFont();
        BackColor = Color.White;
        var title = new Label { Dock = DockStyle.Top, Height = 65, Padding = new Padding(22, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Black, ForeColor = Color.White,
            Font = Theme.UiFont(14, FontStyle.Bold), Text = L10n.T(forPrint ? "Setup.PrintTitle" : "PrintSettings.Title") };
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 11,
            Padding = new Padding(22, 12, 22, 4), AutoScroll = true, BackColor = Color.White };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        for (var i = 0; i < 9; i++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        Add(fields, 0, "PrintSettings.Model", _models);
        Add(fields, 1, "Setup.Connection", _connection);
        Add(fields, 2, "Setup.Queue", _queues);
        Add(fields, 3, "Setup.Port", _port);
        var hostLine = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        hostLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
        hostLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        hostLine.Controls.Add(_host, 0, 0); hostLine.Controls.Add(_test, 1, 0);
        _test.Text = L10n.T("Setup.Test"); Theme.StyleButton(_test);
        Add(fields, 4, "Setup.Host", hostLine);
        Add(fields, 5, "Setup.Width", _width);
        Add(fields, 6, "Setup.Height", _height);
        Add(fields, 7, "Setup.OffsetX", _offsetX);
        Add(fields, 8, "Setup.OffsetY", _offsetY);
        Add(fields, 9, "Setup.Copies", _copies);
        _status.Text = L10n.T("Setup.DriverHint");
        fields.Controls.Add(_status, 0, 10); fields.SetColumnSpan(_status, 2);
        var extras = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 70, Padding = new Padding(22, 2, 22, 0),
            BackColor = Color.White, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        _imported.Text = string.Format(L10n.T("Setup.Imported"), importedCount);
        _imported.Enabled = importedCount > 0;
        _imported.Checked = importedCount > 0;
        extras.Controls.Add(_imported);
        var range = new FlowLayoutPanel { Width = 550, Height = 35, WrapContents = false };
        range.Controls.Add(new Label { Text = L10n.T("Setup.Range"), AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
        _from.Dock = _to.Dock = DockStyle.None; _from.Width = _to.Width = 80;
        range.Controls.Add(_from);
        range.Controls.Add(new Label { Text = "–", AutoSize = true, Margin = new Padding(8, 6, 8, 0) });
        range.Controls.Add(_to); extras.Controls.Add(range);
        _imported.CheckedChanged += (_, _) => UpdateRange();
        UpdateRange();
        extras.Visible = forPrint;
        var ok = new Button { Text = L10n.T(forPrint ? "Setup.Print" : "Dialog.OK"), Width = 110 };
        var cancel = new Button { Text = L10n.T("Dialog.Cancel"), DialogResult = DialogResult.Cancel };
        ok.Click += OnAccept;
        _test.Click += async (_, _) => await TestNetwork();
        var footer = Theme.DialogFooter(ok, cancel);
        Controls.Add(fields); Controls.Add(extras); Controls.Add(footer); Controls.Add(title);
        AcceptButton = ok; CancelButton = cancel;

        _updating = true;
        foreach (var model in PrinterPlugins.All) _models.Items.Add(model.Name);
        if (_models.Items.Count > 0) _models.SelectedItem = project.PrinterModel ?? _models.Items[0];
        if (_models.SelectedIndex < 0 && _models.Items.Count > 0) _models.SelectedIndex = 0;
        PopulateConnections(project.Print?.ConnectionKind);
        try { foreach (string queue in PrinterSettings.InstalledPrinters) _queues.Items.Add(queue); }
        catch (Exception ex) when (ex is InvalidPrinterException or System.ComponentModel.Win32Exception) { }
        var saved = project.Print?.PrinterName;
        if (saved is not null && _queues.Items.Contains(saved)) _queues.SelectedItem = saved;
        else
        {
            var godex = _queues.Items.Cast<string>().FirstOrDefault(x => x.Contains("G530", StringComparison.OrdinalIgnoreCase));
            if (godex is not null) _queues.SelectedItem = godex;
        }
        _host.Text = project.Print?.NetworkHost ?? string.Empty;
        _width.Value = Math.Clamp((decimal)Units.UmToMm(layout.MediaWidthUm), _width.Minimum, _width.Maximum);
        _height.Value = Math.Clamp((decimal)Units.UmToMm(layout.MediaHeightUm), _height.Minimum, _height.Maximum);
        _offsetX.Value = Math.Clamp((decimal)(project.Print?.OffsetXMm ?? 0), _offsetX.Minimum, _offsetX.Maximum);
        _offsetY.Value = Math.Clamp((decimal)(project.Print?.OffsetYMm ?? 0), _offsetY.Minimum, _offsetY.Maximum);
        _copies.Value = Math.Clamp(project.Print?.Copies ?? 1, (int)_copies.Minimum, (int)_copies.Maximum);
        _updating = false;
        _queues.SelectedIndexChanged += (_, _) => UpdatePort(true);
        _connection.SelectedIndexChanged += (_, _) => UpdatePort(false);
        _models.SelectedIndexChanged += (_, _) => { PopulateConnections((_connection.SelectedItem as ConnectionOption)?.Key); UpdatePort(true); };
        UpdatePort(string.IsNullOrWhiteSpace(_host.Text));
    }

    private void UpdateRange()
    {
        var maximum = _imported.Checked ? 1 + _importedCount : 1;
        _from.Maximum = _to.Maximum = maximum;
        _from.Value = 1; _to.Value = maximum;
    }

    private static NumericUpDown Number(decimal minimum, decimal maximum, decimal value, int decimals = 3) => new()
    { Minimum = minimum, Maximum = maximum, Value = value, DecimalPlaces = decimals, Increment = decimals == 0 ? 1 : 0.1m, Dock = DockStyle.Fill };

    private static void Add(TableLayoutPanel panel, int row, string key, Control control)
    {
        panel.Controls.Add(new Label { Text = L10n.T(key), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Dark }, 0, row);
        control.Margin = new Padding(0, 4, 0, 4);
        panel.Controls.Add(control, 1, row);
    }

    private void PopulateConnections(string? preferred)
    {
        var model = PrinterPlugins.All.FirstOrDefault(x => x.Name == _models.SelectedItem as string);
        _connection.Items.Clear();
        if (model is null) return;
        foreach (var key in model.Connections.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(key, "usb", StringComparison.OrdinalIgnoreCase)) _connection.Items.Add(new ConnectionOption("usb", "USB-B"));
            if (string.Equals(key, "ethernet", StringComparison.OrdinalIgnoreCase)) _connection.Items.Add(new ConnectionOption("ethernet", "Ethernet"));
        }
        var match = _connection.Items.Cast<ConnectionOption>().FirstOrDefault(x => x.Key == preferred);
        if (match is not null) _connection.SelectedItem = match;
        else if (_connection.Items.Count > 0) _connection.SelectedIndex = 0;
    }

    private void UpdatePort(bool updateHost)
    {
        if (_updating) return;
        var ethernet = (_connection.SelectedItem as ConnectionOption)?.Key == "ethernet";
        _host.Enabled = _test.Enabled = ethernet;
        var port = PrinterQueues.GetPort(_queues.SelectedItem as string ?? string.Empty);
        _port.Text = string.IsNullOrEmpty(port) ? L10n.T("Setup.UnknownPort") : port;
        if (updateHost && ethernet) _host.Text = PrinterQueues.GetNetworkHost(port);
        _status.Text = L10n.T(ethernet ? "Setup.NetworkHint" : "Setup.DriverHint");
    }

    private async Task TestNetwork()
    {
        var address = _host.Text.Trim();
        if (string.IsNullOrWhiteSpace(address)) { _status.Text = L10n.T("Setup.HostRequired"); return; }
        _test.Enabled = false;
        _status.Text = L10n.T("Setup.Checking");
        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(address, 9100, timeout.Token);
            _status.Text = L10n.T("Setup.Reachable");
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ArgumentException)
        { _status.Text = string.Format(L10n.T("Setup.Unreachable"), ex.Message); }
        finally { if (!IsDisposed) _test.Enabled = (_connection.SelectedItem as ConnectionOption)?.Key == "ethernet"; }
    }

    private void OnAccept(object? sender, EventArgs e)
    {
        if (_forPrint && _from.Value > _to.Value) { _status.Text = L10n.T("Setup.InvalidRange"); return; }
        if (_models.SelectedItem is not string model)
        { _status.Text = L10n.T("Setup.NoModel"); return; }
        var plugin = PrinterPlugins.All.First(x => x.Name == model);
        var minWidth = plugin.MinMediaWidthMm > 0 ? plugin.MinMediaWidthMm : 1;
        var maxWidth = plugin.MaxMediaWidthMm > 0 ? plugin.MaxMediaWidthMm : plugin.MaxPrintWidthMm;
        var minLength = plugin.MinPrintLengthMm > 0 ? plugin.MinPrintLengthMm : 1;
        if ((double)_width.Value < minWidth || (double)_width.Value > maxWidth ||
            (double)_height.Value < minLength || (double)_height.Value > plugin.MaxPrintLengthMm)
        { _status.Text = string.Format(L10n.T("Setup.MediaLimits"), minWidth, maxWidth, minLength, plugin.MaxPrintLengthMm); return; }
        if (_forPrint && (double)_width.Value > plugin.MaxPrintWidthMm &&
            BrandMessageBox.Show(this, string.Format(L10n.T("Setup.WideMedia"), plugin.MaxPrintWidthMm),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        var queue = _queues.SelectedItem as string;
        if (_forPrint && string.IsNullOrWhiteSpace(queue)) { _status.Text = L10n.T("Print.NoPrinter"); return; }
        if (_connection.SelectedItem is not ConnectionOption) { _status.Text = L10n.T("Setup.NoConnection"); return; }
        var ethernet = (_connection.SelectedItem as ConnectionOption)?.Key == "ethernet";
        if (!ethernet && !string.IsNullOrEmpty(queue) && !string.IsNullOrEmpty(PrinterQueues.GetNetworkHost(PrinterQueues.GetPort(queue))))
        { _status.Text = L10n.T("Setup.UsbMismatch"); return; }
        var host = _host.Text.Trim();
        if (ethernet && !string.IsNullOrEmpty(queue))
        {
            var match = PrinterQueues.CompareNetworkHost(PrinterQueues.GetPort(queue), host);
            if (match == PrinterQueues.HostMatch.Mismatch)
            { _status.Text = L10n.T("Setup.HostMismatch"); return; }
            if (match == PrinterQueues.HostMatch.Unknown &&
                BrandMessageBox.Show(this, L10n.T("Setup.QueueUnverified"), Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }
        if (_forPrint && BrandMessageBox.Show(this, L10n.T("Setup.ConfirmMedia"), Text,
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        Result = new PrintSettings { PrinterName = queue, ConnectionKind = ethernet ? "ethernet" : "usb",
            NetworkHost = ethernet ? host : null, OffsetXMm = (double)_offsetX.Value, OffsetYMm = (double)_offsetY.Value,
            Copies = (int)_copies.Value, Temperature = _original.Temperature, CutOffsetMm = _original.CutOffsetMm };
        DialogResult = DialogResult.OK;
        Close();
    }
}
