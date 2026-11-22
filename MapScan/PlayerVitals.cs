namespace Poe2Map;

/// <summary>
///     Oyuncunun can / mana / energy shield havuzlari (otomatik tus, AutoKeys.cs). Life bileseninde uc havuz yapisi
///     0x48 arayla: can +0x178, mana +0x1C8, ES +0x210; her birinde +0x2C toplam, +0x30 simdiki (int).
///
///     Canli olcum 20.11.2026 (patch sonrasi, `MapScan vitals` + `MapScan playervitals`):
///       - can +0x1A4/+0x1A8: olen canavarda simdiki 0'a iniyor (EntityReader.LifeTotal ile ayni alan);
///       - mana +0x1F4/+0x1F8: 561 canavarin hepsinde dolu ve seviyeye gore ayni (1298/1298, 992/992); oyuncuda beceri
///         kullanildikca 237..327 arasinda inip cikiyor (727 toplam, rezerv dusulmus);
///       - ES +0x23C/+0x240: canavarlarin cogunda 0, bazilarinda 12758 / 18011; oyuncuda (CI, can 1/1) 6686..6788 ve
///         vuruldukca dusuyor.
/// </summary>
internal static class PlayerVitals
{
    internal const ulong LifeTotal = 0x1A4;
    internal const ulong ManaTotal = 0x1F4;
    internal const ulong EsTotal = 0x23C;

    // Simdiki deger toplamin hemen ardinda (+4).
    private const int Span = (int)(EsTotal - LifeTotal) + 8;

    internal readonly record struct Pool(int Current, int Total)
    {
        /// <summary>Doluluk yuzdesi; toplam 0 ise (ES'siz karakter) null.</summary>
        internal int? Percent => this.Total > 0 ? (int)Math.Clamp(100L * this.Current / this.Total, 0, 100) : null;

        public override string ToString() => $"{this.Current}/{this.Total}";
    }

    internal readonly record struct Vitals(Pool Life, Pool Mana, Pool Es);

    /// <summary>Life bileseninden uc havuzu tek okumayla alir; okunamazsa ya da degerler anlamsizsa null.</summary>
    internal static Vitals? Read(IntPtr h, ulong lifeComponent)
    {
        var raw = new byte[Span];
        if (!EntityReader.Read(h, lifeComponent + LifeTotal, raw)) { return null; }

        Pool At(ulong total)
        {
            var o = (int)(total - LifeTotal);
            return new Pool(BitConverter.ToInt32(raw, o + 4), BitConverter.ToInt32(raw, o));
        }

        var vitals = new Vitals(At(LifeTotal), At(ManaTotal), At(EsTotal));
        return Sane(vitals.Life) && Sane(vitals.Mana) && Sane(vitals.Es) ? vitals : null;
    }

    private static bool Sane(Pool p) => p.Total is >= 0 and < 10_000_000 && p.Current >= 0 && p.Current <= p.Total;
}
