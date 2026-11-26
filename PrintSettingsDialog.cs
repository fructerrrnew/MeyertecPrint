using System.Drawing.Printing;
using System.IO.Ports;
using System.Net.Sockets;

namespace MeyertecPrint;

/// <summary>Настройка задания, транспорта и параметров подачи носителя.</summary>
public sealed class PrintSettingsDialog : BrandForm
{
    private readonly ComboBox _models = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _backend = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _connection = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _queues = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _host = new() { Dock = DockStyle.Fill };
    private readonly TextBox _usb = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _serial = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _baud = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Label _port = new()
    {
        Dock = DockStyle.Fill, AutoEllipsis = true, Height = 22, ForeColor = Theme.SecondaryText, UseMnemonic = false
    };
    private readonly Label _driverHint = new()
    {
        Dock = DockStyle.Fill, AutoSize = true, ForeColor = Theme.SecondaryText, Margin = new Padding(0, 4, 0, 0)
    };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(17, 8, 17, 4),
        ForeColor = Theme.SecondaryText, AccessibleRole = AccessibleRole.Alert, UseMnemonic = false
    };
    private readonly Label _summary = new()
    {
        Dock = DockStyle.Fill, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 5, 0, 0)
    };
    private readonly Label _stockHint = new()
    {
        Dock = DockStyle.Fill, AutoSize = true, ForeColor = Theme.SecondaryText, Margin = new Padding(0, 5, 0, 0)
    };
    private readonly Control _hostField;
    private readonly Control _queueField, _portField, _usbField, _serialField, _baudField, _gapField;
    private readonly Control _heatField, _stopField;
    private readonly TableLayoutPanel _range;
    private readonly ComboBox? _startSection;
    private readonly CheckBox? _alignToNotch;
    private readonly NumericUpDown _width = Number(1, 300, 60), _height = Number(1, 762, 30);
    private readonly NumericUpDown _offsetX = Number(-50, 50, 0), _offsetY = Number(-50, 50, 0);
    private readonly NumericUpDown _copies = Number(1, 500, 1, 0);
    private readonly NumericUpDown _heat = Number(0, 19, 10, 0), _stop = Number(0, 40, 0, 0);
    private readonly NumericUpDown _gap = Number(0, 100, 0);
    private readonly CheckBox _setHeat = new() { AutoSize = true }, _setStop = new() { AutoSize = true };
    private readonly CheckBox _imported = new() { AutoSize = true };
    private readonly NumericUpDown _from = Number(1, 1, 1, 0), _to = Number(1, 1, 1, 0);
    private readonly int _importedCount;
    private readonly Button _test = new()
    {
        Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(0, 28), Margin = new Padding(7, 0, 0, 0)
    };
    private sealed record ConnectionOption(string Key, string Caption) { public override string ToString() => Caption; }
    private readonly bool _forPrint;
    private readonly bool _hasNotch;
    private readonly PrintSettings _original;
    private bool _updating;
    private bool _testing;
    private int _connectionRevision;
    public PrintSettings Result { get; private set; } = new();
    public string? PrinterModel => _models.SelectedItem as string;
    public long MediaWidthUm => Units.MmToUm((double)_width.Value);
    public long MediaHeightUm => Units.MmToUm((double)_height.Value);
    public bool IncludeImported => _imported.Checked;
    public int RangeFrom => (int)_from.Value;
    public int RangeTo => (int)_to.Value;
    public bool StartOnNotchedSection => !UseNotchAlignment && _startSection?.SelectedIndex == 1;

    public PrintSettingsDialog(ProjectFile project, LabelLayout layout, int importedCount, bool forPrint)
    {
        SuspendLayout();
        _forPrint = forPrint;
        _hasNotch = layout.AlternateNotch is not null;
        _importedCount = importedCount;
        _original = project.Print ?? new PrintSettings();
        Text = L10n.T(forPrint ? "Setup.PrintTitle" : "PrintSettings.Title");
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowIcon = false;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(760, forPrint ? 490 : 420);
        MinimumSize = new Size(615, 385);
        Font = Theme.UiFont();
        BackColor = Theme.Surface;

        var printer = Section("Setup.PrinterSection");
        Add(printer, 1, LabeledField("PrintSettings.Model", _models), LabeledField("Godex.Backend", _backend));
        Add(printer, 2, LabeledField("Setup.Connection", _connection));
        _queueField = LabeledField("Setup.Queue", _queues);
        _portField = LabeledField("Setup.Port", _port);
        Add(printer, 3, _queueField);
        Add(printer, 4, _portField);
        var hostLine = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, RowCount = 1, Margin = Padding.Empty
        };
        hostLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        hostLine.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        hostLine.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _host.Dock = DockStyle.None;
        _host.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _host.Margin = Padding.Empty;
        _host.AccessibleName = L10n.T("Setup.Host");
        _test.Text = L10n.T("Setup.Test");
        _test.Padding = new Padding(6, 0, 6, 0);
        Theme.StyleButton(_test);
        hostLine.Controls.Add(_host, 0, 0);
        hostLine.Controls.Add(_test, 1, 0);
        _hostField = LabeledField("Setup.Host", hostLine);
        Add(printer, 5, _hostField);
        _usbField = LabeledField("Godex.UsbId", _usb);
        _serialField = LabeledField("Godex.SerialPort", _serial);
        _baudField = LabeledField("Godex.Baud", _baud);
        Add(printer, 6, _usbField);
        Add(printer, 7, _serialField, _baudField);
        Add(printer, 8, _driverHint);

        var job = Section("Setup.MediaSection");
        var jobRow = 1;
        Add(job, jobRow++, LabeledField("Setup.Width", _width),
            LabeledField(_hasNotch ? "Setup.FeedPitch" : "Setup.Height", _height));
        if (_hasNotch)
        {
            _alignToNotch = new CheckBox
            {
                Text = L10n.T("Print.AlignToNotch"), AutoSize = true, Checked = _original.AlignToNotch,
                Margin = new Padding(0, 3, 0, 9)
            };
            Add(job, jobRow++, _alignToNotch);
            _alignToNotch.CheckedChanged += (_, _) => UpdateNotchAlignment();
        }
        if (forPrint && _hasNotch)
        {
            _startSection = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            _startSection.Items.Add(L10n.T("Print.SectionFull"));
            _startSection.Items.Add(L10n.T("Print.SectionNotched"));
            _startSection.SelectedIndex = 0;
            Add(job, jobRow++, LabeledField("Print.StartSection", _startSection));
            _startSection.SelectedIndexChanged += (_, _) => UpdateSummary();
        }
        Add(job, jobRow++, LabeledField("Setup.OffsetX", _offsetX), LabeledField("Setup.OffsetY", _offsetY));
        Add(job, jobRow++, LabeledField("Setup.Copies", _copies));
        _heatField = OptionalField("Godex.Darkness", _setHeat, _heat);
        _stopField = OptionalField("Godex.Stop", _setStop, _stop);
        _gapField = LabeledField("Godex.Gap", _gap);
        Add(job, jobRow++, _heatField, _stopField);
        Add(job, jobRow++, _gapField);
        _imported.Text = string.Format(L10n.T("Godex.AdditionalRecords"), importedCount);
        _imported.Checked = importedCount > 0;
        _imported.Margin = new Padding(0, 3, 0, 9);
        _imported.Visible = forPrint && importedCount > 0;
        Add(job, jobRow++, _imported);
        _range = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, RowCount = 1, Margin = Padding.Empty
        };
        _range.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _range.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        Add(_range, 0, LabeledField("Setup.RangeFrom", _from), LabeledField("Setup.RangeTo", _to));
        Add(job, jobRow++, _range);
        _summary.Visible = forPrint;
        Add(job, jobRow++, _summary);
        _stockHint.Visible = _hasNotch;
        Add(job, jobRow, _stockHint);
        _imported.CheckedChanged += (_, _) => UpdateRange();
        _from.ValueChanged += (_, _) => UpdateSummary();
        _to.ValueChanged += (_, _) => UpdateSummary();
        _copies.ValueChanged += (_, _) => UpdateSummary();

        var columns = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, RowCount = 1, Padding = new Padding(15, 14, 15, 12)
        };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        columns.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        printer.Margin = new Padding(0, 0, 8, 0);
        job.Margin = new Padding(8, 0, 0, 0);
        columns.Controls.Add(printer, 0, 0);
        columns.Controls.Add(job, 1, 0);
        columns.SizeChanged += (_, _) =>
        {
            var width = (columns.ClientSize.Width - columns.Padding.Horizontal) / 2;
            var limit = new Size(Math.Max(1, width - printer.Margin.Horizontal - printer.Padding.Horizontal), 0);
            if (_driverHint.MaximumSize != limit) _driverHint.MaximumSize = limit;
            if (_stockHint.MaximumSize != limit) _stockHint.MaximumSize = limit;
        };
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(columns);
        var ok = new Button { Text = L10n.T(forPrint ? "Setup.Print" : "Dialog.Apply") };
        var cancel = new Button { Text = L10n.T("Dialog.Cancel"), DialogResult = DialogResult.Cancel };
        ok.Click += OnAccept;
        _test.Click += async (_, _) => await TestNetwork();
        Controls.Add(scroll);
        Controls.Add(_status);
        Controls.Add(Theme.DialogFooter(ok, cancel));
        AcceptButton = ok;
        CancelButton = cancel;

        _updating = true;
        foreach (var model in PrinterPlugins.All) _models.Items.Add(model.Name);
        if (_models.Items.Count > 0) _models.SelectedItem = project.PrinterModel ?? _models.Items[0];
        if (_models.SelectedIndex < 0 && _models.Items.Count > 0) _models.SelectedIndex = 0;
        PopulateBackends(_original.PrintBackend);
        PopulateConnections(_original.ConnectionKind);
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
        _usb.Text = _original.UsbDevice ?? string.Empty;
        try { _serial.Items.AddRange(SerialPort.GetPortNames().Cast<object>().ToArray()); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { }
        _serial.Text = _original.SerialPort ?? string.Empty;
        _baud.Items.AddRange(GodexPrintJob.BaudRates.Cast<object>().ToArray());
        _baud.SelectedItem = GodexPrintJob.BaudRates.Contains(_original.SerialBaudRate) ? _original.SerialBaudRate : 9600;
        _setHeat.Checked = _original.Temperature.HasValue;
        _heat.Value = Math.Clamp(_original.Temperature ?? 10, 0, 19);
        _setStop.Checked = _original.CutOffsetMm.HasValue;
        _stop.Value = _original.CutOffsetMm is double stop && double.IsFinite(stop)
            ? (decimal)Math.Clamp(Math.Round(stop), 0, 40) : 0;
        _gap.Value = _hasNotch ? 0 : Math.Clamp(_original.MediaGapMm, 0, 100);
        if (_hasNotch) _height.Minimum = 4;
        _width.Value = Math.Clamp((decimal)Units.UmToMm(layout.MediaWidthUm), _width.Minimum, _width.Maximum);
        _height.Value = Math.Clamp((decimal)Units.UmToMm(layout.MediaHeightUm), _height.Minimum, _height.Maximum);
        _width.Enabled = _gap.Enabled = !_hasNotch;
        _offsetX.Value = Math.Clamp((decimal)(project.Print?.OffsetXMm ?? 0), _offsetX.Minimum, _offsetX.Maximum);
        _offsetY.Value = Math.Clamp((decimal)(project.Print?.OffsetYMm ?? 0), _offsetY.Minimum, _offsetY.Maximum);
        _copies.Value = Math.Clamp(project.Print?.Copies ?? 1, (int)_copies.Minimum, (int)_copies.Maximum);
        _updating = false;
        _queues.SelectedIndexChanged += (_, _) => UpdatePort(true);
        _connection.SelectedIndexChanged += (_, _) => UpdatePort(false);
        _backend.SelectedIndexChanged += (_, _) => RefreshDestination();
        _models.SelectedIndexChanged += (_, _) => RefreshDestination(refreshBackends: true);
        _host.TextChanged += (_, _) =>
        {
            _connectionRevision++;
            if (_testing) ShowStatus(string.Empty);
        };
        UpdateRange();
        UpdatePort(string.IsNullOrWhiteSpace(_host.Text));
        ResumeLayout();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Theme.FitDialogToScreen(this);
    }

    private void UpdateRange()
    {
        var maximum = _imported.Checked ? 1 + _importedCount : 1;
        _from.Maximum = _to.Maximum = maximum;
        _from.Value = 1;
        _to.Value = maximum;
        _range.Visible = _forPrint && _imported.Checked;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var count = Math.Max(0, _to.Value - _from.Value + 1) * _copies.Value;
        _summary.Text = string.Format(L10n.T("Setup.PageTotal"), count);
        if (_hasNotch)
        {
            _stockHint.Text = L10n.T("Setup.FixedNotchedStock") + Environment.NewLine + L10n.T("Setup.OffsetCalibration");
            if (UseNotchAlignment)
                _stockHint.Text += Environment.NewLine + L10n.T("Print.AlignToNotchHint");
            else if (_forPrint)
                _stockHint.Text += Environment.NewLine + L10n.T(StartOnNotchedSection ? "Print.StartNotchedHint" : "Print.StartPlain");
        }
    }

    private static NumericUpDown Number(decimal minimum, decimal maximum, decimal value, int decimals = 3) => new()
    {
        Minimum = minimum, Maximum = maximum, Value = value, DecimalPlaces = decimals,
        Increment = decimals == 0 ? 1 : 0.1m, Dock = DockStyle.Fill, TextAlign = HorizontalAlignment.Right
    };

    private static TableLayoutPanel Section(string title)
    {
        var section = new TableLayoutPanel
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2,
            BackColor = SystemInformation.HighContrast ? Theme.Surface : Theme.RailSurface, Padding = new Padding(13)
        };
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        Add(section, 0, new Label
        {
            Dock = DockStyle.Fill, AutoSize = true, Text = L10n.T(title),
            Font = Theme.UiFont(12, FontStyle.Bold), ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, 12)
        });
        return section;
    }

    private static TableLayoutPanel LabeledField(string key, Control field)
    {
        var name = L10n.T(key);
        field.AccessibleName ??= name;
        field.Margin = Padding.Empty;
        var pair = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 0, 10)
        };
        pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pair.Controls.Add(new Label
        {
            Text = name, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, 4)
        }, 0, 0);
        pair.Controls.Add(field, 0, 1);
        return pair;
    }

    private static TableLayoutPanel OptionalField(string key, CheckBox check, NumericUpDown field)
    {
        check.Text = L10n.T(key);
        check.Margin = new Padding(0, 0, 0, 4);
        field.AccessibleName = check.Text;
        field.Margin = Padding.Empty;
        var pair = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 0, 10)
        };
        pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pair.Controls.Add(check, 0, 0);
        pair.Controls.Add(field, 0, 1);
        check.CheckedChanged += (_, _) => field.Enabled = check.Checked;
        field.Enabled = check.Checked;
        return pair;
    }

    private static void Add(TableLayoutPanel panel, int row, Control first, Control? second = null)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(first, 0, row);
        if (second is null)
        {
            panel.SetColumnSpan(first, 2);
            return;
        }
        second.Margin = new Padding(10, second.Margin.Top, second.Margin.Right, second.Margin.Bottom);
        panel.Controls.Add(second, 1, row);
    }

    private void PopulateConnections(string? preferred)
    {
        var model = PrinterPlugins.All.FirstOrDefault(x => x.Name == _models.SelectedItem as string);
        _connection.Items.Clear();
        if (model is null) return;
        var direct = SelectedBackend == GodexPrintJob.DirectBackend;
        foreach (var key in model.Connections.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            switch (key.ToLowerInvariant())
            {
                case "usb" when !direct: _connection.Items.Add(new ConnectionOption("usb", "USB-B")); break;
                case "ethernet": _connection.Items.Add(new ConnectionOption("ethernet", "RJ45 / Ethernet")); break;
                case "serial": _connection.Items.Add(new ConnectionOption("serial", "RS-232")); break;
            }
        }
        var match = _connection.Items.Cast<ConnectionOption>().FirstOrDefault(x => x.Key == preferred);
        if (match is not null) _connection.SelectedItem = match;
        else if (_connection.Items.Count > 0) _connection.SelectedIndex = 0;
    }

    private string SelectedBackend => (_backend.SelectedItem as ConnectionOption)?.Key ?? GodexPrintJob.DriverBackend;

    private bool CanAlignToNotch => _hasNotch &&
        (SelectedBackend is GodexPrintJob.SdkBackend or GodexPrintJob.DirectBackend) &&
        ((_connection.SelectedItem as ConnectionOption)?.Key is "usb" or "ethernet");

    private bool UseNotchAlignment => CanAlignToNotch && _alignToNotch?.Checked == true;

    private void PopulateBackends(string preferred)
    {
        _backend.Items.Clear();
        _backend.Items.Add(new ConnectionOption(GodexPrintJob.DriverBackend, L10n.T("Godex.Driver")));
        if (string.Equals(_models.SelectedItem as string, "G530", StringComparison.OrdinalIgnoreCase))
        {
            _backend.Items.Add(new ConnectionOption(GodexPrintJob.SdkBackend, "GoDEX SDK (EZio)"));
            _backend.Items.Add(new ConnectionOption(GodexPrintJob.DirectBackend, L10n.T("Godex.Direct")));
        }
        _backend.SelectedItem = _backend.Items.Cast<ConnectionOption>().FirstOrDefault(item => item.Key == preferred)
            ?? _backend.Items[0];
    }

    private void RefreshDestination(bool refreshBackends = false)
    {
        if (_updating) return;
        var connection = (_connection.SelectedItem as ConnectionOption)?.Key;
        var backend = SelectedBackend;
        _updating = true;
        try
        {
            if (refreshBackends) PopulateBackends(backend);
            PopulateConnections(connection);
        }
        finally { _updating = false; }
        UpdatePort(false);
    }

    private void UpdatePort(bool updateHost)
    {
        if (_updating) return;
        _connectionRevision++;
        var driver = SelectedBackend == GodexPrintJob.DriverBackend;
        var sdk = SelectedBackend == GodexPrintJob.SdkBackend;
        var connection = (_connection.SelectedItem as ConnectionOption)?.Key;
        var ethernet = connection == "ethernet";
        var serial = connection == "serial";
        var sdkUsb = sdk && connection == "usb";
        _queueField.Visible = driver || sdkUsb;
        _portField.Visible = driver;
        _usbField.Visible = sdkUsb;
        _serialField.Visible = _baudField.Visible = serial && !driver;
        _hostField.Visible = ethernet;
        _gapField.Visible = _heatField.Visible = _stopField.Visible = !driver;
        _test.Enabled = ethernet && !_testing;
        var port = driver ? PrinterQueues.GetPort(_queues.SelectedItem as string ?? string.Empty) : string.Empty;
        _port.Text = string.IsNullOrEmpty(port) ? "—" : port;
        if (updateHost && driver && ethernet) _host.Text = PrinterQueues.GetNetworkHost(port);
        _driverHint.Text = L10n.T(driver
            ? ethernet ? "Setup.NetworkHint" : serial ? "Godex.DriverSerialHint" : "Setup.DriverHint"
            : sdkUsb ? "Godex.SdkUsbHint" : serial ? sdk ? "Godex.SdkSerialPort" : "Godex.SerialHint" : "Godex.DirectHint");
        if (driver && _hasNotch)
            _driverHint.Text += Environment.NewLine + L10n.T("Print.ContinuousDriver");
        UpdateNotchAlignment();
        ShowStatus(driver && _queues.Items.Count == 0 ? L10n.T("Setup.NoQueues") : string.Empty, error: _forPrint);
    }

    private void UpdateNotchAlignment()
    {
        if (_alignToNotch is null) return;
        _alignToNotch.Enabled = CanAlignToNotch;
        if (_startSection is not null) _startSection.Enabled = !UseNotchAlignment;
        UpdateSummary();
    }

    private void ShowStatus(string message, bool error = false, Control? field = null)
    {
        _status.Text = message;
        _status.ForeColor = error ? Theme.ErrorText : Theme.SecondaryText;
        _status.Visible = message.Length > 0;
        field?.Focus();
    }

    private async Task TestNetwork()
    {
        var address = _host.Text.Trim();
        if (address.Length == 0)
        {
            ShowStatus(L10n.T("Setup.HostRequired"), error: true, field: _host);
            return;
        }
        var revision = _connectionRevision;
        _testing = true;
        _test.Enabled = false;
        ShowStatus(L10n.T("Setup.Checking"));
        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(address, GodexPrintJob.NetworkPort, timeout.Token);
            if (!IsDisposed && revision == _connectionRevision) ShowStatus(L10n.T("Setup.Reachable"));
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ArgumentException)
        {
            if (!IsDisposed && revision == _connectionRevision)
                ShowStatus(string.Format(L10n.T("Setup.Unreachable"), ex.Message), error: true);
        }
        finally
        {
            _testing = false;
            if (!IsDisposed) _test.Enabled = (_connection.SelectedItem as ConnectionOption)?.Key == "ethernet";
        }
    }

    private void OnAccept(object? sender, EventArgs e)
    {
        if (_forPrint && _from.Value > _to.Value)
        {
            ShowStatus(L10n.T("Setup.InvalidRange"), error: true, field: _from);
            return;
        }
        if (_models.SelectedItem is not string model)
        {
            ShowStatus(L10n.T("Setup.NoModel"), error: true, field: _models);
            return;
        }
        var plugin = PrinterPlugins.All.First(x => x.Name == model);
        var minWidth = plugin.MinMediaWidthMm > 0 ? plugin.MinMediaWidthMm : 1;
        var maxWidth = plugin.MaxMediaWidthMm > 0 ? plugin.MaxMediaWidthMm : plugin.MaxPrintWidthMm;
        var minLength = plugin.MinPrintLengthMm > 0 ? plugin.MinPrintLengthMm : 1;
        var invalidWidth = (double)_width.Value < minWidth || (double)_width.Value > maxWidth;
        if (invalidWidth || (double)_height.Value < minLength || (double)_height.Value > plugin.MaxPrintLengthMm)
        {
            ShowStatus(string.Format(L10n.T("Setup.MediaLimits"), minWidth, maxWidth, minLength, plugin.MaxPrintLengthMm),
                error: true, field: invalidWidth ? _width : _height);
            return;
        }
        var backend = SelectedBackend;
        var driver = backend == GodexPrintJob.DriverBackend;
        var queue = _queues.SelectedItem as string;
        if (_forPrint && driver && string.IsNullOrWhiteSpace(queue))
        {
            ShowStatus(L10n.T(_queues.Items.Count == 0 ? "Setup.NoQueues" : "Setup.ChooseQueue"), error: true, field: _queues);
            return;
        }
        if (_forPrint && (double)_width.Value > plugin.MaxPrintWidthMm &&
            BrandMessageBox.Show(this, string.Format(L10n.T("Setup.WideMedia"), plugin.MaxPrintWidthMm),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        if (_connection.SelectedItem is not ConnectionOption connection)
        {
            ShowStatus(L10n.T("Setup.NoConnection"), error: true, field: _connection);
            return;
        }
        var ethernet = connection.Key == "ethernet";
        var port = driver && queue is not null ? PrinterQueues.GetPort(queue) : string.Empty;
        var serial = port.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(port[3..].TrimEnd(':'), out _);
        if (driver && queue is not null && connection.Key == "serial" && !serial)
        {
            ShowStatus(L10n.T("Godex.DriverSerialHint"), error: true, field: _queues);
            return;
        }
        if (driver && connection.Key == "usb" && (serial || !string.IsNullOrEmpty(PrinterQueues.GetNetworkHost(port))))
        {
            ShowStatus(L10n.T("Setup.UsbMismatch"), error: true, field: _queues);
            return;
        }
        var host = _host.Text.Trim();
        if (driver && ethernet && !string.IsNullOrEmpty(queue))
        {
            if (serial)
            {
                ShowStatus(L10n.T("Godex.DriverEthernetMismatch"), error: true, field: _queues);
                return;
            }
            var match = PrinterQueues.CompareNetworkHost(port, host);
            if (match == PrinterQueues.HostMatch.Mismatch)
            {
                ShowStatus(L10n.T("Setup.HostMismatch"), error: true, field: _host);
                return;
            }
            if (match == PrinterQueues.HostMatch.Unknown &&
                BrandMessageBox.Show(this, L10n.T("Setup.QueueUnverified"), Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }
        var settings = new PrintSettings
        {
            PrintBackend = backend,
            PrinterName = driver || backend == GodexPrintJob.SdkBackend && connection.Key == "usb" ? queue : null,
            ConnectionKind = connection.Key,
            NetworkHost = ethernet ? host : null,
            SerialPort = connection.Key == "serial" && !driver ? _serial.Text.Trim() : null,
            SerialBaudRate = _baud.SelectedItem is int baud ? baud : 9600,
            UsbDevice = backend == GodexPrintJob.SdkBackend && connection.Key == "usb" ? _usb.Text.Trim() : null,
            OffsetXMm = (double)_offsetX.Value, OffsetYMm = (double)_offsetY.Value,
            Copies = (int)_copies.Value, Temperature = _setHeat.Checked ? (int)_heat.Value : null,
            CutOffsetMm = _setStop.Checked ? (double)_stop.Value : null, MediaGapMm = _gap.Value,
            AlignToNotch = UseNotchAlignment
        };
        if (!driver)
        {
            try
            {
                GodexPrintJob.ValidateParameters(settings);
                if (_forPrint) GodexPrintJob.ValidateConnection(settings);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or PlatformNotSupportedException)
            {
                ShowStatus(ex.Message, error: true);
                return;
            }
        }
        if (_forPrint && BrandMessageBox.Show(this, L10n.T("Setup.ConfirmMedia"), Text,
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        Result = settings;
        DialogResult = DialogResult.OK;
        Close();
    }
}
