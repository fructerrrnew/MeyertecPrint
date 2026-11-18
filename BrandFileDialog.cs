namespace MeyertecPrint;

/// <summary>Выбор файлов в оформлении приложения; используется локальная файловая система и сетевые пути Windows.</summary>
public abstract class BrandFileDialog : IDisposable
{
    private sealed record Entry(string FullPath, bool IsDirectory);
    private sealed record FileFilter(string Title, string Pattern) { public override string ToString() => Title; }
    private static string? _lastDirectory;
    private readonly bool _save;
    public string Title { get; set; } = string.Empty;
    public string Filter { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public bool AddExtension { get; set; }
    protected BrandFileDialog(bool save) => _save = save;
    public void Dispose() { }

    public DialogResult ShowDialog(IWin32Window? owner)
    {
        using var form = new BrandForm { Text = Title, StartPosition = FormStartPosition.CenterParent,
            Size = new Size(830, 580), MinimumSize = new Size(600, 430), BackColor = Color.White,
            Font = Theme.UiFont(), ShowIcon = false, MinimizeBox = false, AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new SizeF(96, 96), KeyPreview = true };
        var header = new Label { Dock = DockStyle.Top, Height = 55, Padding = new Padding(20, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Black, ForeColor = Color.White,
            Font = Theme.UiFont(12, FontStyle.Bold), Text = Title };
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(14, 8, 14, 5), ColumnCount = 3 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 75));
        var drives = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = L10n.T("FileDlg.Drive") };
        var address = new TextBox { Dock = DockStyle.Fill, AccessibleName = L10n.T("FileDlg.Address") };
        var up = new Button { Text = L10n.T("FileDlg.Up"), Dock = DockStyle.Fill }; Theme.StyleButton(up);
        top.Controls.Add(drives, 0, 0); top.Controls.Add(address, 1, 0); top.Controls.Add(up, 2, 0);
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false,
            MultiSelect = false, BorderStyle = BorderStyle.FixedSingle, AccessibleName = L10n.T("FileDlg.Files") };
        list.Columns.Add(L10n.T("FileDlg.Name"), 420); list.Columns.Add(L10n.T("FileDlg.Modified"), 170);
        var status = new Label { Dock = DockStyle.Bottom, Height = 27, ForeColor = Color.Firebrick, Padding = new Padding(15, 0, 0, 0), AutoEllipsis = true };
        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 88, ColumnCount = 2, RowCount = 2, Padding = new Padding(14, 0, 14, 4) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var name = new TextBox { Dock = DockStyle.Fill, Text = Path.GetFileName(FileName), AccessibleName = L10n.T("FileDlg.Name") };
        var filters = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = L10n.T("FileDlg.Type") };
        bottom.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Text = L10n.T("FileDlg.Name") }, 0, 0);
        bottom.Controls.Add(name, 1, 0);
        bottom.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Text = L10n.T("FileDlg.Type") }, 0, 1);
        bottom.Controls.Add(filters, 1, 1);
        var parts = Filter.Split('|');
        for (var i = 0; i + 1 < parts.Length; i += 2) filters.Items.Add(new FileFilter(parts[i], parts[i + 1]));
        if (filters.Items.Count == 0) filters.Items.Add(new FileFilter(L10n.T("File.FilterAll"), "*.*"));
        filters.SelectedIndex = 0;
        var accept = new Button { Text = L10n.T(_save ? "FileDlg.Save" : "FileDlg.Open") };
        var cancel = new Button { Text = L10n.T("Dialog.Cancel"), DialogResult = DialogResult.Cancel };
        var footer = Theme.DialogFooter(accept, cancel);
        form.Controls.Add(list); form.Controls.Add(status); form.Controls.Add(bottom); form.Controls.Add(footer); form.Controls.Add(top); form.Controls.Add(header);
        form.AcceptButton = accept; form.CancelButton = cancel;
        var current = _lastDirectory;
        if (string.IsNullOrEmpty(current) || !Directory.Exists(current)) current = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(current) || !Directory.Exists(current)) current = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(current) || !Directory.Exists(current)) current = Directory.GetCurrentDirectory();
        try { drives.Items.AddRange(Directory.GetLogicalDrives().Cast<object>().ToArray()); }
        catch (IOException) { }

        bool Matches(string file, string pattern)
        {
            foreach (var part in pattern.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (part is "*" or "*.*") return true;
                if (part.StartsWith("*.") && file.EndsWith(part[1..], StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
        void Navigate(string folder)
        {
            try
            {
                var resolved = Path.GetFullPath(folder);
                var directories = Directory.GetDirectories(resolved).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToArray();
                var pattern = ((FileFilter)filters.SelectedItem!).Pattern;
                var files = Directory.GetFiles(resolved).Where(p => Matches(p, pattern)).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToArray();
                current = resolved; address.Text = current; status.Text = string.Empty;
                list.BeginUpdate(); list.Items.Clear();
                foreach (var path in directories)
                {
                    var item = new ListViewItem("▸  " + Path.GetFileName(path)) { Tag = new Entry(path, true) };
                    item.SubItems.Add(L10n.T("FileDlg.Directory")); list.Items.Add(item);
                }
                foreach (var path in files)
                {
                    var item = new ListViewItem(Path.GetFileName(path)) { Tag = new Entry(path, false) };
                    try { item.SubItems.Add(File.GetLastWriteTime(path).ToString("g")); } catch (IOException) { item.SubItems.Add(""); }
                    list.Items.Add(item);
                }
                list.EndUpdate();
                var root = Path.GetPathRoot(current);
                if (root is not null && drives.Items.Contains(root)) drives.SelectedItem = root;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
            { status.Text = string.Format(L10n.T("FileDlg.Invalid"), ex.Message); }
        }
        void Accept()
        {
            try
            {
                var file = name.Text.Trim();
                if (file.Length == 0) { status.Text = L10n.T("FileDlg.NotFound"); return; }
                var path = Path.GetFullPath(Path.Combine(current!, file));
                if (Directory.Exists(path)) { Navigate(path); name.Clear(); return; }
                if (_save && AddExtension && !Path.HasExtension(path))
                {
                    var firstPattern = ((FileFilter)filters.SelectedItem!).Pattern.Split(';')[0];
                    if (firstPattern.StartsWith("*.") && firstPattern != "*.*") path += firstPattern[1..];
                }
                if (!_save && !File.Exists(path)) { status.Text = L10n.T("FileDlg.NotFound"); return; }
                if (_save && !Directory.Exists(Path.GetDirectoryName(path))) { status.Text = L10n.T("FileDlg.NotFound"); return; }
                if (_save && File.Exists(path) && BrandMessageBox.Show(form, L10n.T("FileDlg.Overwrite"), Title,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                FileName = path; _lastDirectory = Path.GetDirectoryName(path); form.DialogResult = DialogResult.OK; form.Close();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
            { status.Text = string.Format(L10n.T("FileDlg.Invalid"), ex.Message); }
        }
        accept.Click += (_, _) => Accept();
        up.Click += (_, _) => { var parent = Directory.GetParent(current!); if (parent is not null) Navigate(parent.FullName); };
        address.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Navigate(address.Text); e.Handled = e.SuppressKeyPress = true; } };
        drives.SelectionChangeCommitted += (_, _) => { if (drives.SelectedItem is string drive) Navigate(drive); };
        filters.SelectionChangeCommitted += (_, _) => Navigate(current!);
        list.SelectedIndexChanged += (_, _) => { if (list.SelectedItems.Count > 0 && list.SelectedItems[0].Tag is Entry { IsDirectory: false } entry) name.Text = Path.GetFileName(entry.FullPath); };
        list.DoubleClick += (_, _) => { if (list.SelectedItems.Count == 0 || list.SelectedItems[0].Tag is not Entry entry) return; if (entry.IsDirectory) { Navigate(entry.FullPath); name.Clear(); } else Accept(); };
        form.KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.L) { address.Focus(); address.SelectAll(); e.Handled = e.SuppressKeyPress = true; } };
        form.Shown += (_, _) => Navigate(current!);
        return owner is null ? form.ShowDialog() : form.ShowDialog(owner);
    }
}

public sealed class BrandedOpenFileDialog : BrandFileDialog { public BrandedOpenFileDialog() : base(false) { } }
public sealed class BrandedSaveFileDialog : BrandFileDialog { public BrandedSaveFileDialog() : base(true) { } }
