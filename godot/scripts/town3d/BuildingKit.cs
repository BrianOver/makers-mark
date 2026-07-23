using Godot;

namespace GodotClient.Town3d;

/// <summary>
/// Assembles each venue's real building from fantasy-town-kit/castle-kit pieces at fixed,
/// deterministic local offsets (no RNG — KTD2) — replacing the single-stall stand-ins T5 shipped
/// with. Every wall/banner/chimney/fence piece in these kits shares one placement convention: at
/// <see cref="Vector3.Zero"/> rotation the piece's outward face sits on local +X, spanning
/// Z -0.5..0.5 (see each GLB's own accessor bounds) — so four copies rotated in 90° steps around Y
/// form a complete 1x1 room, and a roof/chimney/banner dropped at the same origin (no offset)
/// lines up automatically. <see cref="FaceFrontY"/> is the rotation that points a wall's face down
/// local +Z — the direction <c>Building3D.BuildDoorAnchor</c> already places its anchor — so the
/// door-cut wall goes there and every other piece is a multiple of 90° off it.
///
/// <para>Each assembly returns an unscaled root; <see cref="TownAssets.BuildBuilding"/>'s caller
/// (<c>Building3D.Configure</c>) receives it pre-scaled — see <see cref="Cottage"/>'s own
/// <see cref="TownAssets.BuildingScale"/> application.</para>
/// </summary>
internal static class BuildingKit
{
    private const string Kit = TownAssets.FantasyTownKit;
    private const string Castle = TownAssets.CastleKit;

    // Empirically verified against a rendered screenshot (not hand-derived from the rotation
    // matrix alone — Basis.RotatedY's sign convention is easy to get backwards from inspection):
    // this is the Y rotation that turns a wall piece's outward face to point down local +Z.
    private const float FaceFrontY = 270f;
    private const float FaceBackY = 90f;
    private const float FaceRightY = 0f;
    private const float FaceLeftY = 180f;

    public static Node3D Build(string key) => key switch
    {
        "forge" => BuildForge(),
        "market" => BuildMarket(),
        "tavern" => BuildTavern(),
        "minegate" => BuildMineGate(),
        "noticeboard" => BuildNoticeboard(),
        _ => BuildForge(),
    };

    /// <summary>The forge is the first venue wired to the AI-gen pipeline: if a normalized
    /// <c>gen/forge.glb</c> is present it becomes the building's mesh directly (feet-pivoted +
    /// pre-scaled by <c>normalize_glb.py</c>, so no <see cref="TownAssets.BuildingScale"/>), else we
    /// fall back to the Kenney assembly — stone walls, a front door, a gable roof, and a chimney.
    /// Either way a warm forge-glow light is parented to the root near the base.</summary>
    private static Node3D BuildForge()
    {
        var gen = TownAssets.InstantiateGen("forge.glb");
        Node3D root;
        if (gen != null)
        {
            root = new Node3D { Name = "Forge" };
            root.AddChild(gen);
        }
        else
        {
            root = Cottage("Forge", "wall.glb", "wall-door.glb", "roof-gable.glb", chimney: true);
        }

        root.AddChild(new OmniLight3D
        {
            Name = "ForgeGlow",
            Position = new Vector3(0f, 0.35f, 0.4f),
            LightColor = new Color(1f, 0.55f, 0.2f),
            LightEnergy = 1.4f,
            OmniRange = 2.2f,
        });
        return root;
    }

    /// <summary>A single enlarged market stall (its own canvas-roof colour reads "shop" on sight)
    /// with a banner planted beside it — plan's "a stall or a wall building with a banner" option,
    /// keeping the recognizable stall silhouette from T5 rather than hiding it inside a room.
    /// </summary>
    private static Node3D BuildMarket()
    {
        var gen = TownAssets.InstantiateGen("market.glb");
        if (gen != null)
        {
            var g = new Node3D { Name = "Market" };
            g.AddChild(gen);
            return g;
        }

        var root = new Node3D { Name = "Market", Scale = new Vector3(TownAssets.BuildingScale, TownAssets.BuildingScale, TownAssets.BuildingScale) };
        AddPiece(root, Kit, "stall-red.glb", 0f);

        var banner = AddPiece(root, Kit, "banner-red.glb", FaceLeftY);
        if (banner != null)
        {
            banner.Position = new Vector3(-0.55f, 0f, 0f);
        }

        return root;
    }

    /// <summary>A bigger wood-walled cottage (log-cabin colouring distinguishes it from the
    /// forge's stone) topped with a taller high-gable roof, a chimney for the hearth, a green
    /// banner out front, and a lantern by the door.</summary>
    private static Node3D BuildTavern()
    {
        var gen = TownAssets.InstantiateGen("tavern.glb");
        if (gen != null)
        {
            var g = new Node3D { Name = "Tavern" };
            g.AddChild(gen);
            return g;
        }

        var root = Cottage("Tavern", "wall-wood.glb", "wall-wood-door.glb", "roof-high-gable.glb", chimney: true, lantern: true);

        var banner = AddPiece(root, Kit, "banner-green.glb", FaceFrontY);
        if (banner != null)
        {
            banner.Position = new Vector3(0.35f, 0f, 0f);
        }

        return root;
    }

