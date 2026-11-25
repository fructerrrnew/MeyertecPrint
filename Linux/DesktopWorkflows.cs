using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace MeyertecPrint;

internal sealed partial class LinuxMainWindow
{
    private async Task<LabelTemplate?> ChooseTemplate()
    {
        var body = new StackPanel { Spacing = 8 };
        var list = new ListBox { MinHeight = 180, MaxHeight = 280 };
        var details = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var render = new Image { Height = 160, Stretch = Stretch.Uniform };
        var canvas = new LayoutCanvas { Height = 160 };
        var strip = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var sections = Enumerable.Range(0, 3).Select(_ => new LayoutCanvas
        {
            Width = 260, Height = 52, PaperInset = new Thickness(2)
        }).ToArray();
        foreach (var section in sections) strip.Children.Add(section);
        Bitmap? custom = null;
        var templates = _templates.GetAll();
        var dialog = DialogWindow(Tr("Библиотека шаблонов", "Template library"), body, 580, 765);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var use = new Button { Content = Tr("Выбрать", "Use template"), Background = Gold, Foreground = Ink, Margin = new Thickness(4) };
        var import = new Button { Content = Tr("Импорт шаблона…", "Import template…"), Margin = new Thickness(4) };
        var cancel = new Button { Content = Tr("Закрыть", "Close"), Margin = new Thickness(4) };
        body.Children.Add(list);
        body.Children.Add(details);
        body.Children.Add(render);
        body.Children.Add(canvas);
        body.Children.Add(strip);
        buttons.Children.Add(import);
        buttons.Children.Add(use);
        buttons.Children.Add(cancel);
        body.Children.Add(buttons);

        void Select()
        {
            render.Source = null;
            custom?.Dispose();
            custom = null;
            canvas.Clear();
            foreach (var section in sections) section.Clear();
            var selected = list.SelectedIndex;
            use.IsEnabled = selected >= 0 && selected < templates.Count;
            render.IsVisible = canvas.IsVisible = strip.IsVisible = false;
            if (!use.IsEnabled)
            {
                details.Text = Tr("Выберите шаблон или импортируйте файл .mtemplate.", "Choose a template or import an .mtemplate file.");
                return;
            }
            var t = templates[selected];
            details.Text = $"{t.Name} · v{t.Version} · {Units.FormatMm(t.Layout.MediaWidthUm)} × {Units.FormatMm(t.Layout.MediaHeightUm)}" +
                (string.IsNullOrWhiteSpace(t.Material) ? "" : "\n" + t.Material);
            if (t.Layout.AlternateNotch is not null)
                details.Text += "\n" + L10n.T("Template.AlternatingNotch");
            if (t.PreviewPngBase64 is { Length: > 0 } preview)
            {
                try
                {
                    using var data = new MemoryStream(Convert.FromBase64String(preview));
                    custom = new Bitmap(data);
                    render.Source = custom;
                    render.IsVisible = true;
                }
                catch (Exception ex) when (ex is FormatException or ArgumentException or IOException or InvalidOperationException) { }
            }
            if (render.IsVisible) return;
            if (t.Layout.AlternateNotch is null)
            {
                canvas.IsVisible = true;
                canvas.SetPage(t.Layout, t.Print, [], -1, 96);
                return;
            }

            var pages = LabelPrintPages.CreateStockPreview(t.Layout);
            strip.IsVisible = true;
            for (var i = 0; i < sections.Length; i++)
                sections[i].SetPage(pages[i], t.Print, [], -1, showMedia: true);
        }

        void Reload(string? id = null)
        {
            templates = _templates.GetAll();
            list.ItemsSource = templates.Select(t => $"{t.Name}  ·  v{t.Version}").ToList();
            var selected = id is null ? 0 : templates.ToList().FindIndex(t => t.Id == id);
            list.SelectedIndex = templates.Count == 0 ? -1 : Math.Max(0, selected);
            Select();
        }

        list.SelectionChanged += (_, _) => Select();
        use.Click += (_, _) =>
        {
            if (list.SelectedIndex >= 0 && list.SelectedIndex < templates.Count)
                dialog.Close(templates[list.SelectedIndex]);
        };
        cancel.Click += (_, _) => dialog.Close();
        import.Click += async (_, _) =>
        {
            import.IsEnabled = false;
            try
            {
                var path = await PickOpen(TemplateFiles, dialog);
                if (path is not null) Reload(_templates.Import(path).Id);
            }
            catch (Exception ex) { await Message(Tr("Импорт шаблона", "Template import"), ex.Message, dialog); }
            finally { import.IsEnabled = true; }
        };
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) dialog.Close(); };
        Reload(_project?.TemplateId);
        try { return await dialog.ShowDialog<LabelTemplate?>(this); }
        finally
        {
            canvas.Clear();
            foreach (var section in sections) section.Clear();
            custom?.Dispose();
        }
    }

    private async Task SaveTemplate()
    {
        var candidate = _lastSavedTemplate ?? _templates.GetAll().FirstOrDefault(t => t.Id == _project!.TemplateId);
        var form = new StackPanel { Spacing = 8 };
        var name = TextBox(candidate?.Name ?? _project!.Name);
        var material = TextBox(candidate?.Material);
        var revision = new CheckBox { Content = Tr("Сохранить новую версию выбранного шаблона", "Save a new version of the selected template"), IsEnabled = candidate is not null };
        var custom = new CheckBox { Content = Tr("Выбрать свой PNG для предпросмотра", "Choose a custom PNG preview") };
        var export = new CheckBox { Content = Tr("Также сохранить отдельный файл", "Also save a separate file") };
        Field(form, Tr("Название шаблона", "Template name"), name);
        Field(form, Tr("Материал", "Material"), material);
        form.Children.Add(revision);
        form.Children.Add(custom);
        form.Children.Add(export);
        if (!await ConfirmForm(Tr("Сохранить шаблон", "Save template"), form, Tr("Сохранить", "Save"))) return;
        byte[]? preview = null;
        if (custom.IsChecked == true)
        {
            var file = await PickOpen(new Avalonia.Platform.Storage.FilePickerFileType("PNG") { Patterns = ["*.png"] });
            if (file is null) return;
            preview = await ReadImage(file);
        }
        else if (Layout.AlternateNotch is null)
        {
            using var raster = LabelRaster.Render(Layout, _project!.Print, 96);
            using var image = SKImage.FromBitmap(raster);
            using var png = image.Encode(SKEncodedImageFormat.Png, 90);
            preview = png.ToArray();
        }
        var template = LabelTemplate.FromProject(_project!, name.Text ?? "", material.Text ?? "", revision.IsChecked == true ? candidate : null, preview);
        if (export.IsChecked == true)
        {
            var file = await PickSave(TemplateFiles, template.Name, "mtemplate");
            if (file is null) return;
            TemplateLibrary.SaveFile(template, file);
        }
        if (!_templates.Install(template))
            throw new InvalidOperationException(Tr("В библиотеке уже есть такая или более новая версия.", "The library already contains this version or a newer one."));
        _lastSavedTemplate = template;
        _project!.TemplateId = template.Id;
        _project.TemplateVersion = template.Version;
        Changed();
        _status.Text = $"{Tr("Шаблон сохранён", "Template saved")} · {template.Name} v{template.Version}";
    }

    private async Task CheckTemplateUpdatesOnStartup()
    {
        var preferences = TemplateFeedSettings.Load();
        if (string.IsNullOrWhiteSpace(preferences.ManifestUrl)) return;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        try
        {
            var updates = await TemplateFeed.CheckAsync(preferences.GetManifestUri(), _templates, preferences.UseTls,
                cancellationToken: deadline.Token);
            if (updates.Count > 0)
                _status.Text = Tr($"Доступны обновления шаблонов: {updates.Count}", $"Template updates available: {updates.Count}");
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidDataException
            or System.Net.Sockets.SocketException or System.Security.Authentication.AuthenticationException)
        {
            _status.Text = Tr("Проверьте подключение в окне обновлений шаблонов.", "Check the connection in Template updates.");
        }
    }

    private async Task UpdateTemplates()
    {
        var preferences = TemplateFeedSettings.Load();
        var form = new StackPanel { Spacing = 8 };
        var address = TextBox(preferences.ManifestUrl);
        address.Watermark = "ftp://host/path/manifest.json";
        var tls = new CheckBox { Content = Tr("Использовать явный FTPS", "Use explicit FTPS"), IsChecked = preferences.UseTls };
        var user = TextBox(null);
        var password = TextBox(null);
        password.PasswordChar = '•';
        Field(form, Tr("Адрес манифеста FTP", "FTP manifest URL"), address);
        form.Children.Add(tls);
        Field(form, Tr("Имя пользователя (если требуется)", "Username (optional)"), user);
        Field(form, Tr("Пароль (действует только для этой проверки)", "Password (this check only)"), password);
        if (!await ConfirmForm(Tr("Обновления шаблонов", "Template updates"), form, Tr("Проверить", "Check"))) return;
        var useTls = tls.IsChecked == true;
        NetworkCredential? credentials = string.IsNullOrWhiteSpace(user.Text) ? null : new NetworkCredential(user.Text.Trim(), password.Text ?? "");
        if (!useTls && credentials is not null)
            throw new InvalidDataException(Tr("Для входа с паролем включите FTPS.", "Enable FTPS to sign in with a password."));
        preferences.ManifestUrl = address.Text ?? "";
        preferences.UseTls = useTls;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var uri = preferences.GetManifestUri();
        var updates = await TemplateFeed.CheckAsync(uri, _templates, useTls, credentials, timeout.Token);
        preferences.Save();
        if (updates.Count == 0)
        {
            await Message(Tr("Обновления шаблонов", "Template updates"), Tr("Все шаблоны актуальны.", "All templates are up to date."));
            return;
        }
        var changes = new StackPanel { Spacing = 6 };
        foreach (var item in updates)
            changes.Children.Add(new TextBlock
            {
                Text = item.InstalledVersion is { } old ? $"{item.Name} · v{old} → v{item.Version}" :
                    $"{item.Name} · v{item.Version} · {Tr("новый", "new")}",
                TextWrapping = TextWrapping.Wrap
            });
        if (!await ConfirmForm(Tr("Доступны обновления", "Updates available"), changes, Tr("Установить", "Install"))) return;
        using var installTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var installed = await TemplateFeed.InstallAsync(uri, updates, _templates, useTls, credentials, installTimeout.Token);
        await Message(Tr("Обновления шаблонов", "Template updates"), Tr($"Установлено: {installed.Count}", $"Installed: {installed.Count}"));
    }

    private Task AddRecord()
    {
        var fields = LabelPrintPages.CaptureFields(Pages[_record]);
        var labels = _project!.Ribbon.Labels ??= [];
        var printable = LabelPrintPages.PrintableRecords(Layout, labels).ToList();
        var offset = _record == 0 ? 0 : labels.IndexOf(printable[_record - 1]) + 1;
        labels.Insert(offset, new LabelItem
        {
            Name = string.Join(" | ", fields.Values.Take(3)), Fields = fields, UseLayout = fields.Count == 0
        });
        _record++;
        RenumberRecords();
        Changed();
        return Task.CompletedTask;
    }

    private async Task DeleteRecord()
    {
        if (_record <= 0) return;
        var records = LabelPrintPages.PrintableRecords(Layout, _project!.Ribbon.Labels).ToList();
        if (_record > records.Count) return;
        var form = new StackPanel();
        form.Children.Add(new TextBlock { Text = Tr($"Удалить этикетку {_record + 1} из проекта?", $"Delete label {_record + 1} from the project?") });
        if (!await ConfirmForm(Tr("Удаление этикетки", "Delete label"), form, Tr("Удалить", "Delete"))) return;
        _project.Ribbon.Labels.Remove(records[_record - 1]);
        RenumberRecords();
        Changed();
    }

    private void RenumberRecords()
    {
        for (var i = 0; i < _project!.Ribbon.Labels.Count; i++) _project.Ribbon.Labels[i].Index = i + 2;
    }

    private async Task EditRecord()
    {
        var records = LabelPrintPages.PrintableRecords(Layout, _project!.Ribbon.Labels).ToList();
        if (_record == 0 || _record > records.Count) return;
        var record = records[_record - 1];
        var fields = LabelPrintPages.GetBindings(Pages[_record]);
        var inputs = new Dictionary<string, TextBox>();
        var form = new StackPanel { Spacing = 7 };
        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            if (field.Type == CellContentType.Image) continue;
            var input = TextBox(field.Type == CellContentType.Barcode ? field.BarcodeData : field.Text);
            inputs.Add("C" + i, input);
            Field(form, $"{i + 1}. {Describe(field)}", input);
        }
        if (inputs.Count == 0)
        {
            await Message(Tr("Данные этикетки", "Label data"), Tr("На первой этикетке нет полей для подстановки.", "The first label has no variable fields."));
            return;
        }
        if (!await ConfirmForm(Tr("Данные этикетки", "Label data"), form, Tr("Применить", "Apply"))) return;
        var result = inputs.ToDictionary(p => p.Key, p => p.Value.Text ?? "");
        var check = Layout.Clone();
        LabelPrintPages.ApplyFields(check, result);
        record.Fields = result;
        record.UseLayout = result.Count == 0;
        record.Name = string.Join(" | ", result.Values.Take(3));
        Changed();
    }

    private async Task NumberRecords()
    {
        var bindings = LabelPrintPages.GetBindings(Layout)
            .Select((content, index) => (content, index))
            .Where(p => p.content.Type is CellContentType.Empty or CellContentType.Text or CellContentType.Qr or CellContentType.Barcode)
            .ToList();
        if (bindings.Count == 0) throw new InvalidDataException(Tr("Добавьте поле для нумерации на макет.", "Add a field to the layout for numbering."));
        var form = new StackPanel { Spacing = 7 };
        var field = Choice(bindings.Select(p => $"{p.index + 1}. {Describe(p.content)}"));
        var start = Number(1, -1_000_000_000, 1_000_000_000, 1);
        var increment = Number(1, -1_000_000_000, 1_000_000_000, 1);
        var digits = Number(1, 1, 18, 1);
        var count = Number(10, 1, 100_000, 1);
        var prefix = TextBox(null);
        var suffix = TextBox(null);
        Field(form, Tr("Поле", "Field"), field);
        Field(form, Tr("Количество этикеток", "Label count"), count);
        Field(form, Tr("Первый номер", "First number"), start);
        Field(form, Tr("Шаг", "Step"), increment);
        Field(form, Tr("Минимум цифр", "Minimum digits"), digits);
        Field(form, Tr("Префикс", "Prefix"), prefix);
        Field(form, Tr("Суффикс", "Suffix"), suffix);
        form.Children.Add(new TextBlock { Text = Tr("Нумерация заменит текущие записи.", "Numbering replaces the current records.") });
        if (!await ConfirmForm(Tr("Автонумерация", "Automatic numbering"), form, Tr("Создать записи", "Create records"))) return;
        ApplyBatch(LabelNumbering.Prepare(Layout, bindings[field.SelectedIndex].index, (int)(count.Value ?? 1),
            (long)(start.Value ?? 1), (long)(increment.Value ?? 1), (int)(digits.Value ?? 1), prefix.Text ?? "", suffix.Text ?? ""));
    }

    private async Task ImportData()
    {
        var file = await PickOpen(WorksheetFiles);
        if (file is null) return;
        var form = new StackPanel { Spacing = 8 };
        var isExcel = SpreadsheetReader.IsExcelFile(file);
        var worksheets = isExcel ? SpreadsheetReader.GetWorksheetNames(file).ToList() : [];
        if (isExcel && worksheets.Count == 0) throw new InvalidDataException(Tr("В книге нет листов.", "The workbook has no sheets."));
        var sheet = Choice(worksheets);
        var encodings = new[] { "auto", "utf8", "1251", "utf16" };
        var encoding = Choice([Tr("Автоматически", "Automatic"), "UTF-8", "Windows-1251", "UTF-16"]);
        if (isExcel) Field(form, Tr("Лист Excel", "Excel worksheet"), sheet);
        else Field(form, Tr("Кодировка CSV", "CSV encoding"), encoding);
        var header = new CheckBox { Content = Tr("Первая строка содержит заголовки", "First row contains headers") };
        form.Children.Add(header);
        var preview = new TextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = FontFamily.Default };
        form.Children.Add(preview);
        var mappingForm = new StackPanel { Spacing = 6 };
        form.Children.Add(mappingForm);
        var selectors = new Dictionary<int, ComboBox>();
        var bindings = LabelPrintPages.GetBindings(Layout);
        List<List<string>> rows = [];
        var dialog = DialogWindow(Tr("Импорт CSV / Excel", "Import CSV / Excel"), form, 630, 790);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var apply = new Button { Content = Tr("Импортировать", "Import"), Background = Gold, Foreground = Ink, Margin = new Thickness(4) };
        var cancel = new Button { Content = Tr("Отмена", "Cancel"), Margin = new Thickness(4) };
        buttons.Children.Add(apply);
        buttons.Children.Add(cancel);
        form.Children.Add(buttons);

        void Refresh()
        {
            mappingForm.Children.Clear();
            selectors.Clear();
            rows = [];
            apply.IsEnabled = false;
            try
            {
                var data = SpreadsheetReader.ReadFile(file, encodings[Math.Max(0, encoding.SelectedIndex)],
                    isExcel ? sheet.SelectedItem as string : null);
                var headers = header.IsChecked == true ? data.FirstOrDefault() : null;
                rows = (header.IsChecked == true ? data.Skip(1) : data).ToList();
                var columns = data.Select(row => row.Count).DefaultIfEmpty().Max();
                preview.Text = Tr($"Строк для импорта: {rows.Count}", $"Rows to import: {rows.Count}") + "\n" +
                    string.Join("\n", rows.Take(4).Select(r => string.Join("  |  ", r.Take(8).Select(v => v.Length > 35 ? v[..35] + "…" : v))));
                var choices = new List<string> { Tr("Оставить постоянным", "Keep constant") };
                for (var column = 0; column < columns; column++)
                {
                    var caption = headers is not null && column < headers.Count ? headers[column] : "";
                    choices.Add((column + 1).ToString() + (string.IsNullOrWhiteSpace(caption) ? "" : ". " + caption));
                }
                var ordinal = 0;
                for (var i = 0; i < bindings.Count; i++)
                {
                    if (bindings[i].Type == CellContentType.Image) continue;
                    var selector = Choice(choices, ordinal < columns ? ++ordinal : 0);
                    Field(mappingForm, $"{i + 1}. {Describe(bindings[i])}", selector);
                    selectors.Add(i, selector);
                }
                apply.IsEnabled = rows.Count > 0 && selectors.Count > 0;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException or ArgumentException or NotSupportedException)
            {
                preview.Text = ex.Message;
            }
        }

        header.IsCheckedChanged += (_, _) => Refresh();
        sheet.SelectionChanged += (_, _) => Refresh();
        encoding.SelectionChanged += (_, _) => Refresh();
        apply.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) dialog.Close(false); };
        Refresh();
        if (!await dialog.ShowDialog<bool>(this)) return;
        var mapping = selectors.Where(p => p.Value.SelectedIndex > 0).ToDictionary(p => p.Key, p => p.Value.SelectedIndex - 1);
        var used = mapping.Values.ToHashSet();
        if (rows.Any(row => row.Where((value, index) => !used.Contains(index) && !string.IsNullOrWhiteSpace(value)).Any()))
        {
            var confirm = new StackPanel();
            confirm.Children.Add(new TextBlock { Text = Tr("Некоторые заполненные столбцы останутся без сопоставления. Продолжить?", "Some populated columns are unmapped. Continue?"), TextWrapping = TextWrapping.Wrap });
            if (!await ConfirmForm(Tr("Сопоставление данных", "Data mapping"), confirm, Tr("Продолжить", "Continue"))) return;
        }
        ApplyBatch(CsvBatch.Prepare(Layout, rows, mapping));
    }

    private void ApplyBatch(CsvBatch.Prepared batch)
    {
        var proposed = Layout.Clone();
        LabelPrintPages.ApplyFields(proposed, batch.FirstFields);
        _history.Clear();
        _project!.Ribbon.Layout = proposed;
        _project.Ribbon.Labels = batch.Remaining;
        _record = 0;
        Changed();
    }

    private async Task ShowPreview()
    {
        if (_project is null) return;
        var pages = Pages;
        var body = new StackPanel { Spacing = 9 };
        var nav = new WrapPanel();
        var previous = new Button { Content = "‹", Margin = new Thickness(3) };
        var next = new Button { Content = "›", Margin = new Thickness(3) };
        var index = Number(_record + 1, 1, pages.Count, 1);
        index.Width = 105;
        var count = new TextBlock { Text = $"/ {pages.Count}", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) };
        nav.Children.Add(previous);
        nav.Children.Add(index);
        nav.Children.Add(count);
        nav.Children.Add(next);
        var preview = new LayoutCanvas { Height = 360 };
        body.Children.Add(nav);
        if (Layout.AlternateNotch is not null)
            body.Children.Add(new TextBlock { Text = L10n.T("Ribbon.SkipNotchedSections"), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(preview);
        void Refresh()
        {
            var number = (int)(index.Value ?? 1);
            previous.IsEnabled = number > 1;
            next.IsEnabled = number < pages.Count;
            preview.SetPage(pages[number - 1], _project.Print, [], -1, GodexPrintJob.Dpi,
                validateForPrint: true, showMedia: true);
        }
        index.ValueChanged += (_, _) => Refresh();
        previous.Click += (_, _) => index.Value = (index.Value ?? 1) - 1;
        next.Click += (_, _) => index.Value = (index.Value ?? 1) + 1;
        Refresh();
        try
        {
            if (await ConfirmForm(Tr("Предпросмотр печати", "Print preview"), body, Tr("Настроить печать…", "Print settings…"), 730))
                await PrintSettingsDialog();
        }
        finally { preview.Clear(); }
    }

    private async Task PrintSettingsDialog()
    {
        var project = _project!;
        var previous = project.Print;
        var form = new StackPanel { Spacing = 8 };
        var model = Choice(["GoDEX G530"]);
        var kinds = new[] { "usb", "serial", "ethernet" };
        var connection = Choice(["USB", "RS-232", "RJ45 / LAN"], Math.Max(0, Array.IndexOf(kinds, previous.ConnectionKind)));
        var usb = TextBox(previous.UsbDevice);
        usb.Watermark = "/dev/usb/lp0";
        var serial = TextBox(previous.SerialPort);
        serial.Watermark = "/dev/ttyUSB0";
        var baudRates = GodexPrintJob.BaudRates.ToList();
        var baudIndex = baudRates.IndexOf(previous.SerialBaudRate);
        var baud = Choice(baudRates.Select(rate => rate.ToString()), baudIndex >= 0 ? baudIndex : baudRates.IndexOf(9600));
        var host = TextBox(previous.NetworkHost);
        host.Watermark = "192.0.2.10";
        var usbFields = new StackPanel { Spacing = 4 };
        var serialFields = new StackPanel { Spacing = 4 };
        var hostFields = new StackPanel { Spacing = 4 };
        Field(usbFields, Tr("Устройство USB", "USB device"), usb);
        Field(serialFields, Tr("Последовательный порт", "Serial port"), serial);
        Field(serialFields, Tr("Скорость порта", "Baud rate"), baud);
        Field(hostFields, Tr("IP-адрес или имя принтера", "Printer IP address or hostname"), host);
        void Toggle()
        {
            usbFields.IsVisible = connection.SelectedIndex == 0;
            serialFields.IsVisible = connection.SelectedIndex == 1;
            hostFields.IsVisible = connection.SelectedIndex == 2;
        }
        connection.SelectionChanged += (_, _) => Toggle();
        Toggle();
        Field(form, Tr("Принтер", "Printer"), model);
        Field(form, Tr("Подключение", "Connection"), connection);
        form.Children.Add(usbFields);
        form.Children.Add(serialFields);
        form.Children.Add(hostFields);
        var patterned = Layout.AlternateNotch is not null;
        ComboBox? initialSection = null;
        if (patterned)
        {
            var section = Choice([L10n.T("Print.SectionFull"), L10n.T("Print.SectionNotched")]);
            initialSection = section;
            var hint = new TextBlock { TextWrapping = TextWrapping.Wrap };
            void RefreshStart() => hint.Text = L10n.T(section.SelectedIndex == 1 ? "Print.StartNotchedHint" : "Print.StartPlain");
            section.SelectionChanged += (_, _) => RefreshStart();
            RefreshStart();
            Field(form, L10n.T("Print.StartSection"), section);
            form.Children.Add(hint);
        }
        var offsetX = Number((decimal)previous.OffsetXMm, -50, 50);
        var offsetY = Number((decimal)previous.OffsetYMm, -50, 50);
        var gap = Number(patterned ? 0 : previous.MediaGapMm, 0, 100);
        gap.IsEnabled = !patterned;
        var heat = Number(previous.Temperature, 0, 19, 1);
        var cut = Number(previous.CutOffsetMm is { } offset ? (decimal)offset : null, 0, 40, 1);
        var start = Number(1, 1, Pages.Count, 1);
        var end = Number(Pages.Count, 1, Pages.Count, 1);
        var copies = Number(previous.Copies, 1, 500, 1);
        Field(form, Tr("Смещение X, мм", "X offset, mm"), offsetX);
        Field(form, Tr("Смещение Y, мм", "Y offset, mm"), offsetY);
        if (patterned)
            form.Children.Add(new TextBlock { Text = L10n.T("Setup.OffsetCalibration"), TextWrapping = TextWrapping.Wrap });
        Field(form, Tr("Зазор носителя, мм", "Media gap, mm"), gap);
        Field(form, Tr("Нагрев (0–19)", "Darkness (0–19)"), heat);
        Field(form, Tr("Отступ остановки / отреза, мм", "Stop / cut offset, mm"), cut);
        Field(form, Tr("Печатать с этикетки", "Print from label"), start);
        Field(form, Tr("Печатать по этикетку", "Print through label"), end);
        Field(form, Tr("Количество копий", "Copies"), copies);
        var send = new CheckBox { Content = Tr("Отправить задание после сохранения настроек", "Send job after saving settings") };
        form.Children.Add(send);
        if (!await ConfirmForm(Tr("Параметры печати", "Print settings"), form, Tr("Применить", "Apply"))) return;
        var first = (int)(start.Value ?? 1);
        var last = (int)(end.Value ?? Pages.Count);
        if (first > last) throw new InvalidDataException(Tr("Начало диапазона должно быть раньше конца.", "The range start must be before its end."));
        var settings = new PrintSettings
        {
            PrinterName = previous.PrinterName, PrintBackend = "godex-direct", ConnectionKind = kinds[connection.SelectedIndex],
            UsbDevice = EmptyToNull(usb.Text), SerialPort = EmptyToNull(serial.Text),
            SerialBaudRate = baudRates[baud.SelectedIndex], NetworkHost = EmptyToNull(host.Text),
            Copies = (int)(copies.Value ?? 1), OffsetXMm = (double)(offsetX.Value ?? 0), OffsetYMm = (double)(offsetY.Value ?? 0),
            MediaGapMm = patterned ? 0 : gap.Value ?? 0, Temperature = heat.Value is { } temperature ? (int)temperature : null,
            CutOffsetMm = cut.Value is { } stop ? (double)stop : null
        };
        project.Print = settings;
        project.PrinterModel = "G530";
        Changed();
        if (send.IsChecked != true) return;
        var startOnNotchedSection = initialSection?.SelectedIndex == 1;
        var pages = Pages.Skip(first - 1).Take(last - first + 1).ToList();

        using var cancellation = new CancellationTokenSource();
        var body = new StackPanel { Spacing = 8 };
        var status = new TextBlock { Text = Tr("Проверка данных и передача задания…", "Validating and sending the job…") };
        var cancel = new Button { Content = Tr("Отменить", "Cancel"), HorizontalAlignment = HorizontalAlignment.Right };
        body.Children.Add(status);
        body.Children.Add(cancel);
        var progressWindow = DialogWindow(Tr("Печать", "Printing"), body, 455);
        cancel.Click += (_, _) => cancellation.Cancel();
        var closed = false;
        progressWindow.Closing += (_, e) =>
        {
            if (closed) return;
            e.Cancel = true;
            cancellation.Cancel();
        };
        Exception? failure = null;
        progressWindow.Opened += async (_, _) =>
        {
            try
            {
                var progress = new Progress<(long Done, long Total)>(value => status.Text = $"{value.Done} / {value.Total}");
                await Task.Run(() => GodexPrintJob.SendAsync(settings, pages, (layout, print, dpi) =>
                {
                    using var bitmap = LabelRaster.Render(layout, print, dpi, true);
                    return GodexRaster.Create(bitmap.Width, bitmap.Height, (x, y) =>
                    {
                        var pixel = bitmap.GetPixel(x, y);
                        return pixel.Alpha >= 128 && (pixel.Red * 299 + pixel.Green * 587 + pixel.Blue * 114) < 128_000;
                    });
                }, progress, cancellation.Token, startOnNotchedSection: startOnNotchedSection));
            }
            catch (Exception ex) { failure = ex; }
            finally { closed = true; progressWindow.Close(); }
        };
        await progressWindow.ShowDialog(this);
        if (failure is OperationCanceledException)
            _status.Text = Tr("Передача отменена. Проверьте уже напечатанные этикетки.", "Transfer cancelled. Check for labels already printed.");
        else if (failure is not null) throw failure;
        else _status.Text = Tr("Задание передано принтеру.", "The job was sent to the printer.");
    }
}
