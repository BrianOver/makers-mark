# Master Game Systems Design & Implementation Catalog

> Provenance: supplied verbatim by Brian on 2026-07-16 as the expanded future-pillar
> catalog ("these are more to expand the future to-do"). PRESERVED AS WRITTEN — the
> original targets Godot 4.3 GDScript + GOAP + local LLM (Ollama), which differs from
> this repo's locked stack (Godot 4.6.3 .NET, C#, pure deterministic sim, no runtime
> LLM). Do NOT implement directly from this document: the adaptation and the
> core-vs-add-on division live in `master-systems-catalog-division.md` next to this
> file, and every build still flows through the roadmap
> (`docs/plans/2026-07-15-001-roadmap-beyond-v1.md`) and the add-on contract
> (`docs/addon-guide.md`).

This blueprint acts as the comprehensive systems ledger for our 2D Support-NPC Management Game. It details all 11 core systems, establishing their mathematical formulas, game-loop mechanics, engine interactions (System 1: GOAP), and cognitive layers (System 2: Local LLM).

Each system now includes a dedicated Agentic Claude Task Prompt engineered to guide Claude Code through a strict developmental pipeline:

$$\text{Idea} \longrightarrow \text{Research} \longrightarrow \text{Confirm Idea} \longrightarrow \text{Make Plans} \longrightarrow \text{Confirm with User} \longrightarrow \text{Create/Code}$$

## Pillar 1: Hero Classes & Character Sociology

### Detailed System Mechanics & Ideas

AI Heroes are autonomous agents driven by four baseline class archetypes: Warrior (Tank), Mage (Glass Cannon DPS), Rogue (Physical DPS/Stealth), and Priest (Healer/Support).

The Ego & Trauma Thresholds: Heroes possess a dynamic trauma_level counter. If a Priest fails to heal a Warrior before they fall below $15\%$ HP, the Warrior develops a temporary "Trust Fracture" status, decreasing their group affinity.

Sociological Alignment Profiles:

- Priests possess a high innate Altruism rating; they seek out parties with low average HP.
- Rogues possess high Pride and low Altruism; they prefer solo routes and hunt high-value targets, ignoring distressed group requests.
- Warriors seek high-threat encounters, but scale their aggression exponentially if a Priest with high affinity is in their immediate vicinity.

### Mathematical Framework: Hero Behavior Drive

The priority score for an agent to seek a party ($P_{party}$) is modeled as:

$$P_{party} = \left( 100 - \text{SelfPreservation} \right) \times \left( \frac{\text{CurrentThreatLevel}}{\text{IndividualPower}} \right) \times \beta_{class}$$

Where:

- $\text{SelfPreservation} \in [0, 100]$ is a dynamic behavioral value representing fear.
- $\text{IndividualPower}$ is the sum of the hero's gear level, active buffs, and current health percentage.
- $\beta_{class}$ is a class-specific modifier: $\text{Priest} = 1.8$, $\text{Warrior} = 1.5$, $\text{Rogue} = 0.4$, $\text{Mage} = 1.1$.

### CLAUDE TASK PROMPT: HERO SYSTEMS & SOCIOLOGY

```
================================================================================
AGENTIC TASK PROMPT: HERO SYSTEMS & SOCIOLOGY (GODOT 4.3)
================================================================================
You are an expert Game AI Architect. Your task is to design, research, plan, and execute the Hero Class and Character Sociology System.

Do not write raw code immediately. Follow this strict sequence of phases:

PHASE 1: IDEA EXPLORATION & EXPANSION
- Review Pillar 1's specifications (Ego, Trauma Thresholds, Class Profiles).
- Propose 3 unique variations of Class-based Friction Profiles (e.g., how Mages steal threat from Warriors, or how Rogues refuse to share gold).
- Detail how these sociological traits will manifest in behavioral output.

PHASE 2: GODOT ENGINE FEASIBILITY & RESEARCH
- Research the optimal way to structure Hero State Nodes in Godot 4.3.
- Is it better to use dynamic Godot Resources (HeroStats) or a custom Component-based node hierarchy (e.g., TraumaComponent, SociologyBlackboard)?
- Write a short summary analyzing the memory overhead of managing 50+ concurrent HeroAgents with active State Nodes.

PHASE 3: USER CHECKPOINT - PROPOSAL CONFIRMATION
- Stop here and output your proposed design directions, your research conclusions, and your architectural recommendations.
- Prompt the user to select their preferred path and confirm your ideation.
- DO NOT PROCEED TO PHASE 4 UNTIL THE USER RESPONDS.

PHASE 4: SYSTEM DESIGN & PLANNING
- Once the user confirms, design the Class Diagrams, blackboard payload layouts, and System 1 to System 2 communication pipelines.
- Map out the exact JSON payloads we will send to Ollama to update a Hero's personality when they experience a trauma event.

PHASE 5: USER CHECKPOINT - PLAN CONFIRMATION
- Share your diagrams, JSON schemas, and structural layout.
- Ask the user to confirm the structural interfaces before any code is generated.
- DO NOT PROCEED TO PHASE 6 UNTIL THE USER RESPONDS.

PHASE 6: CREATION & CODING
- Generate production-ready Godot 4.3 GDScript files:
  1. `HeroAgent.gd` (Base class extending CharacterBody2D).
  2. `HeroStats.gd` (Dynamic custom resource class).
  3. `SociologyBlackboard.gd` (Utility class tracking affinity tables and trauma profiles).
Ensure no placeholders or missing functions. Include detailed, production-grade comments.
================================================================================
```

