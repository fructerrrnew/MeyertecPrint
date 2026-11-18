using System.Text.Json;

namespace MeyertecPrint;

/// <summary>
/// Плагин принтера: JSON-файл в папке Plugins рядом с exe,
/// описывающий модель принтера и её параметры.
/// Это механизм поддержки сторонних принтеров
/// (НЕ путать с шаблонами печати из ТЗ).
/// Чтобы добавить принтер — положите новый *.json в папку Plugins.
/// </summary>
public class PrinterPlugin
{
    /// <summary>Название модели (отображается в списке "Модель принтера").</summary>
    public string Name { get; set; } = string.Empty;

    public int Version { get; set; } = 1;

    public string? Manufacturer { get; set; }
    public int ResolutionDpi { get; set; }
    public double MaxPrintWidthMm { get; set; }
    public double MinMediaWidthMm { get; set; }
    public double MaxMediaWidthMm { get; set; }
    public double MinPrintLengthMm { get; set; }
    public double MaxPrintLengthMm { get; set; }
    public List<string> Connections { get; set; } = new();
    public string? Source { get; set; }

    public List<PrinterParameter> Parameters { get; set; } = new();
}

/// <summary>
/// Параметр принтера.
/// type: "fixed" — фиксированное значение, показывается только для чтения;
///       "select" — значение выбирается из списка Options.
/// Название для отображения берётся из языкового файла: PrinterParam.{Key}.
/// </summary>
public class PrinterParameter
{
    public string Key { get; set; } = string.Empty;

    public string Type { get; set; } = "fixed";

    /// <summary>Текущее значение (хранится, меняется при выборе в списке).</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Варианты значений для type "select".</summary>
    public List<string> Options { get; set; } = new();
}

/// <summary>Загрузка плагинов принтеров из папки Plugins.</summary>
public static class PrinterPlugins
{
    private static string _pluginsDirectory = string.Empty;

    public static IReadOnlyList<PrinterPlugin> All { get; private set; } = Array.Empty<PrinterPlugin>();

    public static void Initialize(string pluginsDirectory)
    {
        _pluginsDirectory = pluginsDirectory;
        if (!Directory.Exists(_pluginsDirectory))
        {
            Directory.CreateDirectory(_pluginsDirectory);
        }

        LoadAll();
    }

    /// <summary>(Пере)загружает все плагины из папки Plugins.</summary>
    public static void LoadAll()
    {
        var plugins = new List<PrinterPlugin>();
        if (Directory.Exists(_pluginsDirectory))
        {
            foreach (var file in Directory.GetFiles(_pluginsDirectory, "*.json"))
            {
                try
                {
                    var plugin = JsonSerializer.Deserialize<PrinterPlugin>(File.ReadAllText(file),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (plugin is { Name.Length: > 0, ResolutionDpi: >= 72, MaxPrintWidthMm: > 0, MaxPrintLengthMm: > 0, Connections.Count: > 0 })
                    {
                        plugins.Add(plugin);
                    }
                }
                catch (Exception)
                {
                    // Повреждённый файл плагина пропускается, остальные работают
                }
            }
        }

        plugins.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        All = plugins;
    }
}
