using ZXing;
using ZXing.QrCode.Internal;

namespace MeyertecPrint;

/// <summary>QR Code Model 2, UTF-8, уровень коррекции L.</summary>
public static class QrEncoder
{
    /// <summary>Возвращает матрицу без полей; true соответствует тёмному модулю.</summary>
    public static bool[,] Encode(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.CHARACTER_SET] = "UTF-8"
        };
        var code = Encoder.encode(data, ErrorCorrectionLevel.L, hints);
        var matrix = code.Matrix;
        var result = new bool[matrix.Height, matrix.Width];
        for (var row = 0; row < matrix.Height; row++)
            for (var column = 0; column < matrix.Width; column++)
                result[row, column] = matrix[column, row] == 1;
        return result;
    }

    // Сохранено публичное API старого кодировщика для существующих вызовов.
    public static class ReedSolomon
    {
        private static readonly int[] Exp = new int[512];
        private static readonly int[] Log = new int[256];

        static ReedSolomon()
        {
            var x = 1;
            for (var i = 0; i < 255; i++)
            {
                Exp[i] = x;
                Log[x] = i;
                x <<= 1;
                if ((x & 0x100) != 0) x ^= 0x11D;
            }
            for (var i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
        }

        private static int Mul(int a, int b)
            => a == 0 || b == 0 ? 0 : Exp[Log[a] + Log[b]];

        private static int[] Generator(int degree)
        {
            var result = new[] { 1 };
            for (var i = 0; i < degree; i++)
            {
                var next = new int[result.Length + 1];
                for (var j = 0; j < result.Length; j++)
                {
                    next[j] ^= result[j];
                    next[j + 1] ^= Mul(result[j], Exp[i]);
                }
                result = next;
            }
            return result;
        }

        public static byte[] Compute(byte[] data, int ecCount)
        {
            var gen = Generator(ecCount);
            var buffer = new int[data.Length + ecCount];
            Array.Copy(data, buffer, data.Length);
            for (var i = 0; i < data.Length; i++)
            {
                var factor = buffer[i];
                if (factor == 0) continue;
                for (var j = 0; j < gen.Length; j++)
                    buffer[i + j] ^= Mul(gen[j], factor);
            }
            return buffer[data.Length..].Select(v => (byte)v).ToArray();
        }
    }
}
