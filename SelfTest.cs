namespace MeyertecPrint;

/// <summary>
/// Самопроверка без интерфейса (запуск: dotnet run -- --selftest):
/// каталог типов, геометрия 210-805, разбивки, QR, 1D-штрихкоды,
/// round-trip .wstx и .mprj, CSV, Undo/Redo.
/// Код выхода: 0 — все проверки прошли, 1 — есть провалы.
/// </summary>
public static class SelfTest
{
    public static int Run()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var failed = 0;
        void Check(string name, bool condition)
        {
            Console.WriteLine($"{(condition ? "PASS" : "FAIL")}  {name}");
            if (!condition) failed++;
        }

        // ---- 1. Каталог типов ----
        Check("catalog: JSON загружен", LabelTypeCatalog.All.Count >= 7);
        Check("catalog: первая — Custom label", LabelTypeCatalog.All.FirstOrDefault()?.IsCustom == true);
        var reference = LabelTypeCatalog.All.First(x => x.Name == "WAGO 210-805");
        Check("catalog: 210-805 15 × 6 мм, раскладка требует проверки", reference.ReferenceOnly &&
            reference.Geometry.WidthUm == 15000 && reference.Geometry.HeightUm == 6000 && !string.IsNullOrEmpty(reference.Source));
        var type = new LabelType { Geometry = reference.Geometry.Clone() };
        // Отдельный тест исторической геометрии: эти поля не выдаются за каталог производителя.
        type.Geometry.LeftUm = 13000; type.Geometry.RightUm = 1000;
        type.Geometry.TopUm = 1200; type.Geometry.GapBottomUm = 1200;

        // ---- 2. Макет и разбивки ----
        var layout = new LabelLayout
        {
            Geometry = type.Geometry.Clone(),
            Rows =
            {
                new LabelRow
                {
                    HeightUm = type.Geometry.HeightUm,
                    Cells =
                    {
                        new LabelCell { WidthUm = 15000, Content = new CellContent { Type = CellContentType.Text, Text = "KM-1" } },
                        new LabelCell { WidthUm = 15000 },
                    },
                },
            },
        };
        layout.SplitCellIntoColumns(0, 0, 3);
        Check("split: 3 + 1 = 4 ячейки", layout.Rows[0].Cells.Count == 4);
        Check("split: ширина под-ячейки 5000", layout.Rows[0].Cells[0].WidthUm == 5000);
        Check("split: контент скопирован во все под-ячейки",
            layout.Rows[0].Cells.Take(3).All(c => c.Content.Text == "KM-1"));
        layout.SplitRowIntoRows(0, 2);
        Check("split row: 2 строки по 3000 мкм", layout.Rows.Count == 2 && layout.Rows[1].HeightUm == 3000);
        Check("split row: ячейки скопированы", layout.Rows[1].Cells.Count == 4
            && layout.Rows[1].Cells[0].Content.Text == "KM-1");

        // ---- 3. QR ----
        var shortQr = QrEncoder.Encode("123");
        Check("qr: 3 байта → версия 1 (21x21)", shortQr.GetLength(0) == 21);
        var longQr = QrEncoder.Encode(new string('a', 256));
        Check("qr: 256 байт → версия 10 (57x57)", longQr.GetLength(0) == 57 && longQr.GetLength(1) == 57);
        Check("qr: finder — тёмный (0,0)", longQr[0, 0]);
        Check("qr: сепаратор — светлый (0,7)", !longQr[0, 7]);
        Check("qr: finder справа сверху — тёмный (0,56)", longQr[0, 56]);
        var qrUrl = QrEncoder.Encode("https://meyertec.com/km-1");
        Check("qr: ссылка кодируется", qrUrl.GetLength(0) >= 21 && qrUrl.GetLength(0) == qrUrl.GetLength(1));

        // ---- 4. 1D-штрихкоды ----
        Check("ean13: 0012345678905 валиден", BarcodeEncoder.IsValidEan13("0012345678905"));
        Check("ean13: 0012345678901 невалиден", !BarcodeEncoder.IsValidEan13("0012345678901"));
        Check("ean13: 12 цифр невалидны", !BarcodeEncoder.IsValidEan13("001234567890"));
        var ean = BarcodeEncoder.EncodeEan13("0012345678905");
        Check("ean13: 95 модулей", ean.Length == 95);
        Check("ean13: штрих на краях", ean[0] && ean[^1]);
        var c128 = BarcodeEncoder.EncodeCode128("KM-1");
        Check("code128: длина 79 (6×11 + стоп 13)", c128.Length == 79);
        Check("code128: начинается и заканчивается штрихом", c128[0] && c128[^1]);

