using System.Net.Http.Json;
using System.Text.Json;

// Themed asset generator (U15). Dev-time only — never runs at game runtime
// (keeps the no-runtime-LLM boundary). Reads GEMINI_API_KEY from the environment;
// the key is NEVER read from or written to a file, NEVER placed in a URL, and
// NEVER printed — it travels only in the x-goog-api-key request header.
//
// Usage:
//   dotnet run --project tools/AssetGen -- --dry-run          # print prompts, no API calls, no key needed
//   GEMINI_API_KEY=... dotnet run --project tools/AssetGen    # generate into godot/assets/art/
//
// Model: gemini-2.5-flash-image ("Nano Banana"), the current non-deprecated image
// model (Imagen 3/4 shuts down 2026-08-17). generateContent returns image bytes as
// base64 in candidates[0].content.parts[].inlineData.data.

const string Model = "gemini-2.5-flash-image";
const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models";

// Master prompt prefix — kept in sync with docs/style-bible.md.
const string Prefix =
    "Flat stylized 2D game art, fantasy-witchy with a subtle sci-fi tinge. " +
    "Dark desaturated palette: void purple-black background, iron greys, witchy purple accent, " +
    "sci-fi teal on faint circuit traces, warm ember candle-glow rim light. " +
    "Ancient craft touched by faint technology — runes and thin circuitry share the same metal. " +
    "Candlelit not neon. Clean outlines, 2-3 tone shading, no gradients, no text. " +
    "Centered subject, flat void background, square framing. Subject: ";

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

Directory.CreateDirectory(Path.Combine("godot", "assets", "art"));

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
var ok = 0;

foreach (var (file, subject) in subjects)
{
    var prompt = Prefix + subject;
    if (dryRun)
    {
        Console.WriteLine($"[dry-run] {file}\n           {prompt}\n");
        continue;
    }

    using var req = new HttpRequestMessage(HttpMethod.Post, $"{Endpoint}/{Model}:generateContent");
    req.Headers.Add("x-goog-api-key", key); // key in header, never the URL — no leak in logs
    req.Content = JsonContent.Create(new
    {
        contents = new[] { new { parts = new[] { new { text = prompt } } } },
        generationConfig = new { responseModalities = new[] { "IMAGE" } },
    });

    using var resp = await http.SendAsync(req);
    if (!resp.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"FAILED {file}: HTTP {(int)resp.StatusCode} {resp.StatusCode}");
        continue;
    }

    var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
    var b64 = ExtractImage(json);
    if (b64 is null)
    {
        Console.Error.WriteLine($"FAILED {file}: no image part in response");
        continue;
    }

    var path = Path.Combine("godot", "assets", file);
    await File.WriteAllBytesAsync(path, Convert.FromBase64String(b64));
    Console.WriteLine($"wrote {path}");
    ok++;
}

Console.WriteLine(dryRun
    ? "dry-run complete — no images generated."
    : $"generation complete: {ok}/{subjects.Length} written. Review against docs/style-bible.md, then commit the PNGs.");
return 0;

// candidates[0].content.parts[] — the image part carries inlineData.data (base64).
static string? ExtractImage(JsonElement json)
{
    if (!json.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
    {
        return null;
    }

    if (!candidates[0].TryGetProperty("content", out var content)
        || !content.TryGetProperty("parts", out var parts))
    {
        return null;
    }

    foreach (var part in parts.EnumerateArray())
    {
        if (part.TryGetProperty("inlineData", out var inline)
            && inline.TryGetProperty("data", out var data))
        {
            return data.GetString();
        }
    }

    return null;
}
