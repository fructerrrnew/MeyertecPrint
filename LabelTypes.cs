using System.Text.Json;

namespace MeyertecPrint;

/// <summary>Размеры этикеток из каталога; справочные позиции не содержат проверенного раскроя рулона.</summary>
public class LabelType
{
    public string Name { get; set; } = string.Empty;
    public bool IsCustom { get; set; }
    public bool ReferenceOnly { get; set; }
    public string? Source { get; set; }
    public string? Verification { get; set; }
    public LabelGeometry Geometry { get; set; } = new();
    public string DisplayName => IsCustom ? L10n.T("Catalog.Custom") : Name;
    public string SizeDisplay => $"{Units.UmToMm(Geometry.WidthUm):0.###} × {Units.UmToMm(Geometry.HeightUm):0.###} {L10n.T("Unit.Mm")}";
}

/// <summary>Каталог считывается из редактируемого JSON рядом с программой.</summary>
public static class LabelTypeCatalog
{
    public const string Stock60x30 = "60 x 30 mm";
    public static string CatalogPath => Path.Combine(AppContext.BaseDirectory, "Catalog", "labels.json");
    public static IReadOnlyList<LabelType> All { get; } = Load();
    private static IReadOnlyList<LabelType> Load()
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var types = JsonSerializer.Deserialize<List<LabelType>>(File.ReadAllText(CatalogPath), options) ?? new();
            return types.Where(t => !string.IsNullOrWhiteSpace(t.Name) && t.Geometry is { WidthUm: > 0, HeightUm: > 0, LabelsInRow: > 0 }).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Array.Empty<LabelType>();
        }
    }
}
