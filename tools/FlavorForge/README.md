# FlavorForge

Dev-time flavor-pack content generator (P008 / R13). Asks a local model for candidate flavor
lines, rejects anything that would not render cleanly through the game's real template engine,
and either writes a review file or splices accepted lines into the committed pack.

**FlavorForge never runs at game runtime.** It is a console tool under `tools/`; nothing in
`sim/GameSim` or `godot/` references it. Its only relationship to the sim is a read-only call
into `GameSim.Flavor.FlavorEngine.TryRenderTemplate` — the same validation the game itself uses
at render time — so a candidate is accepted if and only if it would actually work in play.

## Why this is safe

1. **The engine is the judge, not the model.** Every candidate line is run through
   `FlavorEngine.TryRenderTemplate` with the target cell's real slot set. A line that drops a
   literal fact (e.g. paraphrases `{item}` as "the blade"), references an unknown placeholder, or
   has an unclosed `{` is rejected — the same rule the game enforces when rendering saves.
2. **The key surface never changes.** The emitter only ever splices additional variants into an
   **existing** `(baseKey, voice)` key already present in the pack file. It never adds a key,
   never adds a same-surface pack, and never touches `Fallbacks`. If the code shape it expects to
   splice into is missing, it fails loudly and writes nothing rather than guessing.
3. **Nothing is written to a real pack without your say-so.** The default mode (`propose`) only
   ever writes a text file under `tools/FlavorForge/proposals/` (git-ignored, not sim source).
   Splicing into the actual pack file requires the explicit `--emit` flag.
4. **CI and every test run against a stub.** `StubModelClient` is the only client used in
   `tools/FlavorForge.Tests`; there is zero network access anywhere in the test suite.

## Setting up a local endpoint

FlavorForge talks to whatever OpenAI-compatible or Ollama-compatible server you already have
running on your own machine. It never calls out to a hosted/cloud model.

**Ollama** (`POST /api/generate`, default `http://localhost:11434`):

```bash
ollama serve
ollama pull llama3.1        # or any model you like
dotnet run --project tools/FlavorForge -- --surface tavern --endpoint http://localhost:11434 \
    --model llama3.1 --api-shape ollama
```

**LM Studio** (OpenAI-compatible `POST /v1/chat/completions`, default `http://localhost:1234`):

```bash
dotnet run --project tools/FlavorForge -- --surface tavern --endpoint http://localhost:1234 \
    --model <the model id LM Studio shows in its Local Server tab> --api-shape openai
```

An unreachable endpoint (server not started, wrong port) fails fast with a clear
`model unavailable: ...` message and a non-zero exit — it never hangs.

## CLI reference

```
flavorforge --surface <tavern|faction|ledger|narrator> (--stub | --endpoint <url> --model <id>)
            [--api-shape ollama|openai] [--count N] [--emit]
            [--pack-file PATH] [--out DIR] [--config PATH]
```

| Flag | Meaning |
| --- | --- |
| `--surface` | Which pack to target: `tavern`, `faction`, `ledger`, or `narrator`. Required. |
| `--stub` | Deterministic dry run with zero network IO — no real candidates, just a pipeline smoke test. |
| `--endpoint` / `--model` | Live local model — base URL and model id. Required together unless `--stub`. |
| `--api-shape` | `ollama` (default) or `openai` — which request/response shape to speak. |
| `--count` | Candidates requested per `(baseKey, voice)` cell (default 6). |
| `--emit` | Splice accepted lines into the pack file. Default is `propose` (review file only). |
| `--pack-file` | Override the target pack file path (used by tests to point at a fixture, never the real pack). |
| `--out` | Override the proposals directory. |
| `--config` | JSON file overlaying `endpoint`/`model`/`candidateCount`/`apiShape` — see `config.sample.json`. CLI flags always win. |

Per-cell tallies (`cell: N accepted / M rejected / K dupes`) are printed to `stderr` for every run
so you can see yield before deciding whether to `--emit`.

## Workflow: propose -> review -> emit -> re-pin

1. **Propose.** Run without `--emit`. This writes
   `tools/FlavorForge/proposals/<surface>.txt` — every accepted candidate, grouped by key. Rejects
   and duplicates never appear in the file; check the stderr tallies if you want to see how many
   were dropped and why.
2. **Review.** Read the proposal file like you would any authored prose. Cut anything that reads
   wrong for the voice, even if the engine accepted it structurally — engine acceptance proves a
   line is *safe*, not that it's *good*.
3. **Emit.** Re-run the SAME command with `--emit` added. This splices the accepted lines
   (verbatim — nothing changes between propose and emit) into the existing
   `ImmutableList.Create(...)` block for each `(baseKey, voice)` key, producing a normal,
   reviewable `git diff`.
4. **Re-pin.** Adding variants changes each touched key's variant count, which shifts the stable
   pick (`FlavorEngine.Render`'s hash-modulo pick) for every save that hits that key — this is
   expected, not a bug (see `TavernPack`'s and siblings' class docs). After an emit:
   - Run the fast lane: `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance`.
   - Read every failing exact-prose golden assertion (e.g.
     `TavernPackTests.Generate_FixedCampaignAndEvents_PinsExactProse`) — confirm the NEW pinned
     line actually reads well before updating the assertion. Never blanket-replace goldens without
     reading them.
   - Commit the pack diff and the golden re-pin together as one deliberate, reviewed change.

`--stub` is useful for steps 1 and 3 in CI or when no local model is running: it always yields
zero candidates (nothing injected), which is enough to prove the plumbing works end to end
without ever touching a real endpoint.

## Adding a surface

The tool reads each pack's own published `SlotNames` and `VoiceProfile.Voices` — see
`Generation/SurfaceContract.cs`. Wiring in a new pack surface means adding one more
`SurfaceContract` entry there (pack, slot names, sample values, and its file path) — never adding
new base keys or slots to an existing pack; that is sim content authoring, not tool plumbing.
