using Guild.Domain.Definitions;

namespace Guild.Application;

public sealed partial class GameController
{
    private static IReadOnlyList<BlessingDetailView> BuildBlessingDetails(AdventurerState actor, ContentBundle definitions)
        => actor.Blessings.Select(owned => new BlessingDetailView(owned.Id,
            definitions.Blessings.FirstOrDefault(x => x.Id == owned.Id)?.Name ?? owned.Id,
            owned.Rank, DescribeBlessing(owned, definitions))).ToArray();

    private static string DescribeBlessing(OwnedBlessing owned, ContentBundle definitions)
        => string.Join(DisplayText.Get("history.effect.separator"), BlessingModifiers(owned, definitions).Select(component =>
        {
            var parameter = DisplayText.Get((component.ParameterId is "str" or "vit" or "dex" or "agi" or "int"
                ? "app.parameter." : "history.parameter.") + component.ParameterId);
            var amount = component.Stage == ModifierStage.Additive ? component.Amount * 100 : component.Amount;
            var number = amount.ToDisplayString();
            if (component.Stage != ModifierStage.Multiplicative && amount > 0) number = "+" + number;
            var text = DisplayText.Format(component.Stage switch
            {
                ModifierStage.Multiplicative => "history.effect.multiply",
                ModifierStage.Additive => "history.effect.percent",
                _ => "history.effect.fixed"
            }, parameter, number);
            if (component.SkillId is not null)
                text = DisplayText.Format("history.effect.skill", definitions.Skills.Single(x => x.Id == component.SkillId).Name, text);
            if (component.Slot is not null) text = DisplayText.Format("history.effect.slot", component.Slot, text);
            return text;
        }));

    private static string EventKindName(string kind) => DisplayText.Get("history.event.kind." + kind);

    private static string DescribeAcquisition(string actorName, string blessingName, int oldRank, int newRank,
        string? replacedName = null, int replacedRank = 0)
    {
        var acquired = DisplayText.Format(oldRank == 0 ? "history.blessing.acquire" : "history.blessing.upgrade",
            actorName, blessingName, Roman(newRank), Roman(oldRank));
        return replacedName is null ? acquired : DisplayText.Format("history.blessing.replace", acquired, replacedName, Roman(replacedRank));
    }
}
