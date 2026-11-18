using RotmgTrackerV1.Core.Enums;

namespace RotmgTrackerV1.Sniffer.Protocol;

// Dane itemu ze stata 80 (UNIQUE_DATA): lista blobów base64 (URL-safe) rozdzielona przecinkami, po jednym na slot.
// Blob: 00 02 04, potem 4 x ushort LE (id enchantu, 0xFFFD = pusty slot), opcjonalny ogon "05 00" (bez znaczenia dla rarity).
// Rarity = liczba zajętych slotów enchantów: 0 Common, 1 Uncommon, 2 Rare, 3 Legendary, 4 Divine.
public sealed record ItemData(IReadOnlyList<int> EnchantIds)
{
    public ItemRarity Rarity => (ItemRarity)Math.Clamp(EnchantIds.Count, 0, 4);
    public string EnchantmentsText => string.Join(",", EnchantIds);
}

public static class ItemDataParser
{
    private const int HeaderLength = 3;
    private const int EnchantSlots = 4;
    private const int EmptyEnchant = 0xFFFD;

    // Wpis dla jednego slotu z całej wartości stata 80 (null = brak lub niepoprawny blob)
    public static ItemData? ParseSlot(string uniqueData, int slotIndex)
    {
        if (string.IsNullOrEmpty(uniqueData) || slotIndex < 0) return null;

        string[] entries = uniqueData.Split(',');
        return slotIndex < entries.Length ? ParseBlob(entries[slotIndex]) : null;
    }

    public static ItemData? ParseBlob(string blob)
    {
        if (string.IsNullOrWhiteSpace(blob)) return null;

        string b64 = blob.Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + ((4 - (b64.Length % 4)) % 4), '=');

        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(b64);
        }
        catch (FormatException)
        {
            return null;
        }

        if (raw.Length < HeaderLength + (EnchantSlots * 2)) return null;

        var ids = new List<int>();
        for (int i = 0; i < EnchantSlots; i++)
        {
            int offset = HeaderLength + (2 * i);
            int value = raw[offset] | (raw[offset + 1] << 8);
            if (value != EmptyEnchant)
                ids.Add(value);
        }

        return new ItemData(ids);
    }
}