using System.IO.Compression;
using System.Text;
using MeyertecPrint;

var failures = 0;
void Check(string name, bool condition)
{
    Console.WriteLine((condition ? "PASS " : "FAIL ") + name);
    if (!condition) failures++;
}
bool Rejects(Action action)
{
    try { action(); return false; }
    catch (Exception ex) when (ex is FormatException or InvalidDataException) { return true; }
}
LabelLayout MakeLayout() => new()
{
    Geometry = new LabelGeometry { LabelsInRow = 1, WidthUm = 30000, HeightUm = 10000 },
    MediaWidthUm = 60000, MediaHeightUm = 30000,
    Rows = { new LabelRow { HeightUm = 10000, Cells = {
        new LabelCell { WidthUm = 10000, Content = new CellContent { Type = CellContentType.Text, Text = "one" } },
        new LabelCell { WidthUm = 10000, Content = new CellContent { Type = CellContentType.Text, Text = "two" } },
        new LabelCell { WidthUm = 10000, Content = new CellContent { Type = CellContentType.Text, Text = "three" } }
    } } }
};
var csv = CsvReader.Parse("number;name\n\"A;1\";\"line1\nline2\"\n\"A\"\"2\";end\n");
Check("CSV quoted delimiter, newline and escaped quote", csv.Rows.Count == 3 && csv.Rows[1][0] == "A;1" && csv.Rows[1][1] == "line1\nline2" && csv.Rows[2][0] == "A\"2");
Check("CSV rejects unclosed quote", Rejects(() => CsvReader.Parse("a;b\n\"bad;x")));
Check("CSV rejects quote in an unquoted value", Rejects(() => CsvReader.Parse("a;b\nhe\"llo;x")));
Check("CSV rejects text after closing quote", Rejects(() => CsvReader.Parse("a;b\n\"x\"oops;y")));
Check("CSV handles tab separator and empty final field", CsvReader.Parse("a\tb\n1\t").Rows[1].SequenceEqual(new[] { "1", "" }));
var tmp = Path.Combine(Path.GetTempPath(), "meyertec-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(tmp);
try
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    var cp = Path.Combine(tmp, "legacy.csv"); File.WriteAllBytes(cp, Encoding.GetEncoding(1251).GetBytes("номер;кабель"));
    Check("CSV auto-detects Windows-1251", CsvReader.ReadFile(cp, "auto") == "номер;кабель");
    File.WriteAllText(cp, "номер;кабель", new UTF8Encoding(true));
    Check("CSV handles UTF-8 BOM", CsvReader.ReadFile(cp, "auto") == "номер;кабель");

    var layout = MakeLayout();
    layout.Geometry.WidthUm = 45000;
    GeometryOperations.Resize(layout, false);
    Check("Geometry preserves split cells and content", layout.Rows[0].Cells.Count == 3 && layout.Rows[0].Cells.Select(c => c.Content.Text).SequenceEqual(new[] { "one", "two", "three" }));
    Check("Geometry distributes exact new width", layout.Rows[0].Cells.Sum(c => c.WidthUm) == 45000 && layout.Rows[0].Cells.All(c => c.WidthUm == 15000));
    var snapshot = layout.Clone(); var undo = new UndoRedo(); undo.Push(snapshot);
    layout.Geometry.WidthUm = 30000; GeometryOperations.Resize(layout, false);
    undo.Undo(ref layout);
    Check("Geometry undo restores all cells", layout.Rows[0].Cells.Count == 3 && layout.Rows[0].Cells.Sum(c => c.WidthUm) == 45000);

    var rows = Enumerable.Range(1, 100).Select(i => new List<string> { "A" + i, "B" + i }).ToList();
    var mapping = new Dictionary<int, int> { [0] = 1, [1] = 0 };
    var batch = CsvBatch.Prepare(layout, rows, mapping);
    Check("CSV imports all 100 rows", batch.Remaining.Count == 99);
    Check("CSV respects explicit column mapping", batch.FirstFields["C0"] == "B1" && batch.FirstFields["C1"] == "A1");
    LabelPrintPages.ApplyFields(layout, batch.FirstFields);
    var pages = LabelPrintPages.Create(layout, batch.Remaining, true);
    Check("Print includes exactly all 100 imported rows", pages.Count == 100 && pages[99].Rows[0].Cells[0].Content.Text == "B100");
    var second = CsvBatch.Prepare(layout, rows.Take(2).ToList(), mapping);
    var project = new ProjectFile { Name = "test", Ribbon = new RibbonData { Layout = layout, Labels = batch.Remaining } };
    LabelPrintPages.ApplyFields(layout, second.FirstFields); project.Ribbon.Labels = second.Remaining;
    Check("Reimport replaces old rows and fields", project.Ribbon.Labels.Count == 1 && project.Ribbon.Labels[0].Fields.Count == 2 && LabelPrintPages.Create(layout, project.Ribbon.Labels, true).Count == 2);
    Check("A failed batch leaves project unchanged", Rejects(() => CsvBatch.Prepare(layout, new List<List<string>> { new() { "only one" } }, mapping)) && project.Ribbon.Labels.Count == 1);
    var image = MakeLayout(); image.Rows[0].Cells[1].Content = new CellContent { Type = CellContentType.Image, ImageBase64 = "unchanged" };
    var imageBatch = CsvBatch.Prepare(image, rows.Take(1).ToList(), new Dictionary<int, int> { [0] = 1, [2] = 0 });
    LabelPrintPages.ApplyFields(image, imageBatch.FirstFields);
    Check("Constant image survives CSV substitution", image.Rows[0].Cells[1].Content.ImageBase64 == "unchanged");
    Check("Direct image binding fails before import", Rejects(() => CsvBatch.Prepare(image, rows.Take(1).ToList(), new Dictionary<int, int> { [1] = 0 })));
    var many = Enumerable.Range(0, 80).Select(i => "v" + i).ToList();
    var manyBatch = CsvBatch.Prepare(layout, new List<List<string>> { many }, new Dictionary<int, int> { [0] = 79 });
    Check("CSV does not truncate columns at 64", manyBatch.FirstFields["C0"] == "v79");

    var workbook = Path.Combine(tmp, "data.xlsx");
    WriteWorkbook(workbook);
    Check("Excel lists workbook sheets", SpreadsheetReader.GetWorksheetNames(workbook).SequenceEqual(["Codes", "Backup"]));
    var excel = SpreadsheetReader.ReadFile(workbook, worksheet: "Codes");
    Check("Excel keeps leading zeros and named columns", excel[1].SequenceEqual(["0007", "", "7"]) &&
        excel[2].SequenceEqual(["008", "", ""]));
    Check("Excel selects the requested sheet", SpreadsheetReader.ReadFile(workbook, worksheet: "Backup")
        .Single().SequenceEqual(["Клемма"]));
    Check("Excel reports a missing worksheet", Rejects(() => SpreadsheetReader.ReadFile(workbook, worksheet: "Missing")));
    var excelBatch = CsvBatch.Prepare(MakeLayout(), excel.Skip(1).ToList(), new Dictionary<int, int> { [0] = 0 });
    Check("One Excel column yields one record per data row", excelBatch.FirstFields["C0"] == "0007" &&
        excelBatch.Remaining.Single().Fields["C0"] == "008");
    var numberingLayout = MakeLayout();
    var numbering = LabelNumbering.Prepare(numberingLayout, 0, 3, 9, -2, 3, "XT-", "-G");
    Check("Numbering formats all values without changing the template", numberingLayout.Rows[0].Cells[0].Content.Text == "one" &&
        numbering.FirstFields["C0"] == "XT-009-G" &&
        numbering.Remaining.Select(record => record.Fields["C0"]).SequenceEqual(["XT-007-G", "XT-005-G"]));
    Check("Numbering rejects overflow before replacing a batch", Rejects(() =>
        LabelNumbering.Prepare(numberingLayout, 0, 2, long.MaxValue, 1, 1)));
    Check("Sequence length counts feed gaps only between labels",
        LabelSequence.CalculateLengthUm(30_000, 3, 2_000) == 94_000 &&
        LabelSequence.CalculateLengthUm(30_000, 1, 2_000) == 30_000 &&
        LabelSequence.CalculateLengthUm(30_000, 0, 2_000) == 0);

    var projectPath = Path.Combine(tmp, "project.mprj"); ProjectFileIo.Save(project, projectPath);
    Check("Project round-trip retains batch", ProjectFileIo.Load(projectPath).Ribbon.Labels.Count == 1);
    var modified = project.Modified; var version = project.Version;
    ProjectFileIo.SaveRecovery(project, Path.Combine(tmp, "recovery.mprj"));
    Check("Recovery snapshot does not mutate project metadata", project.Modified == modified && project.Version == version);
    var manual = new ProjectFile
    {
        Name = "Manual and imported",
        Ribbon = new RibbonData
        {
            Layout = MakeLayout(),
            Labels = [
                new LabelItem { Name = "Manual copy", UseLayout = true },
                new LabelItem { Name = "Variant", Fields = new() { ["C0"] = "variant" } }
            ]
        }
    };
    var manualPath = Path.Combine(tmp, "manual.mprj");
    ProjectFileIo.Save(manual, manualPath);
    var restored = ProjectFileIo.Load(manualPath);
    var restoredPages = LabelPrintPages.Create(restored.Ribbon.GetLayout(), restored.Ribbon.Labels, true);
    Check("Version 4 round-trips manual and mapped records", restored.Version == 4 && restored.Ribbon.Labels[0].UseLayout &&
        restoredPages.Count == 3 && restoredPages[1].Rows[0].Cells[0].Content.Text == "one" &&
        restoredPages[2].Rows[0].Cells[0].Content.Text == "variant");
    var compatible = new ProjectFile { Print = new PrintSettings { Copies = 2 }, Ribbon = new RibbonData { Layout = MakeLayout() } };
    var compatiblePath = Path.Combine(tmp, "compatible.mprj");
    ProjectFileIo.Save(compatible, compatiblePath);
    Check("Driver-only projects retain version 3 compatibility", ProjectFileIo.Load(compatiblePath).Version == 3);

    manual.Print.Temperature = 8;
    manual.Print.NetworkHost = "192.0.2.1";
    var template = LabelTemplate.FromProject(manual, "Маркировка", "Полиэстер");
    var templatePath = Path.Combine(tmp, "editable" + LabelTemplate.FileExtension);
    TemplateLibrary.SaveFile(template, templatePath);
    var loadedTemplate = TemplateLibrary.LoadFile(templatePath);
    var templateProject = loadedTemplate.CreateProject("from template");
    Check("Template JSON preserves layout and portable print settings", File.ReadAllText(templatePath).Contains("Полиэстер") &&
        templateProject.TemplateVersion == 1 && templateProject.Print.Temperature == 8 &&
        templateProject.Print.NetworkHost is null && templateProject.Ribbon.GetLayout().Rows[0].Cells[0].Content.Text == "one");
    var updatedTemplate = LabelTemplate.FromProject(manual, "Updated", "Полиэстер", loadedTemplate);
    var library = new TemplateLibrary(Path.Combine(tmp, "installed"), Path.Combine(tmp, "built-in"));
    library.Import(templatePath);
    Check("Template library upgrades versions and rejects rollback",
        updatedTemplate.Id == loadedTemplate.Id && updatedTemplate.Version == 2 &&
        library.Install(updatedTemplate) && !library.Install(loadedTemplate) && library.GetAll().Single().Name == "Updated");

    var xml = Path.Combine(tmp, "project.wstx"); WstxIo.Write("test", layout, xml);
    Check("WSTX internal round-trip reads content", WstxIo.Read(xml).Layout.Rows[0].Cells[0].Content.Text == layout.Rows[0].Cells[0].Content.Text);
    var text = File.ReadAllText(xml).Replace("</StripBlocks>", "<StripBlock /></StripBlocks>"); File.WriteAllText(xml, text);
    Check("WSTX rejects multiple blocks instead of dropping data", Rejects(() => WstxIo.Read(xml)));
    File.WriteAllText(xml, "<!DOCTYPE LabelStrip [<!ENTITY xxe SYSTEM 'file:///etc/passwd'>]><LabelStrip>&xxe;</LabelStrip>");
    Check("WSTX rejects DTD and external entities", Rejects(() => WstxIo.Read(xml)));
    Check("Print width rejects content wider than head", Rejects(() => LabelPrintLayout.EnsurePrintWidth(layout, 10)));
}
finally { Directory.Delete(tmp, true); }
Console.WriteLine($"Failures: {failures}");
return failures == 0 ? 0 : 1;

