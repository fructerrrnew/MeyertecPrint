using System.Buffers;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeyertecPrint;

public sealed record TemplateFeedUpdate(string Id, string Name, int Version, string File, string Sha256,
    int? InstalledVersion);

/// <summary>Checks and installs a versioned template catalog over passive FTP or explicit FTPS.</summary>
public static class TemplateFeed
{
    private const int MaxManifestBytes = 1024 * 1024;
    private const int MaxEntries = 500;

    public static async Task<IReadOnlyList<TemplateFeedUpdate>> CheckAsync(Uri manifestUri, TemplateLibrary library,
        bool useTls = false, NetworkCredential? credentials = null, CancellationToken cancellationToken = default)
    {
        ValidateUri(manifestUri);
        var bytes = await FetchAsync(manifestUri, MaxManifestBytes, useTls, credentials, cancellationToken)
            .ConfigureAwait(false);
        var manifest = ParseManifest(bytes);
        var installed = library.GetAll().ToDictionary(template => template.Id, template => template.Version);
        return manifest.Select(entry => entry with
            { InstalledVersion = installed.TryGetValue(entry.Id, out var version) ? version : null })
            .Where(entry => entry.Version > (entry.InstalledVersion ?? 0))
            .ToArray();
    }

    public static async Task<IReadOnlyList<LabelTemplate>> InstallAsync(Uri manifestUri,
        IEnumerable<TemplateFeedUpdate> updates, TemplateLibrary library, bool useTls = false,
        NetworkCredential? credentials = null, CancellationToken cancellationToken = default)
    {
        ValidateUri(manifestUri);
        var selected = updates.Take(MaxEntries + 1).ToArray();
        ValidateEntries(selected);
        var installed = new List<LabelTemplate>();
        if (selected.Length == 0) return installed;

        // Validate the selected downloads before changing the library.
        var staging = Directory.CreateTempSubdirectory("meyertec-templates-");
        try
        {
            foreach (var entry in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = await FetchAsync(new Uri(manifestUri, entry.File), TemplateLibrary.MaxTemplateBytes,
                    useTls, credentials, cancellationToken).ConfigureAwait(false);
                if (!SHA256.HashData(bytes).AsSpan().SequenceEqual(Convert.FromHexString(entry.Sha256)))
                    throw TemplateLibrary.Error($"The checksum of template {entry.Id} is invalid.",
                        $"Контрольная сумма шаблона {entry.Id} не совпадает.");
                var template = TemplateLibrary.Parse(bytes);
                if (template.Id != entry.Id || template.Version != entry.Version)
                    throw TemplateLibrary.Error($"The template identifier or version does not match the manifest for {entry.Id}.",
                        $"Идентификатор или версия шаблона {entry.Id} не совпадает с каталогом.");
                await File.WriteAllBytesAsync(Path.Combine(staging.FullName, entry.Id + LabelTemplate.FileExtension),
                    bytes, cancellationToken).ConfigureAwait(false);
            }

            foreach (var entry in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var template = TemplateLibrary.LoadFile(Path.Combine(staging.FullName, entry.Id + LabelTemplate.FileExtension));
                if (library.Install(template)) installed.Add(template);
            }
            return installed;
        }
        finally
        {
            staging.Delete(recursive: true);
        }
    }

    internal static IReadOnlyList<TemplateFeedUpdate> ParseManifest(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith("\uFEFF"u8)) bytes = bytes[3..];
        FeedManifest? manifest;
        try { manifest = JsonSerializer.Deserialize<FeedManifest>(bytes, TemplateLibrary.JsonOptions); }
        catch (JsonException ex)
        {
            throw TemplateLibrary.Error("Invalid template feed JSON.", "Неверный JSON каталога шаблонов.", ex);
        }

        if (manifest is not { SchemaVersion: 1, Templates: not null } ||
            manifest.Templates.Length > MaxEntries || Array.Exists(manifest.Templates, entry => entry is null))
            throw TemplateLibrary.Error("Unsupported template feed format.", "Формат каталога шаблонов не поддерживается.");