        // ---- 5. Round-trip .wstx ----
        var wstxPath = Path.Combine(Path.GetTempPath(), "selftest_" + Guid.NewGuid().ToString("N") + ".wstx");
        var wstxSource = new LabelLayout
        {
            Geometry = type.Geometry.Clone(),
            Rows =
            {
                new LabelRow
                {
                    HeightUm = 6000,
                    Cells =
                    {
                        new LabelCell { WidthUm = 15000, Content = new CellContent { Type = CellContentType.Text, Text = "Привет", FontName = "Arial", FontSizePt = 10, Bold = true } },
                        new LabelCell { WidthUm = 15000, Content = new CellContent { Type = CellContentType.Qr, Text = "QR123" } },
                    },
                },
            },
        };
        try
        {
            WstxIo.Write("SelfTest", wstxSource, wstxPath);
            var (wstxName, wstxBack) = WstxIo.Read(wstxPath);
            Check("wstx: имя сохранено", wstxName == "SelfTest");
            Check("wstx: число строк", wstxBack.Rows.Count == 1);
            Check("wstx: число ячеек", wstxBack.Rows[0].Cells.Count == 2);
            Check("wstx: ширина ячейки", wstxBack.Rows[0].Cells[0].WidthUm == 15000);
            Check("wstx: left offset", wstxBack.Geometry.LeftUm == 13000);
            Check("wstx: top offset", wstxBack.Geometry.TopUm == 1200);
            Check("wstx: текст ячейки", wstxBack.Rows[0].Cells[0].Content.Type == CellContentType.Text
                && wstxBack.Rows[0].Cells[0].Content.Text == "Привет");
            Check("wstx: шрифт сохранён", wstxBack.Rows[0].Cells[0].Content.FontName == "Arial");
            Check("wstx: размер шрифта сохранён", wstxBack.Rows[0].Cells[0].Content.FontSizePt == 10f);
            Check("wstx: жирный сохранён", wstxBack.Rows[0].Cells[0].Content.Bold);
            Check("wstx: QR ячейки", wstxBack.Rows[0].Cells[1].Content.Type == CellContentType.Qr
                && wstxBack.Rows[0].Cells[1].Content.Text == "QR123");
        }
        finally
        {
            File.Delete(wstxPath);
        }

        // ---- 6. Round-trip .mprj ----
        var mprjPath = Path.Combine(Path.GetTempPath(), "selftest_" + Guid.NewGuid().ToString("N") + ".mprj");
        var project = new ProjectFile
        {
            Name = "SelfTest",
            Ribbon = new RibbonData
            {
                LabelWidthMm = 15,
                LabelHeightMm = 6,
                LabelTypeName = "210-805",
                Layout = wstxSource,
            },
        };
        project.Ribbon.Labels.Add(new LabelItem { Index = 1, Name = "Этикетка 1" });
        try
        {
            ProjectFileIo.Save(project, mprjPath);
            var loaded = ProjectFileIo.Load(mprjPath);
            Check("mprj: имя", loaded.Name == "SelfTest");
            Check("mprj: тип", loaded.Ribbon.LabelTypeName == "210-805");
            Check("mprj: макет", loaded.Ribbon.Layout is { Rows.Count: 1 });
            Check("mprj: текст ячейки", loaded.Ribbon.GetLayout().Rows[0].Cells[0].Content.Text == "Привет");
        }
        finally
        {
            File.Delete(mprjPath);
        }

