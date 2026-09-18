# Tone register — Maker's Mark

*The voice the flavor packs are written in. Extracted from §1 of the 2026-07-18 variety-tone
direction doc; that doc is deleted (git history holds it), the pack files cite it as the C4 tone
pass (`TavernPack.cs`, `FactionPack.cs` doc comments), and this file is the register of record. It
describes the register as shipped; the plan of record for what changes next is `MAKERS-MARK.md` §11.*

## 1. Lighter without losing identity

### Constraint honored: voices frozen, packs carry the tone

`VoiceProfile.Voices` is a frozen four-entry modulo pick — `["gruff", "dramatic", "wry", "omen"]`
(`sim/GameSim/Flavor/VoiceProfile.cs`). The freeze note allows append only with a content-change
decision on record, and ANY length change shifts the modulo and re-voices every hero in every
campaign. So the register is carried **inside packs, per base key**, as the target each variant pool
is written to. Pure data; no mechanism; no voice-list edit.

### Per-key register targets (`TavernPack`)

Nine base keys ship (`TavernPack.cs` key constants; `TavernPack.SlotNames` is the single source of
truth for slots). The eight below carry register targets; `venueGraduated` (slot hero only,
forward-ladder L5) landed after the C4 pass and has no register target authored — its pack comment
says only that the line names the graduate, never the dungeon they left. The engine's validation
requires **every slot verbatim in every variant**, and the pack conformance tests sweep it.

| Base key | Slots | Register |
|---|---|---|
| `heroDied` | hero, cause, floor | grim stays grim — the identity anchor. Warm variants allowed (a toast, a fond detail). Never jokes. |
| `killingBlow`, `lethalSave` | hero, item, floor | pride + warmth; attribution-thesis warmth ("that dent is sentimental") |
| `provisioned`, `potionLifesave`, `breakpointClear` | hero, item, floor | comedy-forward |
| `floorRecordSet` | hero, floor | comedy-forward |
| `recruitArrived` | hero | comedy-forward |
| `venueGraduated` | hero | none authored (see above) |
| Fallbacks | — | **unchanged**: the six v1 kinds keep the old hardcoded `GossipGenerator` line verbatim so existing saves stay honest about where their lines came from |

Comic mode per voice: omen = failed portents; gruff = invoices and lectures; dramatic = grandiosity
about mundane things; wry stays wry. Omen keeps its full grim register **only** on `heroDied` (and
any future wipe event). The same treatment applies to `LedgerPack` (`survived` / `died`) and
`FactionPack` (`favored` / `cooled`, slots `{faction}` / `{direction}`) — one faction is voiced
comic-bureaucratic, flavor only.

### Register samples (slot-complete against the shipped schemas)

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

*(Line 8 was corrected during verification: the original draft omitted `{item}`, which fails
`FlavorEngine` slot validation.)*

### What has no voice yet

Stated as description, not as work owed. `BeatType.ToolAssist` is reserved on the contract with no
emitter, and the gossip generator's arm for it is deliberately empty — `GossipTests.
Generator_ToolAssistBeat_StaysUntold` pins the silence. A counter sale (`CounterSaleClosed`) has no
gossip subject; the town never mentions the one sale you close face to face. `ItemMemory(Item,
Kills, Saves)` is recorded on every hero and read by the Sentimental shopping gate, and nothing
speaks it. `docs/reference/text-census.md` §10 carries the full census.

### Guardrails

Deaths and wipes never joke — warmth yes, punchlines no. Comedy is deadpan and understated
(Graveyard Keeper register), never zany: no puns in death lines, no fourth wall, no modern slang.
Gossip stays capped at three lines a day (`GossipGenerator.MaxLinesPerDay`). Pack appends are
byte-sensitive — every batch re-baselines the prose goldens, one PR per batch.
