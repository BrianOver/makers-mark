using System.Net.Http.Json;
using System.Text.Json;

// Themed asset generator (U15). Dev-time only — never runs at game runtime
// (keeps the no-runtime-LLM boundary). Reads GEMINI_API_KEY from the environment;
// the key is NEVER read from or written to a file, and never printed.
//
// Usage:
//   dotnet run --project tools/AssetGen -- --dry-run          # print prompts, no API calls, no key needed
//   GEMINI_API_KEY=... dotnet run --project tools/AssetGen    # generate into godot/assets/art/
//
// Model id + endpoint are named constants below — verify against current Google
// Generative Language / Imagen docs before a real run; the API shape drifts.

const string Model = "imagen-3.0-generate-002"; // TODO: confirm latest Imagen model id at run time
const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models";

// Master prompt prefix — kept in sync with docs/style-bible.md.
const string Prefix =
    "Flat stylized 2D game art, fantasy-witchy with a subtle sci-fi tinge. " +
    "Dark desaturated palette: void purple-black background, iron greys, witchy purple accent, " +
    "sci-fi teal on faint circuit traces, warm ember candle-glow rim light. " +
    "Ancient craft touched by faint technology — runes and thin circuitry share the same metal. " +
    "Candlelit not neon. Clean outlines, 2-3 tone shading, no gradients, no text. " +
    "Centered subject, flat void background. Subject: ";

var subjects = new (string File, string Prompt)[]
{
    ("art/hero_vanguard.png", "a stoic armored vanguard warrior with a rune-etched shield, steel-blue tones"),
    ("art/hero_striker.png",  "a lean crimson-cloaked striker with twin blades, agile stance"),
    ("art/hero_mystic.png",   "a hooded violet mystic wreathed in faint arcane circuitry"),
    ("art/monster_floor1.png", "a mangy cave rat with faintly glowing teal eyes"),
    ("art/monster_floor2.png", "a bristling tunnel spider, chitin traced with circuitry"),
    ("art/monster_floor3.png", "a gaunt deep ghoul, hollow-eyed, rune-scarred"),
    ("art/monster_floor4.png", "a hulking ore golem, iron body seamed with teal light"),
    ("art/monster_floor5.png", "the Forgeworm, a vast molten serpent of iron and ember"),
    ("art/town_backdrop.png", "a candlelit fantasy blacksmith town square at dusk, cobblestones, a mine gate, warm windows"),
    ("art/memorial_stone.png", "a small weathered gravestone with a faint glowing rune, moss-touched"),
};

var dryRun = args.Contains("--dry-run");
var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY");

if (!dryRun && string.IsNullOrWhiteSpace(key))
{
    Console.Error.WriteLine("GEMINI_API_KEY not set. Set it in your environment (never in a file) or pass --dry-run.");
    return 1;
}

var outDir = Path.Combine("godot", "assets");
Directory.CreateDirectory(Path.Combine(outDir, "art"));

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

foreach (var (file, subject) in subjects)
{
    var prompt = Prefix + subject;
    if (dryRun)
    {
        Console.WriteLine($"[dry-run] {file}\n           {prompt}\n");
        continue;
    }

    var url = $"{Endpoint}/{Model}:predict?key={key}";
    var body = new
    {
        instances = new[] { new { prompt } },
        parameters = new { sampleCount = 1, aspectRatio = "1:1" },
    };

    using var resp = await http.PostAsJsonAsync(url, body);
    if (!resp.IsSuccessStatusCode)
    {
        // Never surface the URL (carries the key). Report status only.
        Console.Error.WriteLine($"FAILED {file}: HTTP {(int)resp.StatusCode}");
        continue;
    }

    var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
    var b64 = json.GetProperty("predictions")[0].GetProperty("bytesBase64Encoded").GetString();
    var path = Path.Combine(outDir, file);
    await File.WriteAllBytesAsync(path, Convert.FromBase64String(b64!));
    Console.WriteLine($"wrote {path}");
}

Console.WriteLine(dryRun ? "dry-run complete — no images generated." : "generation complete. Review, then commit the PNGs.");
return 0;
