using System.Runtime.InteropServices;
using System.Drawing.Printing;

namespace MeyertecPrint;

/// <summary>
/// Главное окно MEYERTEC PRINT и команды текущего проекта.
/// </summary>
public partial class MainForm : BrandForm
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
    private ToolStripMenuItem _saveAsItem = null!;
    private ToolStripMenuItem _importCsvItem = null!;
    private ToolStripMenuItem _numberingItem = null!;
    private ToolStripMenuItem _openWstxItem = null!;
    private ToolStripMenuItem _saveWstxItem = null!;
    private ToolStripMenuItem _pageSettingsItem = null!;
    private ToolStripMenuItem _printSettingsItem = null!;
    private ToolStripMenuItem _previewItem = null!;
    private ToolStripMenuItem _printItem = null!;
    private ToolStripMenuItem _viewMenu = null!;
    private ToolStripMenuItem _unitsMmItem = null!;
    private ToolStripMenuItem _unitsInchItem = null!;

    private ToolStrip _toolbar = null!;
    private WelcomeView _welcome = null!;
    private readonly ImageList _rowHeight = new() { ImageSize = new Size(1, 32) };
    private ToolStripButton _tbNew = null!, _tbOpen = null!, _tbSave = null!, _tbImport = null!;
    private ToolStripButton _tbPreview = null!, _tbPrint = null!;
    private ToolStripDropDownButton _tbSettings = null!;

    private TabControl _tabs = null!;
    private TabPage _ribbonTab = null!;
    private TabPage _editorTab = null!;
    private LabelEditorControl _editor = null!;

    private ListView _ribbonList = null!;
    private LabelSequenceView _labelSequence = null!;
    private Label _ribbonHint = null!;
    private ColumnHeader _numberColumn = null!;
    private ColumnHeader _nameColumn = null!;
    private Button _recordAdd = null!, _recordEdit = null!, _recordCopy = null!,
        _recordDelete = null!, _recordUp = null!, _recordDown = null!;
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
        SuspendLayout();
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
        BuildMenu();
        ApplyStrings();
        RefreshProjectUi();

        L10n.Changed += OnLanguageChanged;
        Shown += async (_, _) => await CheckTemplateUpdates();
        try
        {
            _recovery = new RecoveryStore();
            _recoveryTimer.Tick += (_, _) => SaveRecovery();
            _recoveryTimer.Start();
            Shown += (_, _) => OfferRecovery();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { AppLog.Write(ex, "recovery initialization"); }
        ResumeLayout();
    }

    private void BuildMenu()
    {
        var highContrast = SystemInformation.HighContrast;
        _menu = new MenuStrip
        {
            Dock = DockStyle.Top,
            BackColor = highContrast ? SystemColors.Menu : Theme.ProjectBar,
            ForeColor = highContrast ? SystemColors.MenuText : Color.White,
            Font = Font,
            Renderer = highContrast ? new ToolStripSystemRenderer() : new BrandMenuRenderer(),
            Margin = Padding.Empty, Padding = new Padding(12, 4, 12, 4)
        };

        _fileMenu = new ToolStripMenuItem();
        _newItem = new ToolStripMenuItem { ShortcutKeys = Keys.Control | Keys.N };
        _newItem.Click += (_, _) => NewProject();
        _openItem = new ToolStripMenuItem { ShortcutKeys = Keys.Control | Keys.O };
        _openItem.Click += (_, _) => OpenProject();
        _saveItem = new ToolStripMenuItem { ShortcutKeys = Keys.Control | Keys.S };
        _saveItem.Click += (_, _) => SaveProject();
        _saveAsItem = new ToolStripMenuItem { ShortcutKeys = Keys.Control | Keys.Shift | Keys.S };
        _saveAsItem.Click += (_, _) => SaveProject(saveAs: true);
        _importCsvItem = new ToolStripMenuItem();
        _importCsvItem.Click += (_, _) => ImportCsv();
        _numberingItem = new ToolStripMenuItem();
        _numberingItem.Click += (_, _) => NumberLabels();
        _openWstxItem = new ToolStripMenuItem();
        _openWstxItem.Click += (_, _) => OpenWstx();
        _saveWstxItem = new ToolStripMenuItem();
        _saveWstxItem.Click += (_, _) => SaveWstx();
        _pageSettingsItem = new ToolStripMenuItem();
        _pageSettingsItem.Click += (_, _) => PageSettings();
        _printSettingsItem = new ToolStripMenuItem();
        _printSettingsItem.Click += (_, _) => PrintSettings();
        _previewItem = new ToolStripMenuItem { ShortcutKeys = Keys.Control | Keys.Shift | Keys.P };
        _previewItem.Click += (_, _) => ShowPreview();
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

        _fileMenu.DropDownItems.AddRange([
            _newItem, _openItem, _saveItem, _saveAsItem, new ToolStripSeparator(),
            _importCsvItem, _numberingItem, _openWstxItem, _saveWstxItem, new ToolStripSeparator(),
            _pageSettingsItem, _printSettingsItem, _previewItem, _printItem, new ToolStripSeparator(), _exitItem
        ]);

        _languageMenu = new ToolStripMenuItem();
        foreach (var code in L10n.Available())
        {
            var item = new ToolStripMenuItem { Tag = code };
            item.Click += OnLanguageItemClick;
            _languageMenu.DropDownItems.Add(item);
            _languageItems[code] = item;
        }

        _menu.Items.Add(_fileMenu);
        _menu.Items.Add(BuildTemplateMenu());
        _menu.Items.Add(_viewMenu);
        _menu.Items.Add(_languageMenu);

        MainMenuStrip = _menu;
        BuildToolbar();
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
        _tbNew = MakeToolButton("01_new_file", NewProject, caption: false);
        _tbOpen = MakeToolButton("02_open", OpenProject, caption: false);
        _tbSave = MakeToolButton("03_save", () => SaveProject());
        _tbImport = MakeToolButton(null, ImportCsv);
        _tbPreview = MakeToolButton("07_preview", ShowPreview);
        _tbPrint = MakeToolButton("08_print", PrintProject);
        _tbSettings = new ToolStripDropDownButton
        {
            Image = Theme.Icon("06_print_setup", LogicalToDeviceUnits(18)),
            ForeColor = Theme.Text, Padding = new Padding(7, 4, 7, 4)
        };
        _tbSettings.DropDownItems.Add(new ToolStripMenuItem
        {
            Image = Theme.Icon("05_page_setup", LogicalToDeviceUnits(18))
        });
        _tbSettings.DropDownItems.Add(new ToolStripMenuItem());
        _tbSettings.DropDownItems[0].Click += (_, _) => PageSettings();
        _tbSettings.DropDownItems[1].Click += (_, _) => PrintSettings();
        _toolbar = new ToolStrip
        {
            Dock = DockStyle.Fill, GripStyle = ToolStripGripStyle.Hidden, CanOverflow = true,
            LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow,
            TabStop = true, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Font,
            ImageScalingSize = new Size(18, 18), Padding = new Padding(12, 4, 12, 5),
            Renderer = SystemInformation.HighContrast ? new ToolStripSystemRenderer() : new BrandCommandRenderer(_tbPreview)
        };
        _tbPrint.Alignment = _tbPreview.Alignment = ToolStripItemAlignment.Right;
        _toolbar.Items.AddRange([
            _tbNew, _tbOpen, _tbSave, new ToolStripSeparator(), _tbImport,
            _tbSettings, _tbPrint, _tbPreview
        ]);
    }

    private ToolStripButton MakeToolButton(string? icon, Action action, bool caption = true)
    {
        var button = new ToolStripButton
        {
            Image = icon is null ? null : Theme.Icon(icon, LogicalToDeviceUnits(18)),
            DisplayStyle = caption ? ToolStripItemDisplayStyle.ImageAndText : ToolStripItemDisplayStyle.Image,
            ForeColor = Theme.Text, Padding = new Padding(9, 4, 9, 4), Margin = new Padding(0, 0, 4, 0),
            AutoToolTip = false
        };
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
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            ShowItemToolTips = true,
            Margin = Padding.Empty
        };
        _numberColumn = new ColumnHeader();
        _nameColumn = new ColumnHeader();
        _ribbonList.Columns.Add(_numberColumn);
        _ribbonList.Columns.Add(_nameColumn);
        _ribbonList.Resize += (_, _) => FitRibbonColumns();

        _editor = new LabelEditorControl { Dock = DockStyle.Fill };
        _editor.LayoutChanged += OnEditorLayoutChanged;

        _tabs = new TabControl
        {
            Dock = DockStyle.Fill, ItemSize = new Size(0, 34), Padding = new Point(17, 5),
            DrawMode = SystemInformation.HighContrast ? TabDrawMode.Normal : TabDrawMode.OwnerDrawFixed
        };
        _tabs.DrawItem += (_, e) =>
        {
            var selected = e.Index == _tabs.SelectedIndex;
            var tab = _tabs.TabPages[e.Index];
            var bounds = e.Bounds;
            using var fill = new SolidBrush(selected ? Color.White : Theme.LightBackground);
            e.Graphics.FillRectangle(fill, bounds);
            TextRenderer.DrawText(e.Graphics, tab.Text, _tabs.Font, bounds, selected ? Theme.Dark : Theme.SecondaryText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (selected)
            {
                using var accent = new SolidBrush(Theme.Yellow);
                var line = LogicalToDeviceUnits(3);
                e.Graphics.FillRectangle(accent, bounds.Left, bounds.Bottom - line, bounds.Width, line);
                if (_tabs.Focused) e.DrawFocusRectangle();
            }
        };
        _ribbonTab = new TabPage { BackColor = Theme.Surface, Padding = new Padding(16, 12, 16, 8) };
        _ribbonList.DoubleClick += (_, _) => EditRecord();
        _ribbonList.KeyDown += OnRibbonKeyDown;
        _ribbonList.SelectedIndexChanged += (_, _) => UpdateRecordActions();
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 10, 0, 0), WrapContents = true
        };
        Button Action(Action run)
        {
            var button = new Button
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 34),
                Padding = new Padding(7, 0, 7, 0), Margin = new Padding(0, 0, 8, 4)
            };
            Theme.StyleButton(button);
            button.Click += (_, _) => run();
            actions.Controls.Add(button);
            return button;
        }
        _recordEdit = Action(EditRecord);
        _recordAdd = Action(AddRecord);
        actions.Controls.SetChildIndex(_recordAdd, 0);
        _recordCopy = Action(CopyRecord);
        _recordUp = Action(() => MoveRecord(-1));
        _recordDown = Action(() => MoveRecord(1));
        _recordDelete = Action(DeleteRecord);
        _ribbonHint = new Label
        {
            AutoSize = true, Dock = DockStyle.Fill, UseMnemonic = false, Padding = new Padding(12, 9, 12, 9),
            ForeColor = Theme.SecondaryText, BackColor = SystemInformation.HighContrast ? Theme.Surface : Theme.LightBackground,
            Margin = new Padding(0, 0, 0, 10)
        };
        var batch = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        batch.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        batch.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        batch.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        batch.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        batch.Controls.Add(_ribbonHint, 0, 0);
        _labelSequence = new LabelSequenceView { Dock = DockStyle.Fill };
        _labelSequence.PageSelected += index =>
        {
            if (index >= 0 && index < _ribbonList.Items.Count)
                SelectRecord(_ribbonList.Items[index].Tag as LabelItem);
        };
        var strip = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(800, 400),
            SplitterDistance = 215, Panel1MinSize = 52, Panel2MinSize = 65
        };
        strip.Panel1.Controls.Add(_labelSequence);
        strip.Panel2.Controls.Add(_ribbonList);
        batch.Controls.Add(strip, 0, 1);
        batch.Controls.Add(actions, 0, 2);
        _ribbonTab.Controls.Add(batch);
        _editorTab = new TabPage { BackColor = Theme.LightBackground };
        _editorTab.Controls.Add(_editor);
        _tabs.TabPages.Add(_editorTab);
        _tabs.TabPages.Add(_ribbonTab);
        _tabs.SelectedIndexChanged += (_, _) => RefreshSequence();
    }

    private void BuildStatusBar()
    {
        _statusStrip = new StatusStrip
        {
            BackColor = Theme.Surface,
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
        _saveAsItem.Text = L10n.T("Toolbar.SaveAs") + "…";
        _importCsvItem.Text = L10n.T("File.ImportCsv");
        _numberingItem.Text = L10n.T("Numbering.Menu");
        ApplyTemplateStrings();
        _openWstxItem.Text = L10n.T("File.OpenWstx");
        _saveWstxItem.Text = L10n.T("File.SaveAsWstx");
        _pageSettingsItem.Text = L10n.T("Toolbar.PageSetup") + "…";
        _printSettingsItem.Text = L10n.T("File.PrintSettings") + "…";
        _previewItem.Text = L10n.T("Toolbar.Preview") + "…";
        _printItem.Text = L10n.T("Toolbar.Print") + "…";
        _unitsMmItem.Text = L10n.T("View.UnitsMm");
        _unitsInchItem.Text = L10n.T("View.UnitsInch");
        _ribbonTab.Text = L10n.T("Tab.Ribbon");
        _editorTab.Text = L10n.T("Tab.Editor");
        SetToolLabel(_tbNew, "File.NewProject", "Ctrl+N");
        SetToolLabel(_tbOpen, "File.OpenProject", "Ctrl+O");
        SetToolLabel(_tbSave, "Toolbar.Save", "Ctrl+S");
        SetToolLabel(_tbImport, "File.ImportCsv");
        SetToolLabel(_tbSettings, "Toolbar.Settings");
        SetToolLabel(_tbPreview, "Toolbar.Preview", "Ctrl+Shift+P");
        SetToolLabel(_tbPrint, "Toolbar.Print", "Ctrl+P");
        _tbSettings.DropDownItems[0].Text = _pageSettingsItem.Text;
        _tbSettings.DropDownItems[1].Text = _printSettingsItem.Text;
        _welcome.ApplyStrings();
        _editor.ApplyStrings();
        _exitItem.Text = L10n.T("File.Exit");
        _numberColumn.Text = L10n.T("Ribbon.Number");
        _nameColumn.Text = L10n.T("Ribbon.Name");
        _ribbonList.AccessibleName = _ribbonTab.Text;
        _labelSequence.AccessibleName = _ribbonTab.Text;
        _recordAdd.Text = L10n.T("Ribbon.AddRecord");
        _recordCopy.Text = L10n.T("Ribbon.CopyRecord");
        _recordDelete.Text = L10n.T("Ribbon.DeleteRecord");
        _recordUp.Text = L10n.T("Ribbon.Up");
        _recordDown.Text = L10n.T("Ribbon.Down");

        foreach (var (code, item) in _languageItems)
        {
            item.Text = L10n.T("Lang." + code);
            item.Checked = string.Equals(code, L10n.Current, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void SetToolLabel(ToolStripItem item, string key, string? shortcut = null)
    {
        item.Text = item.AccessibleName = L10n.T(key);
        item.ToolTipText = shortcut is null ? item.Text : $"{item.Text} ({shortcut})";
    }

    // ---------------- ЖИЗНЕННЫЙ ЦИКЛ ПРОЕКТА ----------------

    private void NewProject()
    {
        if (!ConfirmDiscard())
            return;

        using var first = new NewProjectDialog();
        while (first.ShowDialog(this) == DialogResult.OK)
        {
            var cleatMode = first.Mode == NewProjectMode.Cleat;
            using var second = new TypeSelectionDialog(cleatMode);
            ProjectFile? project = null;
            while (second.ShowDialog(this) == DialogResult.OK)
            {
                if (!cleatMode)
                {
                    project = CreateLabelProject(first.ProjectName, second.SelectedLabelType);
                    break;
                }
                if (second.SelectedCleat is not { } cleat) return;
                using var dimensions = new MarkerSizeDialog(cleat.DisplayName);
                var result = dimensions.ShowDialog(this);
                if (result == DialogResult.Retry) continue;
                if (result == DialogResult.OK) project = CreateCleatProject(first.ProjectName, cleat, dimensions);
                break;
            }
            if (second.DialogResult == DialogResult.Retry) continue;
            if (project is null) return;

            ClearRecovery();
            _project = project;
            _projectPath = null;
            _dirty = true;
            LoadEditorFromProject();
            RefreshProjectUi();
            return;
        }
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

    private static ProjectFile CreateCleatProject(string name, CleatType cleat, MarkerSizeDialog dimensions)
    {
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

    internal static void SetLabelPaper(PrintDocument document, LabelLayout layout, PageSettings? page = null)
    {
        page ??= document.DefaultPageSettings;
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
            using var progress = new PrintProgressDialog(pages, dialog.RangeFrom, dialog.RangeTo, _project.Print, plugin,
                startOnNotchedSection: dialog.StartOnNotchedSection);
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

    /// <summary>Загружает макет и показывает ленту с чередующимися секциями.</summary>
    private void LoadEditorFromProject()
    {
        if (_project is null)
            return;
        _editor.LoadLayout(_project.Ribbon.GetLayout(),
            _project.Ribbon.LabelTypeName ?? "Custom label", _project.Print);
        _tabs.SelectedTab = _editor.Layout.AlternateNotch is not null && _project.Ribbon.Labels.Count > 0
            ? _ribbonTab : _editorTab;
        if (_tabs.Visible && _tabs.SelectedTab == _editorTab) _editor.FocusCanvas();
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
            if (_project.Print.PrintBackend == "driver")
            {
                var printer = GetPrintDocument();
                if (!string.IsNullOrWhiteSpace(_project.Print.PrinterName)) printer.PrinterSettings.PrinterName = _project.Print.PrinterName;
                var resolution = printer.DefaultPageSettings.PrinterResolution;
                if (resolution.X >= 72) dpiX = resolution.X;
                if (resolution.Y >= 72) dpiY = resolution.Y;
            }
        }
        catch (Exception ex) when (ex is InvalidPrinterException or System.ComponentModel.Win32Exception) { }
        using var dialog = new PreviewDialog(pages, _project.Print, dpiX, dpiY);
        if (dialog.ShowDialog(this) == DialogResult.Yes) PrintProject();
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
            ApplyBatch(CsvBatch.Prepare(_editor.Layout, rows, dialog.TargetColumns));
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            BrandMessageBox.Show(this, ex.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void NumberLabels()
    {
        if (_project is null) return;
        _editor.CommitPendingEdit();
        using var dialog = new NumberingDialog(_editor.Layout);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Prepared is { } batch)
            ApplyBatch(batch);
    }

    private void ApplyBatch(CsvBatch.Prepared batch)
    {
        if (_project is null) return;
        if (_project.Ribbon.Labels.Count > 0 &&
            BrandMessageBox.Show(this, L10n.T("Ribbon.ReplaceBatch"), AppTitle,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        LabelPrintPages.ApplyFields(_editor.Layout, batch.FirstFields);
        _project.Ribbon.Labels = batch.Remaining;
        _editor.Redraw();
        _dirty = true;
        _tabs.SelectedTab = _ribbonTab;
        RefreshProjectUi();
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
        if (_editor.Layout.AlternateNotch is not null)
        {
            BrandMessageBox.Show(this, L10n.T("Wstx.NotchedStock"), AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
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
        var hasProject = _project is not null;
        var enteringWorkspace = hasProject && !_tabs.Visible;
        Text = _project is null ? AppTitle : $"{_project.Name}{(_dirty ? " *" : "")} — {AppTitle}";
        _welcome.Visible = !hasProject;
        _toolbar.Visible = _tabs.Visible = _statusStrip.Visible = hasProject;
        _saveItem.Enabled = _saveAsItem.Enabled = _saveWstxItem.Enabled = _importCsvItem.Enabled =
            _numberingItem.Enabled = _saveTemplateItem.Enabled = _pageSettingsItem.Enabled =
            _printSettingsItem.Enabled = _previewItem.Enabled = _printItem.Enabled = hasProject;

        var previousRecord = SelectedRecord();
        _ribbonList.BeginUpdate();
        _ribbonList.Items.Clear();
        if (_project is not null)
        {
            _ribbonList.Items.Add(new ListViewItem(new[] { "1", L10n.T("Ribbon.CurrentLayout") }));
            foreach (var label in LabelPrintPages.PrintableRecords(_editor.Layout, _project.Ribbon.Labels))
            {
                var name = string.IsNullOrWhiteSpace(label.Name) ? L10n.T("Ribbon.LayoutCopy") : label.Name;
                var item = new ListViewItem(new[] { (_ribbonList.Items.Count + 1).ToString(), name })
                {
                    Tag = label, ToolTipText = name
                };
                if (!SystemInformation.HighContrast && _ribbonList.Items.Count % 2 == 1)
                    item.BackColor = Theme.LightBackground;
                _ribbonList.Items.Add(item);
            }
            SelectRecord(previousRecord);
        }
        _ribbonList.EndUpdate();
        FitRibbonColumns();
        UpdateRecordActions();
        RefreshSequence();
        _ribbonHint.Visible = hasProject;
        if (hasProject)
        {
            var gapUm = Units.MmToUm((double)Math.Clamp(_project!.Print.MediaGapMm, 0, 762));
            var length = LabelSequence.CalculateFeedLengthUm(_editor.Layout, _ribbonList.Items.Count, gapUm);
            _ribbonHint.Text = string.Format(L10n.T("Ribbon.TotalLength"), Units.UmToMm(length), L10n.T("Unit.Mm"));
            if (_editor.Layout.AlternateNotch is not null)
                _ribbonHint.Text += "  ·  " + L10n.T("Ribbon.SkipNotchedSections");
        }

        _statusLeft.Text = string.Format(L10n.T("Status.Records"), _ribbonList.Items.Count);
        var flagUnsaved = _dirty && !SystemInformation.HighContrast;
        _statusRight.ForeColor = flagUnsaved ? Color.FromArgb(105, 72, 0) : Theme.SecondaryText;
        _statusRight.BackColor = flagUnsaved ? Theme.SoftYellow : Theme.Surface;
        _statusRight.Padding = new Padding(8, 1, 8, 1);
        _statusRight.Text = L10n.T(_dirty ? "Status.Unsaved" : "Status.Saved");
        if (enteringWorkspace) _editor.FocusCanvas();
    }

    private void FitRibbonColumns()
    {
        if (_nameColumn is null) return;
        _numberColumn.Width = LogicalToDeviceUnits(64);
        _nameColumn.Width = Math.Max(LogicalToDeviceUnits(100),
            _ribbonList.ClientSize.Width - _numberColumn.Width - SystemInformation.VerticalScrollBarWidth);
    }

    private void RefreshSequence()
    {
        if (_tabs.SelectedTab != _ribbonTab || _project is null) return;
        _labelSequence.SetPages(LabelPrintPages.Create(_editor.Layout, _project.Ribbon.Labels, true), _project.Print);
        _labelSequence.SelectedIndex = _ribbonList.SelectedIndices.Count == 0 ? 0 : _ribbonList.SelectedIndices[0];
    }

    private void UpdateRecordActions()
    {
        var index = _ribbonList.SelectedIndices.Count == 0 ? -1 : _ribbonList.SelectedIndices[0];
        if (index >= 0) _labelSequence.SelectedIndex = index;
        _recordAdd.Enabled = _project is not null;
        _recordEdit.Enabled = index == 0 || index > 0 && SelectedRecord()?.UseLayout == false;
        _recordEdit.Text = L10n.T(index == 0 ? "Ribbon.EditLayout" : "Ribbon.EditRecord");
        _recordCopy.Enabled = index >= 0;
        _recordDelete.Enabled = index > 0;
        _recordUp.Enabled = index > 1;
        _recordDown.Enabled = index > 0 && index < _ribbonList.Items.Count - 1;
    }

    private void OnRibbonKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && _recordEdit.Enabled) EditRecord();
        else if (e.KeyCode == Keys.Delete && _recordDelete.Enabled) DeleteRecord();
        else return;
        e.SuppressKeyPress = true;
    }

    private LabelItem? SelectedRecord() => _ribbonList.SelectedItems.Count == 0 ? null : _ribbonList.SelectedItems[0].Tag as LabelItem;
    private void SelectRecord(LabelItem? record)
    {
        if (_ribbonList.Items.Count == 0) return;
        var item = _ribbonList.Items.Cast<ListViewItem>().FirstOrDefault(x => ReferenceEquals(x.Tag, record)) ?? _ribbonList.Items[0];
        if (_ribbonList.SelectedItems.Count == 1 && _ribbonList.SelectedItems[0] != item)
            _ribbonList.SelectedItems[0].Selected = false;
        item.Selected = item.Focused = true;
        if (_tabs.SelectedTab == _ribbonTab) item.EnsureVisible();
    }
    private static string RecordName(Dictionary<string, string> fields) => string.Join(" | ", fields.Values.Take(3));
    private void EditRecord()
    {
        if (_project is null || _ribbonList.SelectedIndices.Count == 0) return;
        var record = SelectedRecord();
        if (record?.UseLayout == true) return;
        if (record is null)
        {
            _tabs.SelectedTab = _editorTab;
            _editor.FocusCanvas();
            return;
        }
        using var dialog = new RibbonRecordDialog(_editor.Layout, record.Fields);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        record.Fields = dialog.Result; record.Name = RecordName(record.Fields);
        _dirty = true;
        RefreshProjectUi();
    }
    private void CopyRecord()
        => InsertRecord(edit: false);

    private void AddRecord()
        => InsertRecord(edit: true);

    private void InsertRecord(bool edit)
    {
        if (_project is null) return;
        _editor.CommitPendingEdit();
        var selected = SelectedRecord();
        var fields = selected is null || edit ? LabelPrintPages.CaptureFields(_editor.Layout) : new(selected.Fields);
        if (edit && fields.Count > 0)
        {
            using var dialog = new RibbonRecordDialog(_editor.Layout, fields);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            fields = dialog.Result;
        }
        var copy = new LabelItem { Fields = fields, Name = RecordName(fields), UseLayout = fields.Count == 0 };
        var position = selected is null ? 0 : _project.Ribbon.Labels.IndexOf(selected) + 1;
        _project.Ribbon.Labels.Insert(position, copy);
        _dirty = true;
        RefreshProjectUi();
        SelectRecord(copy);
    }
    private void DeleteRecord()
    {
        var record = SelectedRecord();
        if (_project is null || record is null) return;
        if (BrandMessageBox.Show(this, L10n.T("Ribbon.ConfirmDelete"), AppTitle,
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        var index = _ribbonList.SelectedIndices[0];
        _project.Ribbon.Labels.Remove(record);
        _dirty = true;
        RefreshProjectUi();
        SelectRecord(_ribbonList.Items[Math.Min(index, _ribbonList.Items.Count - 1)].Tag as LabelItem);
    }
    private void MoveRecord(int direction)
    {
        if (_project is null || _ribbonList.SelectedIndices.Count == 0) return;
        var record = SelectedRecord();
        var listIndex = _ribbonList.SelectedIndices[0]; var otherIndex = listIndex + direction;
        if (record is null || otherIndex <= 0 || otherIndex >= _ribbonList.Items.Count || _ribbonList.Items[otherIndex].Tag is not LabelItem other) return;
        var from = _project.Ribbon.Labels.IndexOf(record); var to = _project.Ribbon.Labels.IndexOf(other);
        (_project.Ribbon.Labels[from], _project.Ribbon.Labels[to]) = (_project.Ribbon.Labels[to], _project.Ribbon.Labels[from]);
        _dirty = true;
        RefreshProjectUi();
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
            int ink = ColorTranslator.ToWin32(Theme.ProjectBar), white = ColorTranslator.ToWin32(Color.White);
            DwmSetWindowAttribute(Handle, captionColor, ref ink, sizeof(int));
            DwmSetWindowAttribute(Handle, textColor, ref white, sizeof(int));
        }
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _rowHeight.ImageSize = new Size(1, LogicalToDeviceUnits(32));
        var area = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
        Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        CenterToScreen();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            L10n.Changed -= OnLanguageChanged;
            _templateCheck.Cancel();
            _templateCheck.Dispose();
            _recoveryTimer.Dispose();
            _recovery?.Dispose();
            _rowHeight.Dispose();
            _printDocument?.Dispose();
        }
        base.Dispose(disposing);
    }
}
