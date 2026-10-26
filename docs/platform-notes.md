# ModForge — verified platform facts

All of the following was confirmed empirically on this machine against
Slay the Spire 2 `v0.107.1`, Godot 4.5.1 (.NET), Steam app `2868840`.
Nothing here is assumed; each item is reproduced from a real run or from the
game's own decompiled source.

## 1. Mod loading

- The loader is built into the game (`MegaCrit.Sts2.Core.Modding.ModManager`).
- Scans `mods/` next to the executable, plus subscribed Steam Workshop items.
- Manifest: `<Id>.json` (also accepts `mod_manifest.json`). Fields:
  `id`, `pck_name`, `name`, `author`, `description`, `version`,
  `has_pck`, `has_dll`, `dependencies`, `affects_gameplay`, `min_game_version`.
- The DLL file name must equal the manifest `id`.
- Entry point: `[ModInitializer("MethodName")]` on a class with a static method.
  Without it the loader falls back to `Harmony.PatchAll`, which registers no models.
- **Enablement defaults to enabled**: `ModManager` reads
  `_settings.ModList.FirstOrDefault(...)?.IsEnabled ?? true`, so a mod present in
  `mods/` is active. No settings edit is required to install.
- Arms the mod via `AssemblyLoadContext`.

### Verified log sequence for a working mod

```
[INFO] Found mod manifest file ...\mods\P0Minimal\P0Minimal.json
[INFO]   47: ModForge P0 Spike (P0Minimal)
[INFO] Loading assembly DLL ...\mods\P0Minimal\P0Minimal.dll
[INFO] Loading Godot PCK ...\mods\P0Minimal\P0Minimal.pck
[INFO] Calling initializer method of type P0Minimal.ModEntry for P0Minimal, ...
[INFO] Finished mod initialization for 'ModForge P0 Spike' (P0Minimal).
```

## 2. Cards

- A card is `class X : CardModel`. **`CardModel` has no abstract members** — only a
  constructor is mandatory. The constructor also supplies type/rarity/target/cost as
  parameters rather than abstract properties:
  `protected CardModel(int canonicalEnergyCost, CardType type, CardRarity rarity, TargetType targetType, bool shouldShowInCardLibrary = true)`.
- A **public parameterless constructor is required** (`ModelDb.Init` uses
  `Activator.CreateInstance`).
- **Registration is mandatory**: `ModHelper.AddModelToPool<IroncladCardPool, X>()`.
  `IroncladCardPool` is `sealed`, so injection is the only route.
  `CardPoolModel.AllCards` calls `ModHelper.ConcatModelsFromMods(this, ...)`, which
  keys on `poolModel.GetType()`.
- Vanilla character pools (namespace `MegaCrit.Sts2.Core.Models.CardPools`):
  `IroncladCardPool`, `SilentCardPool`, `DefectCardPool`, `NecrobinderCardPool`,
  `RegentCardPool`, `ColorlessCardPool`, `CurseCardPool`, `StatusCardPool`,
  `EventCardPool`, `QuestCardPool`, `TokenCardPool`.
- **No `CharacterModel` is needed** to add cards to an existing character's pool.
- Rarity gates reward appearance: use `Common`/`Uncommon`/`Rare`. `Basic` is filtered
  out of rewards and shops; `Token`/`Status`/`Curse`/`Event`/`Quest` never roll.
- **Portraits are optional**: a missing portrait falls back to the vanilla `beta`
  placeholder (`res://images/packed/card_portraits/beta.png`), no crash.
- Default portrait lookup:
  `res://images/atlases/card_atlas.sprites/<pool_title_lower>/<entry_lower>.tres`
  with PNG fallback under `packed/card_portraits/...`. Override `PortraitPath` to ship art.

## 3. Powers (real buffs)

- A power is `class X : PowerModel` with exactly two required members:
  `PowerType Type` and `PowerStackType StackType`.
- **No registration and no pool are needed.** `ModelDb` discovers every
  `AbstractModel` subclass in loaded mod assemblies.
- `PowerType { None, Buff, Debuff }`, `PowerStackType { None, Counter, Single }`.
  `Single` hides the amount (always 1); `Counter` shows and accumulates it.
