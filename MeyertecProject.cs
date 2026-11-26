using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeyertecPrint;

/// <summary>
/// Модель файла проекта MEYERTEC PRINT.
/// "Проект" — один файл (.mprj), в котором хранится всё задание:
/// вся лента этикеток сразу (как у Wago Smart Script), принтер
/// и настройки печати. Сделал — открыл — напечатал,
/// потом через год открыл — напечатал.
/// Формат JSON (читаемый, легко расширять), версиируется полем Version.
/// </summary>
public class ProjectFile
{
    public const string FileExtension = ".mprj";
    public const int CurrentVersion = 6;

    public int Version { get; set; } = CurrentVersion;

    public string Name { get; set; } = string.Empty;

    public DateTime Created { get; set; } = DateTime.Now;

    public DateTime Modified { get; set; } = DateTime.Now;

    /// <summary>Принтер, для которого сделан проект (в 1-й итерации — Godex 530).</summary>
    public string? PrinterModel { get; set; }
    public string? TemplateId { get; set; }
    public int? TemplateVersion { get; set; }

    public PrintSettings Print { get; set; } = new();

    public RibbonData Ribbon { get; set; } = new();
}

/// <summary>Настройки печати конкретного проекта (драйверные).</summary>
public class PrintSettings
{
    public string? PrinterName { get; set; }
    public string PrintBackend { get; set; } = "driver";
    public string ConnectionKind { get; set; } = "usb";
    public string? NetworkHost { get; set; }
    public string? SerialPort { get; set; }
    public int SerialBaudRate { get; set; } = 9600;
    public string? UsbDevice { get; set; }
    public int Copies { get; set; } = 1;
    public decimal MediaGapMm { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AlignToNotch { get; set; }
    /// <summary>Смещение всего задания по X, мм (может быть отрицательным).</summary>
    public double OffsetXMm { get; set; }

    /// <summary>Смещение всего задания по Y, мм (может быть отрицательным).</summary>
    public double OffsetYMm { get; set; }

    /// <summary>Температура нагрева (null = настройка по умолчанию принтера).</summary>
    public int? Temperature { get; set; }

    /// <summary>Отступ отреза, мм (null = настройка по умолчанию принтера).</summary>
    public double? CutOffsetMm { get; set; }
}

/// <summary>Лента этикеток проекта целиком.</summary>
public class RibbonData
{
    /// <summary>Ширина метки, мм (legacy v1: до появления макета).</summary>
    public double LabelWidthMm { get; set; }

    /// <summary>Высота метки, мм (legacy v1: до появления макета).</summary>
    public double LabelHeightMm { get; set; }

    /// <summary>Имя типа носителя из каталога (Custom label / 210-805 / ...).</summary>
    public string? LabelTypeName { get; set; }

    /// <summary>Полный макет полосы (геометрия в мкм + строки/ячейки).</summary>
    public LabelLayout? Layout { get; set; }