## Pillar 2: Neutral Characters & Town Factions

### Detailed System Mechanics & Ideas

Neutral NPCs populate the town, representing guilds, merchants, tax collectors, and bounty brokers. They act as indirect systemic counter-weights, offering quests, shifting raw market prices, or threatening players with fines if shop safety rules are violated.

The Three Faction Triumvirate:

- The Crownsguard (Law): Demands adherence to shop rules, taxes, and high safety standards. High standing reduces taxes and stops municipal inspections.
- The Shadow Syndicate (Black Market): Offers forbidden raw materials (e.g., Necromantic Bone Dust, Demonic Core Ores) but drops your reputation with the Crownsguard.
- The Grand Conservatory (Mages): Controls the flow of magical enchanting catalysts, granting access to high-tier scroll and enchantment blueprints.

Dynamic Tariff Adjustments: Faction standing alters the import cost of raw ores/herbs. A high standing with the Conservatory lowers raw enchanting material costs by up to $30\%$.

### Mathematical Framework: Tax and Inflation Vector

The town tax rate ($T_{town}$) assessed by the King's Tax Collector is modeled as:

$$T_{town} = T_{base} + \left( \alpha \times \log_{10}(\text{PlayerTotalGold}) \right) + \left( \gamma \times \text{TownChaosValue} \right)$$

Where:

- $T_{base} = 0.05$ (5% baseline taxation level).
- $\alpha = 0.025$ is the scaling factor penalizing excessive hoarding of liquidity.
- $\gamma = 0.15$ is the penalty weight scaling with the active TownChaosValue $\in [0, 1]$.

### CLAUDE TASK PROMPT: NEUTRAL NPC & FACTION SYSTEM

```
================================================================================
AGENTIC TASK PROMPT: TOWN FACTIONS & TAX ENGINE (GODOT 4.3)
================================================================================
You are a Lead Game Economy and Systems Designer. Your task is to design, research, plan, and code the Neutral NPC & Faction balance system.

Follow this strict sequence of phases:

PHASE 1: IDEA EXPLORATION
- Propose 3 detailed scenarios where Factions directly clash in the town square.
- Design faction reward perks (e.g., specialized blueprints) and faction punishments (e.g., the Crownsguard blockade your shopfront).

PHASE 2: SYSTEM 1 RESEARCH
- Research mathematical stability in Godot 4.3. How do we prevent floating-point calculation drift when compounding daily tax calculations?
- Review the performance implications of tracking 10+ dynamic faction registers in a centralized GameState singleton.

PHASE 3: USER CHECKPOINT - PROPOSAL CONFIRMATION
- Present your faction clash concepts and tax stability research findings.
- Seek confirmation from the user on how punitive the faction lockouts should be.
- STOP AND WAIT FOR USER INPUT before moving to planning.

PHASE 4: ARCHITECTURAL PLANNING
- Design the `FactionManager` and `TaxCollector` script lifecycles.
- Plan the class interfaces, signal triggers (e.g., `faction_changed`), and tax schedule tick-rates.

PHASE 5: USER CHECKPOINT - PLAN CONFIRMATION
- Share your data architecture plan and Faction relation schemas.
- Ensure the user approves of the signal structure and transaction interfaces.
- STOP AND WAIT FOR USER INPUT before coding.

PHASE 6: FULL DEVELOPMENT
- Code the full, production-ready system in Godot 4.3 GDScript:
  1. `FactionManager.gd` (Singleton tracking standings, triggers, and tariffs).
  2. `TownTaxEngine.gd` (Calculates weekly taxes using the T_town formula, manages payment schedules, and triggers tax default penalties).
  3. `WanderingMerchant.gd` (Wandering vendor with faction-gated inventory pools).
================================================================================
```

## Pillar 3: Hero Quests & Dynamic Bounties

### Detailed System Mechanics & Ideas

AI Heroes choose their own adventure paths based on utility. The player, however, can influence this trajectory by posting Bounties on a town notice board. The bounty acts as a massive artificial boost to the hero's quest-desire calculation.

Bounty Crowdfunding: Other town NPCs can dynamically add money to bounties. If a local merchant is terrorized by a nearby goblin camp, they will pitch in $10\%$ of their daily profits to raise the bounty value on that node.

Notice Board Competitive Rush: If a high-value bounty is posted, high-affinity rival parties (e.g., a Warrior-Rogue duo vs. a Mage-Priest duo) will compete to reach the dungeon first. If a party arrives and finds the node cleared, they gain disgruntlement points against the player.

