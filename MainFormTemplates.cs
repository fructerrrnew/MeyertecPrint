using System.Drawing.Imaging;

namespace MeyertecPrint;

public partial class MainForm
{
    private readonly TemplateLibrary _templateLibrary = new();
    private readonly CancellationTokenSource _templateCheck = new();
    private IReadOnlyList<TemplateFeedUpdate> _pendingTemplateUpdates = [];
    private int _templateCheckGeneration;
    private ToolStripMenuItem _templatesMenu = null!, _openTemplatesItem = null!, _saveTemplateItem = null!, _updateTemplatesItem = null!;

    private ToolStripMenuItem BuildTemplateMenu()
    {
        _openTemplatesItem = new ToolStripMenuItem();
        _openTemplatesItem.Click += (_, _) => OpenTemplates();
        _saveTemplateItem = new ToolStripMenuItem();
        _saveTemplateItem.Click += (_, _) => SaveTemplate();
        _updateTemplatesItem = new ToolStripMenuItem();
        _updateTemplatesItem.Click += (_, _) =>
        {
            using var updates = new TemplateUpdateDialog(_templateLibrary);
            updates.ShowDialog(this);
            if (updates.CheckedUpdates is { } pending)
            {
                _templateCheckGeneration++;
                _pendingTemplateUpdates = pending;
                ApplyTemplateStrings();
            }
        };
        _templatesMenu = new ToolStripMenuItem();
        _templatesMenu.DropDownItems.AddRange([_openTemplatesItem, _saveTemplateItem, _updateTemplatesItem]);
        return _templatesMenu;
    }

    private void ApplyTemplateStrings()
    {
        var menuName = L10n.T("Menu.Templates");
        _templatesMenu.Text = _pendingTemplateUpdates.Count > 0 ? $"{menuName} ({_pendingTemplateUpdates.Count})" : menuName;
        _openTemplatesItem.Text = L10n.T("Template.Library") + "…";
        _saveTemplateItem.Text = L10n.T("Template.Save") + "…";
        _updateTemplatesItem.Text = L10n.T("Template.Updates") + "…";
    }

    private async Task CheckTemplateUpdates()
    {
        var settings = TemplateFeedSettings.Load();
        if (string.IsNullOrWhiteSpace(settings.ManifestUrl)) return;
        var generation = ++_templateCheckGeneration;
        try
        {
            var updates = await TemplateFeed.CheckAsync(settings.GetManifestUri(), _templateLibrary,
                settings.UseTls, cancellationToken: _templateCheck.Token);
            if (IsDisposed || _templateCheck.IsCancellationRequested || generation != _templateCheckGeneration) return;
            _pendingTemplateUpdates = updates;
            RefreshTemplateBadge();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException
            or System.Net.WebException or OperationCanceledException or System.Net.Sockets.SocketException
            or System.Security.Authentication.AuthenticationException)
        {
            if (ex is not OperationCanceledException) AppLog.Write(ex, "template update check");
        }
    }

    private void RefreshTemplateBadge()
    {
        try
        {
            _pendingTemplateUpdates = TemplateUpdateDialog.FindPendingUpdates(_templateLibrary, _pendingTemplateUpdates);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write(ex, "template update notification");
        }
        ApplyTemplateStrings();
    }

