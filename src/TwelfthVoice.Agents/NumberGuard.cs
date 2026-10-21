using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TwelfthVoice.Agents;

public sealed record FactCheckResult(bool Passed, IReadOnlyList<string> Issues)
{
    public static FactCheckResult Ok { get; } = new(true, Array.Empty<string>());
}

/// <summary>
/// The deterministic half of the fact-checker: every number in a generated line must be
/// traceable to the facts it was written from. Cheap, instant and impossible to talk round,
/// so it runs on every line; the AI fact-checker adds judgement on top.
/// </summary>
public static partial class NumberGuard
{
    /// <summary>Numbers a writer may use without them being in the facts ("one-on-one", "the 90 minutes", "11 players").</summary>
    private static readonly double[] AlwaysAllowed = { 0, 1, 2, 3, 11, 45, 90 };

    [GeneratedRegex(@"(?<![\w.])\d+(?:[.,]\d+)?")]
    private static partial Regex NumberPattern();

    /// <summary>Pulls every number out of text, including non-Western digits and decimal commas.</summary>
    public static IReadOnlyList<double> Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<double>();

        var normalized = Normalize(text);
        var numbers = new List<double>();
        foreach (Match match in NumberPattern().Matches(normalized))
        {
            string token = match.Value;
            int comma = token.IndexOf(',');
            if (comma >= 0)
            {
                string integerPart = token[..comma];
                string fraction = token[(comma + 1)..];
                // "1,000" is a thousands separator; "0,31" or "12,5" is a decimal comma.
                token = fraction.Length == 3 && integerPart != "0" ? integerPart + fraction : integerPart + "." + fraction;
            }

            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                numbers.Add(value);
        }
        return numbers;
    }

    /// <summary>
    /// Expands raw fact values into every form a writer might reasonably print: rounded,
    /// as a percentage, metres as kilometres.
    /// </summary>
    public static HashSet<double> AllowedForms(IEnumerable<double> facts)
    {
        var allowed = new HashSet<double>(AlwaysAllowed);
        foreach (double v in facts)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) continue;
            double a = Math.Abs(v);
            AddRounded(allowed, a);
            if (a > 0 && a <= 1) AddRounded(allowed, a * 100);
            if (a >= 1000) AddRounded(allowed, a / 1000);
        }
        return allowed;
    }

    public static FactCheckResult Check(string? text, IReadOnlySet<double> allowed)
    {
        var unsupported = Extract(text)
            .Where(n => !IsAllowed(n, allowed))
            .Distinct()
            .ToList();

        return unsupported.Count == 0
            ? FactCheckResult.Ok
            : new FactCheckResult(false, unsupported.Select(n => $"The number {Format(n)} isn't in the facts.").ToList());
    }

    /// <summary>A translation must keep the numbers of the original (it may not drop, add or change them).</summary>
    public static FactCheckResult CheckPreserved(string? original, string? translation)
    {
        var expected = Extract(original).Select(Key).OrderBy(x => x).ToList();
        var actual = Extract(translation).Select(Key).OrderBy(x => x).ToList();
        if (expected.SequenceEqual(actual)) return FactCheckResult.Ok;

        var missing = expected.Except(actual).ToList();
        var added = actual.Except(expected).ToList();
        var issues = new List<string>();
        if (missing.Count > 0) issues.Add($"Translation dropped or changed: {string.Join(", ", missing.Select(Format))}.");
        if (added.Count > 0) issues.Add($"Translation introduced: {string.Join(", ", added.Select(Format))}.");
        if (issues.Count == 0) issues.Add("Translation changed how often a number appears.");
        return new FactCheckResult(false, issues);
    }

    private static bool IsAllowed(double n, IReadOnlySet<double> allowed)
    {
        foreach (double a in allowed)
        {
            if (Math.Abs(n - a) <= 0.0051) return true;
        }
        return false;
    }

    private static void AddRounded(HashSet<double> set, double value)
    {
        set.Add(value);
        set.Add(Math.Round(value, 0, MidpointRounding.AwayFromZero));
        set.Add(Math.Floor(value));
        set.Add(Math.Round(value, 1, MidpointRounding.AwayFromZero));
        set.Add(Math.Round(value, 2, MidpointRounding.AwayFromZero));
    }

    private static double Key(double n) => Math.Round(n, 2);

    private static string Format(double n) => n.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Maps any Unicode decimal digit (Arabic-Indic, Devanagari, full-width…) to 0-9.</summary>
    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (char.IsDigit(c) && (c < '0' || c > '9'))
            {
                double value = char.GetNumericValue(c);
                builder.Append(value >= 0 ? (char)('0' + (int)value) : c);
            }
            else if (c == '٫')
            {
                builder.Append('.'); // Arabic decimal separator
            }
            else
            {
                builder.Append(c);
            }
        }
        return builder.ToString();
    }
}