### Mathematical Framework: Quest Selection Score

A hero's desire to select quest $q$ ($D_q$) is governed by:

$$D_q = \left( \frac{\text{GoldReward}}{\text{ThreatLevel}} \times \text{Greed} \right) + \left( \frac{\text{PlayerBounty}}{\text{Distance}} \times \text{Reputation} \right)$$

Where:

- $\text{PlayerBounty}$ is the monetary reward posted by the player using their own cash reserves.
- $\text{ThreatLevel}$ represents the dungeon danger ranking.
- $\text{Reputation}$ is the hero's dynamic relationship rating with the player's shop.

### CLAUDE TASK PROMPT: HERO QUEST SYSTEM

```
================================================================================
AGENTIC TASK PROMPT: NOTICE BOARD & QUEST ENGINE (GODOT 4.3)
================================================================================
You are a Lead AI Programmer and UX Architect. Your task is to design, plan, and build the dynamic notice board and quest engine.

Follow this strict sequence of phases:

PHASE 1: IDEA EXPLORATION
- Propose 3 ways to visualize quest competition between AI Hero parties on a town notice board UI.
- Explain how we can simulate "bounty theft" narratives that create town gossip.

PHASE 2: ENGINE RESEARCH
- Research pathfinding and routing optimizations in Godot 4.3. Since Heroes operate in a 2D map graph, how do we efficiently calculate distance matrices without freezing the main thread?
- Plan the asynchronous thread handling for continuous utility evaluations across 20+ active heroes.

PHASE 3: USER CHECKPOINT - PROPOSAL CONFIRMATION
- Show your quest UI ideas and threading performance solutions.
- Confirm with the user if they want real-time competing paths visible on the world map, or purely interface-driven resolution.
- STOP AND WAIT FOR USER INPUT.

PHASE 4: SYSTEM DESIGN
- Map the data structure of a `QuestInstance`.
- Plan how GOAP actions will query, reserve, lock, and complete a posted quest.

PHASE 5: USER CHECKPOINT - PLAN CONFIRMATION
- Share your schemas for `QuestInstance` and the GOAP query pipeline.
- Get confirmation from the user on the architectural safety locks before writing scripts.
- STOP AND WAIT FOR USER INPUT.

PHASE 6: FULL DEVELOPMENT
- Write and execute complete Godot 4.3 GDScript files:
  1. `QuestBoard.gd` (UI class managing listed quests, player bounty posting, and faction contributions).
  2. `QuestSelector.gd` (Implements the D_q formula, parses active heroes, and routes them to maps).
================================================================================
```

## Pillar 4: Player-Specific Workstation Missions

### Concept & Mechanics

The player is not an adventurer; their "quests" are logistical, environmental, or manufacturing emergencies. For example, a faction orders a massive supply of high-grade shields for a war front, or the forge furnace overheats and must be balanced via manual controls.

The Thermodynamic Forge: The player's main forge workstation has a variable core_temperature gauge. To craft high-tier metals, the player must operate bellows and open vents to keep the temperature within an optimal window. Overheating degrades workstation stability; underheating ruins the raw materials.

Urgent Military Drafts: The local Crownsguard posts sudden, time-gated supply demands (e.g., "Need 12 Steel Breastplates in 3 minutes"). Successfully fulfilling these grants massive faction rep and double payouts, while failing triggers steep faction penalty fees.

### Mathematical Framework: Workstation Stability Decay

The stability of a player's forge workstation ($S_{work}$) decays as:

$$S_{work}(t) = S_0 - \int \left( \delta_{temp} \times \sigma_{wear} \right) dt$$

Where:

- $\delta_{temp}$ represents forge heat deviation from optimal parameters ($\vert{}T_{current} - T_{optimal}\vert{}$).
- $\sigma_{wear}$ represents tool fatigue based on player mini-game success rates.

### CLAUDE TASK PROMPT: WORKSTATION SYSTEM

