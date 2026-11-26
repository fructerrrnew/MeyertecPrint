using System.Buffers.Binary;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeyertecPrint;

/// <summary>A versioned, portable label layout and its print profile.</summary>
public sealed class LabelTemplate
{
    public const string FileExtension = ".mtemplate";
    public const int CurrentSchemaVersion = 3;

    [JsonRequired] public int SchemaVersion { get; set; } = 1;
    [JsonRequired] public string Id { get; set; } = string.Empty;
    [JsonRequired] public int Version { get; set; } = 1;
    [JsonRequired] public string Name { get; set; } = string.Empty;
    public string Material { get; set; } = string.Empty;
    public string? Source { get; set; }
    public string? PrinterModel { get; set; }
    [JsonRequired] public LabelLayout Layout { get; set; } = new();
    public PrintSettings Print { get; set; } = new();
    public string? PreviewPngBase64 { get; set; }

    public ProjectFile CreateProject(string name)
    {
        TemplateLibrary.Validate(this);
        var print = CopyPrintProfile(Print);
        if (print.AlignToNotch)
            print.PrintBackend = OperatingSystem.IsWindows() ? "godex-sdk" : "godex-direct";
        return new ProjectFile
        {
            Name = name.Trim(),
            PrinterModel = PrinterModel,
            TemplateId = Id,
            TemplateVersion = Version,
            Print = print,
            Ribbon = new RibbonData
            {
                LabelTypeName = Name,
                LabelWidthMm = Units.UmToMm(Layout.Geometry.WidthUm),
                LabelHeightMm = Units.UmToMm(Layout.Geometry.HeightUm),
                Layout = Layout.Clone(),
                Labels = LabelPrintPages.CreateStockSampleRecords(Layout)
            }
        };
    }

    public static LabelTemplate FromProject(ProjectFile project, string name, string material,
        LabelTemplate? previous = null, byte[]? previewPng = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var layout = project.Ribbon.GetLayout();
        var template = new LabelTemplate
        {
            SchemaVersion = RequiredSchema(layout),
            Id = previous?.Id ?? Guid.NewGuid().ToString("N"),
            Version = previous?.NextVersion() ?? 1,
            Name = name.Trim(),
            Material = material.Trim(),
            Source = "user",
            PrinterModel = project.PrinterModel,
            Layout = layout.Clone(),
            Print = CopyPrintProfile(project.Print),
            PreviewPngBase64 = previewPng is null ? null : Convert.ToBase64String(previewPng)
        };
        TemplateLibrary.Validate(template);
        return template;
    }

    public LabelTemplate WithPreview(byte[]? previewPng) =>
        Revise(Name, Material, Layout, previewPng is null ? null : Convert.ToBase64String(previewPng));

    public LabelTemplate WithDetails(string name, string material, LabelLayout layout) =>
        Revise(name.Trim(), material.Trim(), layout, PreviewPngBase64);

    private LabelTemplate Revise(string name, string material, LabelLayout layout, string? previewPngBase64)
    {
        var template = new LabelTemplate
        {
            SchemaVersion = Math.Max(SchemaVersion, RequiredSchema(layout)),
            Id = Id,
            Version = NextVersion(),
            Name = name,
            Material = material,
            Source = Source,
            PrinterModel = PrinterModel,
            Layout = layout.Clone(),
            Print = CopyPrintProfile(Print),
            PreviewPngBase64 = previewPngBase64
        };
        TemplateLibrary.Validate(template);
        return template;
    }

    private int NextVersion() => Version < int.MaxValue ? Version + 1 : throw TemplateLibrary.Error(
        "The template has reached the maximum version.", "Достигнут максимальный номер версии шаблона.");

    internal static int RequiredSchema(LabelLayout layout) => layout.AlternateNotch switch
    {
        null => 1,
        { Side: MediaNotchSide.Left } => 3,
        _ => 2
    };

