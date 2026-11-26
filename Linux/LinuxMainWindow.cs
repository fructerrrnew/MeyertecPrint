using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SkiaSharp;

namespace MeyertecPrint;

internal sealed partial class LinuxMainWindow : Window
{
    private const string Brand = "MEYERTEC PRINT";
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#0F202A"));
    private static readonly IBrush Gold = new SolidColorBrush(Color.Parse("#FFCB04"));
    private static readonly IBrush Surface = new SolidColorBrush(Color.Parse("#F1F5F7"));
    private static readonly FilePickerFileType ProjectFiles = new("MEYERTEC (.mprj)") { Patterns = ["*.mprj"] };
    private static readonly FilePickerFileType TemplateFiles = new("MEYERTEC (.mtemplate)") { Patterns = ["*.mtemplate"] };
    private static readonly FilePickerFileType WorksheetFiles = new("CSV / Excel") { Patterns = ["*.csv", "*.xlsx", "*.xls", "*.xlsb"] };
    private readonly TemplateLibrary _templates = new();
    private readonly UndoRedo _history = new();
    private readonly ConditionalWeakTable<LabelItem, UndoRedo> _recordHistory = new();
    private readonly LayoutCanvas _canvas = new();
    private readonly ContentControl _workspace = new();
    private readonly ContentControl _inspector = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _elementsList = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private ProjectFile? _project;
    private string? _file;
    private LabelTemplate? _lastSavedTemplate;
    private bool _dirty;
    private bool _strip;
    private bool _closingConfirmed;
    private int _record;
    private int _selected = -1;
    private bool _updatingList;
    private List<Element> _elements = [];
    private LabelItem? _layoutRecord;
    private LabelLayout? _recordLayout;
    private bool _busy;

    private LabelLayout Layout => _project!.Ribbon.GetLayout();
    private LabelLayout EditingLayout => _recordLayout ?? Layout;
    private IReadOnlyList<LabelLayout> Pages => LabelPrintPages.Create(_project!.Ribbon);
    private bool CanEditLayout => _record == 0 || _recordLayout is not null;
    private UndoRedo LayoutHistory => _layoutRecord is { } record ? _recordHistory.GetOrCreateValue(record) : _history;

    public LinuxMainWindow(string? path)
    {
        Title = Brand;
        Width = 1240;
        Height = 790;
        MinWidth = 720;
        MinHeight = 460;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "IconsUI", "Brand.png");
        if (File.Exists(iconPath)) Icon = new WindowIcon(iconPath);