```
================================================================================
AGENTIC TASK PROMPT: WORKSTATION TEMPERATURE & LOGISTICS (GODOT 4.3)
================================================================================
You are a Senior Simulation Gameplay Programmer. Your task is to design, research, plan, and build the thermodynamic workstation and dynamic supply contract engine.

Follow this strict sequence of phases:

PHASE 1: IDEA EXPLORATION
- Design a compelling, interactive mechanics loop for a "Thermodynamic Forge Balance" mini-game.
- Pitch 3 unique environmental hazard events (e.g., Forge Flare-ups, Anvil Crack, Quench Contamination).

PHASE 2: ENGINE RESEARCH
- Research time-based integration calculations in Godot 4.3. How do we safely run continuous integrations for thermodynamic heat decay inside `_physics_process`?
- Plan the visual representation (e.g., shaders, progress bars, particles) to indicate extreme temperatures without dropping frame rates.

PHASE 3: USER CHECKPOINT - PROPOSAL CONFIRMATION
- Present your thermodynamic mini-game mechanics and physics processing structure.
- Get the user's confirmation on how mechanically intensive the balance mini-game should be.
- STOP AND WAIT FOR USER INPUT.

PHASE 4: ARCHITECTURAL PLANNING
- Design the class architecture for `PlayerWorkstation` and the dynamic `SupplyContractGenerator`.
- Establish signals, event triggers for workstation failures, and contract timeout penalties.

PHASE 5: USER CHECKPOINT - PLAN CONFIRMATION
- Share your class maps, signal architectures, and GUI layout plans.
- Confirm with the user before writing any front-end UI code.
- STOP AND WAIT FOR USER INPUT.

PHASE 6: FULL DEVELOPMENT
- Implement the complete codebase:
  1. `PlayerWorkstation.gd` (Handles thermodynamics, wear and tear, and interactive temperature balance).
  2. `SupplyContractEngine.gd` (Generates time-gated faction orders with dynamic grading rewards/penalties).
  3. `WorkstationUIGame.gd` (Complete interactive Control-node UI with heat gauges and warning systems).
================================================================================
```

## Pillar 5: Map Nodes & Dungeon Graphs

### Detailed System Mechanics & Ideas

The overworld is represented as a structured node graph. Dungeons and hazard zones increase in level and unlock rarer resource nodes over time. The proximity of a dungeon node to the town determines the travel time and fatigue calculations of traveling heroes.

The Node Graph Topography:

```
       [Town Hub]
        /      \
  [Gloomwood]  [Iron Mines]
      |            |
 [Spider Cave] [Volcano Den]
```

Dynamic Node Closures: High hazard ratings can trigger landslides or heavy blizzards, temporarily blocking routes. This forces heroes to recalculate paths, traveling through longer, more fatiguing pathways, which in turn spikes their demand for player-sold travel rations and stamina potions.

### Mathematical Framework: Travel Fatigue Accumulation

A hero's fatigue accumulation ($F_{travel}$) during transit to a node is modeled as:

$$F_{travel} = \left( \frac{\text{NodeDistance}}{\text{MountSpeed}} \right) \times \left( 1 + \text{HazardRating} \right)$$

High fatigue drastically reduces the hero's starting physical and magical attack stats in combat calculations upon reaching the node.

### CLAUDE TASK PROMPT: MAP NODE & DUNGEON GRAPH SYSTEM

```
================================================================================
AGENTIC TASK PROMPT: DUNGEON GRAPH & ROUTING PATHWAYS (GODOT 4.3)
================================================================================
You are a Math and Graphics Gameplay Programmer. Your task is to design, research, plan, and execute the node-graph world map and fatigue engine.

Follow this strict sequence of phases:

PHASE 1: IDEA EXPLORATION
- Design a set of 5 distinct map node types (e.g., Abandoned Keep, Poison Swamp, Core Vent) with unique hazard coefficients and loot pools.
- Propose dynamic world map events that alter node paths (e.g., Rift Incursions blocking trade lanes).

PHASE 2: TECHNICAL RESEARCH
- Research the AStar2D and graph pathfinding capabilities built into Godot 4.3.
- Map out the most efficient way to store, serialize, and render a dynamic node connection graph visually.

PHASE 3: USER CHECKPOINT - PROPOSAL CONFIRMATION
- Display your graph node types, hazard math, and Godot AStar pathfinding strategies.
- Seek user confirmation on map size, complexity, and layout presentation.
- STOP AND WAIT FOR USER INPUT.

PHASE 4: SYSTEM DESIGN
- Layout the node and edge structures in a clean graph schema.
- Design how the F_travel equation impacts a Hero's combat stat structures.

PHASE 5: USER CHECKPOINT - PLAN CONFIRMATION
- Share your graph serialization models and class dependencies.
- Ensure the user approves of how fatigue penalty formulas interface with hero combat loops.
- STOP AND WAIT FOR USER INPUT.

PHASE 6: FULL DEVELOPMENT
- Code and implement:
  1. `WorldMapGraph.gd` (Manager class representing nodes, edges, connections, and hazards).
  2. `MapNodeResource.gd` (Custom resource storing node characteristics, levels, and loot pools).
  3. `TransitFatigueEngine.gd` (Handles physical travel simulation, routes paths, and computes fatigue penalties).
================================================================================
```

## Pillar 6: The Dynamic Chronicle & Narrative Lore

### Detailed System Mechanics & Ideas

To ensure every playthrough is unique, the world history compiles in real time based on simulation outcomes. When a hero dies, an item is created, or a boss is slain, the system records a Historical event entry. This ledger is compressed, serialized, and dynamically injected into the system prompts of all agents in subsequent cycles.

The Ring-Buffered Database: To prevent memory issues, a local SQLite or ring-buffered JSON file maintains a sliding window of historical world records.

