namespace Guild.Application;

public sealed record GameCommand(string Type, string? ActorId = null, string? TargetId = null,
    int X = 0, int Y = 0, string? Value = null, string? CommandId = null);
public sealed record CommandResult(bool Success, string Message);
public sealed record AdventurerView(string Id, string Name, string Rank, int Level, string Hp,
    string MaxHp, string Weapon, bool Deployed, bool Incapacitated, int X, int Y,
    IReadOnlyList<string> Blessings, IReadOnlyList<SkillView> Skills, string TargetPreset = "arrival");
public sealed record SkillView(string Slot, string Name, double Progress);
public sealed record MapEntityView(string Id, string Kind, double X, double Y, double HpRatio);
public sealed record ChoiceView(string Id, string Name, string Description);
public sealed record ItemView(string Id, string Name, string OwnerId, bool Equipped);
public sealed record HistoryView(long Sequence, string Text);
public sealed record GameView(string Screen, string Status, string GuildResources, string MatchResources,
    int Wave, int TotalWaves, int DefenseHp, int Speed, bool Paused, int OwnedCapacity,
    int PendingEvents, string? EventActorId, string? EventKind,
    IReadOnlyList<AdventurerView> Adventurers, IReadOnlyList<MapEntityView> Entities,
    IReadOnlyList<ChoiceView> Choices, IReadOnlyList<ItemView> Items,
    IReadOnlyList<HistoryView> History, int MapWidth = 16, int MapHeight = 10);
