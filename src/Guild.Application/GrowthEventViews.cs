namespace Guild.Application;

public sealed record GrowthEventOptionView(string Kind, string Name, bool CanBegin, string? UnavailableReason);

/// <summary>The queued recipient, not the adventurer selected for inspection in the UI.</summary>
public sealed record GrowthEventPreviewView(string EventId, long Sequence, string ActorId, string ActorName,
    string EventRank, string CurrentRank, string WeaponName, IReadOnlyList<string> Blessings,
    int PendingCount, string? Kind, IReadOnlyList<GrowthEventOptionView> Options);

public sealed partial record GameView
{
    public GrowthEventPreviewView? NextGrowthEvent { get; init; }
}

public sealed partial class GameController
{
    private RankEventState? PeekGrowthEvent()
        => profile.PendingEvents.OrderBy(pending => pending.Sequence).FirstOrDefault();

    /// <summary>Only existing state is projected. No candidate, random outcome or weapon is generated.</summary>
    private GrowthEventPreviewView? BuildNextGrowthEventView()
    {
        var pending = PeekGrowthEvent();
        if (pending is null) return null;
        var actor = Actor(pending.ActorId);
        var definitions = pending.Kind is null ? content : EventContent(pending);
        var weapon = profile.Weapons.Single(item => item.Id == actor.WeaponId && item.OwnerId == actor.Id);
        var blessings = actor.Blessings.Select(owned =>
            $"{owned.Id} {definitions.Blessings.FirstOrDefault(definition => definition.Id == owned.Id)?.Name ?? owned.Id} {Roman(owned.Rank)}").ToArray();
        var options = new (string Kind, string Name)[]
        {
            ("blessing", DisplayText.Get("app.growth.option.blessing")), ("transfer", DisplayText.Get("app.growth.option.transfer")),
            ("fusion", DisplayText.Get("app.growth.option.fusion")), ("random", DisplayText.Get("app.growth.option.random"))
        }.Select(option =>
        {
            var reason = GrowthEventUnavailableReason(pending, actor, option.Kind);
            return new GrowthEventOptionView(option.Kind, option.Name, reason is null, reason);
        }).ToArray();
        return new(pending.Id, pending.Sequence, actor.Id, actor.Name, pending.Rank.ToString(), actor.Rank.ToString(),
            weapon.Name, blessings, profile.PendingEvents.Count, pending.Kind, options);
    }

    private string? GrowthEventUnavailableReason(RankEventState pending, AdventurerState actor, string kind)
    {
        if (session is not null) return DisplayText.Get("app.growth.onlyInGuild");
        if (pending.Kind is not null) return DisplayText.Get("app.growth.completeSelected");
        return kind switch
        {
            "blessing" => Math.Min(3 + actor.DiscardBonus, content.Blessings.Count(definition => definition.Acquisition == "normal"
                && actor.Blessings.All(owned => owned.Id != definition.Id || owned.Rank < 3))) <= 0
                    ? DisplayText.Get("app.growth.noEligibleBlessing") : null,
            "transfer" => profile.Weapons.Any(weapon => weapon.OwnerId == "guild") ? null : DisplayText.Get("app.growth.noTransferableItem"),
            "fusion" => EligibleRecipes(actor, content).Count > 0 ? null : DisplayText.Get("app.growth.noEligibleRecipe"),
            "random" => null,
            _ => DisplayText.Get("app.growth.chooseKind")
        };
    }
}
