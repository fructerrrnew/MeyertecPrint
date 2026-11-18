using System.Runtime.InteropServices;
using System.Drawing.Printing;

namespace MeyertecPrint;

/// <summary>
/// Главное окно MEYERTEC PRINT.
/// Верхняя панель (меню): "Файл", "Вид", "Язык" — дизайн с сайта MEYERTEC
/// (тёмная панель #0f202a, жёлтая подсветка #ffcb04, шрифт Rubik).
/// Все названия берутся из языковых файлов (папка Languages).
/// </summary>
public class MainForm : BrandForm
{
    private const string AppTitle = "MEYERTEC PRINT";

    private MenuStrip _menu = null!;
    private ToolStripMenuItem _fileMenu = null!;
    private ToolStripMenuItem _languageMenu = null!;
    private ToolStripMenuItem _exitItem = null!;
    private readonly Dictionary<string, ToolStripMenuItem> _languageItems = new();

    private ToolStripMenuItem _newItem = null!;
    private ToolStripMenuItem _openItem = null!;
    private ToolStripMenuItem _saveItem = null!;
    private ToolStripMenuItem _importCsvItem = null!;
    private ToolStripMenuItem _openWstxItem = null!;
    private ToolStripMenuItem _saveWstxItem = null!;
    private ToolStripMenuItem _printSettingsItem = null!;
    private ToolStripMenuItem _printItem = null!;
    private ToolStripMenuItem _viewMenu = null!;
    private ToolStripMenuItem _unitsMmItem = null!;
    private ToolStripMenuItem _unitsInchItem = null!;

    private Panel _toolbar = null!;
    private WelcomeView _welcome = null!;
    private readonly ImageList _rowHeight = new() { ImageSize = new Size(1, 34) };
    private readonly ToolTip _toolTips = new();
    private Button _tbNew = null!;
    private Button _tbOpen = null!;
    private Button _tbSave = null!;
    private Button _tbSaveAs = null!;
    private Button _tbPageSetup = null!;
    private Button _tbPrintSettings = null!;
    private Button _tbPreview = null!;
    private Button _tbPrint = null!;

    private TabControl _tabs = null!;
    private TabPage _ribbonTab = null!;
    private TabPage _editorTab = null!;
    private LabelEditorControl _editor = null!;

    private ListView _ribbonList = null!;
    private ColumnHeader _numberColumn = null!;
    private ColumnHeader _nameColumn = null!;
    private Button _recordEdit = null!, _recordCopy = null!, _recordDelete = null!, _recordUp = null!, _recordDown = null!;
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _statusLeft = null!;
    private ToolStripStatusLabel _statusRight = null!;

    private ProjectFile? _project;
    private string? _projectPath;
    private bool _dirty;
    private PrintDocument? _printDocument;
    private RecoveryStore? _recovery;
    private string? _recoveredPath;
    private readonly System.Windows.Forms.Timer _recoveryTimer = new() { Interval = 60_000 };

    public MainForm()
    {
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = AppTitle;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1280, 760);
        MinimumSize = new Size(800, 540);
        BackColor = Theme.LightBackground;
        Font = Theme.UiFont();

        _welcome = new WelcomeView(NewProject, OpenProject);
        BuildRibbonList();
        BuildStatusBar();
        BuildToolbar();
        BuildMenu();
        ApplyStrings();
        RefreshProjectUi();

        L10n.Changed += OnLanguageChanged;
        try
        {
            _recovery = new RecoveryStore();
            _recoveryTimer.Tick += (_, _) => SaveRecovery();
            _recoveryTimer.Start();
            Shown += (_, _) => OfferRecovery();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { AppLog.Write(ex, "recovery initialization"); }
    }