static void WriteWorkbook(string path)
{
    using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
    void Add(string name, string xml)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(xml);
    }
    Add("[Content_Types].xml", """
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
        </Types>
        """);
    Add("_rels/.rels", """
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """);
    Add("xl/workbook.xml", """
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets><sheet name="Codes" sheetId="1" r:id="rId1"/><sheet name="Backup" sheetId="2" r:id="rId2"/></sheets>
        </workbook>
        """);
    Add("xl/_rels/workbook.xml.rels", """
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/>
          <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        </Relationships>
        """);
    Add("xl/styles.xml", """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <numFmts count="1"><numFmt numFmtId="164" formatCode="0000"/></numFmts>
          <cellXfs count="2"><xf numFmtId="0" xfId="0"/><xf numFmtId="164" xfId="0" applyNumberFormat="1"/></cellXfs>
        </styleSheet>
        """);
    Add("xl/worksheets/sheet1.xml", """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
          <row r="1"><c r="A1" t="inlineStr"><is><t>Serial</t></is></c></row>
          <row r="2"><c r="A2" s="1"><v>7</v></c><c r="C2"><f>3+4</f><v>7</v></c></row>
          <row r="4"><c r="A4" t="inlineStr"><is><t>008</t></is></c></row>
        </sheetData></worksheet>
        """);
    Add("xl/worksheets/sheet2.xml", """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
          <row r="1"><c r="A1" t="inlineStr"><is><t>Клемма</t></is></c></row>
        </sheetData></worksheet>
        """);
}