    // Connection details belong to the local project. Shared templates only carry portable print settings.
    private static PrintSettings CopyPrintProfile(PrintSettings? source) => new()
    {
        Copies = source?.Copies ?? 1,
        OffsetXMm = source?.OffsetXMm ?? 0,
        OffsetYMm = source?.OffsetYMm ?? 0,
        Temperature = source?.Temperature,
        CutOffsetMm = source?.CutOffsetMm,
        MediaGapMm = source?.MediaGapMm ?? 0,
        AlignToNotch = source?.AlignToNotch ?? false
    };
}

public sealed class TemplateLibrary
{
    internal const int MaxTemplateBytes = 16 * 1024 * 1024;
    private const int MaxPreviewBytes = 2 * 1024 * 1024;
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        IgnoreReadOnlyProperties = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _builtInDirectory;
    private readonly string _installedDirectory;

    public TemplateLibrary(string? installedDirectory = null, string? builtInDirectory = null)
    {
        _installedDirectory = installedDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeyertecPrint", "Templates");
        _builtInDirectory = builtInDirectory ?? Path.Combine(AppContext.BaseDirectory, "Catalog");
    }

    public IReadOnlyList<LabelTemplate> GetAll()
        => ReadDirectory(_builtInDirectory).Concat(ReadDirectory(_installedDirectory))
            .GroupBy(template => template.Id, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(template => template.Version).First())
            .OrderBy(template => template.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(template => template.Id, StringComparer.Ordinal)
            .ToArray();

    public static LabelTemplate LoadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > MaxTemplateBytes)
            throw Error("The template exceeds the file size limit.", "Файл шаблона слишком большой.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return Parse(bytes);
    }

