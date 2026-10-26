using ModForge.Core.Model;

// 载入旧工程时的兼容测试。
//
// 校验载入后的规整：界面上不会给出的非法组合，从 JSON 直接进来时要被纠正。
//
// 目前只有一条——"取自目标"只对唯一目标有意义。手改过的 JSON 可能在"所有敌人"上带着它，
// 那读的是 cardPlay.Target，会拿到不确定的值。
//
// 规整在 ModProject.FromJson 里做，所以断言直接打在那上面。
//
// Usage: projecttest

int failures = 0;

void Check(string what, bool ok, string detail)
{
    Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}{(detail.Length > 0 ? "  — " + detail : "")}");
    if (!ok)
    {
        failures++;
    }
}

// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("== 计算型数值的\"取自谁\" ==");
{
    // 唯一目标 + 取自目标：合法，必须保留。
    string json = """
    {
      "id": "ACTOR",
      "cards": [
        {
          "id": "DRAIN",
          "effects": [
            { "kind": "LoseHp", "target": "Target", "amount": 0,
              "source": { "actor": "Target", "kind": "MissingHp", "perUnit": 0.5 } }
          ]
        }
      ]
    }
    """;

    ModProject project = ModProject.FromJson(json);
    ValueSource source = project.Cards[0].Effects[0].Source!;

    Check("唯一目标保留\"取自目标\"", source.Actor == ValueSourceActor.Target, source.Actor.ToString());
}

{
    // 多个目标 + 取自目标：没有"那一个目标"，应当规整成自身。
    string json = """
    {
      "id": "ACTOR2",
      "cards": [
        {
          "id": "SWEEP",
          "effects": [
            { "kind": "DealDamage", "target": "AllEnemies", "amount": 0,
              "source": { "actor": "Target", "kind": "MissingHp", "perUnit": 1 } },
            { "kind": "LoseHp", "target": "RandomEnemy", "amount": 0,
              "source": { "actor": "Target", "kind": "Block", "perUnit": 1 } },
            { "kind": "GainBlock", "target": "AllAllies", "amount": 0,
              "source": { "actor": "Target", "kind": "PowerStacks", "perUnit": 1,
                          "powerId": "POWER.STRENGTH_POWER" } }
          ]
        }
      ]
    }
    """;

    ModProject project = ModProject.FromJson(json);
    IReadOnlyList<EffectDef> effects = project.Cards[0].Effects;

    foreach (EffectDef effect in effects)
    {
        Check($"{effect.Target} 的效果改成\"取自自身\"",
            effect.Source!.Actor == ValueSourceActor.Self, effect.Source.Actor.ToString());
    }
}

{
    // 自身目标本来就是自身，规整不该把一个正确的值改坏。
    string json = """
    {
      "id": "ACTOR3",
      "cards": [
        {
          "id": "MEND",
          "effects": [
            { "kind": "Heal", "target": "Self", "amount": 0,
              "source": { "actor": "Self", "kind": "MissingHp", "perUnit": 1 } }
          ]
        }
      ]
    }
    """;

    ModProject project = ModProject.FromJson(json);
    Check("自身目标保持\"取自自身\"",
        project.Cards[0].Effects[0].Source!.Actor == ValueSourceActor.Self,
        project.Cards[0].Effects[0].Source.Actor.ToString());
}

{
    // 状态里的效果：目标是持有者，取自谁没有意义，坏了就规整回去。
    string json = """
    {
      "id": "ACTOR4",
      "powers": [
        {
          "id": "SIPHON", "name": "Siphon", "kind": "Debuff", "stack": "Counter",
          "triggers": [
            { "when": "TurnStart",
              "effect": { "kind": "LoseHp", "target": "AllEnemies", "amount": 0,
                          "amountMode": "Calculated",
                          "source": { "actor": "Target", "kind": "CurrentHp", "perUnit": 0.1 } } }
          ]
        }
      ]
    }
    """;

    ModProject project = ModProject.FromJson(json);
    Check("状态触发里的效果也被规整",
        project.Powers[0].Triggers[0].Effect.Source!.Actor == ValueSourceActor.Self,
        project.Powers[0].Triggers[0].Effect.Source.Actor.ToString());
}

// ---------------------------------------------------------------------------
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("== 载入时规整、其余字段原样保留 ==");
{
    string json = """
    {
      "id": "COMBINED",
      "name": "Combined",
      "version": "1.0.0",
      "cards": [
        {
          "id": "BOTH",
          "name": "Both",
          "description": "字段保留测试。",
          "pool": "necrobinder",
          "type": "Attack",
          "rarity": "Basic",
          "target": "AnyEnemy",
          "energyCost": 1,
          "upgradeCostDelta": -1,
          "effects": [
            { "kind": "DealDamage", "target": "AllEnemies", "amount": 0,
              "source": { "actor": "Target", "kind": "PowerStacks", "perUnit": 2,
                          "powerId": "POWER.POISON_POWER" } }
          ]
        }
      ],
      "powers": [],
      "sharedEffects": []
    }
    """;

    ModProject project = ModProject.FromJson(json);
    CardDef card = project.Cards[0];
    EffectDef effect = card.Effects[0];

    Check("工程 id 保留", project.Id == "COMBINED", project.Id);
    Check("卡牌字段保留", card.Pool == "necrobinder" && card.EnergyCost == 1 && card.UpgradeCostDelta == -1,
        $"{card.Pool}/{card.EnergyCost}/{card.UpgradeCostDelta}");
    Check("取值被改写", effect.Source!.Actor == ValueSourceActor.Self, effect.Source.Actor.ToString());

    // 存回去再读一遍：规整结果要能被序列化，不产生非法状态。
    ModProject again = ModProject.FromJson(project.ToJson());
    Check("写出再读回仍然合法",
        again.Cards[0].Effects[0].Source!.Actor == ValueSourceActor.Self,
        again.Cards[0].Effects[0].Source!.Actor.ToString());
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "RESULT: OK" : $"RESULT: {failures} 项失败");
return failures == 0 ? 0 : 1;