    private void BuildMenu()
    {
        _menu = new MenuStrip
        {
            Dock = DockStyle.Top,
            BackColor = Theme.ProjectBar,
            ForeColor = Color.White,
            Font = Theme.UiFont(9.75f),
            Renderer = new BrandMenuRenderer(),
            Margin = Padding.Empty, Padding = new Padding(14, 3, 14, 3),
            ImageScalingSize = new Size(22, 22),
        };

        _fileMenu = new ToolStripMenuItem();
        _newItem = new ToolStripMenuItem { ShortcutKeys = Keys.Control | Keys.N };
        _newItem.Click += (_, _) => NewProject();
        _openItem = new ToolStripMenuItem { ShortcutKeys = Keys.Control | Keys.O };
        _openItem.Click += (_, _) => OpenProject();
        _saveItem = new ToolStripMenuItem { ShortcutKeys = Keys.Control | Keys.S };
        _saveItem.Click += (_, _) => SaveProject();
        _importCsvItem = new ToolStripMenuItem();
        _importCsvItem.Click += (_, _) => ImportCsv();
        _openWstxItem = new ToolStripMenuItem();
        _openWstxItem.Click += (_, _) => OpenWstx();
        _saveWstxItem = new ToolStripMenuItem();
        _saveWstxItem.Click += (_, _) => SaveWstx();
        _printSettingsItem = new ToolStripMenuItem();
        _printSettingsItem.Click += (_, _) => PrintSettings();
        _printItem = new ToolStripMenuItem { ShortcutKeys = Keys.Control | Keys.P };
        _printItem.Click += (_, _) => PrintProject();
        _exitItem = new ToolStripMenuItem();
        _exitItem.Click += (_, _) => Exit();

        _viewMenu = new ToolStripMenuItem();
        _unitsMmItem = new ToolStripMenuItem { Checked = true, CheckOnClick = true };
        _unitsMmItem.Click += (_, _) => SetUnit(inch: false);
        _unitsInchItem = new ToolStripMenuItem { CheckOnClick = true };
        _unitsInchItem.Click += (_, _) => SetUnit(inch: true);
        _viewMenu.DropDownItems.Add(_unitsMmItem);
        _viewMenu.DropDownItems.Add(_unitsInchItem);

        _fileMenu.DropDownItems.Add(_newItem);
        _fileMenu.DropDownItems.Add(_openItem);
        _fileMenu.DropDownItems.Add(_saveItem);
        _fileMenu.DropDownItems.Add(new ToolStripSeparator());
        _fileMenu.DropDownItems.Add(_importCsvItem);
        _fileMenu.DropDownItems.Add(_openWstxItem);
        _fileMenu.DropDownItems.Add(_saveWstxItem);
        _fileMenu.DropDownItems.Add(new ToolStripSeparator());
        _fileMenu.DropDownItems.Add(_printSettingsItem);
        _fileMenu.DropDownItems.Add(_printItem);
        _fileMenu.DropDownItems.Add(new ToolStripSeparator());
        _fileMenu.DropDownItems.Add(_exitItem);

        _languageMenu = new ToolStripMenuItem();
        foreach (var code in L10n.Available())
        {
            var item = new ToolStripMenuItem { Tag = code };
            item.Click += OnLanguageItemClick;
            _languageMenu.DropDownItems.Add(item);
            _languageItems[code] = item;
        }

        _menu.Items.Add(_fileMenu);
        _menu.Items.Add(_viewMenu);
        _menu.Items.Add(_languageMenu);

        MainMenuStrip = _menu;
        var workspace = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        workspace.Controls.Add(_welcome);
        workspace.Controls.Add(_tabs);

        // Explicit rows keep the menu above project commands when a project opens.
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _statusStrip.Margin = Padding.Empty;
        shell.Controls.Add(_menu, 0, 0);
        shell.Controls.Add(_toolbar, 0, 1);
        shell.Controls.Add(workspace, 0, 2);
        shell.Controls.Add(_statusStrip, 0, 3);
        Controls.Add(shell);
    }

    private void BuildToolbar()
    {
        _toolbar = new Panel
        {
            Dock = DockStyle.Top, Height = 42, BackColor = Theme.ProjectBar,
            Margin = Padding.Empty, Padding = new Padding(14, 0, 12, 0)
        };
        var commands = new FlowLayoutPanel
        {
            Dock = DockStyle.Left, AutoSize = true, WrapContents = false, BackColor = Theme.ProjectBar,
            Padding = new Padding(0, 5, 0, 0)
        };
        _tbNew = MakeToolButton("01_new_file", NewProject);
        _tbOpen = MakeToolButton("02_open", OpenProject);
        _tbSave = MakeToolButton("03_save", () => SaveProject(), caption: true);
        _tbSaveAs = MakeToolButton("04_save_as", () => SaveProject(saveAs: true));
        _tbPageSetup = MakeToolButton("05_page_setup", PageSettings);
        _tbPrintSettings = MakeToolButton("06_print_setup", PrintSettings);
        _tbPreview = MakeToolButton("07_preview", ShowPreview, caption: true, primary: true);
        _tbPrint = MakeToolButton("08_print", PrintProject);
        foreach (var button in new[] { _tbNew, _tbOpen, _tbSave, _tbSaveAs, _tbPageSetup, _tbPrintSettings, _tbPrint, _tbPreview })
            commands.Controls.Add(button);
        _tbPageSetup.Margin = new Padding(14, 0, 0, 0);
        _tbPreview.Margin = new Padding(12, 0, 0, 0);
        _toolbar.Controls.Add(commands);
    }

    private Button MakeToolButton(string icon, Action action, bool enabled = true,
        bool caption = false, bool primary = false)
    {
        var button = new Button
        {
            Image = Theme.Icon(icon, LogicalToDeviceUnits(20)), Size = new Size(32, 32),
            MinimumSize = new Size(32, 32), Margin = new Padding(2, 0, 0, 0),
            AutoSize = caption, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Padding = caption ? new Padding(10, 0, 10, 0) : Padding.Empty,
            Enabled = enabled
        };
        Theme.StyleButton(button);
        button.BackColor = Theme.ProjectBar;
        button.ForeColor = primary ? Theme.Yellow : Color.White;
        button.FlatAppearance.BorderSize = caption ? 1 : 0;
        button.FlatAppearance.BorderColor = primary ? Theme.Yellow : Color.FromArgb(77, 83, 86);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(42, 49, 52);
        button.FlatAppearance.MouseDownBackColor = Theme.Dark;
        button.Click += (_, _) => action();
        return button;
    }