    public static void SaveFile(LabelTemplate template, string path)
    {
        Validate(template);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(template, JsonOptions);
        if (bytes.Length > MaxTemplateBytes)
            throw Error("The template exceeds the file size limit.", "Файл шаблона слишком большой.");

        var fullPath = Path.GetFullPath(path);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public LabelTemplate Import(string path) => InstallCore(LoadFile(path)).Template;

    public bool Install(LabelTemplate template) => InstallCore(template).Installed;

    private (LabelTemplate Template, bool Installed) InstallCore(LabelTemplate template)
    {
        Validate(template);
        Directory.CreateDirectory(_installedDirectory);
        var path = Path.Combine(_installedDirectory, template.Id + LabelTemplate.FileExtension);
        using var installLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var existing = GetAll().FirstOrDefault(current => current.Id == template.Id);
        if (existing is not null && existing.Version >= template.Version) return (existing, false);
        SaveFile(template, path);
        return (template, true);
    }

    private static IEnumerable<LabelTemplate> ReadDirectory(string path)
    {
        if (!Directory.Exists(path)) yield break;
        foreach (var file in Directory.EnumerateFiles(path, "*" + LabelTemplate.FileExtension).Order(StringComparer.Ordinal))
        {
            LabelTemplate template;
            try { template = LoadFile(file); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { continue; }
            yield return template;
        }
    }

    internal static LabelTemplate Parse(ReadOnlySpan<byte> bytes)
    {
        // Text editors may save the JSON as UTF-8 with a BOM.
        if (bytes.StartsWith("\uFEFF"u8)) bytes = bytes[3..];
        LabelTemplate? template;
        try { template = JsonSerializer.Deserialize<LabelTemplate>(bytes, JsonOptions); }
        catch (JsonException ex)
        {
            throw Error("Invalid template JSON.", "Неверный JSON шаблона.", ex);
        }
        Validate(template);
        return template!;
    }

    internal static bool IsValidId(string? id)
        => id is { Length: > 0 and <= 80 } &&
            IsIdCharacter(id[0]) && id.All(ch => IsIdCharacter(ch) || ch == '-');

    private static bool IsIdCharacter(char ch) => ch is >= 'a' and <= 'z' or >= '0' and <= '9';

    internal static void Validate(LabelTemplate? template)
    {
        if (template is null || template.SchemaVersion is < 1 or > LabelTemplate.CurrentSchemaVersion ||
            !IsValidId(template.Id) || template.Version < 1 || string.IsNullOrWhiteSpace(template.Name) ||
            template.Name.Length > 200 || template.Material is null || template.Material.Length > 200)
            throw Error("Invalid template format, identifier, version or name.", "Проверьте формат, идентификатор, версию и название шаблона.");

        var layout = template.Layout;
        var settings = template.Print;
        if (layout is null || layout.Geometry is not { HeightUm: > 0 } || settings is null ||
            settings.Copies is < 1 or > 500 || settings.Temperature is < 0 ||
            settings.MediaGapMm < 0 ||
            settings.CutOffsetMm is double cut && !double.IsFinite(cut))
            throw Error("Invalid layout or print profile.", "Проверьте геометрию шаблона и параметры печати.");
        if (template.SchemaVersion < LabelTemplate.RequiredSchema(layout))
            throw Error("The template format is too old for its notch geometry.", "Версия формата шаблона не поддерживает эту геометрию выреза.");
        if (settings.AlignToNotch && layout.AlternateNotch is null)
            throw new InvalidDataException(L10n.T("Print.NotchAlignmentUnsupported"));
        if (layout.AlternateNotch is not null && settings.MediaGapMm != 0)
        {
            throw new InvalidDataException(L10n.T("Print.ContinuousMediaGap"));
        }

        try
        {
            var plan = LabelPrintLayout.Create(layout);
            plan.EnsureFits(settings.OffsetXMm, settings.OffsetYMm, layout.MediaWidthUm, layout.MediaHeightUm);
            foreach (var cell in plan.Cells)
                ValidateContent(layout.Rows[cell.Row].Cells[cell.Column].Content);
            foreach (var item in layout.Objects ?? [])
            {
                var x = item?.XUm + settings.OffsetXMm * Units.MicronsPerMm;
                var y = item?.YUm + settings.OffsetYMm * Units.MicronsPerMm;
                if (item is null || item.WidthUm <= 0 || item.HeightUm <= 0 || item.CsvColumn < 0 ||
                    x < 0 || y < 0 || x + item.WidthUm > layout.MediaWidthUm || y + item.HeightUm > layout.MediaHeightUm)
                    throw Error("A template object is outside the media.", "Объект шаблона выходит за границы носителя.");
                ValidateContent(item.Content);
            }
        }
        catch (OverflowException ex)
        {
            throw Error("Template dimensions are out of range.", "Размеры шаблона выходят за допустимые пределы.", ex);
        }

        if (template.PreviewPngBase64 is { Length: > 0 } preview)
        {
            if (preview.Length > ((MaxPreviewBytes + 2) / 3) * 4)
                throw Error("The preview exceeds the PNG size limit.", "Файл предпросмотра PNG слишком большой.");
            try
            {
                var bytes = Convert.FromBase64String(preview);
                if (bytes.Length > MaxPreviewBytes || !HasPreviewHeader(bytes))
                    throw Error("The preview must be a PNG of at most 2048 × 2048 pixels.",
                        "Предпросмотр должен быть PNG размером не более 2048 × 2048 пикселей.");
            }
            catch (FormatException ex)
            {
                throw Error("The PNG preview is not valid base64.", "Данные предпросмотра PNG содержат неверный base64.", ex);
            }
        }
    }

    private static void ValidateContent(CellContent? content)
    {
        if (content is null || !Enum.IsDefined(content.Type) || content.Text is null || content.BarcodeData is null ||
            !Enum.IsDefined(content.BarcodeSymbol) || content.FontSizePt is float size && (!float.IsFinite(size) || size is <= 0 or > 500))
            throw Error("Invalid label content in the template.", "Проверьте содержимое этикетки в шаблоне.");
    }

    private static bool HasPreviewHeader(ReadOnlySpan<byte> bytes)
        => bytes.Length >= 45 &&
            bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
            BinaryPrimitives.ReadUInt32BigEndian(bytes[8..]) == 13 && bytes[12..16].SequenceEqual("IHDR"u8) &&
            BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]) is > 0 and <= 2048 &&
            BinaryPrimitives.ReadUInt32BigEndian(bytes[20..]) is > 0 and <= 2048 &&
            bytes[^8..^4].SequenceEqual("IEND"u8);

    internal static InvalidDataException Error(string english, string russian, Exception? inner = null)
        => new(L10n.Current == "ru" ? russian : english, inner);
}