        // Свободные объекты сохраняют координаты и не влияют друг на друга.
        var movable = wstxSource.Clone();
        movable.Objects.Add(new LabelObject { XUm = 2000, YUm = 3000, WidthUm = 10000, HeightUm = 8000,
            Content = new CellContent { Type = CellContentType.Text, Text = "A", FontName = "Arial", FontSizePt = 11, Bold = true } });
        movable.Objects.Add(new LabelObject { XUm = 15000, YUm = 1000, WidthUm = 12000, HeightUm = 12000,
            Content = new CellContent { Type = CellContentType.Qr, Text = "QR" } });
        var copyMovable = movable.Clone();
        copyMovable.Objects[0].XUm = 9000;
        Check("object: clone независим и сохраняет шрифт", movable.Objects[0].XUm == 2000 &&
            copyMovable.Objects[0].Content.FontSizePt == 11 && copyMovable.Objects[0].Content.Bold);
        try
        {
            project.Ribbon.Layout = movable;
            ProjectFileIo.Save(project, mprjPath);
            var loaded = ProjectFileIo.Load(mprjPath);
            Check("object: mprj сохраняет позицию и тип", loaded.Version == 2 &&
                loaded.Ribbon.GetLayout().Objects.Count == 2 && loaded.Ribbon.GetLayout().Objects[1].XUm == 15000 &&
                loaded.Ribbon.GetLayout().Objects[1].Content.Type == CellContentType.Qr);
            WstxIo.Write("ObjectTest", movable, wstxPath);
            var (_, imported) = WstxIo.Read(wstxPath);
            Check("object: wstx сохраняет позицию и содержимое", imported.Objects.Count == 2 &&
                imported.Objects[0].XUm == 2000 && imported.Objects[1].Content.Text == "QR");
        }
        finally { File.Delete(mprjPath); File.Delete(wstxPath); }

        // ---- 7. CSV ----
        var parsed = CsvReader.Parse("a;b;c\n\"x;y\";z\n1;;3");
        Check("csv: разделитель ';'", parsed.Delimiter == ';');
        Check("csv: 3 строки", parsed.Rows.Count == 3);
        Check("csv: кавычки с разделителем", parsed.Rows[1][0] == "x;y");
        Check("csv: пустое поле", parsed.Rows[2][1] == string.Empty);
        var parsedComma = CsvReader.Parse("a,b\n1,2");
        Check("csv: разделитель ','", parsedComma.Delimiter == ',');

        // ---- 8. Каталог клемм (cleat) ----
        var cleats = CleatCatalog.Load();
        Check("cleat: 3 позиции", cleats.Count == 3);
        Check("cleat: первая — MTP 1.5", cleats.Count > 0 && cleats[0].DisplayName == "MTP-1.5");
        Check("cleat: последняя — MTP 4", cleats.Count == 3 && cleats[2].DisplayName == "MTP-4");
        Check("cleat: изображения на месте",
            cleats.Count == 3 && cleats.All(c => c.ResolveImagePath() is not null));

        // ---- 9. Undo/Redo ----
        var undo = new UndoRedo();
        var before = layout.Rows[0].Cells.Count;
        undo.Push(layout.Clone());
        layout.SplitCellIntoColumns(0, 0, 2);
        Check("undo: CanUndo", undo.CanUndo);
        undo.Undo(ref layout);
        Check("undo: состояние восстановлено", layout.Rows[0].Cells.Count == before);
        Check("undo: CanRedo", undo.CanRedo);
        undo.Redo(ref layout);
        Check("redo: состояние возвращено", layout.Rows[0].Cells.Count == before + 1);
        Check("undo: после redo undo снова доступно", undo.CanUndo && !undo.CanRedo);