    public List<LabelItem> Labels { get; set; } = new();
    public bool ApplyLayoutToAll { get; set; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LabelLayout? IndividualLayoutBase { get; set; }

    /// <summary>
    /// Возвращает макет; для старых проектов (.mprj без Layout)
    /// строит дефолтный по типу из каталога или из legacy-размеров.
    /// </summary>
    public LabelLayout GetLayout()
    {
        if (Layout is { Rows.Count: > 0 })
            return Layout;

        var layout = new LabelLayout();
        var type = LabelTypeCatalog.All.FirstOrDefault(t =>
            string.Equals(t.Name, LabelTypeName, StringComparison.Ordinal));
        if (type is not null)
        {
            layout.Geometry = type.Geometry.Clone();
        }
        else
        {
            layout.Geometry = new LabelGeometry
            {
                LabelsInRow = 1,
                WidthUm = Units.MmToUm(LabelWidthMm > 0 ? LabelWidthMm : 15),
                HeightUm = Units.MmToUm(LabelHeightMm > 0 ? LabelHeightMm : 10),
                TopUm = Units.MmToUm(1.2),
                LeftUm = Units.MmToUm(13),
                RightUm = Units.MmToUm(1),
                GapBetweenUm = 0,
                GapBottomUm = Units.MmToUm(1.2),
            };
        }

        var row = new LabelRow { HeightUm = layout.Geometry.HeightUm };
        for (var i = 0; i < layout.Geometry.LabelsInRow; i++)
            row.Cells.Add(new LabelCell { WidthUm = layout.Geometry.WidthUm });
        layout.Rows.Add(row);

        Layout = layout;
        return layout;
    }
}

/// <summary>Одна этикетка в ленте.</summary>
public class LabelItem
{
    /// <summary>Номер этикетки в ленте (с 1).</summary>
    public int Index { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Шаблон материала (будет подключен к библиотеке шаблонов).</summary>
    public string? Template { get; set; }

    /// <summary>Содержимое этикетки: поле → значение (запас на будущее).</summary>
    public Dictionary<string, string> Fields { get; set; } = new();

    /// <summary>Дополнительный экземпляр макета без подстановки данных.</summary>
    public bool UseLayout { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LabelLayout? Layout { get; set; }
}

/// <summary>Чтение и запись файлов проекта.</summary>
public static class ProjectFileIo
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // Имя проекта и названия этикеток — на языке пользователя
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static ProjectFile Load(string path)
    {
        ProjectFile? project;
        try
        {
            project = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(path), Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(ex.Message, ex);
        }

        if (project is null)
            throw new InvalidDataException(L10n.T("Error.FileEmpty"));

        if (project.Version < 1 || project.Version > ProjectFile.CurrentVersion)
            throw new NotSupportedException(string.Format(L10n.T("Error.ProjectVersion"), project.Version));
        if (project.Ribbon?.Layout is { } layout &&
            (layout.MediaWidthUm <= 0 || layout.MediaWidthUm > 300_000 || layout.MediaHeightUm <= 0 || layout.MediaHeightUm > 762_000))
            throw new InvalidDataException(L10n.T("Print.InvalidMediaSize"));
        if (project.Ribbon?.Layout is { AlternateNotch: { } notch } stock &&
            (project.Version < 5 || !notch.Fits(stock.MediaWidthUm, stock.MediaHeightUm)))
            throw new InvalidDataException(L10n.T("Print.InvalidNotch"));
        return project;
    }

    public static void SaveRecovery(ProjectFile project, string path)
    {
        var version = project.Version;
        try
        {
            project.Version = RequiredVersion(project);
            WriteAtomic(project, path);
        }
        finally { project.Version = version; }
    }

    public static void Save(ProjectFile project, string path)
    {
        project.Modified = DateTime.Now;
        project.Version = RequiredVersion(project);
        WriteAtomic(project, path);
    }

    private static int RequiredVersion(ProjectFile project)
    {
        var layout = project.Ribbon?.Layout;
        if (project.Print?.AlignToNotch == true || project.Ribbon is { } ribbon &&
            (!ribbon.ApplyLayoutToAll || ribbon.IndividualLayoutBase is not null || ribbon.Labels.Any(label => label.Layout is not null)))
            return 6;
        if (layout?.AlternateNotch is not null) return 5;
        if (project.TemplateId is not null || project.TemplateVersion is not null ||
            project.Print is { } print && (print.PrintBackend != "driver" || print.ConnectionKind == "serial" ||
                print.SerialPort is not null || print.UsbDevice is not null || print.SerialBaudRate != 9600 ||
                print.Temperature is not null || print.CutOffsetMm is not null || print.MediaGapMm != 0) ||
            project.Ribbon?.Labels.Any(label => label.UseLayout) == true ||
            layout is not null && layout.Rows.SelectMany(row => row.Cells).Select(cell => cell.Content)
                .Concat((layout.Objects ?? []).Select(item => item.Content))
                .Any(content => content.AutoFitText || content.InvertColors))
            return 4;

        var hasNewPrintSettings = !string.IsNullOrWhiteSpace(project.Print?.PrinterName) ||
            project.Print?.ConnectionKind == "ethernet" || (project.Print?.Copies ?? 1) != 1;
        return hasNewPrintSettings || layout is not null &&
            (layout.MediaWidthUm != 60_000 || layout.MediaHeightUm != 30_000)
            ? 3 : layout?.Objects is { Count: > 0 } ? 2 : 1;
    }

    private static void WriteAtomic(ProjectFile project, string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        var temp = Path.Combine(directory, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(project, Options));
            File.Move(temp, fullPath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
