using System.Text.Json;

namespace MeyertecPrint;

public sealed class TemplateFeedSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeyertecPrint", "template-feed.json");

    public string ManifestUrl { get; set; } = string.Empty;
    public bool UseTls { get; set; } = true;

    public static TemplateFeedSettings Load()
    {
        var fallback = new TemplateFeedSettings { ManifestUrl = Environment.GetEnvironmentVariable("MEYERTEC_TEMPLATE_FEED") ?? "" };
        try
        {
            return JsonSerializer.Deserialize<TemplateFeedSettings>(File.ReadAllText(SettingsPath)) ?? fallback;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return fallback;
        }
    }

    public Uri GetManifestUri()
    {
        if (!Uri.TryCreate(ManifestUrl?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeFtp ||
            string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath.EndsWith('/') ||
            Uri.UnescapeDataString(uri.AbsolutePath).Any(char.IsControl))
            throw new InvalidDataException(L10n.T("Template.FeedAddressInvalid"));
        return uri;
    }

    public void Save()
    {
        var uri = GetManifestUri();
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(new TemplateFeedSettings
            {
                ManifestUrl = uri.AbsoluteUri, UseTls = UseTls
            }));
            File.Move(temp, SettingsPath, overwrite: true);
        }
        finally { File.Delete(temp); }
    }
}