        // ---- Геометрия печати и данные этикеток ----
        var stock = LabelTypeCatalog.All.First(x => x.Name == LabelTypeCatalog.Stock60x30);
        Check("print: выбран формат 60 × 30 мм", stock.Geometry.WidthUm == 60000 && stock.Geometry.HeightUm == 30000);
        var printLayout = new LabelLayout
        {
            Geometry = new LabelGeometry { LabelsInRow = 2, WidthUm = 15000, HeightUm = 6000,
                LeftUm = 13000, TopUm = 1200, RightUm = 1000, GapBetweenUm = 2000, GapBottomUm = 1200 },
            Rows = { new LabelRow { HeightUm = 6000, Cells = {
                new LabelCell { WidthUm = 5000, Content = new CellContent { Type = CellContentType.Text, Text = "Первый" } },
                new LabelCell { WidthUm = 5000 }, new LabelCell { WidthUm = 5000 }, new LabelCell { WidthUm = 15000 }
            } } }
        };
        var printPlan = LabelPrintLayout.Create(printLayout);
        Check("print: зазор между этикетками, а не внутри разбитой ячейки",
            printPlan.Cells.Select(c => c.X).SequenceEqual(new long[] { 13000, 18000, 23000, 30000 }));
        Check("print: размеры полосы с полями и зазором", printPlan.WidthUm == 46000 && printPlan.HeightUm == 8400);
        var fitRejected = false;
        try { printPlan.EnsureFits(30, 0); }
        catch (InvalidDataException) { fitRejected = true; }
        Check("print: макет за границами 60 × 30 мм отклоняется", fitRejected);
        var dataRecords = new List<LabelItem> {
            new() { Fields = new() { ["C0"] = "Второй" } },
            new() { Fields = new() { ["C0"] = "Третий" } }, new()
        };
        var printPages = LabelPrintPages.Create(printLayout, dataRecords, true);
        Check("print: текущий макет и строки CSV по порядку", printPages.Count == 3
            && printPages[0].Rows[0].Cells[0].Content.Text == "Первый"
            && printPages[1].Rows[0].Cells[0].Content.Text == "Второй"
            && printPages[2].Rows[0].Cells[0].Content.Text == "Третий");
        Check("print: оригинал не изменён, одиночный режим", printLayout.Rows[0].Cells[0].Content.Text == "Первый"
            && LabelPrintPages.Create(printLayout, dataRecords, false).Count == 1);
        Check("print: число записей CSV", LabelPrintPages.CountImported(printLayout, dataRecords) == 2);
        Check("code128: корректный стоп-символ",
            string.Concat(BarcodeEncoder.EncodeCode128("KM-1").TakeLast(13).Select(b => b ? '1' : '0')) == "1100011101011");
        Check("ean13: корректная правая половина",
            string.Concat(BarcodeEncoder.EncodeEan13("5901234123457").Skip(50).Take(7).Select(b => b ? '1' : '0')) == "1100110");

        // Реальный рендер и перенос на устройство при 300 dpi без принтера.
        var rasterLayout = new LabelLayout
        {
            Geometry = new LabelGeometry { LabelsInRow = 1, WidthUm = 60000, HeightUm = 30000 },
            Rows = { new LabelRow { HeightUm = 30000, Cells = {
                new LabelCell { WidthUm = 60000, Content = new CellContent { Type = CellContentType.Qr, Text = "123" } }
            } } }
        };
        using (var raster = LabelPrintRenderer.Render(rasterLayout, new PrintSettings(), 300, 300))
        using (var device = new Bitmap(raster.Width, raster.Height))
        {
            device.SetResolution(300, 300);
            using (var graphics = Graphics.FromImage(device)) LabelPrintRenderer.DrawToDevice(graphics, raster, 0, 0);
            Check("print: растр 60 × 30 мм при 300 dpi", raster.Width == 709 && raster.Height == 354);
            var same = true;
            var hasInk = false;
            for (var y = 0; y < raster.Height; y += 5)
            for (var x = 0; x < raster.Width; x += 5)
            {
                var pixel = raster.GetPixel(x, y);
                if (pixel.R < 128) hasInk = true;
                if (pixel.ToArgb() != device.GetPixel(x, y).ToArgb()) same = false;
            }
            Check("print: печатный растр переносится без масштабирования", same && hasInk);
        }

        var alternate = new LabelLayout { MediaWidthUm = 40000, MediaHeightUm = 20000,
            Geometry = new LabelGeometry { LabelsInRow = 1, WidthUm = 40000, HeightUm = 20000 },
            Rows = { new LabelRow { HeightUm = 20000, Cells = { new LabelCell { WidthUm = 40000 } } } } };
        using (var bitmap = LabelPrintRenderer.Render(alternate, new PrintSettings(), 300, 300))
            Check("print: размер 40 × 20 мм", bitmap.Width == 472 && bitmap.Height == 236);
        try
        {
            project.Ribbon.Layout = alternate;
            ProjectFileIo.Save(project, mprjPath);
            var restored = ProjectFileIo.Load(mprjPath);
            Check("mprj: размер носителя сохранён", restored.Version == 3 && restored.Ribbon.GetLayout().MediaWidthUm == 40000);
            WstxIo.Write("media", alternate, wstxPath);
            var (_, fromXml) = WstxIo.Read(wstxPath);
            Check("wstx: размер носителя сохранён", fromXml.MediaWidthUm == 40000 && fromXml.MediaHeightUm == 20000);
        }
        finally { File.Delete(mprjPath); File.Delete(wstxPath); }