    /// <summary>The mine entrance: a castle-kit metal gate framed by a rock outcrop on either
    /// side, scaled up so it reads as a landmark from across the plaza.</summary>
    private static Node3D BuildMineGate()
    {
        var gen = TownAssets.InstantiateGen("minegate.glb");
        if (gen != null)
        {
            var g = new Node3D { Name = "MineGate" };
            g.AddChild(gen);
            return g;
        }

        var root = new Node3D { Name = "MineGate", Scale = new Vector3(TownAssets.BuildingScale, TownAssets.BuildingScale, TownAssets.BuildingScale) };

        AddPiece(root, Castle, "metal-gate.glb", FaceFrontY);

        var rockLeft = AddPiece(root, Kit, "rock-large.glb", 0f);
        if (rockLeft != null)
        {
            rockLeft.Position = new Vector3(-0.85f, 0f, 0.1f);
        }

        var rockRight = AddPiece(root, Kit, "rock-large.glb", 180f);
        if (rockRight != null)
        {
            rockRight.Position = new Vector3(0.85f, 0f, 0.1f);
        }

        return root;
    }

    /// <summary>A small covered notice stand — a cross-braced board face on two posts under a
    /// flat roof, instead of the bare wall-panel stand-in T5 shipped.</summary>
    private static Node3D BuildNoticeboard()
    {
        var gen = TownAssets.InstantiateGen("noticeboard.glb");
        if (gen != null)
        {
            var g = new Node3D { Name = "Noticeboard" };
            g.AddChild(gen);
            return g;
        }

        var root = new Node3D { Name = "Noticeboard", Scale = new Vector3(TownAssets.BuildingScale, TownAssets.BuildingScale, TownAssets.BuildingScale) };

        AddPiece(root, Kit, "wall-detail-cross.glb", FaceFrontY);

        var postLeft = AddPiece(root, Kit, "pillar-wood.glb", 0f);
        if (postLeft != null)
        {
            postLeft.Position = new Vector3(-0.4f, 0f, 0f);
        }

        var postRight = AddPiece(root, Kit, "pillar-wood.glb", 0f);
        if (postRight != null)
        {
            postRight.Position = new Vector3(0.4f, 0f, 0f);
        }

        var roof = AddPiece(root, Kit, "roof-flat.glb", 0f);
        if (roof != null)
        {
            roof.Position = new Vector3(0f, 1f, 0f);
        }

        return root;
    }

    /// <summary>Four walls (one the door-cut variant, facing <see cref="FaceFrontY"/>) forming a
    /// 1x1 room, a roof on top, and an optional chimney — the shared shape behind
    /// <see cref="BuildForge"/> and <see cref="BuildTavern"/>; only the piece set and a couple of
    /// optional extras differ between venues.</summary>
    private static Node3D Cottage(string name, string wallAsset, string doorWallAsset, string roofAsset, bool chimney, bool lantern = false)
    {
        var root = new Node3D { Name = name, Scale = new Vector3(TownAssets.BuildingScale, TownAssets.BuildingScale, TownAssets.BuildingScale) };

        AddPiece(root, Kit, doorWallAsset, FaceFrontY);
        AddPiece(root, Kit, wallAsset, FaceBackY);
        AddPiece(root, Kit, wallAsset, FaceRightY);
        AddPiece(root, Kit, wallAsset, FaceLeftY);

        var roof = AddPiece(root, Kit, roofAsset, 0f);
        if (roof != null)
        {
            roof.Position = new Vector3(0f, 1f, 0f);
        }

        if (chimney)
        {
            var stack = AddPiece(root, Kit, "chimney.glb", FaceBackY);
            if (stack != null)
            {
                stack.Position = new Vector3(0f, 1f, 0f);
            }
        }

        if (lantern)
        {
            var lamp = AddPiece(root, Kit, "lantern.glb", 0f);
            if (lamp != null)
            {
                lamp.Position = new Vector3(0.65f, 0f, 0.55f); // just outside the footprint, by the door
                TownAssets.AttachLanternGlow(lamp);
            }
        }

        return root;
    }

    private static Node3D? AddPiece(Node3D root, string kitFolder, string asset, float rotationYDeg)
    {
        var piece = TownAssets.Instantiate(kitFolder, asset);
        if (piece == null)
        {
            return null;
        }

        piece.RotationDegrees = new Vector3(0f, rotationYDeg, 0f);
        root.AddChild(piece);
        return piece;
    }
}
