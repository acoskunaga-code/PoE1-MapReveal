namespace Poe2Map;

/// <summary>
///     Canavar bilesenlerinin PoE1 alanlarini CANLI verinin kendisinden ayirir.
///
///     Can: Life bileseninde iki aday cift (toplam, simdiki) var (monsters komutu: 0x1A4 ve 0x1F4, ikisi de tutuyor).
///     Hangisinin SAGLIK, hangisinin mana oldugunu OLU canavar ayirir: saglik 0'a iner, mana inmez. Haritada
///     olu canavar yoksa saglik/mana ayrilamaz; bu durumda cikti "olu yok" der.
///
///     Nadirlik: ObjectMagicProperties bileseninin her 4 baytlik alani icin tum canavarlardaki degerlerin kumesi
///     cikarilir; hepsi 0..3 olan ve en az iki farkli deger gorulenler aday. Uyanik + uyuyan varliklar birlikte
///     taranir (uyuyanlar cok daha fazla ve daha cesitli).
///
///         vitals
/// </summary>
internal static class VitalProbe
{
    private const int LifeWindow = 0x240;
    private const int MagicWindow = 0x100;

    internal static int Run()
    {
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var terrains = TerrainFinder.Find(h);
            if (LocalPlayer.Locate(h, terrains) is not { } live) { Console.WriteLine("canli alan bulunamadi"); return 1; }

            var area = live.Area.StructAddress - GameLayout.TerrainInArea;
            var life = new List<(string Path, bool Awake, byte[] Raw)>();
            var magic = new List<byte[]>();
            foreach (var (name, offset) in new[] { ("uyanik", EntityReader.AwakeMap), ("uyuyan", EntityReader.SleepingMap) })
            {
                if (EntityReader.ReadMapHeader(h, area + offset) is not { } map) { continue; }

                var seen = 0;
                foreach (var (_, entity) in EntityReader.WalkMap(h, map))
                {
                    if (EntityReader.ReadInfo(h, entity) is not { IsMonster: true } info) { continue; }
                    if (!info.Components.ContainsKey("Monster")) { continue; }

                    seen++;
                    if (info.Components.TryGetValue("Life", out var l))
                    {
                        var raw = new byte[LifeWindow];
                        if (EntityReader.Read(h, l, raw)) { life.Add((info.Path, name == "uyanik", raw)); }
                    }

                    if (info.Components.TryGetValue("ObjectMagicProperties", out var m))
                    {
                        var raw = new byte[MagicWindow];
                        if (EntityReader.Read(h, m, raw)) { magic.Add(raw); }
                    }
                }

                Console.WriteLine($"{name} listede canavar {seen}");
            }

            Console.WriteLine($"Life bileseni {life.Count}, ObjectMagicProperties {magic.Count}\n");
            PrintVitals(life);
            PrintRarity(magic);
            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    private static void PrintVitals(List<(string Path, bool Awake, byte[] Raw)> life)
    {
        // Aday ciftler: toplam @T, simdiki @T+4. Mana/saglik iki aday; arada energy shield (toplam 0) olmali.
        int[] candidates = { 0x1A4, 0x1CC, 0x1F4 };
        Console.WriteLine("Life: aday (toplam,simdiki) ciftleri 0x1A4 / 0x1CC (enerji kalkani?) / 0x1F4");
        var dead = 0;
        foreach (var (path, awake, raw) in life.Take(400))
        {
            var cells = candidates.Select(t => $"{BitConverter.ToInt32(raw, t)}/{BitConverter.ToInt32(raw, t + 4)}").ToArray();
            var isDead = BitConverter.ToInt32(raw, 0x1F4) > 0 && BitConverter.ToInt32(raw, 0x1F8) <= 0;
            var alsoDeadAtMana = BitConverter.ToInt32(raw, 0x1A4) > 0 && BitConverter.ToInt32(raw, 0x1A8) <= 0;
            if (isDead || alsoDeadAtMana) { dead++; }

            if (awake && (isDead || alsoDeadAtMana || life.Count <= 20))
            {
                Console.WriteLine($"  {(isDead ? "OLU(1F4) " : alsoDeadAtMana ? "OLU(1A4) " : "         ")}" +
                                  $"{string.Join("   ", cells)}   {Shorten(path)}");
            }
        }

        Console.WriteLine(dead == 0
            ? "  olu canavar yok: saglik / mana ayrimi bu olcumden cikmadi\n"
            : $"  olu gorunen canavar: {dead}\n");
    }

    private static void PrintRarity(List<byte[]> magic)
    {
        Console.WriteLine("ObjectMagicProperties: butun degerleri 0..3 olan ve en az iki farkli deger gorulen alanlar");
        for (var o = 0x10; o + 4 <= MagicWindow; o += 4)
        {
            var values = magic.Select(r => BitConverter.ToInt32(r, o)).ToList();
            if (values.Any(v => v is < 0 or > 3)) { continue; }

            var groups = values.GroupBy(v => v).OrderBy(g => g.Key).ToList();
            if (groups.Count < 2) { continue; }

            Console.WriteLine($"  +0x{o:X}   " + string.Join("  ", groups.Select(g => $"{g.Key}:{g.Count()}")));
        }
    }

    private static string Shorten(string path) => path.Length <= 60 ? path : "..." + path[^57..];
}