        var entries = manifest.Templates.Select(entry => new TemplateFeedUpdate(entry.Id, entry.Name,
            entry.Version, entry.File, entry.Sha256, null)).ToArray();
        ValidateEntries(entries);
        return entries;
    }

    private static void ValidateEntries(IReadOnlyCollection<TemplateFeedUpdate> entries)
    {
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        if (entries.Count > MaxEntries || entries.Any(entry => entry is null ||
            !TemplateLibrary.IsValidId(entry.Id) || !identifiers.Add(entry.Id) ||
            string.IsNullOrWhiteSpace(entry.Name) || entry.Name.Length > 200 || entry.Version < 1 ||
            !IsTemplateFileName(entry.File) || entry.Sha256 is not { Length: 64 } || !entry.Sha256.All(Uri.IsHexDigit)))
            throw TemplateLibrary.Error("Invalid or duplicate template feed entry.", "Проверьте записи каталога и уникальность шаблонов.");
    }

    private static bool IsTemplateFileName(string? file)
        => file is { Length: > 0 and <= 128 } && file.EndsWith(LabelTemplate.FileExtension, StringComparison.OrdinalIgnoreCase) &&
            char.IsAsciiLetterOrDigit(file[0]) &&
            file.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.');

    private static void ValidateUri(Uri uri)
    {
        if (uri is null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeFtp || string.IsNullOrWhiteSpace(uri.Host) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath.EndsWith('/') ||
            Uri.UnescapeDataString(uri.AbsolutePath).Any(char.IsControl))
            throw TemplateLibrary.Error("Enter an FTP URL for the manifest without login details or query parameters.",
                "Укажите FTP-адрес файла каталога без логина, пароля и параметров запроса.");
    }

    private static async Task<byte[]> FetchAsync(Uri uri, int limit, bool useTls,
        NetworkCredential? credentials, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        var token = deadline.Token;
        token.ThrowIfCancellationRequested();

        // FtpWebRequest is the .NET FTP transport. HttpClient only supports HTTP and HTTPS.
#pragma warning disable SYSLIB0014
        var request = (FtpWebRequest)WebRequest.Create(uri);
#pragma warning restore SYSLIB0014
        request.Method = WebRequestMethods.Ftp.DownloadFile;
        request.EnableSsl = useTls;
        request.UseBinary = true;
        request.UsePassive = true;
        request.KeepAlive = false;
        request.Proxy = null;
        if (credentials is not null) request.Credentials = credentials;
        using var registration = token.Register(request.Abort);
        try
        {
            using var response = (FtpWebResponse)await request.GetResponseAsync().ConfigureAwait(false);
            using var input = response.GetResponseStream();
            using var output = new MemoryStream();
            var buffer = ArrayPool<byte>.Shared.Rent(65_536);
            try
            {
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
                    if (read == 0) return output.ToArray();
                    if (output.Length + read > limit)
                        throw TemplateLibrary.Error("The server file exceeds the allowed size.",
                            "Файл на сервере превышает допустимый размер.");
                    output.Write(buffer, 0, read);
                }
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }
        catch (Exception ex) when (token.IsCancellationRequested && ex is WebException or IOException or OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw TemplateLibrary.Error("The template server did not respond within two minutes.",
                "Сервер шаблонов не ответил в течение двух минут.", ex);
        }
    }

    private sealed class FeedManifest
    {
        [JsonRequired] public int SchemaVersion { get; set; }
        [JsonRequired] public FeedEntry[] Templates { get; set; } = [];
    }

    private sealed class FeedEntry
    {
        [JsonRequired] public string Id { get; set; } = string.Empty;
        [JsonRequired] public string Name { get; set; } = string.Empty;
        [JsonRequired] public int Version { get; set; }
        [JsonRequired] public string File { get; set; } = string.Empty;
        [JsonRequired] public string Sha256 { get; set; } = string.Empty;
    }
}
