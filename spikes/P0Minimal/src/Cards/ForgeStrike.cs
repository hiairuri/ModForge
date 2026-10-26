using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace P0Minimal.Cards;

/// <summary>
/// Deals damage and applies a genuine custom buff, so the spike exercises both
/// card registration and the PowerModel path in one play.
/// </summary>
public sealed class ForgeStrike : CardModel
{
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(7m, ValueProp.Move),
        new PowerVar<Powers.ForgeMightPower>(2m)
    };

    protected override IEnumerable<IHoverTip> ExtraHoverTips => new IHoverTip[]
    {
        HoverTipFactory.FromPower<Powers.ForgeMightPower>()
    };

    public ForgeStrike()
        : base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, nameof(cardPlay.Target));

        await CreatureCmd.Damage(
            choiceContext,
            cardPlay.Target,
            DynamicVars.Damage.BaseValue,
            ValueProp.Move,
            Owner.Creature,
            this);

        // PowerVar<T> registers under the power type's name ("ForgeMightPower").
        await PowerCmd.Apply<Powers.ForgeMightPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars["ForgeMightPower"].BaseValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(3m);
    }
}
