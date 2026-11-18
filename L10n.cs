using System.Text.Json;

namespace MeyertecPrint;

/// <summary>
/// Подсистема локализации. Строки читаются из папки Languages
/// (один *.json на язык, плоская схема ключ→строка).
/// ВСЕ названия кнопок/пунктов меню берутся отсюда: L10n.T("...").
/// Чтобы добавить язык — положите новый файл (например, de.json)
/// в папку Languages, пункт появится в меню "Язык" автоматически.
/// </summary>
public static class L10n
{
    private static readonly Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);
    private static string _languageDirectory = string.Empty;
    private static string PreferencePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeyertecPrint", "language.txt");
    public static string PreferredLanguage()
    {
        try
        {
            var code = File.ReadAllText(PreferencePath).Trim();
            return code is "ru" or "en" or "sr" ? code : "ru";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "ru"; }
    }

    /// <summary>Текущий код языка (ru / en / sr / ...).</summary>
    public static string Current { get; private set; } = "ru";

    /// <summary>Событие смены языка — подписчики перечитывают названия.</summary>
    public static event Action? Changed;

    public static void Initialize(string languageDirectory)
    {
        _languageDirectory = languageDirectory;
        if (!Directory.Exists(_languageDirectory))
        {
            Directory.CreateDirectory(_languageDirectory);
        }
    }

    public static void Load(string code)
    {
        var path = Path.Combine(_languageDirectory, code + ".json");
        if (!File.Exists(path))
        {
            // Файл языка не найден: пробуем английский, а если и его нет —
            // работаем на пустой таблице (T() вернёт ключи, и в интерфейсе
            // будет видно, что папка Languages рядом с exe не скопирована)
            if (code != "en" && File.Exists(Path.Combine(_languageDirectory, "en.json")))
            {
                Load("en");
                return;
            }
            _strings.Clear();
            Current = code;
            Changed?.Invoke();
            return;
        }

        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                     ?? new Dictionary<string, string>();

        _strings.Clear();
        foreach (var (key, value) in parsed)
        {
            _strings[key] = value;
        }

        Current = code;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
            File.WriteAllText(PreferencePath, code);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Changed?.Invoke();
    }

    /// <summary>
    /// Возвращает строку по ключу. Если ключа нет — возвращает сам ключ,
    /// чтобы недочёты в лингвистических файлах были видны в интерфейсе.
    /// </summary>
    public static string T(string key)
        => _strings.TryGetValue(key, out var value) ? value : key;

    /// <summary>Доступные языки = имена *.json в папке Languages.</summary>
    public static string[] Available()
        => Directory.Exists(_languageDirectory)
            ? Directory.GetFiles(_languageDirectory, "*.json")
                       .Select(f => Path.GetFileNameWithoutExtension(f)!)
                       .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                       .ToArray()
            : Array.Empty<string>();
}
