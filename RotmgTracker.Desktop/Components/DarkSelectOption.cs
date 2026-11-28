using RotmgTrackerV1.Core.Enums;

namespace RotmgTrackerV1.Desktop.Components;

public sealed record DarkSelectOption(
    string Value,
    string Label,
    string? SubLabel = null,
    string? Color = null,
    string? DungeonName = null,
    int? BagObjectType = null,
    BagType FallbackBag = BagType.WhiteBag,
    RotmgClass? ClassSprite = null);