- Localization keys: `powers` table, `<ENTRY>.title` / `.description`
  (optionally `.smartDescription`).
- Lifecycle hooks are inherited from `AbstractModel`, including
  `BeforeApplied`, `AfterApplied`, `AfterRemoved`, `AfterSideTurnStart`,
  `AfterSideTurnEnd`, `AfterPlayerTurnStart`, `AfterCardPlayed`,
  `ModifyDamageAdditive`, `ModifyDamageMultiplicative`, `AfterDamageReceived`,
  `AfterBlockGained`, `AfterPowerAmountChanged`, plus `Early`/`Late`/`VeryEarly` variants.
- `AmountOnTurnStart` snapshots the amount at turn start; `DisplayAmount` can be
  overridden to show something other than `Amount`.
- Decay/removal: `PowerCmd.Decrement(power)`, `PowerCmd.TickDownDuration(power)`,
  `PowerCmd.Remove(power)`. `SkipNextDurationTick` suppresses a duration tick.
- Persisted per-power state uses `[SavedProperty]` properties.
- Applying: `PowerCmd.Apply<T>(choiceContext, target, amount, applier, cardSource)`.
- Icons default to `atlases/power_atlas.sprites/<entry>.tres` and
  `powers/<entry>.png`; missing icons fall back to `powers/missing_power.png`.

## 4. Identifiers (critical)

- Model id = `"CATEGORY.ENTRY"`.
- ENTRY = `StringHelper.Slugify(type.Name)`: insert `_` before interior capitals,
  upper-case, whitespace to `_`, strip anything outside `[A-Z0-9_]`.
  `ForgeStrike` -> `FORGE_STRIKE`; `DejaVu` -> `DEJA_VU`.
- CATEGORY = slugified name of the direct child of `AbstractModel`
  (`CardModel` -> `CARD`, `PowerModel` -> `POWER`), with a trailing `_MODEL` removed.
- **Localization keys use the ENTRY alone, not the category**: `cards` table,
  key `FORGE_STRIKE.title`. Verified live:
  `ForgeStrike id=CARD.FORGE_STRIKE ... title='Forge Strike'`.
- Duplicate ids crash startup with `DuplicateModelException`, so generated type names
  must not collide with vanilla or with each other.

## 5. PCK format (Godot 4 pack v3)

```
header  : "GDPC" | u32 format_version | u32 major | u32 minor | u32 patch
          | u32 flags | u64 file_base | u64 dir_offset | 64 reserved bytes   (104 bytes)
payload : file bytes, 16-byte aligned; absolute position = file_base + entry.offset
dir     : at ABSOLUTE dir_offset: u32 entry_count, then entries
entry   : u32 path_len | path bytes | u64 offset | u64 size | 16-byte digest | u32 flags
```

Variations observed across real packs, all handled by the reader:

- `file_base` is `112` in every pack inspected (header is 104, rounded up to 16).
- Entry offsets are **relative to `file_base`**; `dir_offset` is **absolute**.
- The engine's own packs NUL-pad path names to a 4-byte boundary; exported mod packs
  do not. Both must be tolerated.
- Paths are stored **without** the `res://` prefix in official and exported packs.
  (One third-party pack, `regentSkin.pck`, stores them *with* the prefix.)
- Payloads are **uncompressed**: all 15,658 entries in `SlayTheSpire2.pck` have
  `flags == 0`, and no zstd/gzip magic appears anywhere.
- The entry digest field is exactly 16 bytes, so it is MD5 by format requirement.
  It is an archive integrity field, not a security boundary; changing the width would
  break the record layout.

Mod localization lives at `<ModId>/localization/<lang>/<table>.json`, resolved as
`res://<mod.manifest.id>/localization/<language>/<file>`.

## 6. Offline catalog (no game launch required)

- `sts2.dll` is reflectable with `MetadataLoadContext` (metadata only, no code runs).
  The game is self-contained, so load it against the game's own runtime directory
  rather than the host runtime, or `System.Private.CoreLib` loads twice.
- Result: 1,624 concrete `AbstractModel` subclasses; 1,564 after filtering
  `Mock`/`MOCK_`/`DEPRECATED_`/`.MODDED`.