    private void BuildRibbonList()
    {
        _ribbonList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            BorderStyle = BorderStyle.None,
            SmallImageList = _rowHeight,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            MultiSelect = false,
            HideSelection = false,
            BackColor = Color.White,
            ForeColor = Theme.Dark,
            Padding = new Padding(8),
        };
        _numberColumn = new ColumnHeader { Width = 70 };
        _nameColumn = new ColumnHeader { Width = 400 };
        _ribbonList.Columns.Add(_numberColumn);
        _ribbonList.Columns.Add(_nameColumn);

        // Вкладка «Лента» — список этикеток; вкладка «Редактор» — полоса с ячейками
        _editor = new LabelEditorControl();
        // Без Dock.Fill редактор остаётся блоком 200×100 в углу вкладки:
        // холст сжимается до нуля, панели обрезаются (самопроверка: раздел 10)
        _editor.Dock = DockStyle.Fill;
        _editor.LayoutChanged += OnEditorLayoutChanged;

        _tabs = new TabControl
        {
            Dock = DockStyle.Fill, SizeMode = TabSizeMode.Fixed, ItemSize = new Size(124, 33),
            DrawMode = TabDrawMode.OwnerDrawFixed
        };
        _tabs.DrawItem += (_, e) =>
        {
            var selected = e.Index == _tabs.SelectedIndex;
            var tab = _tabs.TabPages[e.Index];
            using var fill = new SolidBrush(selected ? Color.White : Theme.LightBackground);
            e.Graphics.FillRectangle(fill, e.Bounds);
            TextRenderer.DrawText(e.Graphics, tab.Text, _tabs.Font, e.Bounds, Theme.Dark,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (selected)
            {
                using var accent = new SolidBrush(Theme.Yellow);
                e.Graphics.FillRectangle(accent, e.Bounds.X + 8, e.Bounds.Bottom - 3, e.Bounds.Width - 16, 3);
                if (_tabs.Focused) e.DrawFocusRectangle();
            }
        };
        _ribbonTab = new TabPage { BackColor = Color.White, Padding = new Padding(20, 18, 20, 12) };
        _ribbonList.DoubleClick += (_, _) => EditRecord();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, WrapContents = false };
        Button Action(Action run)
        {
            var button = new Button { AutoSize = true, Height = 32, Margin = new Padding(0, 5, 8, 0) };
            Theme.StyleButton(button); button.Click += (_, _) => run(); actions.Controls.Add(button); return button;
        }
        _recordEdit = Action(EditRecord); _recordCopy = Action(CopyRecord); _recordDelete = Action(DeleteRecord);
        _recordUp = Action(() => MoveRecord(-1)); _recordDown = Action(() => MoveRecord(1));
        _ribbonTab.Controls.Add(_ribbonList); _ribbonTab.Controls.Add(actions);
        _editorTab = new TabPage { BackColor = Theme.LightBackground };
        _editorTab.Controls.Add(_editor);
        _tabs.TabPages.Add(_ribbonTab);
        _tabs.TabPages.Add(_editorTab);
    }

    private void BuildStatusBar()
    {
        _statusStrip = new StatusStrip
        {
            BackColor = Color.White,
            ForeColor = Theme.SecondaryText,
            Font = Theme.UiFont(9), SizingGrip = false,
            Padding = new Padding(16, 3, 16, 3)
        };
        _statusLeft = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _statusRight = new ToolStripStatusLabel { TextAlign = ContentAlignment.MiddleRight };
        _statusStrip.Items.Add(_statusLeft);
        _statusStrip.Items.Add(_statusRight);
    }

    /// <summary>Перечитывает все названия из текущих лингвистических файлов.</summary>
    private void ApplyStrings()
    {
        _fileMenu.Text = L10n.T("Menu.File");
        _viewMenu.Text = L10n.T("Menu.View");
        _languageMenu.Text = L10n.T("Menu.Language");
        _newItem.Text = L10n.T("File.NewProject");
        _openItem.Text = L10n.T("File.OpenProject");
        _saveItem.Text = L10n.T("File.SaveProject");
        _importCsvItem.Text = L10n.T("File.ImportCsv");
        _openWstxItem.Text = L10n.T("File.OpenWstx");
        _saveWstxItem.Text = L10n.T("File.SaveAsWstx");
        _printSettingsItem.Text = L10n.T("File.PrintSettings");
        _printItem.Text = L10n.T("Toolbar.Print");
        _unitsMmItem.Text = L10n.T("View.UnitsMm");
        _unitsInchItem.Text = L10n.T("View.UnitsInch");
        _ribbonTab.Text = L10n.T("Tab.Ribbon");
        _editorTab.Text = L10n.T("Tab.Editor");
        SetToolLabel(_tbNew, "Toolbar.NewProject");
        SetToolLabel(_tbOpen, "Toolbar.Open");
        SetToolLabel(_tbSave, "Toolbar.Save");
        SetToolLabel(_tbSaveAs, "Toolbar.SaveAs");
        SetToolLabel(_tbPageSetup, "Toolbar.PageSetup");
        SetToolLabel(_tbPrintSettings, "Toolbar.PrintSettings");
        SetToolLabel(_tbPreview, "Toolbar.Preview");
        SetToolLabel(_tbPrint, "Toolbar.Print");
        _tbSave.Text = "  " + L10n.T("Toolbar.Save");
        _tbPreview.Text = "  " + L10n.T("Toolbar.Preview");
        _welcome.ApplyStrings();
        _editor.ApplyStrings();
        _exitItem.Text = L10n.T("File.Exit");
        _numberColumn.Text = L10n.T("Ribbon.Number");
        _nameColumn.Text = L10n.T("Ribbon.Name");
        _recordEdit.Text = L10n.T("Ribbon.EditRecord"); _recordCopy.Text = L10n.T("Ribbon.CopyRecord");
        _recordDelete.Text = L10n.T("Ribbon.DeleteRecord"); _recordUp.Text = L10n.T("Ribbon.Up"); _recordDown.Text = L10n.T("Ribbon.Down");

        foreach (var (code, item) in _languageItems)
        {
            item.Text = L10n.T("Lang." + code);
            item.Checked = string.Equals(code, L10n.Current, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void SetToolLabel(Button button, string key)
    {
        button.AccessibleName = L10n.T(key);
        _toolTips.SetToolTip(button, button.AccessibleName);
    }

    // ---------------- ЖИЗНЕННЫЙ ЦИКЛ ПРОЕКТА ----------------

    private void NewProject()
    {
        if (!ConfirmDiscard())
            return;

        // Шаг 1: имя и критерий — по типу этикетки или по типу клеммы
        using var first = new NewProjectDialog();
        if (first.ShowDialog(this) != DialogResult.OK)
            return;

        var name = first.ProjectName;
        var mode = first.Mode;

        // Шаг 2: список соответствующих типов, справа — изображение выбранного
        using var second = new TypeSelectionDialog(mode == NewProjectMode.Cleat);
        if (second.ShowDialog(this) != DialogResult.OK)
            return;

        var project = mode == NewProjectMode.Cleat
            ? CreateCleatProject(name, second.SelectedCleat)
            : CreateLabelProject(name, second.SelectedLabelType);
        if (project is null)
            return;

        ClearRecovery();
        _project = project;
        _projectPath = null;
        _dirty = true;
        LoadEditorFromProject();
        RefreshProjectUi();
    }

    /// <summary>
    /// Проект по типу этикетки: геометрия из каталога
    /// (у Custom label в каталоге уже базовая геометрия), 10 этикеток в ленте.
    /// </summary>
    private static ProjectFile? CreateLabelProject(string name, LabelType? type)
    {
        if (type is null)
            return null;

        return BuildProject(name, type.Name, type.Geometry.Clone());
    }

    /// <summary>Проект по типу клеммы: имя — «MTP 1.5», базовая геометрия, 10 этикеток.</summary>
    private ProjectFile? CreateCleatProject(string name, CleatType? cleat)
    {
        if (cleat is null) return null;
        using var dimensions = new MarkerSizeDialog(cleat.DisplayName);
        if (dimensions.ShowDialog(this) != DialogResult.OK) return null;
        var geometry = new LabelGeometry { LabelsInRow = 1, WidthUm = dimensions.WidthUm, HeightUm = dimensions.HeightUm };
        return BuildProject(name, cleat.DisplayName, geometry);
    }

    private static ProjectFile BuildProject(string name, string typeName, LabelGeometry geometry)
    {
        var now = DateTime.Now;
        var project = new ProjectFile
        {
            Name = name,
            Created = now,
            Modified = now,
            Ribbon = new RibbonData
            {
                LabelWidthMm = Units.UmToMm(geometry.WidthUm),
                LabelHeightMm = Units.UmToMm(geometry.HeightUm),
                LabelTypeName = typeName,
                Layout = BuildSingleRowLayout(geometry),
            },
        };

        return project;
    }

    private static LabelLayout BuildSingleRowLayout(LabelGeometry geometry)
    {
        var row = new LabelRow { HeightUm = geometry.HeightUm };
        for (var i = 0; i < geometry.LabelsInRow; i++)
            row.Cells.Add(new LabelCell { WidthUm = geometry.WidthUm });
        return new LabelLayout
        {
            Geometry = geometry,
            MediaWidthUm = Math.Max(25_400, geometry.LeftUm + geometry.WidthUm * geometry.LabelsInRow + geometry.RightUm + geometry.GapBetweenUm * (geometry.LabelsInRow - 1)),
            MediaHeightUm = Math.Max(4_000, geometry.TopUm + geometry.HeightUm + geometry.GapBottomUm),
            Rows = { row },
        };
    }

    private void OpenProject()
    {
        if (!ConfirmDiscard())
            return;

        using var dialog = new BrandedOpenFileDialog
        {
            Title = L10n.T("OpenProject.Title"),
            Filter = ProjectFilter(),
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var opened = ProjectFileIo.Load(dialog.FileName);
            ClearRecovery();
            _project = opened;
            _projectPath = dialog.FileName;
            _dirty = false;
            LoadEditorFromProject();
            RefreshProjectUi();
        }
        catch (Exception ex)
        {
            BrandMessageBox.Show(this,
                string.Format(L10n.T("Error.InvalidFile"), ex.Message),
                AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Сохраняет проект (saveAs: всегда запрашивает файл). Возвращает false, если пользователь отменил.</summary>
    private bool SaveProject(bool saveAs = false)
    {
        if (_project is null)
            return false;

        _editor.CommitPendingEdit();
        string path;
        if (_projectPath is null || saveAs)
        {
            using var dialog = new BrandedSaveFileDialog
            {
                Title = L10n.T("SaveProject.Title"),
                Filter = ProjectFilter(),
                FileName = _project.Name + ProjectFile.FileExtension,
                AddExtension = true,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return false;

            path = dialog.FileName;
        }
        else
        {
            path = _projectPath;
        }

        try
        {
            ProjectFileIo.Save(_project, path);
        }
        catch (Exception ex)
        {
            BrandMessageBox.Show(this,
                string.Format(L10n.T("Error.SaveFailed"), ex.Message),
                AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        _projectPath = path;
        _dirty = false;
        ClearRecovery();
        RefreshProjectUi();
        return true;
    }

    private void Exit() => Close();

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        // Also covers the window close button and Alt+F4.
        if (!e.Cancel && !ConfirmDiscard()) e.Cancel = true;
        if (!e.Cancel)
        {
            _recoveryTimer.Stop();
            ClearRecovery();
            _recovery?.Dispose();
        }
    }

    private void SaveRecovery()
    {
        if (!_dirty || _project is null || _recovery is null) return;
        try { _recovery.Save(_project); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write(ex, "autosave");
            _statusRight.Text = L10n.T("Recovery.WriteFailed");
        }
    }

    private void ClearRecovery()
    {
        try
        {
            _recovery?.Clear();
            if (_recoveredPath is not null && File.Exists(_recoveredPath)) File.Delete(_recoveredPath);
            _recoveredPath = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write(ex, "recovery cleanup"); }
    }

    private void OfferRecovery()
    {
        if (_recovery is null || _project is not null) return;
        try
        {
            var path = _recovery.LatestAbandoned();
            if (path is null) return;
            var recovered = ProjectFileIo.Load(path);
            if (BrandMessageBox.Show(this, string.Format(L10n.T("Recovery.Offer"), recovered.Name, File.GetLastWriteTime(path)),
                AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _project = recovered;
            _projectPath = null; // Never overwrite the original project automatically.
            _recoveredPath = path;
            _dirty = true;
            LoadEditorFromProject();
            RefreshProjectUi();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            AppLog.Write(ex, "recovery load");
            BrandMessageBox.Show(this, string.Format(L10n.T("Recovery.Failed"), ex.Message), AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Если проект изменён — спрашивает: сохранить?
    /// Да → сохранить; Нет → без сохранения; Отмена → действие прерывается (false).
    /// </summary>
    private bool ConfirmDiscard()
    {
        _editor.CommitPendingEdit();
        if (!_dirty)
            return true;

        var result = BrandMessageBox.Show(this,
            L10n.T("Confirm.Discard"),
            AppTitle,
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Warning);

        return result switch
        {
            DialogResult.Yes => SaveProject(),
            DialogResult.No => true,
            _ => false,
        };
    }

    /// <summary>Открывает окно "Настройка печати" (принтер + интерфейс).</summary>
    private PrintDocument GetPrintDocument()
    {
        if (_printDocument is null)
        {
            _printDocument = new PrintDocument { OriginAtMargins = false };
            var installed = PrinterSettings.InstalledPrinters.Cast<string>().ToList();
            var godex = installed.FirstOrDefault(name => name.Contains("G530", StringComparison.OrdinalIgnoreCase));
            if (godex is not null) _printDocument.PrinterSettings.PrinterName = godex;
            else if (!_printDocument.PrinterSettings.IsValid && installed.Count > 0)
                _printDocument.PrinterSettings.PrinterName = installed[0];
        }
        else if (!_printDocument.PrinterSettings.IsValid)
        {
            var installed = PrinterSettings.InstalledPrinters.Cast<string>().ToList();
            var printer = installed.FirstOrDefault(name => name.Contains("G530", StringComparison.OrdinalIgnoreCase))
                ?? installed.FirstOrDefault();
            if (printer is not null) _printDocument.PrinterSettings.PrinterName = printer;
        }
        _printDocument.DocumentName = _project?.Name ?? AppTitle;
        return _printDocument;
    }

    internal static void SetLabelPaper(PrintDocument document, LabelLayout layout)
    {
        var page = document.DefaultPageSettings;
        page.PrinterSettings = document.PrinterSettings;
        var width = checked((int)Math.Round(layout.MediaWidthUm / 254d));
        var height = checked((int)Math.Round(layout.MediaHeightUm / 254d));
        bool Matches(PaperSize size) => Math.Abs(size.Width - width) <= 1 && Math.Abs(size.Height - height) <= 1;
        var selected = page.PrinterSettings.PaperSizes.Cast<PaperSize>().FirstOrDefault(Matches)
            ?? new PaperSize($"{Units.UmToMm(layout.MediaWidthUm):0.###} x {Units.UmToMm(layout.MediaHeightUm):0.###} mm", width, height);
        page.PaperSize = selected;
        page.Landscape = false;
        page.Margins = new Margins(0, 0, 0, 0);
    }

    private void PageSettings()
    {
        if (_project is null || !_editor.HasProject) return;
        _editor.CommitPendingEdit();
        var plugin = PrinterPlugins.All.FirstOrDefault(x => x.Name == _project.PrinterModel);
        using var dialog = new PageSettingsDialog(_editor.Layout, plugin);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _editor.ApplyPrintSettings(_project.Print, dialog.MediaWidthUm, dialog.MediaHeightUm);
    }

    private void PrintSettings() => ShowPrintSetup(false);

    private void PrintProject() => ShowPrintSetup(true);

    private void ShowPrintSetup(bool forPrint)
    {
        if (_project is null || !_editor.HasProject)
        {
            BrandMessageBox.Show(this, L10n.T("Status.NoProject"), AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _editor.CommitPendingEdit();
        try
        {
            var importedCount = LabelPrintPages.CountImported(_editor.Layout, _project.Ribbon.Labels);
            using var dialog = new PrintSettingsDialog(_project, _editor.Layout, importedCount, forPrint);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _project.Print = dialog.Result;
            _project.PrinterModel = dialog.PrinterModel;
            _editor.ApplyPrintSettings(_project.Print, dialog.MediaWidthUm, dialog.MediaHeightUm);
            if (!forPrint) return;

            var pages = LabelPrintPages.Create(_editor.Layout, _project.Ribbon.Labels, dialog.IncludeImported);
            var plugin = PrinterPlugins.All.FirstOrDefault(x => x.Name == _project.PrinterModel);
            using var progress = new PrintProgressDialog(pages, dialog.RangeFrom, dialog.RangeTo, _project.Print, plugin);
            var result = progress.ShowDialog(this);
            if (progress.Error is not null) throw progress.Error;
            if (progress.WasCancelled)
                BrandMessageBox.Show(this, L10n.T("Print.Cancelled"), L10n.T("Toolbar.Print"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else if (result == DialogResult.OK)
                BrandMessageBox.Show(this, L10n.T("Print.Submitted"), L10n.T("Toolbar.Print"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            BrandMessageBox.Show(this, string.Format(L10n.T("Print.Error"), ex.Message), AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ---------------- РЕДАКТОР / ПРЕДПРОСМОТР ----------------

    /// <summary>Загружает макет проекта в редактор и переключает на вкладку «Редактор».</summary>
    private void LoadEditorFromProject()
    {
        if (_project is null)
            return;
        _editor.LoadLayout(_project.Ribbon.GetLayout(),
            _project.Ribbon.LabelTypeName ?? "Custom label", _project.Print);
        _tabs.SelectedTab = _editorTab;
    }

    /// <summary>Редактор изменил макет — синхронизируем с проектом и помечаем как изменённый.</summary>
    private void OnEditorLayoutChanged()
    {
        if (_project is null)
            return;
        _project.Ribbon.Layout = _editor.Layout;
        _project.Ribbon.LabelTypeName = _editor.TypeName;
        _dirty = true;
        RefreshProjectUi();
    }

    /// <summary>Предпросмотр полосы (локальный, без печати).</summary>
    private void ShowPreview()
    {
        if (_project is null || !_editor.HasProject)
            return;
        _editor.CommitPendingEdit();
        var pages = LabelPrintPages.Create(_editor.Layout, _project.Ribbon.Labels, true);
        var plugin = PrinterPlugins.All.FirstOrDefault(x => x.Name == _project.PrinterModel);
        float dpiX = plugin?.ResolutionDpi ?? 300, dpiY = dpiX;
        try
        {
            var printer = GetPrintDocument();
            if (!string.IsNullOrWhiteSpace(_project.Print.PrinterName)) printer.PrinterSettings.PrinterName = _project.Print.PrinterName;
            var resolution = printer.DefaultPageSettings.PrinterResolution;
            if (resolution.X >= 72) dpiX = resolution.X;
            if (resolution.Y >= 72) dpiY = resolution.Y;
        }
        catch (Exception ex) when (ex is InvalidPrinterException or System.ComponentModel.Win32Exception) { }
        using var dialog = new PreviewDialog(pages, _project.Print, dpiX, dpiY);
        dialog.ShowDialog(this);
    }

    /// <summary>Смена единиц отображения (внутри всегда мкм).</summary>
    private void SetUnit(bool inch)
    {
        _unitsMmItem.Checked = !inch;
        _unitsInchItem.Checked = inch;
        _editor.SetUnitInch(inch);
    }

    // ---------------- CSV / WSTX ----------------

    /// <summary>
    /// Импорт CSV: первая строка данных — в ячейки макета (порядок:
    /// строки сверху вниз, ячейки слева направо), остальные строки —
    /// в этикетки ленты (колонка → поле C{N}).
    /// </summary>
    private void ImportCsv()
    {
        if (_project is null)
        {
            BrandMessageBox.Show(this, L10n.T("Status.NoProject"), AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _editor.CommitPendingEdit();
        using var dialog = new CsvImportDialog(_editor.Layout);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.DataRows.Count == 0) return;
        var rows = dialog.DataRows;
        try
        {
            // Prepare and validate the complete batch before mutating the live project.
            var batch = CsvBatch.Prepare(_editor.Layout, rows, dialog.TargetColumns);
            LabelPrintPages.ApplyFields(_editor.Layout, batch.FirstFields);
            _project.Ribbon.Labels = batch.Remaining;
            _editor.Redraw();
            _dirty = true;
            RefreshProjectUi();
            BrandMessageBox.Show(this, string.Format(L10n.T("Csv.ImportedCount"), rows.Count), AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            BrandMessageBox.Show(this, ex.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Открывает файл .wstx как новый проект (макет файла → макет проекта).</summary>
    private void OpenWstx()
    {
        if (!ConfirmDiscard())
            return;

        using var dialog = new BrandedOpenFileDialog
        {
            Title = L10n.T("OpenWstx.Title"),
            Filter = $"{L10n.T("File.FilterWstx")} ({WstxIo.FileExtension[1..]})|*{WstxIo.FileExtension}"
                   + $"|{L10n.T("File.FilterAll")} (*.*)|*.*",
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var (name, layout) = WstxIo.Read(dialog.FileName);
            ClearRecovery();
            var now = DateTime.Now;
            _project = new ProjectFile
            {
                Name = string.IsNullOrEmpty(name)
                    ? Path.GetFileNameWithoutExtension(dialog.FileName)
                    : name,
                Created = now,
                Modified = now,
                Ribbon = new RibbonData
                {
                    LabelTypeName = "Custom label",
                    Layout = layout,
                },
            };
            _projectPath = null;
            _dirty = true;
            LoadEditorFromProject();
            RefreshProjectUi();
        }
        catch (Exception ex)
        {
            BrandMessageBox.Show(this, string.Format(L10n.T("Error.WstxLoad"), ex.Message),
                AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Сохраняет текущий макет в формате .wstx (WAGO, мкм).</summary>
    private void SaveWstx()
    {
        if (_project is null || !_editor.HasProject)
        {
            BrandMessageBox.Show(this, L10n.T("Status.NoProject"), AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _editor.CommitPendingEdit();
        if (BrandMessageBox.Show(this, L10n.T("Wstx.CompatibilityWarning"), AppTitle,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        using var dialog = new BrandedSaveFileDialog
        {
            Title = L10n.T("SaveWstx.Title"),
            Filter = $"{L10n.T("File.FilterWstx")} ({WstxIo.FileExtension[1..]})|*{WstxIo.FileExtension}",
            FileName = _project.Name + WstxIo.FileExtension,
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            WstxIo.Write(_project.Name, _editor.Layout, dialog.FileName);
        }
        catch (Exception ex)
        {
            BrandMessageBox.Show(this, string.Format(L10n.T("Error.WstxSave"), ex.Message),
                AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        RefreshProjectUi();
    }

    // ---------------- ИНТЕРФЕЙС ----------------

    private static string ProjectFilter()
        => $"{L10n.T("File.FilterProject")} ({ProjectFile.FileExtension[1..]})|*{ProjectFile.FileExtension}"
         + $"|{L10n.T("File.FilterAll")} (*.*)|*.*";

    /// <summary>Перерисовывает заголовок, список ленты и статус-бар.</summary>
    private void RefreshProjectUi()
    {
        Text = _project is null ? AppTitle : $"{AppTitle} - {_project.Name}";
        _welcome.Visible = _project is null;
        _toolbar.Visible = _tabs.Visible = _project is not null;
        _saveItem.Enabled = _saveWstxItem.Enabled = _importCsvItem.Enabled = _printItem.Enabled = _project is not null;

        _ribbonList.BeginUpdate();
        _ribbonList.Items.Clear();
        if (_project is not null)
        {
            _ribbonList.Items.Add(new ListViewItem(new[] { "1", L10n.T("Ribbon.CurrentLayout") }));
            foreach (var label in _project.Ribbon.Labels.Where(item => LabelPrintPages.CountImported(_editor.Layout, new[] { item }) > 0))
            {
                var item = new ListViewItem(new[] { (_ribbonList.Items.Count + 1).ToString(), label.Name }) { Tag = label };
                if (_ribbonList.Items.Count % 2 == 1) item.BackColor = Theme.LightBackground;
                _ribbonList.Items.Add(item);
            }
        }
        _ribbonList.EndUpdate();

        _statusLeft.Text = _project is null
            ? L10n.T("Status.NoProject")
            : string.Format(L10n.T("Status.Project"), _project.Name, 1 + LabelPrintPages.CountImported(_editor.Layout, _project.Ribbon.Labels));
        _statusRight.ForeColor = _dirty ? Color.FromArgb(105, 72, 0) : Theme.SecondaryText;
        _statusRight.BackColor = _dirty ? Theme.SoftYellow : Color.White;
        _statusRight.Padding = new Padding(8, 1, 8, 1);
        _statusRight.Text = _project is null
            ? string.Empty
            : (_dirty ? L10n.T("Status.Unsaved") : L10n.T("Status.Saved"));
    }

    private LabelItem? SelectedRecord() => _ribbonList.SelectedItems.Count == 0 ? null : _ribbonList.SelectedItems[0].Tag as LabelItem;
    private void SelectRecord(LabelItem? record)
    {
        RefreshProjectUi();
        var item = _ribbonList.Items.Cast<ListViewItem>().FirstOrDefault(x => ReferenceEquals(x.Tag, record));
        if (item is not null) { item.Selected = true; item.EnsureVisible(); }
    }
    private static string RecordName(Dictionary<string, string> fields) => string.Join(" | ", fields.Values.Take(3));
    private void EditRecord()
    {
        if (_project is null) return;
        var record = SelectedRecord();
        if (record is null) { _tabs.SelectedTab = _editorTab; return; }
        using var dialog = new RibbonRecordDialog(_editor.Layout, record.Fields);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        record.Fields = dialog.Result; record.Name = RecordName(record.Fields);
        _dirty = true; SelectRecord(record);
    }
    private void CopyRecord()
    {
        if (_project is null) return;
        _editor.CommitPendingEdit();
        var selected = SelectedRecord();
        var fields = selected is not null ? new Dictionary<string, string>(selected.Fields) : LabelPrintPages.GetBindings(_editor.Layout)
            .Select((content, index) => (content, index)).Where(x => x.content.Type != CellContentType.Image)
            .ToDictionary(x => "C" + x.index, x => x.content.Type == CellContentType.Barcode ? x.content.BarcodeData : x.content.Text);
        if (fields.Count == 0) return;
        var copy = new LabelItem { Fields = fields, Name = RecordName(fields) };
        var position = selected is null ? 0 : _project.Ribbon.Labels.IndexOf(selected) + 1;
        _project.Ribbon.Labels.Insert(position, copy);
        _dirty = true; SelectRecord(copy);
    }
    private void DeleteRecord()
    {
        var record = SelectedRecord();
        if (_project is null || record is null) return;
        if (BrandMessageBox.Show(this, L10n.T("Ribbon.ConfirmDelete"), AppTitle,
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _project.Ribbon.Labels.Remove(record); _dirty = true; SelectRecord(null);
    }
    private void MoveRecord(int direction)
    {
        if (_project is null || _ribbonList.SelectedIndices.Count == 0) return;
        var record = SelectedRecord();
        var listIndex = _ribbonList.SelectedIndices[0]; var otherIndex = listIndex + direction;
        if (record is null || otherIndex <= 0 || otherIndex >= _ribbonList.Items.Count || _ribbonList.Items[otherIndex].Tag is not LabelItem other) return;
        var from = _project.Ribbon.Labels.IndexOf(record); var to = _project.Ribbon.Labels.IndexOf(other);
        (_project.Ribbon.Labels[from], _project.Ribbon.Labels[to]) = (_project.Ribbon.Labels[to], _project.Ribbon.Labels[from]);
        _dirty = true; SelectRecord(record);
    }

    private void OnLanguageItemClick(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem { Tag: string code })
        {
            L10n.Load(code);
        }
    }

    private void OnLanguageChanged()
    {
        ApplyStrings();
        RefreshProjectUi();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (SystemInformation.HighContrast || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            return;

        const int immersiveDarkMode = 20;
        int enabled = 1;
        DwmSetWindowAttribute(Handle, immersiveDarkMode, ref enabled, sizeof(int));
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            const int captionColor = 35, textColor = 36;
            int black = 0x000000, white = 0xFFFFFF;
            DwmSetWindowAttribute(Handle, captionColor, ref black, sizeof(int));
            DwmSetWindowAttribute(Handle, textColor, ref white, sizeof(int));
        }
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var area = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
        Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        CenterToScreen();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            L10n.Changed -= OnLanguageChanged;
            _recoveryTimer.Dispose();
            _recovery?.Dispose();
            _toolTips.Dispose();
            _rowHeight.Dispose();
            _printDocument?.Dispose();
        }
        base.Dispose(disposing);
    }
}
