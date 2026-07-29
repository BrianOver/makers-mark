using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Professions;

/// <summary>
/// U8 (plan <c>2026-07-28-002</c>): the tanner's scraping-frame input — a hide stretched on a frame,
/// scraped clean cell by cell. Mirrors <see cref="AlchemyReagentPuzzle"/>'s shape deliberately: a
/// flat list of integers and nothing else, so the presentation layer can capture a drag gesture
/// however it likes while the seam stays trivially deterministic (KTD-B/KTD-D).
///
/// <para><paramref name="CellPasses"/> is how many scrape passes each cell received, indexed
/// row-major over <see cref="TanningScrapeScorer.Columns"/> x <see cref="TanningScrapeScorer.Rows"/>.
/// Order carries no meaning — only the counts do — which is what makes this the "coverage and
/// restraint" skill rather than another sequence puzzle. <paramref name="PatchSeed"/> selects which
/// cells hid a flaw or ran thin; the adapter derives it deterministically from the recipe and day
/// (never RNG) and the scorer regenerates the SAME patches from it, so what the player scraped is
/// exactly what gets graded.</para>
/// </summary>
public sealed record TanningScrapeInput(ImmutableList<int> CellPasses, int PatchSeed) : CraftPuzzleInput;