The Rumor Mill Generator: When an elite boss is slain by a party, a chronicle event is logged. When a hero from a different party visits the player's shop, they parse this record via System 2 and dynamically comment on it (e.g., "I heard Roger's party finally took down the Gloomwood Beast... with your armor, no less!").

### Mathematical Framework: Event Lore Prominence Index

The priority index ($I_{lore}$) of a past event to be remembered by an LLM agent is:

$$I_{lore} = \text{EventSeverity} \times e^{-\lambda \times \Delta t} \times \left( 1 + \text{FactionStanding} \right)$$

Where:

- $\text{EventSeverity}$ is a predefined constant based on impact (e.g., Hero Death = $10.0$, Weapon Enchanted = $2.0$).
- $\lambda$ is a natural decay constant (older stories fade first).
- $\Delta t$ is the cycles elapsed since the event occurred.

### CLAUDE TASK PROMPT: CHRONICLE & NARRATIVE ENGINE

```
================================================================================
AGENTIC TASK PROMPT: THE DYNAMIC CHRONICLE & RUMOR ENGINE (GODOT 4.3)
================================================================================
You are an expert AI Integration and Database Developer. Your task is to design, research, plan, and build the dynamic chronicle tracker and local LLM narrative feedback pipeline.

Follow this strict sequence of phases:

PHASE 1: IDEA EXPLORATION
- Design a list of 10 distinct "High Severity" events that should trigger a chronicle entry.
- Propose how we can dynamically craft gossip structures for heroes visiting the town tavern.

PHASE 2: ENGINE RESEARCH
- Research fast file serialization options in Godot 4.3 (e.g., binary files, custom JSON, or SQLite plugins).
- Analyze performance and context-window optimization to ensure compiling raw text histories doesn't bog down Ollama call times.

PHASE 3: USER CHECKPOINT - PROPOSAL CONFIRMATION
- Present your narrative logging patterns, your database performance strategies, and decay mechanics.
- Get confirmation from the user regarding the length and volume of history we should keep in active memory.
- STOP AND WAIT FOR USER INPUT.

PHASE 4: SYSTEM DESIGN
- Outline the `ChronicleEvent` JSON schema and the SQLite table formats.
- Map the data ingestion flow from game event -> SQLite -> I_lore calculation -> local LLM context window.

PHASE 5: USER CHECKPOINT - PLAN CONFIRMATION
- Display your SQLite schemas, JSON formats, and the exact Ollama system prompt templates.
- Get user verification before finalizing data structures.
- STOP AND WAIT FOR USER INPUT.

PHASE 6: FULL DEVELOPMENT
- Code and deliver:
  1. `DynamicChronicle.gd` (Handles JSON/SQLite writing, event logging, and database queries).
  2. `LoreDecayCalculator.gd` (Executes the I_lore priority calculation, sorting and pruning old stories).
  3. `LocalLLMBridge.gd` (Pings Ollama with dynamic context blocks, returning dialogue profiles).
================================================================================
```

## Pillar 7: Vanity, Ego, & Theme Customizations

### Detailed System Mechanics & Ideas

In addition to raw combat stats, the player can craft vanity items: custom dyes, weapon glows, and cosmetic flags. These do not increase physical armor, but dramatically elevate the Hero's Ego and Confidence values.

Cosmetic Customization Grading: Weapon glows (e.g., "Fiery Orange," "Arcane Blue") scale based on the rarity of catalysts used. A legendary vanity item causes a hero's EgoValue to spike dramatically.

The Bragging State Loop: When a hero equipped with vanity items returns to the town tavern, their behavioral state switches to "Bragging." They show off their glowing armor, boosting their reputation with other low-level heroes and raising the general public desire to buy from the player's shop.

### Mathematical Framework: Ego-Driven Threat Tolerance

A highly customized, proud hero calculates their combat threat threshold ($\Theta_{threat}$) as:

$$\Theta_{threat} = \text{BaseThreatThreshold} \times \left( 1 + \text{EgoValue} \right)$$

Where:

- $\text{EgoValue}$ scales up with cosmetic customization quality. This causes the hero to engage tougher bosses with significantly less fear, occasionally sparking reckless bravery.

### CLAUDE TASK PROMPT: VANITY SYSTEMS

