using System.Numerics;
using System.Text.Json;
using Guild.Domain.Definitions;
using Guild.Domain.Economy;
using Guild.Domain.Randomness;

namespace Guild.Application;

public sealed partial class GameController
{
    private static void RequireEconomyJson(string json, bool profileDocument)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("economy", out var economy))
            throw InvalidEconomy("保存文書に経済状態がありません。");
        if (!profileDocument && economy.ValueKind == JsonValueKind.Null) return;
        if (economy.ValueKind != JsonValueKind.Object)
            throw InvalidEconomy("経済状態の形式が不正です。");
        var required = profileDocument
            ? new[] { "version", "materials", "factions", "reservation", "offeredEquipment", "deliveryCompletions", "dispatchRng", "offerRng" }
            : new[] { "version", "catalogVersion", "contribution", "victoryMaterials", "dispatch" };
        foreach (var property in required)
            if (!economy.TryGetProperty(property, out _))
                throw InvalidEconomy("経済状態の必須項目がありません: " + property);
    }

    private static void ValidateEconomyDocuments(PlayerProfile player, MatchSession? match, ContentBundle definitions)
    {
        // Old document versions predate economy state. Their migration initializes it
        // only after the complete old profile/match pair has passed preflight.
        if (player.FormatVersion < 3)
        {
            if (player.Economy is { } legacy)
            {
                if (legacy.Version != 1)
                    throw new NotSupportedException("旧保存に未知のギルド経済形式が含まれています。データは変更していません。");
                if (legacy.Materials is null || legacy.Materials.Count != 0
                    || legacy.Factions is null || legacy.Factions.Count != 0 || legacy.Reservation is not null
                    || legacy.OfferedEquipment is null || legacy.OfferedEquipment.Count != 0
                    || legacy.DeliveryCompletions is null || legacy.DeliveryCompletions.Count != 0
                    || !string.IsNullOrEmpty(legacy.LastReport))
                    throw InvalidEconomy("旧保存形式に新しい経済状態が混在しています。");
                ValidateEconomyRandom(legacy.DispatchRng);
                ValidateEconomyRandom(legacy.OfferRng);
            }
            if (match?.Economy is not null)
                throw InvalidEconomy("旧作戦形式に新しい経済状態が混在しています。");
            return;
        }
        var economy = player.Economy ?? throw InvalidEconomy("ギルド経済状態がありません。");
        if (economy.Version != 1) throw new NotSupportedException("このギルド経済形式は読み込めません。データは変更していません。");
        var catalog = EconomyCatalog.Default;
        var materials = catalog.Materials.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var deliveries = catalog.Deliveries.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var equipment = catalog.Deliveries.Where(x => x.Kind == "equipment").Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var factions = new HashSet<string>(["C", "M", "K", "T"], StringComparer.Ordinal);
        if (economy.Materials is null || !materials.SetEquals(economy.Materials.Keys)
            || economy.Materials.Values.Any(x => x < 0)
            || economy.Factions is null || !factions.SetEquals(economy.Factions.Keys)
            || economy.Factions.Values.Any(x => x is null || x.Trust is < -100 or > 100)
            || economy.DeliveryCompletions is null
            || economy.DeliveryCompletions.Any(x => !deliveries.Contains(x.Key) || x.Value < 0)
            || economy.LastReport is null)
            throw InvalidEconomy("素材・勢力関係・納品記録が不正です。");
        ValidateEconomyRandom(economy.DispatchRng);
        ValidateEconomyRandom(economy.OfferRng);
        if (economy.OfferedEquipment is null || !equipment.SetEquals(economy.OfferedEquipment.Keys)
            || economy.OfferedEquipment.Values.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id)
                || string.IsNullOrWhiteSpace(x.TemplateId) || string.IsNullOrWhiteSpace(x.Name) || x.OwnerId != "guild"
                || x.BasePower <= 0 || x.AttackIntervalMs <= 0 || x.RangeCenti <= 0
                || x.SkillIds is null || x.SkillIds.Any(string.IsNullOrWhiteSpace))
            || economy.OfferedEquipment.Values.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != equipment.Count
            || economy.OfferedEquipment.Values.Any(x => player.Weapons.Any(w => w.Id == x.Id)))
            throw InvalidEconomy("納品装備の掲示記録が不正です。");
        foreach (var (id, weapon) in economy.OfferedEquipment)
        {
            var template = definitions.WeaponTemplates.FirstOrDefault(x => x.Id == weapon.TemplateId);
            if (template is null || template.WeaponType != catalog.Deliveries.Single(x => x.Id == id).WeaponType
                || weapon.SkillIds.Any(skill => !definitions.Skills.Any(x => x.Id == skill)))
                throw InvalidEconomy("納品装備の武器種・技能が不正です。");
        }

        var owned = player.Adventurers.ToDictionary(x => x.Id, StringComparer.Ordinal);
        if (economy.Reservation is { } reservation)
        {
            if (match is not null || reservation.ActorIds is null || reservation.ActorIds.Count is < 1 or > 6
                || reservation.ActorIds.Distinct(StringComparer.Ordinal).Count() != reservation.ActorIds.Count
                || reservation.ActorIds.Any(x => !owned.ContainsKey(x) || owned[x].Hp <= 0)
                || !player.Adventurers.Any(x => !reservation.ActorIds.Contains(x.Id, StringComparer.Ordinal) && x.Hp > 0)
                || !catalog.Missions.Any(x => x.Id == reservation.MissionId))
                throw InvalidEconomy("派遣予約の冒険者・行先が不正です。");
        }
        // A migrated in-progress match keeps its exact old battle and rewards.
        if (match?.Economy is not { } operation) return;
        if (operation.Version != 1 || operation.CatalogVersion != catalog.Version)
            throw new NotSupportedException("この作戦経済形式は読み込めません。データは変更していません。");
        if (operation.Contribution < 0 || operation.VictoryMaterials is null
            || !materials.SetEquals(operation.VictoryMaterials.Keys) || operation.VictoryMaterials.Values.Any(x => x < 0))
            throw InvalidEconomy("作戦予算・素材報酬が不正です。");
        var snapshots = new[] { match.Current, match.MatchStart, match.WaveStart }.Where(x => x is not null).ToArray();
        if (snapshots.Any(x => x!.Setup.InitialResources != 1000 + operation.Contribution))
            throw InvalidEconomy("拠出額と作戦開始予算が一致しません。");
        if (operation.Dispatch is not { } dispatch) return;
        if (dispatch.MatchId != match.MatchId || dispatch.Mission is null || dispatch.Members is null
            || dispatch.Members.Count is < 1 or > 6 || dispatch.SuccessPercent is < 0 or > 100 || dispatch.Roll is < 0 or > 99
            || dispatch.RankExperience is null || dispatch.Members.Any(x => x is null))
            throw InvalidEconomy("派遣記録が不正です。");
        var memberIds = dispatch.Members.Select(x => x.ActorId).ToHashSet(StringComparer.Ordinal);
        if (memberIds.Count != dispatch.Members.Count || memberIds.Any(x => !owned.ContainsKey(x))
            || !memberIds.SetEquals(dispatch.RankExperience.Keys) || dispatch.RankExperience.Values.Any(x => x < 0)
            || !player.Adventurers.Any(x => !memberIds.Contains(x.Id) && x.Hp > 0)
            || dispatch.Members.Any(x => x.Rank < 1 || x.Hp <= 0 || x.WeaknessStacks < 0
                || x.BlessingIds is null || x.BlessingIds.Any(string.IsNullOrWhiteSpace)
                || x.BlessingIds.Distinct(StringComparer.Ordinal).Count() != x.BlessingIds.Count
                || x.Rank != owned[x.ActorId].Rank || x.Hp != owned[x.ActorId].Hp || x.WeaknessStacks != owned[x.ActorId].WeaknessStacks
                || !x.BlessingIds.ToHashSet(StringComparer.Ordinal).SetEquals(owned[x.ActorId].Blessings.Select(b => b.Id))))
            throw InvalidEconomy("派遣対象と保存済み冒険者が一致しません。");
        var missionCatalog = new EconomyCatalog { Version = operation.CatalogVersion, Materials = catalog.Materials, Missions = [dispatch.Mission] };
        missionCatalog.EnsureValid();
        var frozen = match.Current.Setup.Content;
        if (dispatch.Mission.BlessingIds.Any(id => !frozen.Blessings.Any(x => x.Id == id))
            || dispatch.Members.Any(x => x.BlessingIds.Any(id => !frozen.Blessings.Any(b => b.Id == id))))
            throw InvalidEconomy("派遣に不明な加護が記録されています。");
        foreach (var snapshot in snapshots)
        {
            var units = snapshot!.Units.Where(x => memberIds.Contains(x.Id)).ToArray();
            if (units.Length != memberIds.Count || units.Any(x => x.Deployed || x.EverDeployed)
                || snapshot.Setup.PartyIds?.Any(memberIds.Contains) == true
                || units.Any(x => x.WeaponType != dispatch.Members.Single(m => m.ActorId == x.Id).WeaponType))
                throw InvalidEconomy("派遣中の冒険者が戦闘へ参加しています。");
        }
        if (match.LevelChoices.Keys.Any(memberIds.Contains))
            throw InvalidEconomy("派遣中の冒険者に作戦内成長が記録されています。");
        var evaluation = DispatchCalculator.Evaluate(dispatch.Mission,
            dispatch.Members.Select(x => new DispatchMember(x.ActorId, x.Rank, x.WeaponType, x.BlessingIds)).ToArray());
        if (evaluation.SuccessPercent != dispatch.SuccessPercent
            || dispatch.Members.Any(x => dispatch.RankExperience[x.ActorId]
                != DispatchRankExperience(x.Rank, dispatch.Mission.RecommendedRank, dispatch.Roll < dispatch.SuccessPercent)))
            throw InvalidEconomy("派遣成功率・成長報酬が出発時条件と一致しません。");
    }

    private static void ValidateEconomyRandom(PcgState? state)
    {
        if (state is null || (state.Increment & 1) == 0)
            throw InvalidEconomy("派遣・納品装備の乱数状態が不正です。");
    }

    private static InvalidDataException InvalidEconomy(string message)
        => new(message + " データは変更していません。");
}