- Names come from the game PCK's uncompressed loc tables. Coverage is complete:
  cards 576/576, relics 296/296, characters 7/7 with both EN and ZHS names;
  powers 238/257 (the rest are card-derived helpers whose names live in `cards.json`).
- Monster tables key on `.name`, not `.title`.

## 7. Toolchain

- .NET SDK 9.0.314 and 10.0.300 are installed, with `Microsoft.NETCore.App.Ref` 9.0.16.
- Roslyn is on disk at `<dotnet>\sdk\9.0.314\Roslyn\bincore\Microsoft.CodeAnalysis.CSharp.dll`,
  so export can compile in-process and fully offline.
- Reference assemblies for generated mods: `sts2.dll`, `GodotSharp.dll`, `0Harmony.dll`
  from `data_sts2_windows_x86_64/`, all with `Private=false`.

## 8. Tooling notes

- The game cannot be launched directly: it needs Steam, or it fails with
  `No appID found`. Launch with `Start-Process 'steam://rungameid/2868840'`.
- A direct launch attempt archives the previous log to
  `logs/godot<timestamp>.log`, so it does not destroy history.
- The game log at `%APPDATA%/SlayTheSpire2/logs/godot.log` records mod discovery,
  assembly load, PCK mount, and initializer invocation, which makes verification
  scriptable. In-mod diagnostics (a Harmony postfix printing to `GD.Print`) are the
  most reliable way to assert runtime facts such as pool membership.

## 9. Textures: .ctex, and why card art must be WebP

Custom card art ships as a Godot compressed texture (`.ctex`). The layout is taken
from the engine source (`scene/resources/compressed_texture.cpp`) and verified
byte-for-byte against shipped portraits:

```
@0   "GST2"
@4   u32 version (1)
@8   u32 width
@12  u32 height
@16  u32 detect flags
@20  u32 mipmap limit
@24  u32 reserved
@28  u32 reserved
@32  u32 reserved
@36  u32 data format   (1 = embedded PNG, 2 = embedded WEBP, 0 = raw)
@40  u16 width
@42  u16 height
@44  u32 mipmap count
@48  u32 image format
@52  u32 payload size
@56  payload
```

**Only the WebP container works at runtime.** This was measured inside the shipping
game by loading three variants from a probe mod:

| Variant | Result |
|---|---|
| `.ctex` with a PNG payload (`data_format=1`) | **fails** — `Failed loading resource` |
| `.ctex` with a WebP payload (`data_format=2`) | **loads** — `CompressedTexture2D`, correct dimensions |
| `.png` path resolved through an `.import` stub | fails |

The exported template is built without the PNG unpacker, which is also why every
texture the game itself ships embeds WebP (confirmed across 3,485 `.ctex` entries:
the only non-WebP ones are `data_format=0` raw BC7, `format == 22`).

Consequences for mod generation:

- Card art must be WebP. PNG input is converted with a local Godot install running
  `tools/godot-helpers/convert_to_webp.gd` headless; Godot is a convenient converter
  because it needs no extra dependency and produces a lossless WebP.
- Art is stored under the mod's own route and the generated card overrides
  `PortraitPath` to point at it. Writing a bare `.ctex` at a `.png` route does **not**
  work: the engine resolves textures through import remaps, which a mod cannot supply.
- `.ctex` is a registered extension loading directly as `CompressedTexture2D`, so no
  `.import` stub is needed for a route that is referenced explicitly.

## 10. Skin packages

Godot stores imported assets under an opaque hashed name in `.godot/imported/`, while
the *source route* lives in the neighbouring `*.import` stub. The stub is therefore the
authoritative map from a game route to the bytes implementing it, and skin reading is
built on that mapping. Note that some exporters omit `source_file`, so the stub's own
filename (minus `.import`) is the reliable route.

Skin routes, from the game's own asset layout:

```
res://animations/characters/<character>/...
res://animations/character_select/<character>/...
res://animations/merchant/<character>/...
res://animations/rest_site/<character>/...
res://animations/monsters/<monster>/...
```

Godot's Spine importer emits, per source asset:

```
<name>.atlas -> <hash>.spatlas   (JSON; readable)
<name>.skel  -> <hash>.spskel    (Spine binary)
<name>.png   -> <hash>.ctex      (compressed texture)
```