```
================================================================================
AGENTIC TASK PROMPT: VANITY, EGO, & CONFIDENCE BOOSTER (GODOT 4.3)
================================================================================
You are a Lead UI and Behavioral AI Programmer. Your task is to design, research, plan, and build the cosmetic crafting and ego-modifier system.

Follow this strict sequence of phases:

PHASE 1: IDEA EXPLORATION
- Design 3 classes of vanity options (Dyes, Particle Trails, Weapon Glyphs) and define how they scale a hero's mental metrics (Greed, Pride, Courage).
- Describe how a "Taunting" or "Bragging" behavioral state loop affects nearby AI heroes.

PHASE 2: ENGINE RESEARCH
- Research the best way to handle dynamic cosmetic sprites, colors, and particle systems in a 2D Godot environment (e.g., utilizing dynamic canvas shaders or multi-pass materials).
- Analyze memory overhead for handling 20+ glowing/particle-emitting moving sprites.

PHASE 3: USER CHECKPOINT - PROPOSAL CONFIRMATION
- Present your shader designs, vanity customization mechanics, and behavioral logic graphs.
- Get user confirmation on the visual styling and behavioral impact scaling.
- STOP AND WAIT FOR USER INPUT.

PHASE 4: SYSTEM DESIGN
- Define the schema for vanity-extended `ItemResources`.
- Plan how GOAP's threat evaluations will incorporate the Theta_threat formula.

PHASE 5: USER CHECKPOINT - PLAN CONFIRMATION
- Display your dynamic shader interfaces, resource layouts, and GOAP logic connections.
- Ensure the user is satisfied with the architecture before writing code.
- STOP AND WAIT FOR USER INPUT.

PHASE 6: FULL DEVELOPMENT
- Implement the full system:
  1. `VanityCraftingEngine.gd` (Handles cosmetic customization blueprints, color dyes, and enchantment glows).
  2. `EgoBehaviorModifier.gd` (GDScript that alters GOAP combat and routing evaluations based on cosmetic confidence).
  3. `VanityShaderController.gd` (Applies dynamic visual adjustments, materials, and particles to the 2D sprites).
================================================================================
```

## Pillar 8: Spells, Talents, & Combat Abilities

### Detailed System Mechanics & Ideas

To help heroes survive, players can unlock, transcribe, and sell Spell Scrolls and Ability Blueprints. These dynamically extend the hero's combat capabilities, adding new immediate tactical triggers to their GOAP actions (e.g., teaching a Mage to cast Blink when cornered).

The Talent Planner: When a hero levels up, they can purchase specialized training scrolls from you to unlock new class-talent branches (e.g., teaching a Warrior Shield Wall or a Priest Circle of Healing).

Scroll Transcription: The player uses rare paper and magical ink to copy spells. Higher quality transcriptions reduce the mana cost or cooldown of the spell for the hero who uses it.

### Mathematical Framework: Ability Utility Calculation

The tactical utility score ($U_{ability}$) for an AI Mage to use a defensive teleport ability (Blink) is calculated as:

$$U_{ability} = \left( 1 - \frac{\text{CurrentHP}}{\text{MaxHP}} \right) \times \text{EnemyProximityDistance} \times \text{HasMana}$$

If $U_{ability} \ge 0.75$, the GOAP planner instantly prioritizes the Blink action over casting generic damage spells.

### CLAUDE TASK PROMPT: ABILITY UTILITY ENGINE

```
================================================================================
AGENTIC TASK PROMPT: SPELL TRANSCRIPTION & ABILITY DECISION ENGINE (GODOT 4.3)
================================================================================
You are an expert Gameplay Programmer and Spell System Architect. Your task is to design, research, plan, and execute the modular combat spell/ability registry.

Follow this strict sequence of phases:

PHASE 1: IDEA EXPLORATION
- Design a list of 8 complete abilities (2 per baseline class: Warrior, Mage, Rogue, Priest) with detailed mechanics, cooldowns, costs, and tactical triggers.
- Propose how scroll transcription quality modifies spell execution stats.

PHASE 2: ENGINE RESEARCH
- Research decoupled modular capability systems in Godot 4.3. How do we dynamically attach and detach spell mechanics (as Node components or Callable variables) to autonomous characters at runtime?
- Examine how we can integrate combat calculations cleanly without dropping performance during large group fights.

PHASE 3: USER CHECKPOINT - PROPOSAL CONFIRMATION
- Present your ability designs, dynamic attachment architecture, and mathematical triggers.
- Confirm with the user which ability variations should be prioritized.
- STOP AND WAIT FOR USER INPUT.

PHASE 4: SYSTEM DESIGN
- Outline the `AbilityResource` data schema.
- Plan the GOAP action integration layer so agents scan, evaluate, and fire spell routines dynamically during combat.

PHASE 5: USER CHECKPOINT - PLAN CONFIRMATION
- Show your schemas, components, and target selection diagrams.
- Verify the API layout with the user before starting to code.
- STOP AND WAIT FOR USER INPUT.

PHASE 6: FULL DEVELOPMENT
- Code the complete system in Godot 4.3:
  1. `AbilityRegistry.gd` (Resource database of active combat spells and attributes).
  2. `SpellScrollTranscripter.gd` (Player interface for scribing scrolls and applying quality scaling).
  3. `GOAPCombatAbilityTrigger.gd` (Engine that processes combat state variables and executes dynamic ability triggers).
================================================================================
```

## Pillar 9: Overworld Enemies & Monster Dens

### Detailed System Mechanics & Ideas

Enemies operate in regional monster dens. The monster threat is fluid; untamed nests breed Elite variant mobs and send raiding parties toward the town gate, driving up localized threat and depleting town stability.

Den Evolution Mechanics: If left unchecked, a dungeon's InfectionRate increases daily. When it hits $100\%$, it mutates, spawning a dangerous World Boss that locks down adjacent map nodes and prevents standard hero questing.

