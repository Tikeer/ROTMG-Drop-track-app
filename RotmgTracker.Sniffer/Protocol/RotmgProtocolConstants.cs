using RotmgTrackerV1.Core.Enums;

namespace RotmgTrackerV1.Sniffer.Protocol;

public static class RotmgProtocolConstants
{
    // Główne ID pakietów RotMG Exalt (w razie aktualizacji DECA zmieniasz je tylko tutaj)
    public const byte PacketCreate = 57;        // Client -> Server: utworzenie postaci (classType, skinType, flagi) - potwierdzone na capture
    public const byte PacketLoad = 61;          // Client -> Server: zawiera CharId wybranej postaci (int32 + bool)
    public const byte PacketInvDrop = 19;       // Client -> Server: wyrzucenie itemu z plecaka (Anti-Drop)
    public const byte PacketUpdate = 42;        // Server -> Client: nowe obiekty na mapie (gracze, worki, bossowie)
    public const byte PacketNewTick = 10;       // Server -> Client: aktualizacja statystyk i zawartości worków
    public const byte PacketMapInfo = 92;       // Server -> Client: nazwa mapy / dungeona przy wejściu
    public const byte PacketCreateSuccess = 101;// Server -> Client: ObjectId naszej postaci i jej CharId
    public const byte PacketDamage = 75;        // Server -> Client: obrażenia zadane bossowi + flaga zabicia
    public const byte PacketDeath = 46;         // Server -> Client: śmierć postaci (Graveyard)

    // ID statystyk w ObjectStatusData (StatData)
    public const int StatMaxHp = 0;
    public const int StatHp = 1;
    public const int StatSize = 2;
    public const int StatNextLevelExp = 3;
    public const int StatExp = 4;
    public const int StatLevel = 5;
    public const int StatInventory0 = 8;  // Sloty 8..11 = założony ekwipunek gracza; w worku 8..15 = 8 slotów dropu!
    public const int StatInventory7 = 15;
    public const int StatName = 31;       // String: nick gracza
    public const int StatCurrFame = 57;   // Aktualny Base Fame postaci
    public const int StatUniqueData = 80; // String: dane enchantów / rzadkości przedmiotu
    public const int StatSkinId = 82;

    // Które statystyki w protokole RotMG są przesyłane jako UTF String zamiast CompressedInt
    public static bool IsStringStat(int statType) =>
        statType is 6 or 31 or 38 or 54 or 62 or 80 or 82 or 115 or 121 or 127 or 128 or 147;
    
    // Wszystkie worki z lootem ("Loot Bag N" w XML = BagType N)
    public static bool IsLootBagContainer(int objectType) =>
        objectType is 0x0500 or 0x0506 or 0x0507 or 0x0508 or 0x0509
            or 0x050B or 0x050C or 0x050E or 0x050F or 0x0510;

    // Śledzimy: egg (0x0508), cyan/niebieski (tylko Marki), biały (+Boost), czerwony, pomarańczowy.
    // Brązowy/różowy/fioletowy (0x0500/0x0506/0x0507) celowo pomijamy - zalałyby log i słownik worków.
    public static bool IsTrackedBagContainer(int objectType) =>
        objectType is 0x0508 or 0x0509 or 0x050B or 0x050C or 0x050E or 0x050F or 0x0510;

    // Mapowanie wewnętrznych ObjectType klas w RotMG na nasz enum RotmgClass
    private static readonly Dictionary<int, RotmgClass> ClassObjectTypes = new()
    {
        { 0x0300, RotmgClass.Rogue },
        { 0x0307, RotmgClass.Archer },
        { 0x030e, RotmgClass.Wizard },
        { 0x0310, RotmgClass.Priest },
        { 0x031d, RotmgClass.Warrior },
        { 0x031e, RotmgClass.Knight },
        { 0x031f, RotmgClass.Paladin },
        { 0x0320, RotmgClass.Assassin },
        { 0x0321, RotmgClass.Necromancer },
        { 0x0322, RotmgClass.Huntress },
        { 0x0323, RotmgClass.Mystic },
        { 0x0324, RotmgClass.Trickster },
        { 0x0325, RotmgClass.Sorcerer },
        { 0x0326, RotmgClass.Ninja },
        { 0x030d, RotmgClass.Samurai },
        { 0x031b, RotmgClass.Bard },
        { 0x0331, RotmgClass.Summoner },
        { 0x0336, RotmgClass.Kensei },
        { 0x0339, RotmgClass.Druid }
    };

    public static bool TryGetPlayerClass(int objectType, out RotmgClass rotmgClass) =>
        ClassObjectTypes.TryGetValue(objectType, out rotmgClass);

    // Mapowanie IP serwerów RotMG (AWS) na czytelne nazwy
    private static readonly Dictionary<string, string> ServerIpMap = new()
    {
        { "18.195.161.79", "EUWest" },
        { "3.68.86.112", "EUWest2" },
        { "18.159.132.11", "EUNorth" },
        { "3.121.230.94", "EUEast" },
        { "54.236.204.42", "USEast" },
        { "52.87.214.128", "USEast2" },
        { "54.215.221.17", "USWest" },
        { "54.183.175.88", "USWest3" },
        { "3.25.146.90", "Australia" },
        { "13.229.104.22", "Asia" }
    };

    public static string ResolveServerName(string ipAddress) =>
        ServerIpMap.GetValueOrDefault(ipAddress, $"Server ({ipAddress})");
}