using System.Numerics;
using Guild.Domain.Numerics;

namespace Guild.Domain.Battle;

/// <summary>Values used by the existing simulation, queried without stepping or capturing it.</summary>
public sealed record UnitDecisionValues(BigInteger Hp, BigInteger MaxHp, Rational AttackPower,
    Rational PhysicalDefense, Rational MagicalDefense, Rational AttackIntervalMs,
    Rational MovementCentiPerSecond, int WeaponRangeCenti, int WatchRadiusCenti,
    int ActionRadiusCenti, Rational ActualX, Rational ActualY, int AnchorX, int AnchorY);

public sealed partial class BattleSimulation
{
    public UnitDecisionValues DescribeUnit(string id)
    {
        var unit = Ally(id) ?? throw new ArgumentException("The owned adventurer does not exist.", nameof(id));
        return new(unit.Hp, unit.MaxHp, CombatMath.Attack(unit),
            CombatMath.Defense(unit, false), CombatMath.Defense(unit, true),
            EffectiveAttackIntervalMs(unit), EffectiveMoveCentiPerSecond(unit), EffectiveRange(unit),
            400, 300, unit.X, unit.Y, unit.AnchorX, unit.AnchorY);
    }
}
