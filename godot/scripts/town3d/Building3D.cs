using System.Collections.Generic;
using Godot;

namespace GodotClient.Town3d;

/// <summary>
/// T5: one interactable building in the 3D town — a mesh (Kenney GLB via <see
/// cref="TownAssets.BuildingScene"/> or a primitive wedge fallback), a layer-2 <see
/// cref="StaticBody3D"/> footprint blocking the player from walking through the base, a larger
/// layer-2/mask-4 interact <see cref="Area3D"/> the player's own layer-4 body is detected inside
/// (proximity only — see <see cref="WorldInput3D"/>, never a click target itself), a billboard
/// <see cref="Label3D"/> name tag, and a <see cref="Marker3D"/> "DoorAnchor" a body-radius
/// outside the footprint — the point <c>PlayerController.MoveToAndInteract</c> (T6) walks the
/// player to.
/// </summary>
public partial class Building3D : Node3D
{
    /// <summary>Player capsule radius (matches <c>PlayerController</c>'s collider) plus a small
    /// clearance margin — how far outside the footprint box <see cref="DoorAnchor"/> sits so the
    /// player can stand there without the footprint's own collision pushing them off it.</summary>
    private const float BodyRadius = 0.4f;

    private static readonly Vector3 FootprintSize = new(2.4f, 2.4f, 2.4f);
    private static readonly Vector3 InteractSize = new(4.4f, 3f, 4.4f);
    private static readonly Color HighlightEmission = new(1f, 0.85f, 0.35f);

    /// <summary>Stable identity ("forge" | "market" | "tavern" | "minegate" | "noticeboard") —
    /// matches <see cref="Town3D.FindBuilding"/>'s lookup key.</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>Payload raised on <see cref="WorldInput3D.Interacted"/> — the existing 2D-town
    /// vocabulary MainUi's <c>OnTownBuildingClicked</c> switch already handles ("Forge" | "Shop" |
    /// "Tavern" | "Gate"), plus "Bounties" for the noticeboard (T8 wires that case).</summary>
    public string ClickKey { get; private set; } = string.Empty;

    public Node3D Mesh { get; private set; } = null!;
    public StaticBody3D Footprint { get; private set; } = null!;
    public Area3D Interact { get; private set; } = null!;
    public Label3D Label { get; private set; } = null!;
    public Marker3D DoorAnchor { get; private set; } = null!;

    public Vector3 DoorAnchorGlobal => DoorAnchor.GlobalPosition;

    /// <summary>Test/inspection surface for <see cref="SetHighlighted"/> — the material swap
    /// itself is an implementation detail (and degrades gracefully for non-<see
    /// cref="StandardMaterial3D"/> surfaces), so callers/tests read intent through this flag
    /// rather than reaching into material state.</summary>
    public bool IsHighlighted { get; private set; }

    private readonly List<StandardMaterial3D> _highlightMaterials = new();

    /// <summary>
    /// Builds every child (mesh, footprint, interact zone, label, door anchor) fresh — call once
    /// per instance, before adding this node to the live tree (mirrors <c>Town3D.BuildPlayer</c>'s
    /// own "assemble fully, then AddChild" idiom).
    /// </summary>
    public void Configure(string key, string label, string clickKey, Vector3 pos, PackedScene? scene)
    {
        Key = key;
        ClickKey = clickKey;
        Name = $"Building_{key}";
        Position = pos;

        Mesh = scene != null ? scene.Instantiate<Node3D>() : BuildPrimitiveWedge();
        Mesh.Name = "Mesh";
        AddChild(Mesh);
        CollectHighlightMaterials(Mesh);

        Footprint = BuildFootprint();
        AddChild(Footprint);

        Interact = BuildInteractArea();
        AddChild(Interact);

        Label = BuildLabel(label);
        AddChild(Label);

        DoorAnchor = BuildDoorAnchor();
        AddChild(DoorAnchor);
    }

