using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace P0Minimal.Powers;

/// <summary>
/// A real buff: it lives in the power bar, stacks its amount, and decays one
/// stack per own turn end. Proves the PowerModel path needs no registration.
/// </summary>
public sealed class ForgeMightPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    /// <summary>Adds its amount as flat damage to the owner's attacks.</summary>
    public override decimal ModifyDamageAdditive(
        Creature? target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (dealer != Owner || !props.IsPoweredAttack())
        {
            return 0m;
        }

        return Amount;
    }

    /// <summary>Lose one stack at the end of each of the owner's turns.</summary>
    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (side == Owner.Side)
        {
            await PowerCmd.Decrement(this);
        }
    }
}
