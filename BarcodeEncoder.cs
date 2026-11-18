namespace MeyertecPrint;

/// <summary>
/// Генерация 1D-штрихкодов (предложение для новой реализации — не свойство
/// эталона WPF-редактора): Code 128 (подмножество B) и EAN-13.
/// Ширина — в модулях; рендер в пиксели — на стороне вызывающего.
/// </summary>
public static class BarcodeEncoder
{
    /// <summary>Паттерны Code 128: 6 элементов (штрих/промежуток), сумма 11; STOP (106) — 7 элементов, сумма 13.</summary>
    private static readonly string[] Code128Patterns =
    {
        "212222","222122","222221","121223","121322","131222","122213","122312",
        "132212","221213","221312","231212","112232","122132","122231","113222",
        "123122","123221","223211","221132","221231","213212","223112","312131",
        "311222","321122","321221","312212","322112","322211","212123","212321",
        "232121","111323","131123","131321","112313","132113","132311","211313",
        "231113","231311","112133","112331","132131","113123","113321","133121",
        "313121","211331","231131","213113","213311","213131","311123","311321",
        "331121","312113","312311","332111","314111","221411","431111","111224",
        "111422","121124","121421","141122","141221","112214","112412","122114",
        "122411","142112","142211","241211","221114","413111","241112","134111",
        "111242","121142","121241","114212","124112","124211","411212","421112",
        "421211","212141","214121","412121","111143","111341","131141","114113",
        "114311","411113","411311","113141","114131","311141","411131",
        "211412","211214","211232","2331112", // 103 START A, 104 START B, 105 START C, 106 STOP (7 элементов)
    };

    private const int StartB = 104;
    private const int Stop = 106;

    /// <summary>
    /// Code 128 (subset B): любые печатаемые ASCII-символы (32–126).
    /// Возвращает последовательность модулей: true = штрих.
    /// </summary>
    public static bool[] EncodeCode128(string data)
    {
        if (string.IsNullOrEmpty(data))
            throw new ArgumentException(L10n.T("Barcode.ErrorEmpty"));
        foreach (var ch in data)
        {
            if (ch < 32 || ch > 126)
                throw new ArgumentException(string.Format(L10n.T("Barcode.ErrorCharacter"), ch));
        }

        var values = new List<int> { StartB };
        foreach (var ch in data) values.Add(ch - 32);

        var sum = StartB;
        for (var i = 0; i < values.Count - 1; i++)
            sum += values[i + 1] * (i + 1);
        values.Add(sum % 103);
        values.Add(Stop);

        var modules = new List<bool>();
        foreach (var v in values)
        {
            var pattern = Code128Patterns[v];
            for (var i = 0; i < pattern.Length; i++)
            {
                var width = pattern[i] - '0';
                for (var w = 0; w < width; w++)
                    modules.Add(i % 2 == 0); // чётные индексы — штрих
            }
        }
        return modules.ToArray();
    }

    // ---------------- EAN-13 ----------------

    private static readonly string[] L =
    {
        "0001101","0011001","0010011","0111101","0100011","0110001","0101111",
        "0111011","0110111","0001011",
    };

    /// <summary>Чётность кодов левой половины по первой цифре.</summary>
    private static readonly string[] Parity =
    {
        "LLLLLL","LLGLGG","LLGGLG","LLGGGL","LGLLGG","LGGLLG",
        "LGGGLL","LGLGLG","LGLGGL","LGGLGL",
    };

    private static string G(string l)
    {
        // G-код получается обращением и инверсией L-кода.
        var chars = l.Reverse().ToArray();
        return new string(chars.Select(c => c == '0' ? '1' : '0').ToArray());
    }

    /// <summary>Проверяет контрольную цифру EAN-13. Валидный код → true.</summary>
    public static bool IsValidEan13(string data)
    {
        if (data is null || data.Length != 13) return false;
        foreach (var ch in data)
        {
            if (ch < '0' || ch > '9') return false;
        }

        var sum = 0;
        for (var i = 0; i < 12; i++)
            sum += (data[i] - '0') * (i % 2 == 0 ? 1 : 3);
        var check = (10 - sum % 10) % 10;
        return check == data[12] - '0';
    }

    /// <summary>
    /// EAN-13: 13 цифр с валидной контрольной. Возвращает модули
    /// (true = штрих): 101 + 42 (L/G по парности) + 01010 + 42 (R) + 101.
    /// </summary>
    public static bool[] EncodeEan13(string data)
    {
        if (!IsValidEan13(data))
            throw new ArgumentException(L10n.T("Barcode.ErrorEan"));

        var guardL = "101";
        var center = "01010";
        var guardR = "101";

        var firstDigit = data[0] - '0';
        var parity = Parity[firstDigit];

        var modules = new List<bool>();
        void Add(string s)
        {
            foreach (var c in s) modules.Add(c == '1');
        }

        Add(guardL);
        for (var i = 1; i <= 6; i++)
        {
            var digit = data[i] - '0';
            Add(parity[i - 1] == 'L' ? L[digit] : G(L[digit]));
        }
        Add(center);
        for (var i = 7; i <= 12; i++)
        {
            Add(new string(L[data[i] - '0'].Select(c => c == '0' ? '1' : '0').ToArray()));
        }
        Add(guardR);
        return modules.ToArray();
    }

    /// <summary>Кодирует по выбранному символу.</summary>
    public static bool[] Encode(BarcodeSymbol symbol, string data)
        => symbol == BarcodeSymbol.Code128
            ? EncodeCode128(data)
            : EncodeEan13(data);
}
