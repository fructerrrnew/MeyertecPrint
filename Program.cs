namespace MeyertecPrint;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Самопроверка без интерфейса: dotnet run -- --selftest
        if (args.Length > 0 && args[0] == "--selftest")
            Environment.Exit(SelfTest.Run());

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            AppLog.Write(e.Exception, "UI exception");
            BrandMessageBox.Show(null, string.Format(L10n.T("Error.Unexpected"), AppLog.PathName), "MEYERTEC PRINT",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) AppLog.Write(ex, "unhandled exception");
        };

        // Языковые файлы лежат в папке Languages рядом с exe
        L10n.Initialize(Path.Combine(AppContext.BaseDirectory, "Languages"));

        // Плагины принтеров (модель + параметры) лежат в папке Plugins рядом с exe
        PrinterPlugins.Initialize(Path.Combine(AppContext.BaseDirectory, "Plugins"));

        // Язык по умолчанию — русский
        L10n.Load(L10n.PreferredLanguage());

        Application.Run(new MainForm());
    }
}
