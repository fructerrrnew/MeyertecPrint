namespace MeyertecPrint;

/// <summary>
/// Единицы: внутри хранится МИКРОН (мкм) — как в WAGO .wstx и с внутренней
/// точностью smart SCRIPT (1 мкм). На экран выводится мм.
/// </summary>
public static class Units
{
    public const double MicronsPerMm = 1000.0;

    public static long MmToUm(double mm) => (long)Math.Round(mm * MicronsPerMm);

    public static double UmToMm(long um) => um / MicronsPerMm;

    /// <summary>Форматирует мкм как мм (напр. «15 мм»).</summary>
    public static string FormatMm(long um)
        => $"{UmToMm(um):0.###} {L10n.T("Unit.Mm")}";
}

public enum CellContentType { Empty, Text, Qr, Barcode, Image }

public enum BarcodeSymbol { Code128, Ean13 }

/// <summary>Содержимое одной ячейки. Тип определяет, какие поля имеют смысл.</summary>
public class CellContent
{
    public CellContentType Type { get; set; } = CellContentType.Empty;

    /// <summary>Текст (для Text и как источник для Qr).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Данные 1D-штрихкода.</summary>
    public string BarcodeData { get; set; } = string.Empty;

    public BarcodeSymbol BarcodeSymbol { get; set; } = BarcodeSymbol.Code128;

    /// <summary>Изображение в base64 (PNG или JPEG).</summary>
    public string? ImageBase64 { get; set; }

    /// <summary>Формат изображения: "png" / "jpeg".</summary>
    public string? ImageFormat { get; set; }

    /// <summary>
    /// Шрифт для текста (имя семейства; null — шрифт по умолчанию).
    /// Старые файлы (.mprj/.wstx без поля) открываются с дефолтом.
    /// </summary>
    public string? FontName { get; set; }

    /// <summary>Размер шрифта, pt (null — авто по высоте ячейки).</summary>
    public float? FontSizePt { get; set; }

    /// <summary>Жирный для текста.</summary>
    public bool Bold { get; set; }

    /// <summary>Размер шрифта ограничен указанным максимумом и подбирается по ширине и высоте.</summary>
    public bool AutoFitText { get; set; }

    /// <summary>Печать элемента в негативе в пределах его рамки.</summary>
    public bool InvertColors { get; set; }

    /// <summary>Глубокое копирование содержимого (для снапшотов и разбивки).</summary>
    public CellContent Clone()
        => new()
        {
            Type = Type,
            Text = Text,
            BarcodeData = BarcodeData,
            BarcodeSymbol = BarcodeSymbol,
            ImageBase64 = ImageBase64,
            ImageFormat = ImageFormat,
            FontName = FontName,
            FontSizePt = FontSizePt,
            Bold = Bold,
            AutoFitText = AutoFitText,
            InvertColors = InvertColors,
        };
}

/// <summary>Независимый объект этикетки; координаты и размеры в мкм от её левого верхнего угла.</summary>
public class LabelObject
{
    public long XUm { get; set; }
    public long YUm { get; set; }
    public long WidthUm { get; set; }
    public long HeightUm { get; set; }
    // При переносе старой ячейки сохраняет её исходную колонку CSV.
    public int? CsvColumn { get; set; }
    public CellContent Content { get; set; } = new();

    public LabelObject Clone() => new()
    {
        XUm = XUm, YUm = YUm, WidthUm = WidthUm, HeightUm = HeightUm, CsvColumn = CsvColumn,
        Content = Content.Clone()
    };
}

/// <summary>Ячейка в строке макета. Ширина — мкм.</summary>
public class LabelCell
{
    public long WidthUm { get; set; }

    public CellContent Content { get; set; } = new();
}

/// <summary>Строка макета (ряд ячеек). Высота — мкм.</summary>
public class LabelRow
{
    public long HeightUm { get; set; }

    public List<LabelCell> Cells { get; set; } = new();
}

/// <summary>Геометрия макета (все размеры — мкм).</summary>
public class LabelGeometry
{
    public int LabelsInRow { get; set; } = 1;