Animation names are recovered by scanning the skeleton for ASCII identifier runs and
ranking those matching conventional combat names. This is a candidate list, not a
parse: the format has no animation index, so the editor lets the user pick or type a
name. Verified against real packs, where it recovers `attack1`, `death_pop`,
`idle_breath`, `hurt3`, `Idle`, `Cast`, `Dead`.

The gameplay slot names the game requests are fixed constants in
`MegaCrit.Sts2.Core.Animation.AnimState`: `attackAnim = "attack"`,
`castAnim = "cast"`, `dieAnim = "die"`, `hurtAnim = "hurt"`,
`idleAnim = "idle_loop"`, `reviveAnim = "revive"`, `stunAnim = "stun"`. A skin whose
animations are named differently is never used for those moments unless the requested
name is translated, which is what the generated animation remap does.

`MegaSprite` has no `SetAnimation`; animations are applied through
`MegaAnimationState.SetAnimation(animationName, loop, trackId)`, which is the method a
remap patch must target.

## 11. PCK writing

The writer mirrors Godot's own `core/io/pck_packer.cpp`: entry offsets are stored
relative to `file_base` (`store_64(ofs - file_base)`), and payloads are padded to the
packer's alignment. `file_base` is the first 16-byte boundary after the 104-byte
header (112 in every pack inspected), and the directory table is written last.

The entry digest field is exactly 16 bytes, so it is MD5 by format requirement. It is
an archive integrity field, not a security boundary, and changing its width would
shift the record layout and make the pack unreadable.

## 12. Extracting the game's own card artwork

Card frames and banners live inside VRAM-compressed atlases stored as BC7, which an
ordinary image decoder cannot read. They can be extracted with a headless Godot script:

1. Mount the game pack with `ProjectSettings.load_resource_pack`.
2. Load the sprite resource; it is an `AtlasTexture` naming an atlas and a region.
3. Read the atlas's `.import` stub to find the imported file holding the real pixels.
   Reading the raw `.png` fails, because an exported build replaces it.
4. `Image.decompress()` the atlas before cropping: a compressed image cannot be blitted.
5. Crop to the sprite's `region` and save as PNG.

Verified output: a 598x844 attack frame, a 653x145 banner and a 74x74 energy icon, all
with real pixel content.

**Useful measured geometry** for composing a card preview, expressed as fractions of the
frame's bounds:

| Region | Horizontal | Vertical |
|---|---|---|
| Frame art window | 7.9% – 91.3% | 10.0% – 54.9% |
| Banner opaque area | 8.0% – 91.7% | full height |

The artwork must be layered *behind* the frame so the frame's inner border draws over
its edges.

## 13. Animation names and slot binding

Skeleton names are stored as inline length-prefixed strings, but the declared length can
include trailing bytes beyond the text. A naive character scan therefore over-reads
(turning `idle_loop` into `idle_loopk`), and a binding to a wrong name silently does
nothing. Names are truncated to a plausible declared length where one is present.

Slot suggestions must not use plain substring matching: it maps `entry` onto `sentry` and
binds a gameplay slot to an unrelated animation. Matching is exact, then prefix, then
word-boundary.

The Spine version string is a length-prefixed value near the start of the file; the
declared length can exceed the version text, so only the leading `major.minor.patch` run
is used. Real skeleton versions report `4.2.43`.

## 14. Card descriptions need matching variables

A card description is a SmartFormat template, and every `{Name...}` placeholder must have
a matching dynamic variable declared in `CanonicalVars`, or formatting fails at runtime:

```
ERROR: Localization formatting error! No source extension could handle the selector named "EmberMightPower"
```

The text still renders in some places, so this is easy to miss, but it is a genuine error
in the log and leaves the card's numbers unresolved. Vanilla cards declare a variable per
number, including one per applied power (`Bash` uses `new PowerVar<VulnerablePower>(2m)`
for its `{VulnerablePower:diff()}` token).

`PowerVar<T>` registers under the power's *type name*, not its model id, so the
placeholder for `POWER.EMBER_MIGHT_POWER` is `EmberMightPower`. When generating a card
that applies a power, emit the matching `PowerVar<T>` and read the applied amount from it,
which also makes an upgrade adjust the description and the effect together.

