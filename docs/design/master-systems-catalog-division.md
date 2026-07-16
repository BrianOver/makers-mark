# Master systems catalog — core vs add-on division (decided 2026-07-16)

Companion to `master-systems-catalog.md` (Brian's 11-pillar expansion, preserved verbatim).
This doc is the ORCHESTRATOR'S ruling on what each pillar means for OUR codebase: what must
be built in the main/primary session (mechanisms, contracts, registries, resolver hooks,
anything touching `Contracts/`, `GameComposition`, kernel, or balance goldens) versus what
fans out to task-Claudes as data-shaped add-ons per `docs/addon-guide.md`.

## Standing adaptations (apply to EVERY pillar)

1. **Stack transposition.** The catalog's prompts target Godot 4.3 GDScript with game rules
   in engine scripts. Our locked stack: Godot 4.6.3-stable .NET, C#, ALL rules in
   `sim/GameSim` (pure, deterministic), `godot/` adapter-only. Every catalog prompt gets
   rewritten to our contract style at dispatch time; `.gd` file lists map to sim modules +
   thin panels. The originals stay verbatim as design intent.
2. **Integer math.** Catalog formulas use floats, `log10`, `e^-λt`. Hard rule: integer/
   fixed-point equivalents (e.g. decay via integer half-life steps, log via lookup table)
   — designed per system when its core is built. The catalog's formulas define INTENT
   (shape of the curve), not literal code.
3. **Runtime LLM (System 2 / Ollama): PARKED — needs Brian's decision.** Standing decision
   from planning: NO runtime LLM in NPC decision loops (originally token cost; determinism
   is the deeper reason — same seed must mean same world). Ollama is local/free, so cost no
   longer decides it, but sim-loop LLM still breaks determinism and the golden-replay gate.
   Viable middle path if wanted: sim stays 100% deterministic and emits structured events;
   an OPTIONAL presentation-layer LLM rewords those events into dialogue flavor (never
   feeding back into sim state). Until Brian rules, all pillar work uses deterministic
   template-driven text (the existing gossip system's approach).
4. **Phase-gated prompts.** The catalog's Idea→Research→Confirm→Plan→Confirm→Code pipeline
   with user checkpoints maps cleanly onto our brainstorm→plan→work workflow — keep it for
   add-on dispatches where Brian wants to steer content.

## Per-pillar ruling

Format: what lands where. "MAIN" = orchestrating session (core). "ADD-ON" = task-Claude
after the named core exists. Roadmap phase refs are to `2026-07-15-001-roadmap-beyond-v1.md`.

### Pillar 1 — Hero Classes & Sociology → roadmap P3 (classes) + P5 (personality)
- MAIN: sociological state contracts (trauma level, affinity/trust tables, self-preservation),
  party-formation priority scoring hook (the P_party shape, integerized), trust-fracture
  mechanism, class-modifier slot in ClassDefinition.
- ADD-ON: each archetype (Warrior/Mage/Rogue/Priest) as a ClassDefinition data unit incl.
  its β-modifier + alignment profile; friction-profile variants as trait data.

### Pillar 2 — Neutral NPCs & Town Factions → roadmap P4 (economy) + P5 (drama)
- MAIN: faction registry (FactionDefinition + standing state on GameState), tax/tariff
  phase system (T_town shape, integerized — log via bracket table), standing-driven price
  modifier hook, inspection/penalty event mechanism.
- ADD-ON: each faction (Crownsguard, Shadow Syndicate, Grand Conservatory) as data —
  perks, punishments, vendor pools, tariff tables; neutral-NPC vendor variants.

### Pillar 3 — Hero Quests & Dynamic Bounties → extends v1 bounty spine; slot into P4
- MAIN: bounty crowdfunding mechanism (NPC contributions), quest-desire scoring extension
  (D_q folds into existing bounty-judging), party competition + disgruntlement state,
  cleared-node arrival handling. This is spine surgery on `Bounties/` + `Drama/` — stays here.
- ADD-ON: quest/bounty TYPE templates (escort, cull, retrieval) as data once the quest-type
  registry exists.

### Pillar 4 — Player Workstation Missions → NEW core, schedule after P2 (own phase or P4 rider)
- MAIN: supply-contract system (time-gated orders = bounties pointed AT the player; contract
  registry + grading + faction-rep payout), workstation stability state, and the CRITICAL
  seam: minigames are PRESENTATION — a minigame's outcome enters the sim as a graded action
  input (e.g. CraftAction gains an optional performance grade), keeping the sim pure and
  replays deterministic. Design that action contract here.
- ADD-ON: each profession's workstation minigame (godot-side UI unit: forge rhythm, alembic
  balance) + contract content packs. Fully parallelizable once the seam exists.

### Pillar 5 — Map Nodes & Dungeon Graphs → roadmap P4 venues, direct fit
- MAIN: venue/node-graph registry (nodes, edges, hazards, closures), travel-fatigue
  mechanic (F_travel integerized) feeding combat stats, dynamic closure events, routing.
- ADD-ON: individual map nodes/venues as VenueDefinition data (type, hazard coefficient,
  loot pool, monster table) — the roadmap's headline fan-out.

### Pillar 6 — Dynamic Chronicle & Lore → extends shipped U14 chronicle; slot into P5
- MAIN: lore-prominence scoring (I_lore with integer decay steps instead of e^-λt) over the
  EXISTING event log, rumor-mill selection hook feeding the existing gossip system,
  chronicle retention window. No SQLite — our snapshot/event-log model already covers it.
- ADD-ON: gossip/rumor template packs keyed to event types.
- PARKED: the Ollama dialogue layer (see standing adaptation 3).

### Pillar 7 — Vanity, Ego & Cosmetics → slot into P5 (personality) after P3
- MAIN: EgoValue state + ego-driven threat-tolerance hook in expedition targeting
  (Θ_threat integerized), bragging behavioral state in town/tavern layer, vanity item
  category on Item (stats-neutral, ego-weighted).
- ADD-ON: cosmetic content sets (dyes, glows, glyphs) as data; visual treatments
  (shaders/particles) as presentation units per set.

### Pillar 8 — Spells, Talents & Abilities → roadmap P3 augment layer, direct fit
- MAIN: AbilityDefinition registry + combat-trigger evaluation in the resolver (U_ability
  as integer threshold rule over recorded state; deterministic trigger order), scroll
  transcription as a profession output type, ability slots on Hero/ClassDefinition.
- ADD-ON: the 8 launch abilities (2 per class) as data units — textbook parallel fan-out;
  scroll recipe packs via the profession add-on path.

### Pillar 9 — Overworld Enemies & Monster Dens → roadmap P4 venues extension
- MAIN: den escalation clock (InfectionRate per venue, day-tick), mutation-modifier
  framework (procedural modifiers drawn from the kernel RNG stream), world-boss lockdown
  events, town-raid event mechanism, S_mob scaling (integer).
- ADD-ON: mutation traits (Fire Shielded, Acidic Spit, ...) as data; den/boss types per venue.

### Pillar 10 — Player Threats & Disasters → roadmap P5 drama, player-facing arm
- MAIN: disaster scheduler (deterministic, seeded), suspicion/vulnerability state
  (V_bandit integerized), mitigation mechanism (mercenary/warding spend), audit mechanic
  tied to faction standing + material provenance (needs Item history — already exists).
- ADD-ON: disaster templates (audit, extortion, fire, strike) as data + their panel UIs.

### Pillar 11 — Master Professions → LARGELY SHIPPED (our P1) + in-flight P2
- Already covered: profession-as-data kernel (P1, merged), consumable demand loop (P2 spine,
  in flight), U_buy ≈ existing legible ShoppingAi (extend TrustFactor when P5 relationships land).
- MAIN (new bits): legendary behavior-altering items (item → hero-behavior modifier hook —
  contract work, pairs with Pillar 7 ego hook), 4-tier expansion of the tier-gate model.
- ADD-ON: everything else here is already the addon-guide path; workstation minigames per
  profession ride the Pillar 4 seam.

## Sequencing impact on the roadmap

Roadmap P2–P5 phases absorb the pillars as noted above. Net-new work not in the roadmap:
Pillar 4's workstation/supply-contract core (player-facing active play) and Pillar 10's
disaster core. Proposed slots: supply contracts ride P4 (they generalize bounties/commissions);
disasters ride P5 (drama). Minigame presentation can start any time after its seam is defined.

## Dispatch rule of thumb

If the unit ships a `*Definition` + tests and asks for one registration line — task-Claude.
If it ships a new contract type, phase system, resolver read, or scoring hook — here.

HOW a catalog prompt converts to this stack (mapping table + worked example) is the standing contract in `catalog-prompt-transposition.md` — fill it at dispatch, never re-derive.