        _elementsList.SelectionChanged += (_, _) =>
        {
            if (!_updatingList) SelectElement(_elementsList.SelectedIndex);
        };
        _canvas.SelectionRequested += SelectElement;
        _canvas.MoveRequested += (index, dx, dy) =>
        {
            if (!CanEditLayout || index < 0 || index >= _elements.Count || _elements[index].Object is not { } item) return;
            var offsetX = Units.MmToUm(_project!.Print.OffsetXMm);
            var offsetY = Units.MmToUm(_project.Print.OffsetYMm);
            var left = Math.Max(0, -offsetX);
            var top = Math.Max(0, -offsetY);
            var right = EditingLayout.MediaWidthUm - item.WidthUm - Math.Max(0, offsetX);
            var bottom = EditingLayout.MediaHeightUm - item.HeightUm - Math.Max(0, offsetY);
            if (left > right || top > bottom) return;
            var x = Math.Clamp(item.XUm + dx, left, right);
            var y = Math.Clamp(item.YUm + dy, top, bottom);
            if (x == item.XUm && y == item.YUm) return;
            LayoutHistory.Push(EditingLayout);
            item.XUm = x;
            item.YUm = y;
            LayoutChanged();
        };
        KeyDown += (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            Func<Task>? action = e.Key switch
            {
                Key.N => NewProject,
                Key.O => OpenProject,
                Key.S => () => SaveProject(e.KeyModifiers.HasFlag(KeyModifiers.Shift)),
                Key.P => ShowPreview,
                _ => null
            };
            if (action is null) return;
            e.Handled = true;
            _ = Run(action);
        };
        Closing += async (_, e) =>
        {
            if (!_dirty || _closingConfirmed) return;
            e.Cancel = true;
            if (_busy) return;
            await Run(async () =>
            {
                if (!await ConfirmDiscard()) return;
                _closingConfirmed = true;
                Close();
            });
        };
        Closed += (_, _) => _canvas.Clear();
        RebuildShell();
        Opened += async (_, _) =>
        {
            await Run(async () =>
            {
                if (!string.IsNullOrWhiteSpace(path)) LoadProject(path);
                await CheckTemplateUpdatesOnStartup();
            });
        };
    }

    private static string Tr(string ru, string en) => L10n.Current == "en" ? en : ru;

    private void RebuildShell()
    {
        // Remove the reusable content hosts from their old visual parents on a language change.
        if (_inspector.Parent is ScrollViewer oldInspector) oldInspector.Content = null;
        if (Content is Grid previous)
        {
            previous.Children.Remove(_workspace);
            previous.Children.Remove(_status);
        }
        _workspace.Content = null;
        _inspector.Content = null;

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        var brand = new TextBlock
        {
            Text = Brand, FontSize = 18, FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White
        };
        var language = new ComboBox
        {
            ItemsSource = new[] { "Русский", "English" },
            SelectedIndex = L10n.Current == "en" ? 1 : 0,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        language.SelectionChanged += (_, _) =>
        {
            var code = language.SelectedIndex == 1 ? "en" : "ru";
            if (code == L10n.Current) return;
            L10n.Load(code);
            RebuildShell();
        };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(16, 8) };
        top.Children.Add(brand);
        Grid.SetColumn(language, 1);
        top.Children.Add(language);
        root.Children.Add(new Border { Background = Ink, Child = top });

        var commands = new WrapPanel { Margin = new Thickness(12, 6) };
        commands.Children.Add(Action(Tr("Новый проект", "New project"), NewProject));
        commands.Children.Add(Action(Tr("Открыть…", "Open…"), OpenProject));
        if (_project is not null)
        {
            commands.Children.Add(Action(Tr("Сохранить", "Save"), () => SaveProject(false)));
            commands.Children.Add(Action(Tr("Сохранить как…", "Save as…"), () => SaveProject(true)));
            commands.Children.Add(Action(Tr("Шаблоны…", "Templates…"), SelectAndApplyTemplate));
            commands.Children.Add(Action(Tr("Сохранить шаблон…", "Save template…"), SaveTemplate));
            commands.Children.Add(Action(Tr("Импорт CSV / Excel…", "Import CSV / Excel…"), ImportData));
            commands.Children.Add(Action(L10n.T("File.ExportLabelData"), ExportLabelData));
            commands.Children.Add(Action(Tr("Автонумерация…", "Numbering…"), NumberRecords));
            commands.Children.Add(Action(Tr("Предпросмотр…", "Preview…"), ShowPreview, primary: true));
        }
        else commands.Children.Add(Action(Tr("Библиотека шаблонов…", "Template library…"), SelectAndApplyTemplate));
        commands.Children.Add(Action(Tr("Обновления шаблонов…", "Template updates…"), UpdateTemplates));
        Grid.SetRow(commands, 1);
        root.Children.Add(commands);
        Grid.SetRow(_workspace, 2);
        root.Children.Add(_workspace);
        _status.Margin = new Thickness(16, 8);
        Grid.SetRow(_status, 3);
        root.Children.Add(_status);
        Content = root;

        if (_project is null)
        {
            var welcome = new StackPanel
            {
                Spacing = 13, MaxWidth = 530,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(18)
            };
            welcome.Children.Add(new TextBlock { Text = Tr("Редактор маркировки", "Label designer"), FontSize = 38, FontWeight = FontWeight.Bold, Foreground = Ink, TextWrapping = TextWrapping.Wrap });
            welcome.Children.Add(new TextBlock { Text = Tr("Выберите шаблон, добавьте данные и подготовьте этикетки к печати.", "Pick a template, add data and prepare your labels for printing."), FontSize = 16, TextWrapping = TextWrapping.Wrap });
            welcome.Children.Add(Action(Tr("Создать проект", "Create project"), NewProject, primary: true));
            welcome.Children.Add(Action(Tr("Открыть проект…", "Open project…"), OpenProject));
            _workspace.Content = new Border { Background = Surface, Child = welcome };
        }
        else RefreshProject();
    }

    private void RefreshProject()
    {
        if (_project is null) return;
        var pages = Pages;
        _record = Math.Clamp(_record, 0, pages.Count - 1);
        _layoutRecord = _record > 0 && !_project.Ribbon.ApplyLayoutToAll
            ? LabelPrintPages.PrintableRecords(_project.Ribbon).ElementAt(_record - 1)
            : null;
        _recordLayout = _layoutRecord is null ? null : LabelPrintPages.ResolveRecordLayout(_project.Ribbon, _layoutRecord);
        _elements = CanEditLayout ? GetElements(EditingLayout) : [];
        if (_selected >= _elements.Count) _selected = -1;
        Title = _project.Name + (_dirty ? " *" : "") + " — " + Brand;
        var gap = Layout.AlternateNotch is null ? Units.MmToUm((double)Math.Clamp(_project.Print.MediaGapMm, 0, 100)) : 0;
        var length = LabelSequence.CalculateFeedLengthUm(Layout, pages.Count, gap);
        var stockNote = Layout.AlternateNotch is null ? "" : L10n.T("Ribbon.AlternatingNotchedSections") + "   ·   ";
        _status.Text = $"{Tr("Этикеток", "Labels")} {pages.Count}   ·   {Tr("Длина ленты", "Run length")} {Units.FormatMm(length)}   ·   " +
            stockNote + (_dirty ? Tr("Есть несохранённые изменения", "Unsaved changes") : Tr("Сохранено", "Saved"));

        // The canvas stays alive across edits. Move it out of its previous layout first.
        if (_canvas.Parent is Panel old) old.Children.Remove(_canvas);
        _workspace.Content = null;
        if (_inspector.Parent is ScrollViewer oldScroll) oldScroll.Content = null;

        var editor = new Grid { ColumnDefinitions = new ColumnDefinitions("*,330"), ColumnSpacing = 10, Margin = new Thickness(12, 0) };
        var editing = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var picker = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        picker.Children.Add(Action(_strip ? Tr("Одна этикетка", "Single label") : Tr("Лента", "Label strip"), () =>
        {
            _strip = !_strip;
            RefreshProject();
            return Task.CompletedTask;
        }));
        picker.Children.Add(Action("‹", () => NavigateRecord(-1), enabled: _record > 0));
        var position = new NumericUpDown
        {
            Minimum = 1, Maximum = pages.Count, Value = _record + 1, Width = 105, Margin = new Thickness(3),
            FormatString = "0", Increment = 1
        };
        position.ValueChanged += (_, _) =>
        {
            if (position.Value is { } value && value >= 1 && value <= pages.Count && value == decimal.Truncate(value) && (int)value - 1 != _record)
            {
                SelectRecord((int)value - 1);
            }
        };
        picker.Children.Add(position);
        picker.Children.Add(Action("›", () => NavigateRecord(1), enabled: _record < pages.Count - 1));
        picker.Children.Add(Action(Tr("Добавить этикетку", "Add label"), AddRecord));
        picker.Children.Add(Action(Tr("Удалить этикетку", "Delete label"), DeleteRecord, enabled: _record > 0));
        var applyToAll = new CheckBox
        {
            Content = L10n.T("Ribbon.ApplyLayoutToAll"), IsChecked = _project.Ribbon.ApplyLayoutToAll,
            Margin = new Thickness(5, 0), VerticalAlignment = VerticalAlignment.Center
        };
        applyToAll.IsCheckedChanged += (_, _) => _ = Run(() =>
        {
            LabelPrintPages.SetApplyLayoutToAll(_project.Ribbon, applyToAll.IsChecked == true);
            _selected = -1;
            Changed();
            return Task.CompletedTask;
        });
        picker.Children.Add(applyToAll);
        editing.Children.Add(picker);

        Control view;
        if (_strip)
        {
            var run = new WrapPanel { Margin = new Thickness(6) };
            const int windowSize = 12;
            var start = _record / windowSize * windowSize;
            for (var i = start; i < Math.Min(start + windowSize, pages.Count); i++)
            {
                var index = i;
                var card = new LayoutCanvas { Width = 160, Height = 95 };
                card.SetPage(pages[index], _project.Print, [], -1, 96, showMedia: true);
                var button = new Button
                {
                    Margin = new Thickness(4), Padding = new Thickness(5), Background = index == _record ? Gold : null,
                    Content = new StackPanel { Children = { card, new TextBlock { Text = (index + 1).ToString() } } }
                };
                button.Click += (_, _) => _ = Run(() => { _strip = false; SelectRecord(index); return Task.CompletedTask; });
                button.DetachedFromVisualTree += (_, _) => card.Clear();
                run.Children.Add(button);
            }
            view = new ScrollViewer { Content = run, Background = Surface };
        }
        else
        {
            _canvas.SetPage(pages[_record], _project.Print, _elements, _selected, showMedia: true);
            view = _canvas;
        }
        Grid.SetRow(view, 1);
        editing.Children.Add(view);
        var editActions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        if (CanEditLayout)
        {
            editActions.Children.Add(Action(Tr("Добавить текст", "Add text"), () => AddElement(CellContentType.Text)));
            editActions.Children.Add(Action("QR", () => AddElement(CellContentType.Qr)));
            editActions.Children.Add(Action(Tr("Штрихкод", "Barcode"), () => AddElement(CellContentType.Barcode)));
            editActions.Children.Add(Action(Tr("Изображение", "Image"), AddImage));
            editActions.Children.Add(Action(Tr("Отменить", "Undo"), () => Undo(false), enabled: LayoutHistory.CanUndo));
            editActions.Children.Add(Action(Tr("Повторить", "Redo"), () => Undo(true), enabled: LayoutHistory.CanRedo));
        }
        if (_record > 0)
            editActions.Children.Add(Action(Tr("Изменить данные записи…", "Edit record data…"), EditRecord));
        Grid.SetRow(editActions, 2);
        editing.Children.Add(editActions);
        editor.Children.Add(editing);

        var inspector = new ScrollViewer { Content = _inspector, Padding = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(inspector, 1);
        editor.Children.Add(inspector);
        _workspace.Content = editor;
        RefreshInspector();
    }

    private void RefreshInspector()
    {
        _inspector.Content = null;
        if (_elementsList.Parent is Panel previous) previous.Children.Remove(_elementsList);
        var content = new StackPanel { Spacing = 7, Margin = new Thickness(2, 0, 8, 9) };
        content.Children.Add(new TextBlock
        {
            Text = Tr("Макет и элементы", "Layout and elements"), FontSize = 18, FontWeight = FontWeight.SemiBold
        });
        content.Children.Add(Action(Tr("Размеры носителя…", "Media size…"), EditMedia));
        if (!CanEditLayout)
        {
            content.Children.Add(new TextBlock { Text = Tr("Положение и формат полей задаются на первой этикетке.", "Set field positions and styles on the first label."), TextWrapping = TextWrapping.Wrap });
            content.Children.Add(Action(Tr("Изменить этикетку", "Edit label data"), EditRecord));
            _inspector.Content = content;
            return;
        }

        _updatingList = true;
        _elementsList.ItemsSource = _elements.Select((e, i) => $"{i + 1}. {Describe(e.Content)}").ToList();
        _elementsList.SelectedIndex = _selected;
        _updatingList = false;
        content.Children.Add(_elementsList);

        if (_selected >= 0 && _selected < _elements.Count)
            AddElementFields(content, _elements[_selected]);
        _inspector.Content = content;
    }

    private void AddElementFields(StackPanel form, Element element)
    {
        var data = TextBox(element.Content.Type == CellContentType.Barcode ? element.Content.BarcodeData : element.Content.Text);
        data.AcceptsReturn = element.Content.Type is CellContentType.Text or CellContentType.Empty;
        data.MinHeight = data.AcceptsReturn ? 62 : 32;
        data.TextWrapping = TextWrapping.Wrap;
        if (element.Content.Type != CellContentType.Image) Field(form, Tr("Данные для печати", "Print data"), data);
        var font = TextBox(element.Content.FontName);
        var fontSize = Number(element.Content.FontSizePt is { } size ? (decimal)size : null, 1, 500, 0.5m);
        var bold = new CheckBox { Content = Tr("Полужирный", "Bold"), IsChecked = element.Content.Bold };
        var fit = new CheckBox { Content = Tr("Автоподгонка", "Fit text"), IsChecked = element.Content.AutoFitText || element.Content.FontSizePt is null };
        var invert = new CheckBox { Content = Tr("Инверсия цвета", "Invert colors"), IsChecked = element.Content.InvertColors };
        var symbol = Choice(["Code 128", "EAN-13"], element.Content.BarcodeSymbol == BarcodeSymbol.Ean13 ? 1 : 0);
        if (element.Content.Type is CellContentType.Empty or CellContentType.Text)
        {
            Field(form, Tr("Семейство шрифта", "Font family"), font);
            Field(form, Tr("Размер шрифта, пт", "Font size, pt"), fontSize);
            form.Children.Add(bold);
            form.Children.Add(fit);
        }
        else if (element.Content.Type == CellContentType.Barcode) Field(form, Tr("Тип штрихкода", "Barcode type"), symbol);
        form.Children.Add(invert);

        var x = Number(Mm(element.X), 0, 300);
        var y = Number(Mm(element.Y), 0, 762);
        var width = Number(Mm(element.Width), 0.1m, 300);
        var height = Number(Mm(element.Height), 0.1m, 762);
        if (element.Object is not null)
        {
            pairedField(form, "X, mm", x, "Y, mm", y);
            pairedField(form, Tr("Ширина, мм", "Width, mm"), width, Tr("Высота, мм", "Height, mm"), height);
            form.Children.Add(Action(Tr("Удалить элемент", "Delete element"), () =>
            {
                LayoutHistory.Push(EditingLayout);
                EditingLayout.Objects.Remove(element.Object);
                _selected = -1;
                LayoutChanged();
                return Task.CompletedTask;
            }));
        }
        else form.Children.Add(Action(Tr("Перемещать свободно", "Detach from cell"), () =>
        {
            LayoutHistory.Push(EditingLayout);
            var binding = LabelPrintPages.GetBindings(EditingLayout).IndexOf(element.Content);
            EditingLayout.Objects.Add(new LabelObject
            {
                XUm = element.X, YUm = element.Y, WidthUm = element.Width, HeightUm = element.Height,
                CsvColumn = binding < 0 ? null : binding, Content = element.Content.Clone()
            });
            EditingLayout.Rows[element.Row].Cells[element.Column].Content = new CellContent();
            _selected = GetElements(EditingLayout).Count - 1;
            LayoutChanged();
            return Task.CompletedTask;
        }));
        form.Children.Add(Action(Tr("Применить", "Apply"), () =>
        {
            var newWidth = ToUm(width);
            var newHeight = ToUm(height);
            var newX = ToUm(x);
            var newY = ToUm(y);
            if (element.Object is not null)
            {
                var printX = newX + Units.MmToUm(_project!.Print.OffsetXMm);
                var printY = newY + Units.MmToUm(_project.Print.OffsetYMm);
                if (newWidth <= 0 || newHeight <= 0 || printX < 0 || printY < 0 ||
                    printX + newWidth > EditingLayout.MediaWidthUm || printY + newHeight > EditingLayout.MediaHeightUm)
                    throw new InvalidDataException(Tr("Элемент выходит за размеры носителя.", "The element exceeds the media bounds."));
            }
            LayoutHistory.Push(EditingLayout);
            var input = data.Text ?? "";
            if (element.Content.Type == CellContentType.Barcode)
            {
                element.Content.BarcodeData = input;
                element.Content.BarcodeSymbol = symbol.SelectedIndex == 1 ? BarcodeSymbol.Ean13 : BarcodeSymbol.Code128;
            }
            else if (element.Content.Type != CellContentType.Image)
            {
                if (element.Content.Type == CellContentType.Empty) element.Content.Type = CellContentType.Text;
                element.Content.Text = input;
            }
            if (element.Content.Type == CellContentType.Text)
            {
                element.Content.FontName = EmptyToNull(font.Text);
                element.Content.FontSizePt = fontSize.Value is { } pts ? (float)pts : null;
                element.Content.Bold = bold.IsChecked == true;
                element.Content.AutoFitText = fit.IsChecked == true;
            }
            element.Content.InvertColors = invert.IsChecked == true;
            if (element.Object is { } obj) { obj.XUm = newX; obj.YUm = newY; obj.WidthUm = newWidth; obj.HeightUm = newHeight; }
            LayoutChanged();
            return Task.CompletedTask;
        }, primary: true));

        static void pairedField(StackPanel target, string first, Control a, string second, Control b)
        {
            var pair = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
            var left = new StackPanel();
            var right = new StackPanel();
            Field(left, first, a);
            Field(right, second, b);
            Grid.SetColumn(right, 1);
            pair.Children.Add(left);
            pair.Children.Add(right);
            target.Children.Add(pair);
        }
    }

    private static string Describe(CellContent content)
    {
        var type = content.Type switch
        {
            CellContentType.Empty => Tr("Пустая ячейка", "Empty cell"),
            CellContentType.Text => Tr("Текст", "Text"),
            CellContentType.Image => Tr("Изображение", "Image"),
            CellContentType.Barcode => Tr("Штрихкод", "Barcode"),
            _ => "QR"
        };
        var value = content.Type == CellContentType.Barcode ? content.BarcodeData : content.Text;
        var caption = value.Replace('\r', ' ').Replace('\n', ' ');
        return caption.Length == 0 ? type : $"{type} · {(caption.Length > 34 ? caption[..34] + "…" : caption)}";
    }

    private static List<Element> GetElements(LabelLayout layout)
    {
        var positions = LabelPrintLayout.Create(layout).Cells;
        var result = positions.Where(c => layout.Rows[c.Row].Cells[c.Column].Content.Type != CellContentType.Empty || layout.Objects.Count == 0)
            .Select(c => new Element(layout.Rows[c.Row].Cells[c.Column].Content, null, c.Row, c.Column, c.X, c.Y, c.Width, c.Height)).ToList();
        result.AddRange(layout.Objects.Select(o => new Element(o.Content, o, -1, -1, o.XUm, o.YUm, o.WidthUm, o.HeightUm)));
        return result;
    }

    private void SelectElement(int index)
    {
        if (!CanEditLayout) return;
        _selected = index >= 0 && index < _elements.Count ? index : -1;
        _canvas.Select(_selected);
        RefreshInspector();
    }

    private Task NavigateRecord(int direction)
    {
        SelectRecord(_record + direction);
        return Task.CompletedTask;
    }

    private void SelectRecord(int record)
    {
        _record = record;
        _selected = -1;
        RefreshProject();
    }

    private Task AddElement(CellContentType type) => AddElement(new CellContent { Type = type, AutoFitText = type == CellContentType.Text });

    private Task AddElement(CellContent content)
    {
        var layout = EditingLayout;
        var width = Math.Min(25_000, layout.MediaWidthUm);
        var height = Math.Min(content.Type is CellContentType.Qr or CellContentType.Image ? 20_000 : 8_000, layout.MediaHeightUm);
        LayoutHistory.Push(layout);
        layout.Objects.Add(new LabelObject
        {
            XUm = (layout.MediaWidthUm - width) / 2,
            YUm = (layout.MediaHeightUm - height) / 2,
            WidthUm = width, HeightUm = height, Content = content
        });
        _selected = GetElements(layout).Count - 1;
        LayoutChanged();
        return Task.CompletedTask;
    }

    private async Task AddImage()
    {
        var file = await PickOpen(new FilePickerFileType(Tr("Изображения", "Images")) { Patterns = ["*.png", "*.jpg", "*.jpeg"] });
        if (file is null) return;
        var bytes = await ReadImage(file);
        await AddElement(new CellContent
        {
            Type = CellContentType.Image, ImageBase64 = Convert.ToBase64String(bytes),
            ImageFormat = file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpeg"
        });
    }

    private static async Task<byte[]> ReadImage(string path)
    {
        if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException(Tr("Изображение слишком большое.", "The image is too large."));
        return await File.ReadAllBytesAsync(path);
    }

    private Task Undo(bool redo)
    {
        var layout = EditingLayout;
        var history = LayoutHistory;
        if (redo ? history.Redo(ref layout) : history.Undo(ref layout))
        {
            if (_layoutRecord is null) _project!.Ribbon.Layout = layout;
            else _recordLayout = layout;
            LayoutChanged();
        }
        return Task.CompletedTask;
    }

    private async Task EditMedia()
    {
        var old = Layout;
        var notchLength = old.AlternateNotch is { } notch ? Number(Mm(notch.HeightUm), 0.001m, 762) : null;
        var notched = notchLength is not null;
        var form = new StackPanel { Spacing = 8 };
        var w = Number(Mm(old.MediaWidthUm), 1, 300);
        var h = Number(Mm(old.MediaHeightUm), notched ? 4 : 1, 762);
        w.IsEnabled = !notched;
        Field(form, Tr("Ширина носителя, мм", "Media width, mm"), w);
        Field(form, notched ? L10n.T("Setup.FeedPitch") : Tr("Длина страницы, мм", "Page length, mm"), h);
        if (notchLength is not null)
        {
            Field(form, L10n.T("Setup.NotchLength"), notchLength);
            form.Children.Add(new TextBlock
            {
                Text = L10n.T("Setup.FixedNotchedStock"), TextWrapping = TextWrapping.Wrap
            });
        }
        if (!await ConfirmForm(Tr("Носитель", "Media"), form, Tr("Применить", "Apply"))) return;
        var candidate = old.Clone();
        if (notchLength is not null)
            GeometryOperations.ResizeNotchedStockPitch(candidate, ToUm(h), ToUm(notchLength));
        else
        {
            candidate.MediaWidthUm = ToUm(w);
            candidate.MediaHeightUm = ToUm(h);
            if (candidate.Rows is [var row] && row.Cells is [var cell] &&
                row.HeightUm == old.MediaHeightUm && cell.WidthUm == old.MediaWidthUm)
            {
                candidate.Geometry.WidthUm = cell.WidthUm = candidate.MediaWidthUm;
                candidate.Geometry.HeightUm = row.HeightUm = candidate.MediaHeightUm;
            }
        }
        using var raster = LabelRaster.Render(candidate, _project!.Print, 96);
        _history.Push(old);
        _project.Ribbon.Layout = candidate;
        Changed();
    }

    private void Changed()
    {
        _dirty = true;
        RefreshProject();
    }

    private void LayoutChanged()
    {
        if (_layoutRecord is { } record) LabelPrintPages.StoreRecordLayout(record, EditingLayout);
        Changed();
    }

    private async Task NewProject()
    {
        if (!await ConfirmDiscard()) return;
        var template = await ChooseTemplate();
        if (template is null) return;
        await CreateProject(template);
    }

    private async Task CreateProject(LabelTemplate template)
    {
        var form = new StackPanel { Spacing = 8 };
        var name = TextBox(template.Name);
        Field(form, Tr("Имя проекта", "Project name"), name);
        if (!await ConfirmForm(Tr("Новый проект", "New project"), form, Tr("Создать", "Create"))) return;
        var title = EmptyToNull(name.Text) ?? throw new InvalidDataException(Tr("Укажите имя проекта.", "Enter a project name."));
        ReplaceProject(template.CreateProject(title), null, true, template);
    }

    private Task SelectAndApplyTemplate() => WithChosenTemplate();

    private async Task WithChosenTemplate()
    {
        var template = await ChooseTemplate();
        if (template is null || !await ConfirmDiscard()) return;
        await CreateProject(template);
    }

    private async Task OpenProject()
    {
        if (!await ConfirmDiscard()) return;
        var path = await PickOpen(ProjectFiles);
        if (path is not null) LoadProject(path);
    }

    private void LoadProject(string path)
    {
        var project = ProjectFileIo.Load(path);
        ArgumentNullException.ThrowIfNull(project.Ribbon);
        project.Print ??= new PrintSettings();
        GetElements(project.Ribbon.GetLayout());
        ReplaceProject(project, path, false);
    }

    private void ReplaceProject(ProjectFile project, string? path, bool dirty, LabelTemplate? template = null)
    {
        _project = project;
        _file = path;
        _lastSavedTemplate = template;
        _dirty = dirty;
        _selected = -1;
        _record = 0;
        _strip = project.Ribbon.GetLayout().AlternateNotch is not null && Pages.Count > 1;
        _history.Clear();
        RebuildShell();
    }

    private async Task SaveProject(bool saveAs)
    {
        if (_project is null) return;
        var target = saveAs || _file is null ? await PickSave(ProjectFiles, _project.Name, "mprj") : _file;
        if (target is null) return;
        ProjectFileIo.Save(_project, target);
        _file = target;
        _dirty = false;
        RefreshProject();
    }

    private async Task<bool> ConfirmDiscard()
    {
        if (!_dirty) return true;
        var form = new StackPanel { Spacing = 8 };
        form.Children.Add(new TextBlock
        {
            Text = Tr("В проекте есть несохранённые изменения.", "The project has unsaved changes."),
            TextWrapping = TextWrapping.Wrap
        });
        var dialog = DialogWindow(Tr("Сохранение проекта", "Save project"), form, 440);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = Tr("Сохранить", "Save"), Margin = new Thickness(4) };
        var discard = new Button { Content = Tr("Продолжить без сохранения", "Discard changes"), Margin = new Thickness(4) };
        var cancel = new Button { Content = Tr("Отмена", "Cancel"), Margin = new Thickness(4) };
        save.Click += (_, _) => dialog.Close(1);
        discard.Click += (_, _) => dialog.Close(2);
        cancel.Click += (_, _) => dialog.Close(0);
        actions.Children.Add(save);
        actions.Children.Add(discard);
        actions.Children.Add(cancel);
        form.Children.Add(actions);
        var decision = await dialog.ShowDialog<int>(this);
        if (decision == 1) await SaveProject(false);
        return decision == 2 || decision == 1 && !_dirty;
    }

    private async Task Run(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        try { await action(); }
        catch (Exception ex)
        {
            await Message(Tr("Ошибка", "Error"), ex.Message);
        }
        finally { _busy = false; }
    }

    private Button Action(string caption, Func<Task> execute, bool primary = false, bool enabled = true)
    {
        var button = new Button { Content = caption, IsEnabled = enabled, Margin = new Thickness(3) };
        if (primary)
        {
            button.Background = Gold;
            button.Foreground = Ink;
            button.FontWeight = FontWeight.SemiBold;
        }
        button.Click += (_, _) => _ = Run(execute);
        return button;
    }

    private static void Field(Panel form, string caption, Control control)
    {
        var label = new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap };
        Avalonia.Automation.AutomationProperties.SetName(control, caption);
        form.Children.Add(label);
        form.Children.Add(control);
    }

    private static TextBox TextBox(string? value) => new() { Text = value ?? "", HorizontalAlignment = HorizontalAlignment.Stretch };
    private static NumericUpDown Number(decimal? value, decimal min, decimal max, decimal step = 0.1m) => new()
    {
        Value = value.HasValue ? Math.Clamp(value.Value, min, max) : null,
        Minimum = min, Maximum = max, Increment = step, FormatString = "0.###",
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private static ComboBox Choice(IEnumerable<string> options, int selected = 0) => new()
    {
        ItemsSource = options.ToList(), SelectedIndex = selected, HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private static string? EmptyToNull(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private static decimal Mm(long um) => um / 1000m;
    private static long ToUm(NumericUpDown box) => Units.MmToUm((double)(box.Value ?? 0));

    private static Window DialogWindow(string title, Control body, int width, int maxHeight = 690) =>
        new()
        {
            Title = title, Width = width, MaxHeight = maxHeight,
            SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border
            {
                Padding = new Thickness(17),
                Child = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
            }
        };

    private async Task<bool> ConfirmForm(string title, StackPanel body, string caption, int width = 460, Window? owner = null)
    {
        var dialog = DialogWindow(title, body, width);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 9, 0, 0) };
        var confirm = new Button { Content = caption, Background = Gold, Foreground = Ink };
        var cancel = new Button { Content = Tr("Отмена", "Cancel") };
        confirm.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        buttons.Children.Add(confirm);
        buttons.Children.Add(cancel);
        body.Children.Add(buttons);
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) dialog.Close(false); };
        return await dialog.ShowDialog<bool>(owner ?? this);
    }

    private async Task Message(string title, string text, Window? owner = null)
    {
        var body = new StackPanel { Spacing = 13 };
        body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        var dialog = DialogWindow(title, body, 480);
        var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => dialog.Close();
        body.Children.Add(ok);
        dialog.KeyDown += (_, e) => { if (e.Key is Key.Escape or Key.Enter) dialog.Close(); };
        await dialog.ShowDialog(owner ?? this);
    }

    private async Task<string?> PickOpen(FilePickerFileType type, Window? owner = null)
    {
        var files = await (owner ?? this).StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { FileTypeFilter = [type], AllowMultiple = false });
        if (files.Count == 0) return null;
        return files[0].TryGetLocalPath() ?? throw new IOException(Tr("Выберите локальный файл.", "Choose a local file."));
    }

    private async Task<string?> PickSave(FilePickerFileType type, string title, string extension)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            FileTypeChoices = [type], SuggestedFileName = title, DefaultExtension = extension, ShowOverwritePrompt = true
        });
        if (file is null) return null;
        return file.TryGetLocalPath() ?? throw new IOException(Tr("Выберите локальный файл.", "Choose a local file."));
    }

    private sealed record Element(CellContent Content, LabelObject? Object, int Row, int Column, long X, long Y, long Width, long Height);

    private sealed class LayoutCanvas : Control
    {
        private Bitmap? _bitmap;
        private bool _hasNotchedStock;
        private long _mediaWidth;
        private long _mediaHeight;
        private PrintSettings _settings = new();
        private IReadOnlyList<Element> _elements = [];
        private int _selected = -1;
        private int _dragging = -1;
        private Point _dragStart;
        private Vector _delta;
        private string? _error;
        public event Action<int>? SelectionRequested;
        public event Action<int, long, long>? MoveRequested;

        public Thickness PaperInset { get; init; } = new(19, 20);

        public void SetPage(LabelLayout layout, PrintSettings settings, IReadOnlyList<Element> elements, int selected,
            int dpi = 144, bool validateForPrint = false, bool showMedia = false)
        {
            Clear();
            _settings = settings;
            _mediaWidth = layout.MediaWidthUm;
            _mediaHeight = layout.MediaHeightUm;
            _hasNotchedStock = showMedia && layout.AlternateNotch is not null;
            _elements = elements;
            _selected = selected;
            try
            {
                using var raster = LabelRaster.Render(layout, settings, dpi, validateForPrint, showMedia);
                _bitmap = ToBitmap(raster);
            }
            catch (Exception ex) { _error = ex.Message; }
            InvalidateVisual();
        }

        public void Clear()
        {
            _bitmap?.Dispose();
            _bitmap = null;
            _error = null;
        }

        public void Select(int index)
        {
            _selected = index;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            context.FillRectangle(Surface, new Rect(Bounds.Size));
            var paper = Paper;
            if (_bitmap is not null && paper.Width > 0)
            {
                if (!_hasNotchedStock)
                    context.FillRectangle(Brushes.LightSlateGray, paper.Translate(new Vector(2, 3)));
                context.DrawImage(_bitmap, paper);
            }
            else if (_error is not null)
            {
                var text = new FormattedText(_error, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    Typeface.Default, 13, Brushes.DarkRed) { MaxTextWidth = Math.Max(1, Bounds.Width - 28) };
                context.DrawText(text, new Point(14, 14));
            }
            if (_selected >= 0 && _selected < _elements.Count)
            {
                var rect = ElementBounds(_elements[_selected]);
                if (_dragging == _selected) rect = rect.Translate(_delta);
                context.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#B17800")), 2, DashStyle.Dash), rect);
            }
        }

        private Rect Paper
        {
            get
            {
                if (_mediaWidth <= 0 || _mediaHeight <= 0) return default;
                var scale = Math.Min(Math.Max(0, Bounds.Width - PaperInset.Left - PaperInset.Right) / _mediaWidth,
                    Math.Max(0, Bounds.Height - PaperInset.Top - PaperInset.Bottom) / _mediaHeight);
                var width = _mediaWidth * scale;
                var height = _mediaHeight * scale;
                return new Rect((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height);
            }
        }

        private Rect ElementBounds(Element element)
        {
            var paper = Paper;
            if (_mediaWidth == 0 || _mediaHeight == 0) return default;
            var sx = paper.Width / _mediaWidth;
            var sy = paper.Height / _mediaHeight;
            return new Rect(paper.X + (element.X + Units.MmToUm(_settings.OffsetXMm)) * sx,
                paper.Y + (element.Y + Units.MmToUm(_settings.OffsetYMm)) * sy, element.Width * sx, element.Height * sy);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _dragStart = e.GetPosition(this);
            _dragging = -1;
            for (var index = _elements.Count - 1; index >= 0; index--)
            {
                if (!ElementBounds(_elements[index]).Contains(_dragStart)) continue;
                SelectionRequested?.Invoke(index);
                if (_elements[index].Object is not null)
                {
                    _dragging = index;
                    _delta = default;
                    e.Pointer.Capture(this);
                }
                break;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_dragging < 0) return;
            _delta = e.GetPosition(this) - _dragStart;
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_dragging < 0) return;
            var dragging = _dragging;
            _dragging = -1;
            e.Pointer.Capture(null);
            var paper = Paper;
            if (paper.Width > 0 && paper.Height > 0 && _delta.Length > 3)
                MoveRequested?.Invoke(dragging, (long)(_delta.X * _mediaWidth / paper.Width), (long)(_delta.Y * _mediaHeight / paper.Height));
            InvalidateVisual();
        }
    }

    private static Bitmap ToBitmap(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = data.AsStream();
        return new Bitmap(stream);
    }
}
