# Tone register — Maker's Mark (2026-07-18)

Extracted from §1 of the 2026-07-18 variety-tone direction doc. That doc is deleted (git history holds it); the pack files cite it as the C4 tone pass (`TavernPack.cs`, `FactionPack.cs` doc comments), and this file is the register of record.

## 1. Tone amendment — lighter without losing identity

### Constraint honored: voices frozen, packs carry the tone

`VoiceProfile.Voices` is a frozen 4-entry modulo pick — `["gruff", "dramatic", "wry", "omen"]` (`sim/GameSim/Flavor/VoiceProfile.cs:31`); the freeze note (`:20-23`) allows append only with a content-change decision on record, and ANY length change shifts the modulo and re-voices every hero in every campaign. So the tone shift is implemented **inside packs, per base key**, as register targets on the ≥4-variant pools (conformance floor, `TavernPack.cs:39`). Pure data; no mechanism; no voice-list edit.

### Per-key register targets (TavernPack appends)

Nine base keys ship (`sim/GameSim/Flavor/Packs/TavernPack.cs:52-82`, slots in `SlotNames` `:88`); the eight below carry register targets, and `venueGraduated` (slot hero only, forward-ladder L5) landed after this table and is not register-targeted here. The committed slots — the engine's validation requires **every slot verbatim in every variant**:

| Base key | Slots | Register | Change |
|---|---|---|---|
| `heroDied` | hero, cause, floor | grim stays grim — the identity anchor | +1 *warm* (not comic) variant per voice: a toast, a fond detail. Never jokes. |
| `killingBlow`, `lethalSave` | hero, item, floor | pride + warmth | +2 variants/voice; attribution-thesis warmth ("that dent is sentimental") |
| `provisioned`, `potionLifesave`, `breakpointClear` | hero, item, floor | comedy-forward | +2–3 comic variants/voice |
| `floorRecordSet` | hero, floor | comedy-forward | +2–3 comic variants/voice |
| `recruitArrived` | hero | comedy-forward | +2–3 comic variants/voice |
| Fallbacks | — | **unchanged** | verbatim-history rule (`TavernPack.cs:20-24`) |

Comic mode per voice: omen = failed portents; gruff = invoices/lectures; dramatic = grandiosity about mundane things; wry stays wry. Omen keeps its full grim register **only** on `heroDied` (and future wipe events). Same treatment applies to `LedgerPack` `survived`/`died` (`sim/GameSim/Flavor/Packs/LedgerPack.cs:49,52`) and `FactionPack` `favored`/`cooled` (`sim/GameSim/Flavor/Packs/FactionPack.cs:55,58`, slots `{faction}`/`{direction}`) — voice one faction comic-bureaucratic (idea #18), flavor-only.

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

### Guardrails

Deaths and wipes never joke — warmth yes, punchlines no. Comedy is deadpan/understated (Graveyard Keeper register), never zany: no puns in death lines, no fourth-wall, no modern slang. Gossip stays capped at 3 lines/day (`GossipGenerator.MaxLinesPerDay`, `sim/GameSim/Drama/GossipGenerator.cs:50`). Pack appends are byte-sensitive → prose-golden re-baseline per batch; one PR per batch.

---

