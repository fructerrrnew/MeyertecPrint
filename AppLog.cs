namespace MeyertecPrint;

public static class AppLog
{
    public static string PathName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeyertecPrint", "errors.log");
    public static void Write(Exception ex, string context)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
            File.AppendAllText(PathName, $"[{DateTimeOffset.Now:O}] {context}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
