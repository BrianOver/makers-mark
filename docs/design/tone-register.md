# Tone register — Maker's Mark (2026-07-18)

Extracted verbatim from docs/design/2026-07-18-variety-tone-direction.md §1 (the direction of record).

## 1. Tone amendment — lighter without losing identity

### Constraint honored: voices frozen, packs carry the tone

`VoiceProfile.Voices` is a frozen 4-entry modulo pick — `["gruff", "dramatic", "wry", "omen"]` (`sim/GameSim/Flavor/VoiceProfile.cs:31`); the freeze note (`:20-23`) allows append only with a content-change decision on record, and ANY length change shifts the modulo and re-voices every hero in every campaign. So the tone shift is implemented **inside packs, per base key**, as register targets on the ≥4-variant pools (conformance floor, `TavernPack.cs:26-27`). Pure data; no mechanism; no voice-list edit.

### Per-key register targets (TavernPack appends)

The 8 shipped base keys and their committed slots (`sim/GameSim/Flavor/Packs/TavernPack.cs:31-70`) — the engine's validation requires **every slot verbatim in every variant**:

| Base key | Slots | Register | Change |
|---|---|---|---|
| `heroDied` | hero, cause, floor | grim stays grim — the identity anchor | +1 *warm* (not comic) variant per voice: a toast, a fond detail. Never jokes. |
| `killingBlow`, `lethalSave` | hero, item, floor | pride + warmth | +2 variants/voice; attribution-thesis warmth ("that dent is sentimental") |
| `provisioned`, `potionLifesave`, `breakpointClear` | hero, item, floor | comedy-forward | +2–3 comic variants/voice |
| `floorRecordSet` | hero, floor | comedy-forward | +2–3 comic variants/voice |
| `recruitArrived` | hero | comedy-forward | +2–3 comic variants/voice |
| Fallbacks | — | **unchanged** | verbatim-history rule (`TavernPack.cs:20-24`) |

Comic mode per voice: omen = failed portents; gruff = invoices/lectures; dramatic = grandiosity about mundane things; wry stays wry. Omen keeps its full grim register **only** on `heroDied` (and future wipe events). Same treatment applies to `LedgerPack` `survived`/`died` (`sim/GameSim/Flavor/Packs/LedgerPack.cs:37,40`) and `FactionPack` `favored`/`cooled` (`sim/GameSim/Flavor/Packs/FactionPack.cs:34,37`, slots `{faction}`/`{direction}`) — voice one faction comic-bureaucratic (idea #18), flavor-only.

### Register samples (10 lines, slot-complete against the shipped schemas)

1. gruff/`provisioned`: `Sold {hero} a {item} for floor {floor}. Charged extra for the lecture on holding it right. No refunds on the lecture.`
2. wry/`provisioned`: `{hero} asked if the {item} comes in 'lucky.' It does now, apparently. Floor {floor} can check the paperwork.`
3. dramatic/`recruitArrived`: `{hero} has ARRIVED! The door has been informed. It remains a door, but a prouder one.`
4. omen/`recruitArrived`: `The signs foretold {hero}'s coming. The signs also foretold a rain of frogs. One out of two. Again.`
5. gruff/`floorRecordSet`: `{hero} hit floor {floor}. Deepest yet. Bought a round, then counted the change. Twice.`
6. wry/`killingBlow`: `{hero}'s {item} did the hard part on floor {floor}. {hero} did the yelling. Both essential, reportedly.`
7. dramatic/`lethalSave`: `DEATH reached for {hero} on floor {floor} — and struck {item} instead! The smith shall hear of this dent. At length.`
8. omen/`potionLifesave`: `A red vial on floor {floor}, and {hero} breathing yet — the {item} gets the credit the portents wanted. The portents have been asked to cite their sources.`
9. gruff/`breakpointClear`: `Floor {floor} gate's open. {hero}'s {item} did the arguing. Iron argues best.`
10. wry/`heroDied` (warm, NOT comic): `Floor {floor}. {cause}. {hero} would have called it 'a Tuesday.' Raise a quiet one.`

*(Line 8 corrected during verification: the original draft omitted `{item}`, which fails `FlavorEngine` slot validation.)*

### New comedy surfaces (pack-shaped; seams flagged)

1. **Mundane-gossip variants — TODAY, S.** Smuggle tavern-mishap color into existing keys as appended variants (lost-cat references in `recruitArrived`, dart-tournament asides in `floorRecordSet`). No new subject needed. This is wave-C row C4.
2. **ShopPack (`itemBought/{voice}`, slots hero/item/price)** — per-class shopping quirk lines. Seam needed: stamped purchase event + `Describe` arm in `GossipGenerator.cs` (core file). Wave-D row D7 territory.
3. **Comic camp events** — pack entries for the camp checkpoint. Dependency: staged-resolution plan `docs/plans/2026-07-17-002` U4 camp verbs (BOARD gate **G5**). Wave-D row D5.
4. **Fan letters (`letterReceived/{voice}`, slots hero/item/kills/saves)** — `ItemMemory(Item, Kills, Saves)` exists on the contract (`sim/GameSim/Contracts/Heroes.cs:34,47`). Seam needed: letter emitter + surface. Wave-D row D6, highest attribution-thesis payoff.
5. **`ToolAssist` finish** — pre-reserved beat with no emitter (`sim/GameSim/Contracts/Enums.cs:52`, untold arm `GossipGenerator.cs:190`, pinned by `GossipTests.Generator_ToolAssistBeat_StaysUntold`). Cheapest new-subject move (S–M). Wave-D row D7 first step.

### Guardrails

Deaths and wipes never joke — warmth yes, punchlines no. Comedy is deadpan/understated (Graveyard Keeper register), never zany: no puns in death lines, no fourth-wall, no modern slang. Gossip stays capped at 3 lines/day (`GossipGenerator.MaxLinesPerDay`, `sim/GameSim/Drama/GossipGenerator.cs:39`). Pack appends are byte-sensitive → prose-golden re-baseline per batch; one PR per batch.

---

