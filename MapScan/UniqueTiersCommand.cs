namespace Poe2Map;

/// <summary>
///     MapScan uniquetiers &lt;filtre | -&gt; [cikti = data\unique-tiers.txt]: Uniques sekmesinin listesini uretir
///     (UniqueTierBuilder; "-" = filtresiz, gruplar esya sinifina gore). Ayar penceresindeki "Build list" dugmesiyle ayni.
/// </summary>
internal static class UniqueTiersCommand
{
    internal static int Run(string[] args)
    {
        if (args.Length < 1) { Console.WriteLine("kullanim: uniquetiers <filtre | -> [cikti]"); return 2; }

        var output = args.Length > 1 ? args[1] : Path.Combine("data", "unique-tiers.txt");
        foreach (var line in UniqueTierBuilder.Build(args[0] == "-" ? null : args[0], output)) { Console.WriteLine(line); }

        Console.WriteLine($"yazildi: {output}");
        return 0;
    }
}