        var fittedText = new CellContent
        {
            Type = CellContentType.Text, Text = "XT-01234567890123456789",
            FontSizePt = 16, AutoFitText = true, InvertColors = true
        };
        var fittedLayout = alternate.Clone();
        fittedLayout.Objects.Add(new LabelObject
        {
            XUm = 1000, YUm = 1000, WidthUm = 12000, HeightUm = 5000, Content = fittedText
        });
        using (var raster = LabelPrintRenderer.Render(fittedLayout, new PrintSettings(), 300, 300, validateForPrint: true))
        {
            Check("print: подгонка текста и инверсия в рамке элемента",
                raster.GetPixel(13, 13).R < 128 && raster.GetPixel(0, 0).R > 240);
        }
        using (var source = new Bitmap(1, 1))
        using (var png = new MemoryStream())
        {
            source.SetPixel(0, 0, Color.Red);
            source.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            var imageLayout = alternate.Clone();
            imageLayout.Objects.Add(new LabelObject
            {
                XUm = 1000, YUm = 1000, WidthUm = 10000, HeightUm = 5000,
                Content = new CellContent
                {
                    Type = CellContentType.Image, ImageBase64 = Convert.ToBase64String(png.ToArray()), InvertColors = true
                }
            });
            using var raster = LabelPrintRenderer.Render(imageLayout, new PrintSettings(), 300, 300);
            var pixel = raster.GetPixel(71, 41);
            Check("print: инверсия изображения и фона рамки",
                pixel.R < 128 && pixel.G > 128 && pixel.B > 128 &&
                raster.GetPixel(13, 13).R < 128 && raster.GetPixel(0, 0).R > 240);
        }
        var manual = fittedLayout.Clone();
        manual.Objects[0].Content.AutoFitText = false;
        var manualOverflow = false;
        try { using var raster = LabelPrintRenderer.Render(manual, new PrintSettings(), 300, 300, validateForPrint: true); }
        catch (InvalidDataException) { manualOverflow = true; }
        Check("print: ручной шрифт сверх рамки отклоняется", manualOverflow);
        Check("strip: длина учитывает межэтикеточный зазор", LabelSequence.CalculateLengthUm(30000, 3, 2000) == 94000);
        var terminal = TemplateLibrary.LoadFile(Path.Combine(AppContext.BaseDirectory, "Catalog", "meyertec-terminal-50x10.mtemplate"));
        var terminalStock = terminal.Layout.Clone();
        terminalStock.Objects.Add(new LabelObject
        {
            WidthUm = terminalStock.MediaWidthUm, HeightUm = terminalStock.MediaHeightUm,
            Content = new CellContent { Type = CellContentType.Text, Text = "X", InvertColors = true }
        });
        var stockPages = LabelPrintPages.CreateStockPreview(terminalStock);
        using (var plain = LabelPrintRenderer.Render(stockPages[0], terminal.Print, 300, 300))
        using (var printed = LabelPrintRenderer.Render(stockPages[1], terminal.Print, 300, 300))
        using (var preview = LabelPrintRenderer.Render(stockPages[1], terminal.Print, 300, 300, showMedia: true))
        {
            Check("print: на секции с вырезом исключается только область выреза",
                plain.Width == 591 && plain.Height == 118 && plain.GetPixel(584, 59).R < 128 &&
                printed.GetPixel(584, 59).R == 255 && printed.GetPixel(584, 28).R < 128 &&
                printed.GetPixel(534, 35).R < 128 && printed.GetPixel(530, 59).R < 128);
            Check("preview: вырез виден на правом крае", preview.GetPixel(584, 59).R < 250);
        }
        var terminalPrintPages = LabelPrintPages.Create(terminal.Layout, [new LabelItem { Fields = new() { ["C0"] = "X2" } }], true);
        var stripPrint = new PrintSettings { Copies = 2, Temperature = 19 };
        static GodexRaster RenderStripSample(LabelLayout page, PrintSettings settings, int dpi)
        {
            var column = page.Objects[0].Content.Text[^1] - '1' + (page.ActiveNotch is null ? 0 : 2);
            return GodexRaster.Create(591, 118, (x, y) => y == 0 && x == column);
        }
        var strip = GodexPrintJob.EncodeContinuousBlock(terminalPrintPages, stripPrint, RenderStripSample, 0, 4,
            CancellationToken.None);
        var stripHeader = "^Q40,0,0\r\n^W50\r\n^P1\r\n^C1\r\n^R0\r\n^H19\r\n^L\r\nQ0,0,74,472\r"u8;
        Check("print: один растр сохраняет записи на всех секциях между копиями",
            strip.AsSpan().StartsWith(stripHeader) && strip.AsSpan().EndsWith("E\r\n"u8) &&
            strip.Length == stripHeader.Length + 74 * 472 + 3 &&
            strip[stripHeader.Length] == 0x80 && strip[stripHeader.Length + 118 * 74] == 0x10 &&
            strip[stripHeader.Length + 236 * 74] == 0x80 && strip[stripHeader.Length + 354 * 74] == 0x10);
        var notchFirst = GodexPrintJob.EncodeContinuousBlock(terminalPrintPages, stripPrint, RenderStripSample, 0, 4,
            CancellationToken.None, startOnNotchedSection: true);
        Check("print: старт с выреза меняет чередование без пропуска первой записи",
            notchFirst.AsSpan().StartsWith(stripHeader) && notchFirst.Length == strip.Length &&
            notchFirst[stripHeader.Length] == 0x20 && notchFirst[stripHeader.Length + 118 * 74] == 0x40);
        try
        {
            WstxIo.Write("raster formatting", fittedLayout, wstxPath);
            var (_, restored) = WstxIo.Read(wstxPath);
            var formatting = restored.Objects[0].Content;
            Check("wstx: подгонка, инверсия и шрифт по умолчанию",
                formatting.AutoFitText && formatting.InvertColors && formatting.FontName is null && formatting.FontSizePt == 16);
        }
        finally { File.Delete(wstxPath); }

