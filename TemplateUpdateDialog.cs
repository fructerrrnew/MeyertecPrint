using System.Net;
using System.Security.Authentication;

namespace MeyertecPrint;

internal sealed class TemplateUpdateDialog : BrandForm
{
    private readonly TemplateLibrary _library;
    private readonly TextBox _address = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _tls = new() { AutoSize = true };
    private readonly TextBox _username = new() { Dock = DockStyle.Fill };
    private readonly TextBox _password = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly ListBox _list = new()
    {
        Dock = DockStyle.Fill, IntegralHeight = false, SelectionMode = SelectionMode.None, HorizontalScrollbar = true
    };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(13, 4, 13, 3), AutoEllipsis = true
    };
    private readonly Button _check = new(), _install = new();
    private IReadOnlyList<TemplateFeedUpdate> _updates = [];
    private CancellationTokenSource? _operation;
    private bool _closeRequested;

    public IReadOnlyList<TemplateFeedUpdate>? CheckedUpdates { get; private set; }

    public TemplateUpdateDialog(TemplateLibrary library)
    {
        _library = library;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = L10n.T("Template.Updates");
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = ShowIcon = false;
        ClientSize = new Size(690, 440);
        MinimumSize = new Size(530, 385);
        Font = Theme.UiFont();
        BackColor = Theme.Surface;
        var settings = TemplateFeedSettings.Load();
        _address.Text = settings.ManifestUrl;
        _tls.Text = L10n.T("Template.FeedTls");
        _tls.Checked = settings.UseTls;
        _status.Text = L10n.T("Template.FeedHint");
        _status.UseMnemonic = false;

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Top, Height = 133, ColumnCount = 2, RowCount = 4, Padding = new Padding(12, 9, 12, 4)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 164));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Field(int row, string key, TextBox box)
        {
            var caption = new Label { Text = L10n.T(key), AutoSize = true, Anchor = AnchorStyles.Left };
            box.AccessibleName = caption.Text;
            fields.Controls.Add(caption, 0, row);
            fields.Controls.Add(box, 1, row);
        }
        Field(0, "Template.FeedAddress", _address);
        fields.Controls.Add(_tls, 1, 1);
        Field(2, "Template.FeedUsername", _username);
        Field(3, "Template.FeedPassword", _password);
        _list.AccessibleName = Text;
        _check.Text = L10n.T("Template.Check");
        _install.Text = L10n.T("Template.Install");
        _install.Enabled = false;
        _check.Click += async (_, _) => await Run(install: false);
        _install.Click += async (_, _) => await Run(install: true);
        var close = new Button { Text = L10n.T("Dialog.Close"), DialogResult = DialogResult.Cancel };
        Controls.Add(_list);
        Controls.Add(_status);
        Controls.Add(fields);
        Controls.Add(Theme.DialogFooter(_check, close, _install));
        AcceptButton = _check;
        CancelButton = close;
        _address.TextChanged += (_, _) => ResetUpdates();
        _tls.CheckedChanged += (_, _) => ResetUpdates();
        _username.TextChanged += (_, _) => ResetUpdates();
        _password.TextChanged += (_, _) => ResetUpdates();
        ResumeLayout();
    }

    private void ResetUpdates()
    {
        _updates = [];
        _list.Items.Clear();
        _install.Enabled = false;
    }

    private void ShowUpdates(IReadOnlyList<TemplateFeedUpdate> updates)
    {
        _updates = CheckedUpdates = updates;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var item in updates)
            _list.Items.Add(item.InstalledVersion is int current
                ? $"{item.Name}  ·  v{current} → v{item.Version}"
                : $"{item.Name}  ·  v{item.Version}  ·  {L10n.T("Template.New")}");
        _list.EndUpdate();
    }

    internal static IReadOnlyList<TemplateFeedUpdate> FindPendingUpdates(TemplateLibrary library,
        IReadOnlyList<TemplateFeedUpdate> updates)
    {
        if (updates.Count == 0) return updates;
        var installed = library.GetAll().ToDictionary(item => item.Id, item => item.Version);
        return updates.Select(item => item with
            { InstalledVersion = installed.TryGetValue(item.Id, out var version) ? version : null })
            .Where(item => item.Version > (item.InstalledVersion ?? 0))
            .ToArray();
    }

    private async Task Run(bool install)
    {
        if (_operation is not null) return;
        var settings = new TemplateFeedSettings { ManifestUrl = _address.Text, UseTls = _tls.Checked };
        using var operation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        _operation = operation;
        _address.Enabled = _tls.Enabled = _username.Enabled = _password.Enabled = _check.Enabled = _install.Enabled = false;
        _status.Text = L10n.T("Setup.Checking");
        try
        {
            var uri = settings.GetManifestUri();
            var credentials = string.IsNullOrWhiteSpace(_username.Text) ? null : new NetworkCredential(_username.Text, _password.Text);
            if (!settings.UseTls && credentials is not null)
                throw new InvalidDataException(L10n.T("Template.FeedTlsRequired"));
            if (install)
            {
                var installed = await TemplateFeed.InstallAsync(uri, _updates, _library, settings.UseTls, credentials, operation.Token);
                if (IsDisposed) return;
                ShowUpdates(FindPendingUpdates(_library, _updates));
                _status.Text = string.Format(L10n.T("Template.Installed"), installed.Count);
            }
            else
            {
                ResetUpdates();
                var updates = await TemplateFeed.CheckAsync(uri, _library, settings.UseTls, credentials, operation.Token);
                if (IsDisposed) return;
                settings.Save();
                ShowUpdates(updates);
                _status.Text = updates.Count == 0 ? L10n.T("Template.UpToDate")
                    : string.Format(L10n.T("Template.UpdateCount"), updates.Count);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException
            or WebException or OperationCanceledException or System.Net.Sockets.SocketException or AuthenticationException)
        {
            if (!IsDisposed && install)
            {
                try { ShowUpdates(FindPendingUpdates(_library, _updates)); }
                catch (Exception refreshError) when (refreshError is IOException or UnauthorizedAccessException)
                {
                    AppLog.Write(refreshError, "template update count refresh");
                }
            }
            if (!IsDisposed && !_closeRequested)
                _status.Text = ex is OperationCanceledException ? L10n.T("Template.FeedTimedOut") : ex.Message;
        }
        finally
        {
            _operation = null;
            if (!IsDisposed && _closeRequested)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
            else if (!IsDisposed)
            {
                _address.Enabled = _tls.Enabled = _username.Enabled = _password.Enabled = _check.Enabled = true;
                _install.Enabled = _updates.Count > 0;
            }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_operation is not null)
        {
            e.Cancel = true;
            _closeRequested = true;
            _operation.Cancel();
        }
        base.OnFormClosing(e);
    }

    protected override void OnLoad(EventArgs e) { base.OnLoad(e); Theme.FitDialogToScreen(this); }
}
