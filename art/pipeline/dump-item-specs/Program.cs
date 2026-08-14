// Emits the exact prompt / negative / seed / sampler settings the committed AssetSpecs already
// define for every `item-*` icon, as JSON for `gen-item-variants.py` to render from.
//
// WHY THIS EXISTS AS A TOOL rather than a hand-written JSON file: the variants must be SIBLINGS of
// the shipped icon — same master prompt, same track profile, same palette-family clause, same
// negative escalation — differing only in seed. Retyping any of that into a data file would create
// a second, silently-drifting copy of the art contract, and the first person to edit an AssetSpec
// would ship a variant that no longer matches its own base. Reading it back out of
// `AssetRegistry` keeps the specs the single source of truth.
//
//   dotnet run --project art/pipeline/dump-item-specs -- <output.json>
//
// Local-only: not in Game.sln, never run by CI. See ../README.md §5.
using System.Text.Json;
using GameArt;

var outPath = args.Length > 0 ? args[0] : "item-jobs.json";

var jobs = new List<object>();
foreach (var (id, spec) in AssetRegistry.All)
{
    if (!id.StartsWith("item-", StringComparison.Ordinal)) continue;

    var profile = ArtTrackProfiles.For(spec.Track);
    jobs.Add(new
    {
        id,
        subject = spec.Subject,
        prompt = ArtTrackProfiles.ComposePrompt(spec),
        negative = ArtTrackProfiles.ComposeNegative(spec),
        seed = AssetSeed.SeedFor(id),
        width = spec.Width ?? profile.Width,
        height = spec.Height ?? profile.Height,
        steps = spec.Steps ?? profile.Steps,
        cfg = (spec.CfgMilli ?? profile.CfgMilli) / 1000.0,
        sampler = spec.SamplerId ?? profile.SamplerId,
        scheduler = spec.SchedulerId ?? profile.SchedulerId,
        normalMap = spec.NormalMap,
    });
}

File.WriteAllText(outPath, JsonSerializer.Serialize(new { count = jobs.Count, jobs },
    new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine($"wrote {jobs.Count} item specs to {outPath}");
