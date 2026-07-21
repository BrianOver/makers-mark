"""
normalize_glb.py — headless Blender post-processor for AI-generated 3D meshes.

Turns the dense, arbitrarily-scaled/oriented mesh that TRELLIS.2 / Hunyuan3D / TripoSG
emit into a Godot-ready, low-poly, feet-pivoted GLB. This is the pipeline step that
makes AI-gen assets actually usable in the town (see docs/design/2026-07-20-3d-asset-gen-plan.md).

RUN (Blender 4.x, headless — never opens a window):
    blender -b -P tools/blender/normalize_glb.py -- \
        --in  raw/forge_raw.glb \
        --out godot/assets/models/gen/forge.glb \
        --tris 3000 \
        --pivot base \
        --up-forward "Y,-Z"

What it does, in order:
  1. Load the input GLB into a clean empty scene.
  2. Join all mesh objects into one.
  3. Decimate to a triangle budget (Collapse modifier; ratio derived from current tri count).
  4. Recenter: set the object's origin to the chosen pivot
       - "base"   -> bottom-centre (feet on the ground)  [default, correct for Godot Y-up placement]
       - "center" -> volumetric centre
     then move the object so that pivot sits at world origin (0,0,0).
  5. Apply scale + rotation (so Godot sees identity transforms).
  6. Optional uniform rescale to a target height in metres (--height).
  7. Export a single GLB (glTF 2.0 binary), Y-up, +materials, ready for Godot's importer.

DETERMINISTIC + OFFLINE: no network, no RNG, bounded work. Safe to run in a batch.
Exit code 0 on success, non-zero on failure (so a batch wrapper can detect + continue).

NOTE: written against the Blender 4.x Python API (bpy). Not yet run on this machine
(Blender is not installed here — install per the runbook, then smoke-test on one asset).
Treat the first real run as a smoke test and eyeball the result in Godot before batching.
"""

import argparse
import math
import sys

import bpy  # provided by the Blender runtime; not a pip package


def _args():
    # Blender passes script args after a literal "--"; everything before is Blender's own.
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser(description="Normalize an AI-generated GLB for Godot.")
    p.add_argument("--in", dest="src", required=True, help="input .glb path")
    p.add_argument("--out", dest="dst", required=True, help="output .glb path")
    p.add_argument("--tris", type=int, default=3000, help="target triangle budget (0 = no decimation)")
    p.add_argument("--pivot", choices=["base", "center"], default="base",
                   help="where to place the origin: base=feet-on-ground (default), center=volumetric centre")
    p.add_argument("--height", type=float, default=0.0,
                   help="if >0, uniformly rescale so the object is this tall in metres (Godot units)")
    return p.parse_args(argv)


def _reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def _import_glb(path):
    bpy.ops.import_scene.gltf(filepath=path)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        raise RuntimeError(f"no mesh objects found in {path}")
    return meshes


def _join(meshes):
    bpy.ops.object.select_all(action="DESELECT")
    for m in meshes:
        m.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    return bpy.context.view_layer.objects.active


def _tri_count(obj):
    # Evaluate with modifiers applied so triangulation reflects the real render mesh.
    deps = bpy.context.evaluated_depsgraph_get()
    eval_obj = obj.evaluated_get(deps)
    mesh = eval_obj.to_mesh()
    mesh.calc_loop_triangles()
    n = len(mesh.loop_triangles)
    eval_obj.to_mesh_clear()
    return n


def _decimate(obj, target_tris):
    if target_tris <= 0:
        return
    current = _tri_count(obj)
    if current <= target_tris:
        return
    mod = obj.modifiers.new(name="Decimate", type="DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = max(0.01, min(1.0, target_tris / float(current)))
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)


def _set_origin(obj, pivot):
    # Godot places instances by their origin; feet-on-ground is what the town's placement
    # code (Building3D/HeroActor3D) expects. Compute the target point in world space, then
    # use the 3D cursor + Blender's origin-set op.
    bpy.context.view_layer.objects.active = obj
    # World-space bounding box corners:
    corners = [obj.matrix_world @ __import_vector(c) for c in obj.bound_box]
    xs = [c.x for c in corners]
    ys = [c.y for c in corners]
    zs = [c.z for c in corners]
    cx = (min(xs) + max(xs)) / 2.0
    cy = (min(ys) + max(ys)) / 2.0
    # Blender is Z-up internally; "base" = lowest Z (glTF export converts to Y-up for Godot).
    if pivot == "base":
        cz = min(zs)
    else:
        cz = (min(zs) + max(zs)) / 2.0
    bpy.context.scene.cursor.location = (cx, cy, cz)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    # Move the object so its (new) origin sits at world zero.
    obj.location = (0.0, 0.0, 0.0)


def __import_vector(seq):
    from mathutils import Vector
    return Vector(seq)


def _apply_transforms(obj):
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)


def _rescale_to_height(obj, height_m):
    if height_m <= 0:
        return
    dims = obj.dimensions
    tall = max(dims.z, 1e-6)  # Blender Z-up = height before export
    factor = height_m / tall
    obj.scale = (factor, factor, factor)
    _apply_transforms(obj)


def _export_glb(obj, path):
    import os
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        use_selection=True,
        export_yup=True,            # Godot expects Y-up
        export_apply=True,          # bake remaining modifiers
    )


def main():
    a = _args()
    _reset_scene()
    meshes = _import_glb(a.src)
    obj = _join(meshes)
    _decimate(obj, a.tris)
    _set_origin(obj, a.pivot)
    _apply_transforms(obj)
    _rescale_to_height(obj, a.height)
    _export_glb(obj, a.dst)
    print(f"[normalize_glb] OK  {a.src} -> {a.dst}  (~{_tri_count(obj)} tris, pivot={a.pivot})")


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:  # noqa: BLE001 — batch wrapper needs a non-zero exit on any failure
        print(f"[normalize_glb] FAILED: {exc}", file=sys.stderr)
        sys.exit(1)
