using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeyertecPrint;

/// <summary>
/// Позиция каталога клемм (тестовые данные: cleat\cleat.json).
/// </summary>
public class CleatType
{
    public string Type { get; set; } = string.Empty;

    public double Size { get; set; }
    public string? Model { get; set; }
    public double? BodyWidthMm { get; set; }
    public double? BodyLengthMm { get; set; }
    public double? BodyHeightMm { get; set; }
    public bool MarkerDimensionsVerified { get; set; }
    public string? Source { get; set; }

    [JsonPropertyName("image_path")]
    public string ImagePath { get; set; } = string.Empty;

    /// <summary>
    /// Название позиции в списке (напр. «MTP 1.5»).
    /// Инвариантная культура: техническое обозначение всегда с точкой,
    /// независимо от локали системы.
    /// </summary>
    public string DisplayName
        => Model ?? $"{Type} {Size.ToString("0.###", CultureInfo.InvariantCulture)}";

    /// <summary>Полный путь к изображению; null, если файла нет.</summary>
    public string? ResolveImagePath()
    {
        if (string.IsNullOrWhiteSpace(ImagePath))
            return null;
        var dir = Path.GetDirectoryName(CleatCatalog.CatalogPath);
        if (dir is null)
            return null;
        var full = Path.GetFullPath(Path.Combine(dir, ImagePath));
        return File.Exists(full) ? full : null;
    }
}

/// <summary>
/// Каталог клемм: читается из папки cleat рядом с exe
/// (cleat.json — список позиций, image_path — относительно папки cleat).
/// Новая клемма = новая запись в cleat.json + файл изображения.
/// </summary>
public static class CleatCatalog
{
    /// <summary>Путь к cleat.json (папка cleat рядом с exe).</summary>
    public static string CatalogPath
        => Path.Combine(AppContext.BaseDirectory, "cleat", "cleat.json");

    private static IReadOnlyList<CleatType>? _all;

    /// <summary>
    /// Все клеммы каталога; пустой список, если папки cleat нет
    /// или файл повреждён (в диалоге показывается подсказка).
    /// </summary>
    public static IReadOnlyList<CleatType> All
    {
        get
        {
            if (_all is null)
                _all = Load();
            return _all;
        }
    }

    public static IReadOnlyList<CleatType> Load()
    {
        if (!File.Exists(CatalogPath))
            return Array.Empty<CleatType>();

        try
        {
            // Имена полей в cleat.json («type», «size») сопоставляем без учёта регистра
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<List<CleatType>>(File.ReadAllText(CatalogPath), options)
                   ?? new List<CleatType>();
        }
        catch (JsonException)
        {
            return Array.Empty<CleatType>();
        }
    }

}