Elite Mutations: Enemies spawn with random procedural modifiers (e.g., "Fire Shielded," "Acidic Spit," "Vampiric Strikes") that require highly specific defensive preparations from the heroes, forcing them to purchase countermeasures from the player's shop.

### Mathematical Framework: Mob Variation Generation

When a dungeon spawns an enemy, its stat modifiers ($S_{mob}$) scale as:

$$S_{mob} = S_{base} \times \left( 1 + \left( \lambda_{growth} \times \text{DaysUntouched} \right) \right)$$

Where:

- $\lambda_{growth}$ is the daily evolution coefficient.
- $\text{DaysUntouched}$ represents how long since a hero party successfully routed the node.

### CLAUDE TASK PROMPT: ENEMY SYSTEMS

```
================================================================================
AGENTIC TASK PROMPT: MONSTER DEN ESCALATION & EVOLUTION LOOPS (GODOT 4.3)
================================================================================
You are a Procedural Systems Developer and Combat Balance Designer. Your task is to design, research, plan, and build the monster generation and regional den escalation architecture.

Follow this strict sequence of phases:

PHASE 1: IDEA EXPLORATION
- Design 4 distinct procedural enemy mutation traits (e.g., Spore Spreader, Shield Wall, Poison Blood) and define how they counter standard hero tactics.
- Describe a town raid scenario where untamed dens attack the city gates.

PHASE 2: ENGINE RESEARCH
- Research procedural parameter generation and dynamic scaling in Godot 4.3.
- Map out an efficient way to run a global clock-tick that updates 10+ monster dens asynchronously without affecting active gameplay threads.

PHASE 3: USER CHECKPOINT - PROPOSAL CONFIRMATION
- Present your procedural mutation ideas, scaling algorithms, and den clock synchronization.
- Confirm with the user how aggressive the den evolution should be in punishing player delay.
- STOP AND WAIT FOR USER INPUT.

PHASE 4: SYSTEM DESIGN
- Blueprint the `MonsterDen` node-state and the database representation for dynamic mob instances.
- Plan the combat stat calculations utilizing the S_mob formula.

PHASE 5: USER CHECKPOINT - PLAN CONFIRMATION
- Share your data blueprints, mutation databases, and den lifecycle diagrams.
- Verify with the user before beginning implementation.
- STOP AND WAIT FOR USER INPUT.

PHASE 6: FULL DEVELOPMENT
- Develop the core modules:
  1. `MonsterDenManager.gd` (Tracks overworld dens, escalates threats, and triggers mutations).
  2. `EnemyNPCGenerator.gd` (Handles procedural dynamic mutations, scaling stats using S_mob).
  3. `TownInvasionEngine.gd` (Triggers town gate defense encounters when local threat overflows).
================================================================================
```

## Pillar 10: Player-Centric Threats & Disasters

### Concept & Mechanics

The player's shop is not safe from external hazards. The player must actively manage and counter business disasters: forgery audits, bandit extortions, supply chain strikes, and environmental workshop fires. These test structural and financial management rather than physical combat prowess.

The Forgery Audit: Royal auditors perform random inventory inspections. If the player has used cheap materials or illicit Shadow Syndicate ores to craft standard guard breastplates, they face steep fines or a dynamic "Suspicion" score increase.

Bandit Extortions: Nearby bandit syndicates issue periodic ultimatums (e.g., "Pay 500 Gold or face a supply raid"). Paying the bribe drops alignment with the Crownsguard; refusing triggers an interactive defense event where the player must deploy traps or hire mercenary guards to protect the shopfront.

### Mathematical Framework: Disaster Mitigation Requirement

The vulnerability score of the player's shop to a bandit shakedown ($V_{bandit}$) is:

$$V_{bandit} = \left( \text{PlayerGoldScore} \times \left( 1 - \text{MercenaryGuardsScore} \right) \right) + \text{LocalCorruptionScore}$$

Where:

- $\text{MercenaryGuardsScore}$ is scaled by the player hiring protection or paying town guard bribes.

### CLAUDE TASK PROMPT: PLAYER THREATS & DISASTERS

