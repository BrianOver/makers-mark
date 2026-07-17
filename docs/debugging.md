# Debugging Maker's Mark — the self-serve manual

For any Claude (or human) diagnosing a bug, test failure, or behavior anomaly. The sim is
deterministic — that is the superpower: **any state reproduces byte-identically from seed +
action log.** Use it before reading resolver code or guessing.

## 1. The deterministic repro recipe

1. **Capture** — from a live CLI session: `export [path]` writes the campaign chronicle
   (seed, day, roster, full event log) to `runs/`. From a batch: chronicles are already there.
2. **Replay** — a fresh campaign with the same seed + the same actions IS the same world:
   `GameComposition.NewCampaign(seed)` + `kernel.Tick(state, actions)` per phase. The golden-replay
   test (`sim/GameSim.Tests`) is the executable spec of this guarantee.
3. **Bisect by day** — the anomaly names a day window. Re-run to day N-1, then step one phase at a
   time (`next` in the CLI) watching the event log. Events are stamped and ordered; the first
   surprising event names the module that emitted it.
4. **Isolate** — write a focused xUnit test that builds the minimal state and calls the one
   system/handler directly (see any `*ConformanceTests` for the pattern). Fix, then re-run lanes.

Batch repro of a reported anomaly: `dotnet run --project sim/GameSim.Cli -- batch --seeds 1 --seed <s> --days <window-end>`
then inspect the chronicle or replay interactively with `--seed <s>`.

## 2. Test lanes (what to run, when)

| Lane | Command | Needs |
|---|---|---|
| Sim fast lane | `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance` | nothing |
| Balance gate | `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category=Balance` | nothing |
| Art conformance | `dotnet test art/GameArt.Tests/GameArt.Tests.csproj` | nothing |
| Engine tests | `dotnet test godot/tests --settings .runsettings` | Godot 4.6.3 via GODOT_BIN; a display (CI uses xvfb) |

Run the fast lane before reporting ANY work done (CLAUDE.md rule 1).

## 3. Where logs & artifacts live

- `runs/*.json` — exported chronicles (CLI `export`, batch runner). Input to Analytics.
- `runs/report.md` + `runs/anomalies.md` — `dotnet run --project tools/Analytics -- runs`.
- CI: trx artifacts per lane (`sim-test-results`, `balance-test-results`, `engine-test-results`);
  engine-lane failure step dumps recent Godot logs.
- Godot user logs (local): `%APPDATA%\Godot\app_userdata\` / `~/.local/share/godot/`.
- ComfyUI (art gen, local): `C:\Tools\comfy_boot*.log` + server console.

## 4. Known failure shapes (check these FIRST)

| Symptom | Cause | Fix |
|---|---|---|
| Golden-replay mismatch | An RNG draw was added/removed/reordered — every seed's world shifted | Find the new/moved draw; if intentional, re-record goldens as a deliberate reviewed commit |
| `net8.0` in a csproj diff | Godot editor injected a TFM downgrade | `git checkout -- godot/GodotClient.csproj`; TFM lives in `Directory.Build.props` only (CLAUDE.md rule 3) |
| Engine tests "Connection timeout" | gdUnit4Net launches Godot WITHOUT --headless; no display | Run under xvfb (CI does); locally ensure a desktop session |
| Save fails to load after a contract change | New member not trailing-optional | Follow the `PreP4Save`/`PreP5Save` pin pattern in `SaveLoadTests` — absent member must default sanely |
| Flavor/gossip text changed unexpectedly | StableHash input or variant list changed | Voice/variant lists are FROZEN append-only; check `FlavorEngine`/`VoiceProfile` doc comments |
| Determinism breaks cross-OS only | float / transcendental `Math.*` / `string.GetHashCode` snuck into sim | Integer-only in `sim/GameSim`; use `IntegerCurves` / `StableHash` |
| Balance gate red after tuning | Bands moved | Re-tune or re-baseline deliberately — never loosen the assertion to pass |

## 5. The telemetry loop

`docs/telemetry-loop.md` — batch → analytics → anomalies → Claude proposes data-tuning PR →
gates → Brian approves. Anomaly entries carry their own repro pointers (section 1 applies).