    private void OpenTemplates()
    {
        try
        {
            using var dialog = new TemplateLibraryDialog(_templateLibrary);
            var result = dialog.ShowDialog(this);
            RefreshTemplateBadge();
            if (result != DialogResult.OK || dialog.SelectedTemplate is not { } template || !ConfirmDiscard())
                return;

            ClearRecovery();
            _project = template.CreateProject(dialog.ProjectName);
            _projectPath = null;
            _dirty = true;
            LoadEditorFromProject();
            RefreshProjectUi();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            BrandMessageBox.Show(this, ex.Message, L10n.T("Template.Library"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveTemplate()
    {
        if (_project is null) return;
        _editor.CommitPendingEdit();
        try
        {
            var previous = _templateLibrary.GetAll().FirstOrDefault(item => item.Id == _project.TemplateId);
            using var details = new TemplateDetailsDialog(previous?.Name ?? _project.Name, previous?.Material ?? "", previous?.Version);
            if (details.ShowDialog(this) != DialogResult.OK) return;
            using var save = new BrandedSaveFileDialog
            {
                Title = L10n.T("Template.Save"), Filter = $"{L10n.T("Menu.Templates")} (*{LabelTemplate.FileExtension})|*{LabelTemplate.FileExtension}",
                FileName = "template" + LabelTemplate.FileExtension, AddExtension = true
            };
            if (save.ShowDialog(this) != DialogResult.OK) return;

            var preview = TemplateImage.Render(_editor.Layout, _project.Print);
            var template = LabelTemplate.FromProject(_project, details.TemplateName, details.Material,
                details.SaveNew ? null : previous, preview);
            TemplateLibrary.SaveFile(template, save.FileName);
            if (!_templateLibrary.Install(template))
                throw new InvalidDataException(L10n.T("Template.VersionAlreadyInstalled"));
            _project.TemplateId = template.Id;
            _project.TemplateVersion = template.Version;
            _dirty = true;
            RefreshProjectUi();
            RefreshTemplateBadge();
            BrandMessageBox.Show(this, string.Format(L10n.T("Template.Saved"), template.Name, template.Version),
                L10n.T("Template.Save"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            BrandMessageBox.Show(this, ex.Message, L10n.T("Template.Save"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal static class TemplateImage
{
    public static byte[]? Render(LabelLayout layout, PrintSettings print)
    {
        try
        {
            using var bitmap = LabelPrintRenderer.Render(layout, print, 96, 96);
            var scale = Math.Min(1d, 800d / Math.Max(bitmap.Width, bitmap.Height));
            using var preview = new Bitmap(bitmap, new Size(Math.Max(1, (int)(bitmap.Width * scale)),
                Math.Max(1, (int)(bitmap.Height * scale))));
            using var stream = new MemoryStream();
            preview.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException or System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }

    public static Bitmap Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var original = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
        return new Bitmap(original);
    }
}

internal sealed class TemplateDetailsDialog : BrandForm
{
    private readonly TextBox _name = new() { Dock = DockStyle.Top };
    private readonly TextBox _material = new() { Dock = DockStyle.Top };
    private readonly CheckBox _new = new() { AutoSize = true, Dock = DockStyle.Top };

    public string TemplateName => _name.Text.Trim();
    public string Material => _material.Text.Trim();
    public bool SaveNew => _new.Checked;

    public TemplateDetailsDialog(string name, string material, int? currentVersion)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = L10n.T("Template.Save");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(460, 245);
        MaximizeBox = MinimizeBox = ShowIcon = false;
        Font = Theme.UiFont();
        BackColor = Theme.Surface;
        _name.Text = name;
        _material.Text = material;
        _new.Checked = currentVersion is null;
        _new.Visible = currentVersion is not null;
        _new.Text = L10n.T("Template.SaveNew");
        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16)
        };
        void Field(TextBox box, string key)
        {
            var label = new Label { Text = L10n.T(key), AutoSize = true };
            box.AccessibleName = label.Text;
            box.Dock = DockStyle.None;
            box.Width = 410;
            box.Margin = new Padding(0, 1, 0, 10);
            content.Controls.Add(label);
            content.Controls.Add(box);
        }
        Field(_name, "Template.Name");
        Field(_material, "Template.Material");
        if (currentVersion is not null)
        {
            var version = new Label { AutoSize = true, Text = string.Format(L10n.T("Template.NewVersion"), currentVersion.Value + 1) };
            content.Controls.Add(version);
            content.Controls.Add(_new);
            _new.CheckedChanged += (_, _) => version.Visible = !_new.Checked;
        }
        var save = new Button { Text = L10n.T("Toolbar.Save"), DialogResult = DialogResult.OK, Enabled = TemplateName.Length > 0 };
        _name.TextChanged += (_, _) => save.Enabled = TemplateName.Length > 0;
        var cancel = new Button { Text = L10n.T("Dialog.Cancel"), DialogResult = DialogResult.Cancel };
        Controls.Add(content);
        Controls.Add(Theme.DialogFooter(save, cancel));
        AcceptButton = save;
        CancelButton = cancel;
        ResumeLayout();
    }
}

internal sealed class TemplateLibraryDialog : BrandForm
{
    private readonly TemplateLibrary _library;
    private readonly ListBox _items = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Canvas };
    private readonly Label _info = new() { Dock = DockStyle.Top, Height = 52, Padding = new Padding(8), UseMnemonic = false };
    private readonly TextBox _projectName = new() { Dock = DockStyle.Top };
    private readonly Button _create = new() { DialogResult = DialogResult.OK };
    private readonly Button _picture = new() { AutoSize = true };
    private readonly Button _export = new() { AutoSize = true };

    public LabelTemplate? SelectedTemplate => (_items.SelectedItem as Option)?.Template;
    public string ProjectName => _projectName.Text.Trim();

    private sealed record Option(LabelTemplate Template)
    {
        public override string ToString() => $"{Template.Name}  ·  v{Template.Version}";
    }

    public TemplateLibraryDialog(TemplateLibrary library)
    {
        _library = library;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = L10n.T("Template.Library");
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = ShowIcon = false;
        ClientSize = new Size(850, 480);
        MinimumSize = new Size(580, 380);
        Font = Theme.UiFont();
        BackColor = Theme.Surface;

        var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(14) };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        _items.AccessibleName = Text;
        columns.Controls.Add(_items, 0, 0);
        var detail = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0) };
        detail.Controls.Add(_preview);
        detail.Controls.Add(_info);
        columns.Controls.Add(detail, 1, 0);

        var tools = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(14, 0, 0, 0) };
        var import = new Button { Text = L10n.T("Template.Import"), AutoSize = true };
        _picture.Text = L10n.T("Template.SetPreview");
        _export.Text = L10n.T("Template.Export");
        Theme.StyleButton(import);
        Theme.StyleButton(_picture);
        Theme.StyleButton(_export);
        import.Click += (_, _) => Import();
        _picture.Click += (_, _) => ChangePreview();
        _export.Click += (_, _) => Export();
        tools.Controls.AddRange([import, _picture, _export]);

        var nameRow = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 41, ColumnCount = 2, Padding = new Padding(14, 2, 14, 0) };
        nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var name = new Label { Text = L10n.T("NewProject.Name"), AutoSize = true, Anchor = AnchorStyles.Left };
        _projectName.AccessibleName = name.Text;
        nameRow.Controls.Add(name, 0, 0);
        nameRow.Controls.Add(_projectName, 1, 0);

        _create.Text = L10n.T("NewProject.Create");
        var close = new Button { Text = L10n.T("Dialog.Close"), DialogResult = DialogResult.Cancel };
        Controls.Add(columns);
        Controls.Add(tools);
        Controls.Add(nameRow);
        Controls.Add(Theme.DialogFooter(_create, close));
        AcceptButton = _create;
        CancelButton = close;
        _items.SelectedIndexChanged += (_, _) => ShowSelected();
        _projectName.TextChanged += (_, _) => _create.Enabled = SelectedTemplate is not null && ProjectName.Length > 0;
        Reload();
        ResumeLayout();
    }

    private void Reload(string? id = null)
    {
        _items.BeginUpdate();
        _items.Items.Clear();
        foreach (var template in _library.GetAll()) _items.Items.Add(new Option(template));
        var index = id is null ? -1 : _items.Items.Cast<Option>().ToList().FindIndex(option => option.Template.Id == id);
        if (_items.Items.Count > 0) _items.SelectedIndex = Math.Max(0, index);
        _items.EndUpdate();
        if (_items.Items.Count == 0) ShowSelected();
    }

    private void ShowSelected()
    {
        var template = SelectedTemplate;
        _projectName.Text = template?.Name ?? "";
        _picture.Enabled = _export.Enabled = _create.Enabled = template is not null;
        _preview.Image?.Dispose();
        _preview.Image = null;
        if (template is null)
        {
            _info.Text = L10n.T("Template.Empty");
            return;
        }
        _info.Text = $"{template.Material}  ·  {Units.UmToMm(template.Layout.MediaWidthUm):0.###} × " +
            $"{Units.UmToMm(template.Layout.MediaHeightUm):0.###} {L10n.T("Unit.Mm")}";
        if (template.PreviewPngBase64 is { Length: > 0 } encoded)
        {
            try { _preview.Image = TemplateImage.Decode(Convert.FromBase64String(encoded)); }
            catch (Exception ex) when (ex is ArgumentException or FormatException or System.Runtime.InteropServices.ExternalException or OutOfMemoryException)
            { _info.Text += Environment.NewLine + L10n.T("Template.InvalidPreview"); }
        }
        if (_preview.Image is not null) return;
        var bytes = TemplateImage.Render(template.Layout, template.Print);
        if (bytes is not null)
        {
            try { _preview.Image = TemplateImage.Decode(bytes); }
            catch (ArgumentException) { _info.Text += Environment.NewLine + L10n.T("Template.InvalidPreview"); }
        }
    }

    private void Import()
    {
        using var dialog = new BrandedOpenFileDialog
        {
            Title = L10n.T("Template.Import"), Filter = $"{L10n.T("Menu.Templates")} (*{LabelTemplate.FileExtension})|*{LabelTemplate.FileExtension}"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { Reload(_library.Import(dialog.FileName).Id); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        { BrandMessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ChangePreview()
    {
        if (SelectedTemplate is not { } template) return;
        using var dialog = new BrandedOpenFileDialog { Title = _picture.Text, Filter = "PNG (*.png)|*.png" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 2_000_000) throw new InvalidDataException(L10n.T("Template.InvalidPreview"));
            var bytes = File.ReadAllBytes(dialog.FileName);
            var updated = template.WithPreview(bytes);
            using var decoded = TemplateImage.Decode(bytes);
            if (!_library.Install(updated)) throw new InvalidDataException(L10n.T("Template.VersionAlreadyInstalled"));
            Reload(updated.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException
            or System.Runtime.InteropServices.ExternalException or OutOfMemoryException)
        { BrandMessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void Export()
    {
        if (SelectedTemplate is not { } template) return;
        using var dialog = new BrandedSaveFileDialog
        {
            Title = _export.Text, Filter = $"{L10n.T("Menu.Templates")} (*{LabelTemplate.FileExtension})|*{LabelTemplate.FileExtension}",
            FileName = template.Id + LabelTemplate.FileExtension, AddExtension = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { TemplateLibrary.SaveFile(template, dialog.FileName); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        { BrandMessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    protected override void OnLoad(EventArgs e) { base.OnLoad(e); Theme.FitDialogToScreen(this); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _preview.Image?.Dispose();
        base.Dispose(disposing);
    }
}