    /// <summary>Toggles the emissive tint on every collected surface material — a no-op on
    /// surfaces whose active material isn't a <see cref="StandardMaterial3D"/> (a small number of
    /// imported GLB materials could be some other <see cref="BaseMaterial3D"/> subtype; skipping
    /// those degrades to "no glow on that surface" rather than clobbering its texture with a
    /// blank material).</summary>
    public void SetHighlighted(bool on)
    {
        IsHighlighted = on;
        foreach (var material in _highlightMaterials)
        {
            material.EmissionEnabled = on;
            material.Emission = on ? HighlightEmission : Colors.Black;
        }
    }

    /// <summary>Placeholder shape used whenever <see cref="TownAssets.BuildingScene"/> returns
    /// null (asset missing/renamed) — a distinct wedge silhouette rather than a plain box, so a
    /// degraded building still reads as "a building" rather than "an untextured crate". The base
    /// color lives on the <see cref="PrismMesh"/> resource's own <see
    /// cref="PrimitiveMesh.Material"/> rather than <see
    /// cref="GeometryInstance3D.MaterialOverride"/> — the latter takes render-priority over a
    /// per-surface <see cref="MeshInstance3D.SetSurfaceOverrideMaterial"/> (which is exactly how
    /// <see cref="SetHighlighted"/> applies its glow), so setting it here would silently swallow
    /// the highlight on this fallback path while the surface material set by
    /// <see cref="CollectHighlightMaterials"/> renders underneath it, unseen.</summary>
    private static Node3D BuildPrimitiveWedge() => new MeshInstance3D
    {
        Mesh = new PrismMesh
        {
            Size = new Vector3(2f, 2f, 2f),
            Material = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.45f, 0.35f) },
        },
        Position = new Vector3(0, 1f, 0),
    };

    /// <summary>Recursively duplicates each <see cref="MeshInstance3D"/> surface's active
    /// material into a per-instance <see cref="StandardMaterial3D"/> override — duplicating
    /// (rather than mutating the imported/shared base material resource) keeps a highlighted
    /// building from bleeding its glow onto every other instance of the same GLB.</summary>
    private void CollectHighlightMaterials(Node root)
    {
        if (root is MeshInstance3D instance && instance.Mesh != null)
        {
            var surfaceCount = instance.Mesh.GetSurfaceCount();
            for (var i = 0; i < surfaceCount; i++)
            {
                if (instance.GetActiveMaterial(i) is not StandardMaterial3D baseMaterial)
                {
                    continue;
                }

                var duplicate = (StandardMaterial3D)baseMaterial.Duplicate();
                instance.SetSurfaceOverrideMaterial(i, duplicate);
                _highlightMaterials.Add(duplicate);
            }
        }

        foreach (var child in root.GetChildren())
        {
            CollectHighlightMaterials(child);
        }
    }

    private static StaticBody3D BuildFootprint()
    {
        var body = new StaticBody3D { Name = "Footprint", CollisionLayer = 2, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D
        {
            Name = "FootprintShape",
            Shape = new BoxShape3D { Size = FootprintSize },
            Position = new Vector3(0, FootprintSize.Y / 2f, 0),
        });
        return body;
    }

    private static Area3D BuildInteractArea()
    {
        var area = new Area3D
        {
            Name = "Interact",
            CollisionLayer = 2,
            CollisionMask = 4,
            Monitoring = true,
        };
        area.AddChild(new CollisionShape3D
        {
            Name = "InteractShape",
            Shape = new BoxShape3D { Size = InteractSize },
            Position = new Vector3(0, InteractSize.Y / 2f, 0),
        });
        return area;
    }

    private static Label3D BuildLabel(string text) => new()
    {
        Name = "Label3D",
        Text = text,
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        Position = new Vector3(0, FootprintSize.Y + 0.8f, 0),
        FontSize = 48,
        OutlineSize = 8,
    };

    /// <summary>A body-radius outside the footprint (along local +Z) — close enough for
    /// <see cref="WorldInput3D"/>'s (larger) interact zone to still contain it, far enough that
    /// the footprint's own collision never contests the player standing there.</summary>
    private static Marker3D BuildDoorAnchor() => new()
    {
        Name = "DoorAnchor",
        Position = new Vector3(0, 0, FootprintSize.Z / 2f + BodyRadius + 0.2f),
    };
}