```
================================================================================
AGENTIC TASK PROMPT: SHOP DISASTERS & ECONOMIC CRISIS ENGINE (GODOT 4.3)
================================================================================
You are a Lead Simulation Programmer and Game State Architect. Your task is to design, research, plan, and build the player-centric threat engine, safety audits, and extortion crises.

Follow this strict sequence of phases:

PHASE 1: IDEA EXPLORATION
- Design the mechanics for 3 dynamic business disasters: Forgery Audits, Bandit Extortion, and Workshop Fires.
- Outline how the player interacts with these threats (e.g., placing sand buckets, forging false accounting papers, bribing guard inspectors).

PHASE 2: ENGINE RESEARCH
- Research dynamic state serialization in Godot 4.3 to ensure player inventory safety is preserved through sudden disaster states.
- Plan the visual warning and screen-space warning systems that signal an impending audit or bandit raid.

PHASE 3: USER CHECKPOINT - PROPOSAL CONFIRMATION
- Display your threat events, UI wireframes, and mitigation math models.
- Seek the user's confirmation on the severity, recovery methods, and frequency of these disasters.
- STOP AND WAIT FOR USER INPUT.

PHASE 4: SYSTEM DESIGN
- Layout the classes for `DisasterRunner` and the `MercenaryRegistry`.
- Structure the mathematical equations used to calculate risk indicators like V_bandit.

PHASE 5: USER CHECKPOINT - PLAN CONFIRMATION
- Display your class diagrams, threat calculators, and transition states.
- Confirm with the user before writing physical collision or event coding loops.
- STOP AND WAIT FOR USER INPUT.

PHASE 6: FULL DEVELOPMENT
- Execute complete, well-commented GDScript implementations:
  1. `DisasterManager.gd` (Calculates hazard metrics, schedules audits, and triggers fire/flood events).
  2. `MercenaryContractor.gd` (Manages shop defense mechanics, hired guards, and protective wards).
  3. `AuditInterface.gd` (Handles the interactive audit negotiation dialogs, corruption bribes, and asset evaluation).
================================================================================
```

## Pillar 11: Master Professions System (Consolidated)

### Concept & Mechanics

The foundation of the physical town interface. Players manage workstations to process raw assets into structured gear. Each tier shifts combat values and modifies hero tactical parameters, while unique Master items fundamentally re-route AI behavior paths.

Blacksmithing Workflow: Smelt ores -> Hammer on anvil to a matching rhythm (using physical alignment inputs) -> Quench -> Polish. Normal items boost base stats; Legendary items alter hero behavior directly (e.g., Slayer's Axe pushes them to seek elite bounties).

Potion Brewing Workflow: Grind herbs -> Mix solvents -> Balance thermodynamic heat levels in the alembic -> Distill. Failing a brew produces toxic waste; success creates rapid healing potions which heroes score highly in survival utility.

### Mathematical Framework: General Purchase Utility

The likelihood of an AI hero prioritizing a purchase from the player's inventory is:

$$U_{buy} = \text{NeedWeight} \times \text{Affordability} \times \text{ItemQuality} \times \text{TrustFactor}$$

Where:

- $\text{NeedWeight} = 100 - \text{CurrentStatePercent}$ (e.g., $100 - \text{Health}\%$ for health potions).
- $\text{Affordability} = 1.0$ if $\text{HeroGold} \ge \text{ItemPrice}$ else $0.0$.
- $\text{ItemQuality} \in [1.0, 2.5]$ (Normal, Rare, Legendary).

### CLAUDE TASK PROMPT: PROFESSION ARCHITECTURE IMPLEMENTATION

```
================================================================================
AGENTIC TASK PROMPT: MASTER PROFESSIONS & CRAFTING WORKSTATIONS (GODOT 4.3)
================================================================================
You are an Expert Systems Architect and Gameplay Designer. Your task is to design, research, plan, and build the Master Profession and Workstation pipeline.

Target Profession: [USER TO SPECIFY - e.g., BLACKSMITHING, POTION BREWING]

Follow this strict sequence of phases:

PHASE 1: IDEA EXPLORATION
- Propose 3 unique physical gameplay mechanics for this profession's workstation (e.g., dynamic rhythm timing for smithing, solvent balancing for alchemy).
- Design a list of 8 complete recipes stretching across Apprentice, Journeyman, Expert, and Master tiers.

PHASE 2: ENGINE RESEARCH
- Research the math execution loops in Godot 4.3 needed to handle item generation variations (e.g., applying statistical standard distribution to output item stats based on minigame performance).
- Plan the structure of Custom Godot Resources (`ItemBlueprint`, `CraftedItem`) to ensure clean inventory handling.

PHASE 3: USER CHECKPOINT - PROPOSAL CONFIRMATION
- Display your minigame designs, item recipes, and Godot serialization strategies.
- Seek user confirmation on the crafting rhythm and balancing parameters.
- STOP AND WAIT FOR USER INPUT.

PHASE 4: SYSTEM DESIGN
- Layout the schema of the dynamic Recipe Database.
- Plan the GOAP transaction integration layers for purchasing evaluation (U_buy).

PHASE 5: USER CHECKPOINT - PLAN CONFIRMATION
- Show your database layouts, class dependency maps, and transaction models.
- Get the user's confirmation before executing any scripts.
- STOP AND WAIT FOR USER INPUT.

PHASE 6: FULL DEVELOPMENT
- Develop the core components:
  1. `ProfessionWorkstation.gd` (Manages state machine logic: Idle, Processing, MiniGame, Output).
  2. `RecipeDatabase.gd` (Dynamic recipe lists and material ratios across all 4 tiers).
  3. `ItemTransactionInterface.gd` (Handles physical transaction calculations, registers U_buy, and handles gold exchanges).
================================================================================
```
