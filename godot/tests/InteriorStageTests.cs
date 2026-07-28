// DEFERRED (2.5D pivot): tested the staged-interior flow (walk → E-interact → InteriorStage opens →
// hotspot opens the matching drawer → exit restores avatar to the door). The 2.5D slice routes a
// building click straight to its drawer (MainUi.OnTownBuildingClicked → OpenPanel); the InteriorStage
// widget is left wired-but-dormant. Restore these when interiors return (Control-based InteriorStage
// resurrection). See docs/plans/2026-07-27-006-feat-2p5d-stardew-pivot-plan.md.