## 15. Design notes

Decisions that are not obvious from the code, and the reasoning behind them.

### A card's numbers are derived, not stored

Damage and block are effects, not separate card fields. An earlier version had both
(a `Damage` property *and* a damage effect), which meant two sources of truth that could
disagree: the number on the card face and the number actually dealt. Deriving the values
from the effect list removes that class of bug entirely, and `EffectResolver.DynamicVars`
is the only place the mapping lives.

### Shared effects are referenced by id, not by position

The reference is `SharedEffectId`, a stable string, rather than something like
"Applied Effect = Row 3". A positional reference silently repoints at a different effect
when rows are reordered, which produces wrong behaviour with no error. An id cannot.

### Powers declare no dynamic variables

A generated `CardModel` declares its numbers through `CanonicalVars`, so effects can read
them back with `DynamicVars["Damage"]`. A `PowerModel` has no equivalent, so the emitter
uses `EmitContext.HasDynamicVars` to fall back to literal amounts. Reading
`DynamicVars[...]` from a power throws at runtime, which is easy to write by accident when
sharing an emitter between the two.

### The generator and the validator share one resolver

`EffectResolver` expands references and derives numbers for both, so what validation
accepts cannot drift from generation produces. Anything that inspects a card's effects
should call it rather than reimplement the logic.

## 16. Multi-language text

The game ships **14 languages**, taken from its own `localization/` directory names:

```
deu eng esp fra ita jpn kor pol ptb rus spa tha tur zhs
```

A mod provides text per language by writing `localization/<lang>/<table>.json`, and the
game merges each into its base table. Verified in-game:

```
[INFO] Found loc table from mod: eng cards.json. Merging with base loc table
[INFO] Found loc table from mod: zhs cards.json. Merging with base loc table
```

**English is the fallback.** The game's localization tables fall back to English per key,
so a language that is not written displays the English text rather than a blank. Two
consequences shape the exporter:

- English must always be written. Skipping it (which an early version did, because
  English trivially "matches" itself) would leave every untranslated language blank.
- Other languages are written only when actually filled in. Writing an untranslated copy
  would replace the fallback with text identical to English, which is harmless but
  misleading to read in the pack.

Text is stored as a flat object keyed by language code (`{"eng": "...", "zhs": "..."}`),
which is the same shape as the game's own tables. Only filled languages are serialized.

### Validation is per language

Each language's description is checked separately, because different translations can
reference different values. Two bugs are easy to introduce here and were both caught by
testing:

- Checking only the English text hides a broken translation.
- Reporting a token as missing without first confirming it is absent produced false
  warnings on every correct description.

### Suggested descriptions are written per language

"Fill from effects" generates a sentence in the target language rather than translating
English, because card text is shown directly to players. Chinese phrasing follows the
game's own translations (格挡 for Block, 消耗 for Exhaust, 抽牌 for draw). A value read
from a live stat cannot be rendered as a number, so those are described instead
("回复生命，数值取决于自身的格挡。").

## 17. Calculated values

The game expresses "damage equal to your hand size" through `CalculatedVar`: a
`CalculationBaseVar` (the fixed part), a `CalculationExtraVar` (the per-unit step), and a
`CalculatedDamageVar` / `CalculatedBlockVar` whose `WithMultiplier` supplies the unit
count.

Verified from the game's own cards:

```csharp
// SoulStorm: 9 damage + 2 per Soul in the exhaust pile
new CalculationBaseVar(9m),
new ExtraDamageVar(2m),
new CalculatedDamageVar(ValueProp.Move).WithMultiplier(
    (CardModel card, Creature? _) => card.Owner.PlayerCombatState?.ExhaustPile.Cards.Count(...) ?? 0)
```

Three facts matter for generating this:

- `WithMultiplier` takes a **static** function; the engine throws if the delegate
  captures an instance (`"Multiplier calc must be static!"`).
- The signature is `(CardModel card, Creature? target)`, so a generated method has to
  match it exactly.
- The description placeholder is **`{CalculatedDamage:diff()}`** /
  **`{CalculatedBlock:diff()}`**, not `{Damage:diff()}`. Confirmed against shipped text:
  `"Deal {CalculatedDamage:diff()} damage."`.

