using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace clib.Extensions;

public static partial class StringExtensions {
    extension(string s) {
        public bool ContainsIgnoreCase(string needle)
            => s.Contains(needle, StringComparison.OrdinalIgnoreCase);
        public bool IsEmpty => string.IsNullOrEmpty(s);
        public bool EqualsIgnoreCase(string other) => string.Equals(s, other, StringComparison.OrdinalIgnoreCase);

        public bool TryParseVector3(out Vector3 output) {
            output = Vector3.Zero;
            if (ParseVector3().Match(s) is { Success: true } match) {
                var x = float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var y = float.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                var z = float.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture);
                output = new Vector3(x, y, z);
                return true;
            }
            return false;
        }

        public string ToBase64() {
            var jsonBytes = Encoding.UTF8.GetBytes(s);
            using var output = new MemoryStream();
            using (var brotli = new BrotliStream(output, CompressionLevel.Optimal))
                brotli.Write(jsonBytes, 0, jsonBytes.Length);
            return Convert.ToBase64String(output.ToArray());
        }

        public string FromBase64() {
            var compressedBytes = Convert.FromBase64String(s);
            using var input = new MemoryStream(compressedBytes);
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            brotli.CopyTo(output);
            return Encoding.UTF8.GetString(output.ToArray());
        }

        public int FromRomanNumeral() {
            static int ToNum(char c) => c switch {
                'I' => 1,
                'V' => 5,
                'X' => 10,
                'L' => 50,
                'C' => 100,
                'D' => 500,
                'M' => 1000,
                _ => 0
            };
            return s.Select((c, i) => {
                var cur = ToNum(c);
                return i + 1 < s.Length && cur < ToNum(s[i + 1]) ? -cur : cur;
            }).Sum();
        }

        public string ToTitleCase() => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLower());
        public string GetLast(int tail_length) => tail_length >= s.Length ? s : s[^tail_length..];
        public string SplitWords() => SplitWords().Replace(s, " ").Trim();
        public string FilterNonAlphanumeric() => FilterNonAlphanumeric().Replace(s, string.Empty);

        public string EnsureIsCommand() => s.StartsWith('/') ? s : $"/{s}";
    }

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    // smart word split for things in pascal case while also handling acronyms/initialisms
    private static partial Regex SplitWords();

    [GeneratedRegex("[^\\p{L}\\p{N}]")]
    private static partial Regex FilterNonAlphanumeric();

    [GeneratedRegex(@"(-?\d+(\.\d+)?),(-?\d+(\.\d+)?),(-?\d+(\.\d+)?)")]
    private static partial Regex ParseVector3();
}

