namespace TwelfthVoice.Simulator;

/// <summary>Seeded random helper so every match is reproducible from its seed.</summary>
internal sealed class Rng
{
    private readonly Random _random;

    public Rng(int seed) => _random = new Random(seed);

    public double Next() => _random.NextDouble();

    public double Range(double min, double max) => min + _random.NextDouble() * (max - min);

    public int Int(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);

    public bool Chance(double probability) => _random.NextDouble() < probability;

    /// <summary>Normally distributed value (Box-Muller).</summary>
    public double Normal(double mean, double standardDeviation)
    {
        double u1 = 1.0 - _random.NextDouble();
        double u2 = _random.NextDouble();
        double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return mean + standardDeviation * z;
    }

    public T Pick<T>(IReadOnlyList<T> items) => items[_random.Next(items.Count)];

    public T PickWeighted<T>(IReadOnlyList<T> items, Func<T, double> weight)
    {
        var weights = new double[items.Count];
        double total = 0;
        for (int i = 0; i < items.Count; i++)
        {
            weights[i] = Math.Max(0, weight(items[i]));
            total += weights[i];
        }

        if (total <= 0) return Pick(items);

        double roll = _random.NextDouble() * total;
        for (int i = 0; i < items.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0) return items[i];
        }

        return items[^1];
    }
}

/// <summary>FNV-1a hash: unlike string.GetHashCode it is stable across processes.</summary>
internal static class StableHash
{
    public static int Of(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in value)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return (int)(hash & 0x7FFFFFFF);
        }
    }
}