Only damage and block have calculated variables. Everything else — a power amount, a card
count — has no such variable, so those values are computed in a generated helper method
instead. Applying a fixed amount to `CalculatedDamage` would read a variable that does not
exist for that kind.

### Value sources

A value source is `fixed + perUnit × source`, which covers everything the engine's
`CalculatedVar` can express, including the 0.5 coefficients vanilla uses ("half your
hand"). Sources the generator resolves:

| Group | Examples | Resolution |
|---|---|---|
| Status stacks | Strength, Poison, Plating | `creature.GetPower<T>()?.Amount ?? 0` |
| Creature stats | CurrentHp, MaxHp, MissingHp, Block | `creature.CurrentHp` and friends |
| Player resources | Energy, Stars, Gold, TurnNumber | `player.PlayerCombatState` |
| Pile counts | Hand, Draw, Discard, Exhaust | `player.PlayerCombatState!.Hand.Cards.Count` |

Pile counts and resources belong to the player, not to whichever creature a status is
attached to, so they are read from `Player` rather than from `Owner`.

## 18. Pile operations and triggers

Pile manipulation from a card body uses:

```
CardPileCmd.Add(card, PileType.Draw)                          // move
CardPileCmd.Add(card, PileType.Exhaust)                       // exhaust
CardPileCmd.AddGeneratedCardToCombat(card.CreateClone(), ...)  // copy into a pile
player.RunState.CreateCard<T>(player)                          // make a named card
```

`PileType` is `{ Draw, Hand, Discard, Exhaust, Play, ... }` and `CardPilePosition` is
`{ Bottom, Top, Random }`.

Per-effect triggers map to card hooks. Every hook below is `public virtual` on
`AbstractModel` **except** `OnTurnEndInHand`, which is `protected`; overriding with the
wrong visibility is a compile error.

| Trigger | Hook |
|---|---|
| OnPlay | `OnPlay` |
| AfterPlayed | `AfterCardPlayed` |
| OnDraw | `AfterCardDrawn` |
| OnDiscard | `AfterCardDiscarded` |
| OnExhaust | `AfterCardExhausted` |
| StartOfTurn / EndOfTurn | `AfterSideTurnStart` / `AfterSideTurnEnd` |
| EndOfTurnInHand | `OnTurnEndInHand` (protected) |

A card that reacts to turn events in any pile needs `ShouldReceiveCombatHooks => true`,
because `CardModel` otherwise only receives hooks while it is in a combat pile
(`Pile?.IsCombatPile ?? false`). Hooks that concern "this card" must also compare the
incoming card against `this`, or every card's events trigger the effect.

## 19. Game terminology for the interface

The editor shows game concepts (card types, rarities, keywords), so it needs the game's
own wording rather than a hand-written translation. Those names live in three tables:

| Concept | Table | Key |
|---|---|---|
| Card types | `extensions` | `EXTENSION.card.humanizedCardTypes.<type>` |
| Rarities | `gameplay_ui` | `CARD_RARITY.<RARITY>` |
| Keywords | `card_keywords` | `<KEYWORD>.title` |

Verified against the shipped Chinese table:

```
EXTENSION.card.humanizedCardTypes.attack  -> 攻击
CARD_RARITY.UNCOMMON                      -> 罕见
EXHAUST.title                             -> 消耗
```

Two traps when reading these:

- The same key text can appear in several tables, so the table name has to be part of the
  cache key or one table's entry silently overwrites another's.
- The editor's interface language is `zh` / `en`, while the game's localization folders are
  `zhs` / `eng`. Using the interface code directly as a table key finds nothing and falls
  back to English with no error — the failure is silent, so it is worth asserting on.

Concepts that do not exist in the game (ModForge's own vocabulary, such as value sources
and pile placements) carry their own bilingual names, kept separate from the game lookups
so a missing lookup is distinguishable from a term that was never in the game.

## 20. Orbs, summons, enchantments, and damage buffs

All four are reachable from a generated card. Signatures below were read from the decompiled
assembly (`D:\ModForge\decompiled`) and then confirmed by generating a mod that compiles
against the shipped `sts2.dll` and loads in game.

### Orbs

`MegaCrit.Sts2.Core.Commands.OrbCmd`:

```csharp
Task AddSlots(Player player, int amount);
Task Channel<T>(PlayerChoiceContext ctx, Player player) where T : OrbModel;
Task EvokeNext(PlayerChoiceContext ctx, Player player, bool dequeue = true);
```

Orbs are model classes, not an enum: `LightningOrb`, `FrostOrb`, `DarkOrb`, `PlasmaOrb`,
`GlassOrb` (all in `MegaCrit.Sts2.Core.Models.Orbs`). Channeling is per-orb, and it evokes
the front orb automatically when the queue is full — so a loop matches vanilla behaviour.
`OrbCmd.Evoke(...)` itself is **private**; go through `EvokeNext` / `EvokeLast`.

### Summon

`MegaCrit.Sts2.Core.Commands.OstyCmd.Summon(PlayerChoiceContext, Player, decimal amount, AbstractModel? source)`
where `amount` is the summon's HP. Vanilla cards pair it with a `SummonVar` so the number
renders on the card face.

There is **no generic "summon any creature"** command — `OstyCmd` is hard-wired to the
Necrobinder pet. Other pets exist but are only created from relic internals via
`PlayerCmd.AddPet<T>`, which needs a custom `MonsterModel` plus art and scenes. A card-only
generator can therefore offer "summon" only in the Osty sense.

### Enchantments

`MegaCrit.Sts2.Core.Commands.CardCmd`:

```csharp
T? Enchant<T>(CardModel card, decimal amount) where T : EnchantmentModel;
EnchantmentModel? Enchant(EnchantmentModel enchantment, CardModel card, decimal amount);
void ClearEnchantment(CardModel card);
```

There is no batch call; the game loops (`GnarledHammer`). Each `Enchant<T>` pulls a fresh
mutable copy from the model DB, so looping is correct and does not share one instance across
cards. Vanilla enchantments live in `MegaCrit.Sts2.Core.Models.Enchantments` (22 of them,
e.g. `Sharp`, `Corrupted`, `Momentum`). Custom enchantments would need an icon asset.

### Damage buffs

The damage pipeline calls `ModifyDamageAdditive` / `ModifyDamageMultiplicative` on every
hook listener, and **cards in the deck are themselves listeners** (`RunState.IterateHookListeners`
adds every deck card and its enchantment). So:

- **"All Strike cards +N this combat"** — a generated `PowerModel` that returns `Amount` when
  `cardSource.Tags.Contains(CardTag.Strike)`. `CardTag` is `None, Strike, Defend, Minion, OstyAttack, Shiv`.
  This is exactly how the vanilla `StrikeDummy` relic and `AccuracyPower` work, so it matches
  the game's own semantics rather than matching card names.
- **"This card +N this combat"** — add to `DynamicVars["Damage"].BaseValue`; combat cards are
  throwaway copies, so the deck original is untouched (vanilla `Claw` does this).
- **"This card +N permanently"** — needs a `[SavedProperty]` field, because
  `SerializableCard` persists only `Id`, `CurrentUpgradeLevel`, `Enchantment`, `Props`
  (saved properties) and floor. The `DynamicVar` values themselves are *not* saved. Apply the
  boost by **delta** rather than recomputing, so an upgrade's contribution is not clobbered.
  Also write to `DeckVersion` (the deck copy) or the change is lost when combat ends — see
  vanilla `TheScythe` / `GeneticAlgorithm`.

### Repeat count

`Repeat` is a loop around the whole effect, so it reads as a suffix on the sentence
("造成 4 点伤害，重复 3 次。"). The generated code wraps the statements in a `for` loop.

## 21. Reading card stats from the game assembly

Card numbers are **not** in any data file. The PCK holds only localization, and card
descriptions are templates (`造成{Damage:diff()}点伤害。`) — the actual constants are compiled
into `data_sts2_windows_x86_64/sts2.dll`. So the editor reads the CIL directly
(`src/ModForge.Core/Catalog/CardStatsReader.cs`), using `System.Reflection.Metadata`.

Validated against an independent snapshot of the same game version (578 cards, produced by a
different tool that parses the same IL):

- **energy cost, card type, rarity, target: 578/578 exact** (hard line — these are literal
  constructor arguments)
- pool 568/578 (the other 8 have no portrait, which is the only source for pool)
- ~720 values, ~486 upgrade deltas
- 16 known differences, each explained in `tools/statstest` (computed-at-runtime values,
  cards declaring several same-named vars, and two cards whose art lives in another pool's
  folder). The tool fails on any *unexpected* difference, so a regression cannot hide.

The card definitions turned out to be highly regular, which is what makes this tractable:

```
.ctor          ldc.i4 <cost> <type> <rarity> <target> <showInLibrary>; call CardModel::.ctor
CanonicalVars  ldc.i4.N (+ newobj Decimal) ; newobj DamageVar/BlockVar/PowerVar<T>/…
OnUpgrade      get_DynamicVars → get_<Prop> → ldc.i4.N (+ Decimal) → UpgradeValueBy
```

Things that bit us, recorded so they are not rediscovered:

- **Hand-rolled opcode tables lose cards silently.** One missing single-byte opcode
  (`ldarg.0` = `0x02`) stops the parse mid-method and the card yields *nothing* — which looks
  like "the reader is broken", not "one opcode is missing". Build the table by reflecting over
  `System.Reflection.Emit.OpCodes` instead.
- **`stelem` has a whole family** (`0x9B`–`0xA2`). Keyword and value arrays are assembled with
  them; mapping only one variant means arrays read as empty.
- **The first constructor argument is not "the previous number".** `DamageVar(6m, ValueProp.Move)`
  compiles to `ldc.i4.6; newobj Decimal; ldc.i4.8; newobj DamageVar` — naive backward search
  returns `ValueProp.Move` (8), so every Strike "dealt 8 damage". Track the evaluation stack,
  and remember `Decimal`'s argument is the value the outer constructor wants.
- **Signature type handles are coded indexes.** In generic signatures
  (`PowerVar<StrengthPower>`) the operand is a TypeDefOrRef *coded* index; feeding it to
  `MetadataTokens.EntityHandle` decodes the wrong type. Shift right by 2 and mask the low bits.
- **`1m` is not a literal.** The single most expensive lesson here: an upgrade delta written
  `UpgradeValueBy(1m)` compiles to `ldsfld System.Decimal::One`, so a naive "read the numeric
  literal" scan finds *nothing* and ~half of all upgrade deltas silently go missing. `Decimal`'s
  named constants (`Zero`/`One`/`MinusOne`/`MaxValue`/`MinValue`) live in the **framework**
  assembly, so there is no field definition to find — the values have to be special-cased.
  Other constants really are hoisted into a compiler-generated type, and those must be resolved
  through `MemberReference` (not just `FieldDefinition`) to reach the field.
- **Field data must be read from the file, not the metadata image.** `PEReader.GetEntireImage()`
  covers only the metadata blob; field initial data (static array literals, hoisted constants)
  sits in another section. Convert the RVA to a file offset via the PE section table and read
  from the full image bytes instead.
- **Cost/type/rarity/target are 100% reliable** (literal constructor arguments). Base *values*
  are reliable except for ~27 computed cards (`CalculatedDamageVar`), which genuinely have no
  constant — those are flagged `HasComputedValues` rather than guessed.
- **Pool is inferred from card art paths** (`images/packed/card_portraits/<pool>/<stem>.png.import`).
  That is the only ready source; ~8 cards have no portrait and therefore no pool.

### X-cost and "free to play"

`CardModel.SpendResources()` spends energy and stars **before** `OnPlay` runs
(`CardModel.cs:1815`), and an X-cost card records what it spent in
`EnergyCost.CapturedXValue`. So an X-cost card cannot read "current energy" in its effect — by
then it is 0. Use `ResolveEnergyXValue()` / `ResolveStarXValue()` instead (they also apply
`Hook.ModifyXValue`, so relics like Chemical X work).

For cost manipulation the game has, on `CardEnergyCost`:
`SetUntilPlayed` / `SetThisTurnOrUntilPlayed` / `SetThisTurn` / `SetThisCombat` and the matching
`Add*` variants (all take `reduceOnly`). Note that `CardModel.SetToFreeThisTurn()` /
`SetToFreeThisCombat()` clear **both** energy and stars, while setting an energy cost only
clears energy — the two are different effects and the editor presents them separately.
