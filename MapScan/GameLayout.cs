namespace Poe2Map;

/// <summary>
///     PoE1 (27.10.2026 surumu) alan nesnesi (AreaInstance) yerlesimi. Hicbiri baskasindan alinmadi: hepsi bu
///     surumde CANLI oyunda, yapisal sinavlarla bulundu; yamadan sonra ayni komutlarla yeniden bulunur.
///
///         zemin yapisi alan icinde     0xBF0          arealearn   zemin +0x08 alan nesnesine geri bakiyor
///         yerel oyuncu isaretcisi      0x970          arealearn   yolu Metadata/Characters/ ile baslayan varlik
///         uyanik / uyuyan varlik map   0xA28 / 0xA38  arealearn   yurunebilir kirmizi-siyah agac, varliklar cozuluyor
///         Render konumu (x, y, z)      0x120          arealearn   yurunen oyuncuyu izliyor, izgarada yurunebilir hucre
///
///     PoE2 karsiliklari: 0x8D0 / 0x5D0 / 0x6F0 / 0x700 / 0x138. Varlik -> canli alan baglantisi (+0x78) iki oyunda ayni
///     ve LocalPlayer.AreaLink'te duruyor.
/// </summary>
internal static class GameLayout
{
    // 13.11.2026 yamasi, CANLI `MapScan arealearn` (hideout, 22:07): zemin 0xBF0 -> 0xC48, oyuncu 0x970 -> 0x980,
    // uyanik 0xA28 -> 0xA58, uyuyan 0xA38 -> 0xA68; Render konumu 0x120 ve varlik -> alan +0x78 degismedi.
    // (Statik karsilastirmanin +0x268 tahmini baska bir yapiymis; dogru kayma orada da 970->980, A28->A58 olarak vardi.)
    internal const ulong TerrainInArea = 0xC48;
    internal const ulong LocalPlayerInArea = 0x980;
    internal const ulong AwakeMap = 0xA58;
    internal const ulong SleepingMap = 0xA68;
    internal const ulong RenderPosition = 0x120;

    /// <summary>
    ///     Render bilesenindeki gorunen ad (std::wstring). Gecis kapilarinda gidilen alanin adi ("The Solaris Temple
    ///     Level 1"); exitnames ile bulundu. DLL ipucu Pos + 0x20 diyordu, canlida Pos + 0x28.
    /// </summary>
    internal const ulong RenderName = 0x148;
}
