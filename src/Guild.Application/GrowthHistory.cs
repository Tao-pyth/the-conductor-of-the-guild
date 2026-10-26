using System.Text.Json;
using Guild.Domain.Serialization;
using Guild.Persistence;

namespace Guild.Application;

public sealed partial class GameController
{
    private long growthHistoryRevision = -1;
    private IReadOnlyList<HistoryView> growthHistory = [];

    private IReadOnlyList<HistoryView> BuildGrowthHistory()
    {
        if (growthHistoryRevision == revision) return growthHistory;
        var result = new List<HistoryView>();
        MatchSession? finalMatch = null;
        foreach (var entry in history)
        {
            if (entry.Kind is "StartMatch" or "Retry") { finalMatch = null; continue; }
            if (entry.Kind == "WaveEnd")
            {
                var observed = ReadHistoryDocument<MatchSession>(entry.AfterJson);
                if (observed?.Phase is "Victory" or "Defeat") finalMatch = observed;
                continue;
            }
            if (entry.Kind == "ReturnToGuild")
            {
                result.Add(new(entry.Sequence, ProjectMatchHistory(entry, finalMatch)));
                finalMatch = null;
                continue;
            }
            if (entry.Kind is not ("Rename" or "Recruit" or "ExpandRoster" or "BeginEvent" or "ChooseBlessing"
                or "DiscardBlessings" or "RerollBlessings" or "TransferItem" or "EquipItem" or "Fuse"
                or "ResolveRandom" or "DeclineRandom" or "BuyWeapon")) continue;
            result.Add(new(entry.Sequence, ProjectChoiceHistory(entry)));
        }
        growthHistoryRevision = revision;
        return growthHistory = result.ToArray();
    }

    private static string ProjectMatchHistory(StoredHistory entry, MatchSession? finalMatch)
    {
        // Historical audit rows have sufficient outcome information only if a terminal WaveEnd exists.
        // Do not infer a victory from a reward or invent a missing defeated Wave.
        if (finalMatch is null) return entry.Text;
        var invasion = finalMatch.Current.Setup.Content.Stages
            .FirstOrDefault(x => x.Id == finalMatch.Current.Setup.StageId)?.Mode == "invasion";
        var stage = finalMatch.Current.Setup.Content.Stages.FirstOrDefault(x => x.Id == finalMatch.Current.Setup.StageId);
        var before = ReadHistoryDocument<PlayerProfile>(entry.BeforeJson);
        var after = ReadHistoryDocument<PlayerProfile>(entry.AfterJson);
        if (stage is null || before is null || after is null) return entry.Text;
        var outcome = DisplayText.Format(finalMatch.Phase == "Victory" ? "history.match.victory" : "history.match.defeat",
            DisplayText.Get(invasion ? "history.unit.section" : "history.unit.wave"), finalMatch.Current.Wave);
        var summary = DisplayText.Format("history.match.record", outcome, stage.Name, after.Resources - before.Resources);
        var rankChanges = after.Adventurers.Select(actor =>
        {
            var oldActor = before.Adventurers.FirstOrDefault(x => x.Id == actor.Id);
            return oldActor is not null && oldActor.Rank != actor.Rank
                ? DisplayText.Format("history.match.rank", actor.Name, oldActor.Rank, actor.Rank) : null;
        }).Where(x => x is not null);
        return string.Join(DisplayText.Get("history.effect.separator"), new[] { summary }.Concat(rankChanges));
    }

    private static string ProjectChoiceHistory(StoredHistory entry)
    {
        if (entry.Kind is not ("BeginEvent" or "ChooseBlessing")) return entry.Text;
        var before = ReadHistoryDocument<PlayerProfile>(entry.BeforeJson);
        var after = ReadHistoryDocument<PlayerProfile>(entry.AfterJson);
        if (before is null || after is null) return entry.Text;
        var actor = after.Adventurers.FirstOrDefault(x => x.Id == entry.ActorId);
        if (actor is null) return entry.Text;
        if (entry.Kind == "BeginEvent")
        {
            var pending = after.PendingEvents.FirstOrDefault(x => x.ActorId == actor.Id && x.Kind is not null);
            if (pending?.Kind is "blessing" or "transfer" or "fusion" or "random")
                return DisplayText.Format("history.event.selected", actor.Name, pending.Rank, EventKindName(pending.Kind));
            return entry.Text;
        }
        var oldActor = before.Adventurers.FirstOrDefault(x => x.Id == actor.Id);
        var selected = before.PendingEvents.FirstOrDefault(x => x.ActorId == actor.Id && x.Kind is not null);
        if (oldActor is null || selected?.DefinitionJson is null) return entry.Text;
        var changed = actor.Blessings.FirstOrDefault(x => oldActor.Blessings.All(old => old.Id != x.Id || old.Rank != x.Rank));
        if (changed is null) return entry.Text;
        Guild.Domain.Definitions.ContentBundle definitions;
        try { definitions = EventContent(selected); }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException) { return entry.Text; }
        var blessing = definitions.Blessings.FirstOrDefault(x => x.Id == changed.Id);
        if (blessing is null) return entry.Text;
        var oldRank = oldActor.Blessings.FirstOrDefault(x => x.Id == changed.Id)?.Rank ?? 0;
        var removed = oldActor.Blessings.FirstOrDefault(x => actor.Blessings.All(current => current.Id != x.Id));
        var replaced = removed is null ? null : definitions.Blessings.FirstOrDefault(x => x.Id == removed.Id)?.Name;
        return DescribeAcquisition(actor.Name, blessing.Name, oldRank, changed.Rank, replaced, removed?.Rank ?? 0);
    }

    private static T? ReadHistoryDocument<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, DomainJson.Options); }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException) { return null; }
    }
}