    public long WidthUm { get; set; }   // Width
    public long HeightUm { get; set; }  // Height
    public long TopUm { get; set; }     // Top Distance
    public long LeftUm { get; set; }    // Left
    public long RightUm { get; set; }   // Right
    public long GapBetweenUm { get; set; } // Distance to the next label (между)
    public long GapBottomUm { get; set; }  // Distance to the next label (снизу)
}

public enum MediaNotchSide { Right, Left }

/// <summary>A recess at an edge of the material, measured in microns.</summary>
public sealed record MediaNotch(long DepthUm, long HeightUm, long CenterYUm,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    MediaNotchSide Side = MediaNotchSide.Right)
{
    internal bool Fits(long widthUm, long heightUm) => Enum.IsDefined(Side) && DepthUm > 0 && DepthUm < widthUm &&
        HeightUm > 0 && HeightUm <= heightUm && CenterYUm >= HeightUm / 2 &&
        CenterYUm <= heightUm - (HeightUm - HeightUm / 2);
}

/// <summary>
/// Макет этикетки: геометрия + сетка строк/ячеек.
/// Аналог LabelStrip (rows → cells) из WAGO LabelDESIGNER.
/// </summary>
public class LabelLayout
{
    // Физический размер одной страницы носителя, а не ширина или высота ячейки.
    // Значения по умолчанию сохраняют совместимость с прежними проектами 60 × 30 мм.
    public long MediaWidthUm { get; set; } = 60_000;
    public long MediaHeightUm { get; set; } = 30_000;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public MediaNotch? AlternateNotch { get; set; }
    internal bool IsNotchedSection { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public MediaNotch? ActiveNotch => IsNotchedSection ? AlternateNotch : null;
    public LabelGeometry Geometry { get; set; } = new();

    public List<LabelRow> Rows { get; set; } = new();

    public List<LabelObject> Objects { get; set; } = new();

    public int Columns => Rows.Count > 0 ? Rows[0].Cells.Count : 0;

    public long StripWidthUm
    {
        get
        {
            if (Rows.Count == 0) return Geometry.WidthUm;
            long sum = 0;
            foreach (var c in Rows[0].Cells) sum += c.WidthUm;
            return sum;
        }
    }

    public long StripHeightUm
    {
        get
        {
            long sum = 0;
            foreach (var r in Rows) sum += r.HeightUm;
            return sum;
        }
    }

    /// <summary>
    /// Разбивка ячейки (row, col) на N равных колонок (минимум 2);
    /// содержимое копируется во все под-ячейки (поведение эталона).
    /// </summary>
    public void SplitCellIntoColumns(int row, int col, int columns)
    {
        if (columns < 2)
            throw new ArgumentOutOfRangeException(nameof(columns), L10n.T("Error.MinColumns"));
        var r = Rows[row];
        var source = r.Cells[col];
        var width = source.WidthUm / columns;
        var replacements = new List<LabelCell>();
        for (var i = 0; i < columns; i++)
            replacements.Add(new LabelCell { WidthUm = width, Content = source.Content.Clone() });
        r.Cells.RemoveAt(col);
        r.Cells.InsertRange(col, replacements);
    }

    /// <summary>
    /// Разбивка строки на N равных по высоте строк (минимум 2);
    /// ячейки с содержимым копируются в каждую новую строку.
    /// </summary>
    public void SplitRowIntoRows(int row, int rowsCount)
    {
        if (rowsCount < 2)
            throw new ArgumentOutOfRangeException(nameof(rowsCount), L10n.T("Error.MinRows"));
        var source = Rows[row];
        var height = source.HeightUm / rowsCount;
        var newRows = new List<LabelRow>();
        for (var i = 0; i < rowsCount; i++)
            newRows.Add(new LabelRow
            {
                HeightUm = height,
                Cells = source.Cells.Select(c => c.Clone()).ToList(),
            });
        Rows.RemoveAt(row);
        Rows.InsertRange(row, newRows);
    }
}
