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

    var projectPath = Path.Combine(tmp, "project.mprj"); ProjectFileIo.Save(project, projectPath);
    Check("Project round-trip retains batch", ProjectFileIo.Load(projectPath).Ribbon.Labels.Count == 1);
    var modified = project.Modified; var version = project.Version;
    ProjectFileIo.SaveRecovery(project, Path.Combine(tmp, "recovery.mprj"));
    Check("Recovery snapshot does not mutate project metadata", project.Modified == modified && project.Version == version);
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

namespace MeyertecPrint
{
    public static class L10n { public static string T(string key) => key; }
}