        // ---- 10. Раскладка редактора (headless: форму не показываем) ----
        // Защита от регрессии: редактор обязан занимать вкладку целиком,
        // иначе холст сжимается до нуля (Dock.Fill теряется).
        List<Control> AllChildren(Control root)
        {
            var result = new List<Control>();
            void Walk(Control c)
            {
                foreach (Control child in c.Controls)
                {
                    result.Add(child);
                    Walk(child);
                }
            }
            Walk(root);
            return result;
        }

        // (а) Точная проверка регрессии: MainForm обязан повесить редактор на
        //     всю вкладку (Dock.Fill). Без него редактор — блок 200×100 в углу,
        //     холст сжимается до нуля. (Сам TabControl headless не проверяем:
        //     страница вкладки получает прямоугольник только при показе окна.)
        var editorField = typeof(MainForm).GetField("_editor",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        using (var mainForm = new MainForm())
        {
            var mainEditor = editorField!.GetValue(mainForm) as LabelEditorControl;
            Check("editor: в MainForm выставлен Dock.Fill", mainEditor is { Dock: DockStyle.Fill });
        }

        // (б) Поведение: редактор с Dock.Fill растягивается на контейнер,
        //     холст получает рабочую область, инструменты и поля на месте.
        using (var host = new Panel { Size = new Size(1250, 650) })
        {
            var editor = new LabelEditorControl { Dock = DockStyle.Fill };
            host.Controls.Add(editor);
            host.PerformLayout();
            editor.PerformLayout();

            var all = AllChildren(editor);
            var canvas = all.OfType<CanvasPanel>().FirstOrDefault();
            var toolButtons = all.OfType<Button>().Where(b => b.Tag is EditorTool).ToList();

            Check("editor: занимает контейнер целиком", editor.Width > 1200 && editor.Height > 500);
            Check("editor: холст — рабочая область", canvas is { Width: > 600, Height: > 300 });
            Check("editor: 7 кнопок инструментов", toolButtons.Count == 7);
            Check("editor: кнопки не обрезаны", toolButtons.All(b => b.Parent is { } p && b.Bottom <= p.Height));
            // Геометрия, размер шрифта и четыре координаты свободного объекта.
            Check("editor: 13 числовых полей", all.OfType<NumericUpDown>().Count() == 13);
        }

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? "ВСЕ ПРОВЕРКИ ПРОШЛИ"
            : $"ПРОВАЛОВ: {failed}");
        return failed == 0 ? 0 : 1;
    }
}
