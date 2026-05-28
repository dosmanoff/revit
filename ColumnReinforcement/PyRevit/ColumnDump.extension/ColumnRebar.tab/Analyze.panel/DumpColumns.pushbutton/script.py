# -*- coding: utf-8 -*-
"""Dump structural columns + spatial context to a JSON file.

Companion to the ColumnReinforcement plugin. The JSON is meant to be read by
Claude (or another agent) which then produces assignments.csv per
assignments-csv-guide.md.

Output schema is intentionally flat and explicit so an LLM doesn't have to
guess. All section dimensions are in inches; all elevations and plan offsets
are in feet (Revit's internal unit).
"""

from __future__ import print_function, division

import datetime
import io
import json
import math
import os
import traceback

from Autodesk.Revit.DB import (
    BuiltInCategory,
    BuiltInParameter,
    Element,
    FamilyInstance,
    FilteredElementCollector,
    Options,
    PlanarFace,
    StorageType,
    ViewDetailLevel,
    XYZ,
)
from Autodesk.Revit.DB.Structure import RebarBarType, RebarHookType

from pyrevit import forms, revit, script

doc = revit.doc
uidoc = revit.uidoc
output = script.get_output()

FT_TO_IN = 12.0
Z_TOLERANCE_FT = 1.0 / 96.0          # 1/8" — same as the C# HostContext
XY_NEIGHBOUR_TOL_FT = 1.5            # 18" — columns "in the same stack"; insets resolve true offsets
SCHEMA_VERSION = 5

# ACI 318 §10.7.4.1: maximum crank slope = 1:6 (inward shift per unit vertical).
ACI_MAX_CRANK_SLOPE = 1.0 / 6.0
# Conservative budget consumed by the lower lap + lower bend offset + upper lap (inches);
# the remaining column height is available for the diagonal segment of a Cranked splice.
CRANK_LAP_BUDGET_IN = 30.0


# ──────────────────────────────────────────────────────────────────────────────
# Helpers
# ──────────────────────────────────────────────────────────────────────────────

def eid_value(eid):
    """ElementId → JSON-safe integer.

    Revit 2024+ exposes `ElementId.Value` as .NET `Int64` → Python `long` in
    IronPython 2.7. The bundled json encoder only treats Python `int` as a
    JSON number (long falls through to `default` and raises), so we coerce.
    For IDs that don't fit in `Int32` we fall back to a string — the spec
    doesn't promise ElementIds will be numeric, and a string is safer than a
    silent overflow.
    """
    if eid is None:
        return None
    raw = eid.Value if hasattr(eid, "Value") else eid.IntegerValue
    try:
        coerced = int(raw)
        # Python 2 / IronPython 2.7: `int(huge_long)` returns long again. The
        # encoder still wouldn't accept it — stringify.
        if type(coerced).__name__ == "long":
            return str(raw)
        return coerced
    except Exception:
        return str(raw)


def _json_default(obj):
    """Last-resort encoder for values json doesn't recognise — mostly IronPython
    `long`s sneaking in from Revit API integer properties we haven't coerced."""
    try:
        coerced = int(obj)
        if type(coerced).__name__ == "long":
            return str(obj)
        return coerced
    except Exception:
        return str(obj)


def get_param_string(elem, bip):
    p = elem.get_Parameter(bip)
    if p is None or not p.HasValue:
        return None
    if p.StorageType == StorageType.String:
        return p.AsString()
    return p.AsValueString()


def get_element_name(elem):
    """Robust `Element.Name` access.

    In Revit 2024+ the `Name` property on several Element subclasses
    (RebarBarType, Level, ElementType, …) is exposed in a way that IronPython
    can't reach via plain `elem.Name` — the attribute lookup raises
    `AttributeError: Name`. Calling the .NET property getter directly via
    `Element.Name.GetValue(elem)` works regardless.
    """
    if elem is None:
        return None
    try:
        return Element.Name.GetValue(elem)
    except Exception:
        try:
            return elem.Name
        except Exception:
            return None


def safe_round(x, n=4):
    if x is None:
        return None
    try:
        return round(float(x), n)
    except Exception:
        return None


# ──────────────────────────────────────────────────────────────────────────────
# Parameter serialisation — generic dump of every readable parameter, so the
# downstream agent sees Comments, custom shared parameters (e.g. "Corner Mark",
# "Column.Reaction"), schedule notes encoded as text, etc. Without this the
# dump only carries what the script explicitly extracts, which is brittle.
# ──────────────────────────────────────────────────────────────────────────────

def serialize_param(p):
    """Convert a Revit Parameter to a JSON-friendly value, or None if empty."""
    if p is None or not p.HasValue:
        return None
    try:
        st = p.StorageType
    except Exception:
        return None
    try:
        if st == StorageType.String:
            s = p.AsString()
            return s if s else None
        if st == StorageType.Integer:
            return int(p.AsInteger())
        if st == StorageType.Double:
            raw = safe_round(p.AsDouble(), 6)
            display = None
            try:
                display = p.AsValueString()
            except Exception:
                pass
            if display:
                return {"raw": raw, "display": display}
            return raw
        if st == StorageType.ElementId:
            eid = p.AsElementId()
            v = eid_value(eid)
            if v is None or v == -1:
                return None
            ref = doc.GetElement(eid)
            if ref is None:
                return {"id": v}
            return {"id": v, "name": get_element_name(ref)}
    except Exception:
        return None
    return None


def dump_parameters(elem):
    """Dict of every parameter on elem with a non-null value. Keyed by display name."""
    out = {}
    if elem is None:
        return out
    try:
        params = elem.Parameters
    except Exception:
        return out
    for p in params:
        try:
            name = p.Definition.Name
        except Exception:
            continue
        value = serialize_param(p)
        if value is None:
            continue
        # Two parameters can technically share a name (shared + builtin); keep the first.
        if name not in out:
            out[name] = value
    return out


def cover_info(elem, bip_name):
    """Extract a RebarHostCover-style parameter (CLEAR_COVER_TOP / BOTTOM / OTHER).

    Returns {name, distance_in} or None when not set.
    """
    bip = getattr(BuiltInParameter, bip_name, None)
    if bip is None:
        return None
    p = elem.get_Parameter(bip)
    if p is None or not p.HasValue:
        return None
    try:
        cover_type_id = p.AsElementId()
    except Exception:
        return None
    if cover_type_id is None:
        return None
    v = eid_value(cover_type_id)
    if v is None or v == -1:
        return None
    cover_type = doc.GetElement(cover_type_id)
    if cover_type is None:
        return None
    try:
        return {
            "name": get_element_name(cover_type),
            "distance_in": safe_round(cover_type.CoverDistance * FT_TO_IN, 3),
        }
    except Exception:
        return None


# ──────────────────────────────────────────────────────────────────────────────
# Cross-section analysis — mirrors Domain/ColumnGeometry.cs
# ──────────────────────────────────────────────────────────────────────────────

def get_main_solid(inst):
    opts = Options()
    opts.ComputeReferences = False
    opts.IncludeNonVisibleObjects = False
    opts.DetailLevel = ViewDetailLevel.Fine

    geo = inst.get_Geometry(opts)
    if geo is None:
        return None

    best = None
    best_vol = 0.0
    for obj in geo:
        # Top-level Solid
        try:
            vol = obj.Volume
            if vol > best_vol:
                best = obj
                best_vol = vol
                continue
        except AttributeError:
            pass
        # GeometryInstance → unwrap
        try:
            inner_geo = obj.GetInstanceGeometry()
        except AttributeError:
            continue
        for inner in inner_geo:
            try:
                vol = inner.Volume
                if vol > best_vol:
                    best = inner
                    best_vol = vol
            except AttributeError:
                pass
    return best


def find_bottom_face(solid):
    best = None
    lowest_z = float("inf")
    for f in solid.Faces:
        if not isinstance(f, PlanarFace):
            continue
        if f.FaceNormal.DotProduct(XYZ.BasisZ) > -0.99:
            continue
        if f.Origin.Z < lowest_z:
            lowest_z = f.Origin.Z
            best = f
    return best


def analyse_cross_section(inst, tr):
    """Return (width_ft, depth_ft, section_str) or None."""
    solid = get_main_solid(inst)
    if solid is None:
        return None

    bottom = find_bottom_face(solid)
    if bottom is None:
        return None

    loops = bottom.EdgeLoops
    if loops.Size < 1:
        return None

    outer = loops.get_Item(0)

    all_arcs = True
    any_line = False
    for i in range(outer.Size):
        c = outer.get_Item(i).AsCurve()
        cname = type(c).__name__
        if cname == "Line":
            any_line = True
            all_arcs = False
        elif cname != "Arc":
            all_arcs = False

    section = "Round" if (all_arcs and not any_line) else "Rectangular"

    x_min = float("inf")
    x_max = float("-inf")
    y_min = float("inf")
    y_max = float("-inf")
    for i in range(outer.Size):
        for pt in outer.get_Item(i).Tessellate():
            rel = pt - tr.Origin
            x = rel.DotProduct(tr.BasisX)
            y = rel.DotProduct(tr.BasisY)
            if x < x_min:
                x_min = x
            if x > x_max:
                x_max = x
            if y < y_min:
                y_min = y
            if y > y_max:
                y_max = y

    width = x_max - x_min
    depth = y_max - y_min
    if width <= 0 or depth <= 0:
        return None
    return (width, depth, section)


def fallback_aabb(bb, tr):
    """If we can't get a clean bottom face — project AABB corners onto local axes."""
    min_x = float("inf")
    max_x = float("-inf")
    min_y = float("inf")
    max_y = float("-inf")
    mn, mx = bb.Min, bb.Max
    corners = [
        XYZ(mn.X, mn.Y, mn.Z), XYZ(mx.X, mn.Y, mn.Z),
        XYZ(mn.X, mx.Y, mn.Z), XYZ(mx.X, mx.Y, mn.Z),
        XYZ(mn.X, mn.Y, mx.Z), XYZ(mx.X, mn.Y, mx.Z),
        XYZ(mn.X, mx.Y, mx.Z), XYZ(mx.X, mx.Y, mx.Z),
    ]
    for c in corners:
        rel = c - tr.Origin
        x = rel.DotProduct(tr.BasisX)
        y = rel.DotProduct(tr.BasisY)
        if x < min_x:
            min_x = x
        if x > max_x:
            max_x = x
        if y < min_y:
            min_y = y
        if y > max_y:
            max_y = y
    return (max_x - min_x, max_y - min_y, "Rectangular")


def column_geometry(inst):
    """Returns dict with section, width_ft, depth_ft, height_ft, base_center, local_x_deg."""
    if inst.IsSlantedColumn:
        raise ValueError("slanted column not supported")

    tr = inst.GetTotalTransform()
    bb = inst.get_BoundingBox(None)
    if bb is None:
        raise ValueError("no bounding box")

    height = bb.Max.Z - bb.Min.Z
    cross = analyse_cross_section(inst, tr) or fallback_aabb(bb, tr)
    width, depth, section = cross

    basis_x_xy = (tr.BasisX.X, tr.BasisX.Y)
    basis_y_xy = (tr.BasisY.X, tr.BasisY.Y)

    # Canonicalise so LocalX is always the SHORTER in-plan side (width <= depth),
    # mirroring Domain/ColumnGeometry.cs. This keeps the dump's local frame — and
    # therefore the +x/-x/+y/-y face semantics, the neighbour offsets, and the
    # per-face insets below — identical to what the C# engine builds, so the
    # face selectors the agent emits land on the right bars. Rotating +90° about
    # Z (newX = oldY, newY = -oldX) preserves right-handedness.
    if section == "Rectangular" and width > depth:
        width, depth = depth, width
        new_x = basis_y_xy
        new_y = (-basis_x_xy[0], -basis_x_xy[1])
        basis_x_xy, basis_y_xy = new_x, new_y

    rot_rad = math.atan2(basis_x_xy[1], basis_x_xy[0])
    rot_deg = math.degrees(rot_rad)

    return {
        "section": section,
        "width_ft": width,
        "depth_ft": depth,
        "height_ft": height,
        "base_center": XYZ(tr.Origin.X, tr.Origin.Y, bb.Min.Z),
        "top_z_ft": bb.Max.Z,
        "rotation_deg": rot_deg,
        "basis_x_xy": basis_x_xy,
        "basis_y_xy": basis_y_xy,
    }


def faces_block(g):
    """Explicit per-face semantics for the agent, in the canonical frame.

    Keys +x / -x / +y / -y are EXACTLY the engine's LongTopModes face selectors.
    After canonicalisation LocalX is the short side, so the ±X faces (perpendicular
    to LocalX, spanning the depth) are the LONG faces, and the ±Y faces are SHORT.
    `face_insets_in` on the neighbour columns is measured against these same faces.
    """
    if g["section"] == "Round":
        return None
    width_in = g["width_ft"] * FT_TO_IN     # short (along LocalX)
    depth_in = g["depth_ft"] * FT_TO_IN     # long  (along LocalY)
    bx = g["basis_x_xy"]
    by = g["basis_y_xy"]
    deg = lambda vx, vy: safe_round(math.degrees(math.atan2(vy, vx)), 1)
    return {
        "+x": {"plan_length_in": safe_round(depth_in, 3), "kind": "long",
               "outward_normal_world_deg": deg(bx[0], bx[1])},
        "-x": {"plan_length_in": safe_round(depth_in, 3), "kind": "long",
               "outward_normal_world_deg": deg(-bx[0], -bx[1])},
        "+y": {"plan_length_in": safe_round(width_in, 3), "kind": "short",
               "outward_normal_world_deg": deg(by[0], by[1])},
        "-y": {"plan_length_in": safe_round(width_in, 3), "kind": "short",
               "outward_normal_world_deg": deg(-by[0], -by[1])},
    }


# ──────────────────────────────────────────────────────────────────────────────
# Non-coaxial neighbour analysis
# ──────────────────────────────────────────────────────────────────────────────

def normalise_angle_deg(a):
    """Fold to (-180, 180]."""
    a = a % 360.0
    if a > 180.0:
        a -= 360.0
    if a <= -180.0:
        a += 360.0
    return a


def relative_pose(this_g, other_g):
    """Other column's center + rotation expressed in this column's local frame.

    Returns dict with keys:
      offset_local_x_in, offset_local_y_in — center-to-center offset (inches, in
        this column's LocalX/LocalY axes)
      relative_rotation_deg              — other.rotation - this.rotation, folded to (-180, 180]
      axis_aligned                       — True when relative rotation is a multiple of 90° (±1°)
      axis_swap                          — True when the aligned rotation is 90° or 270° (W↔D swap)
    """
    dx = other_g["base_center"].X - this_g["base_center"].X
    dy = other_g["base_center"].Y - this_g["base_center"].Y
    bx = this_g["basis_x_xy"]
    by = this_g["basis_y_xy"]

    off_x_ft = dx * bx[0] + dy * bx[1]
    off_y_ft = dx * by[0] + dy * by[1]

    rel_rot = normalise_angle_deg(other_g["rotation_deg"] - this_g["rotation_deg"])
    folded = abs(((rel_rot + 45.0) % 90.0) - 45.0)        # distance from nearest 90° multiple
    axis_aligned = folded < 1.0
    axis_swap = axis_aligned and abs(abs(rel_rot) - 90.0) < 1.0

    return {
        "offset_local_x_in": safe_round(off_x_ft * FT_TO_IN, 3),
        "offset_local_y_in": safe_round(off_y_ft * FT_TO_IN, 3),
        "relative_rotation_deg": safe_round(rel_rot, 2),
        "axis_aligned": axis_aligned,
        "axis_swap": axis_swap,
    }


def face_insets_rect_rect(this_g, other_g, pose):
    """Per-face inset of other (rectangular) column inside this (rectangular) column,
    measured in this column's local frame.

    Positive value = the other face is INSIDE this face by that many inches (concrete
    "shoulder" available for a cranked dowel). Negative = the other column overhangs
    this column on that face — Cranked is geometrically impossible on that side.

    Returns None if either column is non-rectangular or relative rotation is not
    a multiple of 90°.
    """
    if this_g["section"] != "Rectangular" or other_g["section"] != "Rectangular":
        return None
    if not pose["axis_aligned"]:
        return None

    this_w_in = this_g["width_ft"] * FT_TO_IN
    this_d_in = this_g["depth_ft"] * FT_TO_IN
    if pose["axis_swap"]:
        other_w_in_local = other_g["depth_ft"] * FT_TO_IN
        other_d_in_local = other_g["width_ft"] * FT_TO_IN
    else:
        other_w_in_local = other_g["width_ft"] * FT_TO_IN
        other_d_in_local = other_g["depth_ft"] * FT_TO_IN

    cx = pose["offset_local_x_in"]
    cy = pose["offset_local_y_in"]

    plus_x = (this_w_in / 2.0) - (cx + other_w_in_local / 2.0)
    minus_x = (cx - other_w_in_local / 2.0) - (-this_w_in / 2.0)
    plus_y = (this_d_in / 2.0) - (cy + other_d_in_local / 2.0)
    minus_y = (cy - other_d_in_local / 2.0) - (-this_d_in / 2.0)

    return {
        "plus_x_in": safe_round(plus_x, 3),
        "minus_x_in": safe_round(minus_x, 3),
        "plus_y_in": safe_round(plus_y, 3),
        "minus_y_in": safe_round(minus_y, 3),
    }


def radial_inset_round(this_g, other_g, pose):
    """For round-round stacks: radial inset accounting for center offset.

    Returns {radial_inset_in, max_inward_in, max_outward_in} or None for non-round.
    """
    if this_g["section"] != "Round" or other_g["section"] != "Round":
        return None
    this_r_in = (this_g["width_ft"] * FT_TO_IN) / 2.0
    other_r_in = (other_g["width_ft"] * FT_TO_IN) / 2.0
    off = math.hypot(pose["offset_local_x_in"] or 0.0, pose["offset_local_y_in"] or 0.0)
    return {
        # Closest gap from this column's surface to the other column's surface on the
        # side where they're closest (≥ 0 means other column fits inside).
        "radial_inset_in": safe_round(this_r_in - other_r_in - off, 3),
        # Worst-case inward shift the dowel must traverse on the "far" side.
        "max_inward_in": safe_round(this_r_in - other_r_in + off, 3),
        "center_offset_in": safe_round(off, 3),
    }


def assess_cranked(this_g, max_inward_inset_in):
    """Apply ACI 318 §10.7.4.1 (1:6 slope cap) to decide whether a Cranked splice
    can geometrically reach the worst-case face inset of the upper column."""
    if max_inward_inset_in is None or max_inward_inset_in <= 0.001:
        # Either no inset (same size or larger above) or upper column overhangs us:
        # Cranked is irrelevant. Return None to signal "not applicable".
        return None

    column_height_in = this_g["height_ft"] * FT_TO_IN
    available_vertical_in = max(column_height_in - CRANK_LAP_BUDGET_IN, 0.0)
    min_required_vertical_in = max_inward_inset_in / ACI_MAX_CRANK_SLOPE   # = 6 * inset

    feasible = available_vertical_in >= min_required_vertical_in

    # 1:n notation — larger n = shallower slope = more comfortable.
    if max_inward_inset_in > 0 and available_vertical_in > 0:
        slope_1_to_n = available_vertical_in / max_inward_inset_in
    else:
        slope_1_to_n = None

    return {
        "max_inward_inset_in": safe_round(max_inward_inset_in, 3),
        "available_vertical_in": safe_round(available_vertical_in, 2),
        "min_required_vertical_in_at_1to6": safe_round(min_required_vertical_in, 2),
        "available_slope_1_to_n": safe_round(slope_1_to_n, 2) if slope_1_to_n else None,
        "feasible_aci_1_to_6": feasible,
        "lap_budget_assumption_in": CRANK_LAP_BUDGET_IN,
    }


# ──────────────────────────────────────────────────────────────────────────────
# Slab detection — mirrors Domain/HostContext.cs
# ──────────────────────────────────────────────────────────────────────────────

def find_slab_below(geom):
    """Return (element, kind_str) or (None, None)."""
    base_z = geom["base_center"].Z
    cx, cy = geom["base_center"].X, geom["base_center"].Y

    best = None
    best_kind = None
    best_top_z = float("-inf")

    for cat in (BuiltInCategory.OST_StructuralFoundation, BuiltInCategory.OST_Floors):
        kind = "StructuralFoundation" if cat == BuiltInCategory.OST_StructuralFoundation else "Floor"
        col = (FilteredElementCollector(doc)
               .OfCategory(cat)
               .WhereElementIsNotElementType())
        for e in col:
            bb = e.get_BoundingBox(None)
            if bb is None:
                continue
            if bb.Max.Z > base_z + Z_TOLERANCE_FT:
                continue
            if cx < bb.Min.X or cx > bb.Max.X:
                continue
            if cy < bb.Min.Y or cy > bb.Max.Y:
                continue
            if bb.Max.Z > best_top_z:
                best_top_z = bb.Max.Z
                best = e
                best_kind = kind
    return best, best_kind


def find_slab_above(geom):
    top_z = geom["top_z_ft"]
    cx, cy = geom["base_center"].X, geom["base_center"].Y

    # The slab "above" is the one the column connects to at its top — its TOP must
    # reach at least the column top (so a bent bar can develop inside it), and it
    # must overlap the column in plan. A column modelled up to the top of the slab
    # (or partway into it) has that slab's bottom BELOW its top, so requiring
    # slab.Min.Z >= columnTop would skip the connecting slab and pick the NEXT slab
    # up. Choose the nearest qualifying slab = smallest top elevation. Mirrors
    # Domain/HostContext.cs FindSlabAbove (PR-44).
    best = None
    best_top_z = float("inf")

    col = (FilteredElementCollector(doc)
           .OfCategory(BuiltInCategory.OST_Floors)
           .WhereElementIsNotElementType())
    for e in col:
        bb = e.get_BoundingBox(None)
        if bb is None:
            continue
        if bb.Max.Z < top_z - Z_TOLERANCE_FT:
            continue
        if cx < bb.Min.X or cx > bb.Max.X:
            continue
        if cy < bb.Min.Y or cy > bb.Max.Y:
            continue
        if bb.Max.Z < best_top_z:
            best_top_z = bb.Max.Z
            best = e
    return best


def slab_info(elem, kind, role):
    """Pack the bits Claude needs for dowels / bent splice / etc."""
    if elem is None:
        return None
    bb = elem.get_BoundingBox(None)
    info = {
        "kind": kind,
        "element_id": eid_value(elem.Id),
        "type_name": get_element_name(doc.GetElement(elem.GetTypeId())),
        "thickness_in": None,
    }
    if bb is not None:
        info["top_elevation_ft"] = safe_round(bb.Max.Z)
        info["bottom_elevation_ft"] = safe_round(bb.Min.Z)
        info["thickness_in"] = safe_round((bb.Max.Z - bb.Min.Z) * FT_TO_IN, 3)
    # Refine with the type-level Thickness param when present (more accurate than AABB).
    try:
        type_elem = doc.GetElement(elem.GetTypeId())
        bip = getattr(BuiltInParameter, "FLOOR_ATTR_DEFAULT_THICKNESS_PARAM", None) \
            or getattr(BuiltInParameter, "FLOOR_ATTR_THICKNESS_PARAM", None)
        if type_elem is not None and bip is not None:
            p = type_elem.get_Parameter(bip)
            if p is not None and p.HasValue:
                info["thickness_in"] = safe_round(p.AsDouble() * FT_TO_IN, 3)
    except Exception:
        pass
    info["role"] = role
    return info


# ──────────────────────────────────────────────────────────────────────────────
# Column-above / column-below
# ──────────────────────────────────────────────────────────────────────────────

def find_neighbouring_columns(target_geom, all_columns):
    """Return (above_dict_or_none, below_dict_or_none).

    "Above" = a column whose base sits within Z-tolerance of this one's top, with XY
    centre within XY_NEIGHBOUR_TOL_FT. "Below" = mirror.
    """
    above = None
    below = None
    above_dist = float("inf")
    below_dist = float("inf")

    t_top = target_geom["top_z_ft"]
    t_base = target_geom["base_center"].Z
    tx, ty = target_geom["base_center"].X, target_geom["base_center"].Y

    for entry in all_columns:
        og = entry["_geom"]
        if og is None:
            continue
        if entry["_inst"].Id.Equals(target_geom["_inst"].Id):
            continue
        ox, oy = og["base_center"].X, og["base_center"].Y
        dxy = math.hypot(ox - tx, oy - ty)
        if dxy > XY_NEIGHBOUR_TOL_FT:
            continue

        if abs(og["base_center"].Z - t_top) <= Z_TOLERANCE_FT * 6 and dxy < above_dist:
            above = entry
            above_dist = dxy
        if abs(og["top_z_ft"] - t_base) <= Z_TOLERANCE_FT * 6 and dxy < below_dist:
            below = entry
            below_dist = dxy

    def pack(entry):
        if entry is None:
            return None
        g = entry["_geom"]
        is_round = g["section"] == "Round"
        pose = relative_pose(target_geom, g)
        face_insets = face_insets_rect_rect(target_geom, g, pose)
        radial = radial_inset_round(target_geom, g, pose)
        return {
            "element_id": eid_value(entry["_inst"].Id),
            "mark": entry.get("mark"),
            "type": entry.get("type"),
            "section": g["section"],
            "width_in": safe_round(g["width_ft"] * FT_TO_IN, 3) if not is_round else None,
            "depth_in": safe_round(g["depth_ft"] * FT_TO_IN, 3) if not is_round else None,
            "diameter_in": safe_round(g["width_ft"] * FT_TO_IN, 3) if is_round else None,
            "relative": pose,
            "face_insets_in": face_insets,
            "radial_inset": radial,
        }

    return pack(above), pack(below)


def max_inward_inset(neighbour):
    """Worst-case inward inset (inches) the splice must traverse to reach the
    neighbour's perimeter. Returns None when not computable (mixed sections,
    or aligned-rotation requirement not met)."""
    if neighbour is None:
        return None
    fi = neighbour.get("face_insets_in")
    if fi is not None:
        positives = [v for v in (fi["plus_x_in"], fi["minus_x_in"],
                                 fi["plus_y_in"], fi["minus_y_in"]) if v is not None]
        if not positives:
            return None
        # Max INWARD inset that a Cranked splice must traverse on its worst face.
        # Negative values mean the upper column overhangs on that face — Cranked
        # is impossible there. Pick the max of the positive insets; flag negatives
        # in hints separately.
        positive_only = [v for v in positives if v > 0]
        return max(positive_only) if positive_only else 0.0
    radial = neighbour.get("radial_inset")
    if radial is not None and radial.get("max_inward_in") is not None:
        return max(radial["max_inward_in"], 0.0)
    return None


def overhang_faces(neighbour):
    """List of face names where the upper column overhangs the lower — Cranked is
    geometrically impossible on these sides."""
    if neighbour is None:
        return []
    fi = neighbour.get("face_insets_in")
    if fi is None:
        return []
    out = []
    for name, key in (("+X", "plus_x_in"), ("-X", "minus_x_in"),
                       ("+Y", "plus_y_in"), ("-Y", "minus_y_in")):
        v = fi.get(key)
        if v is not None and v < -0.05:    # ~1/16" tolerance
            out.append({"face": name, "overhang_in": safe_round(-v, 3)})
    return out


# ──────────────────────────────────────────────────────────────────────────────
# Document-level info — levels, available rebar types
# ──────────────────────────────────────────────────────────────────────────────

def levels_list():
    out = []
    col = (FilteredElementCollector(doc)
           .OfCategory(BuiltInCategory.OST_Levels)
           .WhereElementIsNotElementType())
    for lv in col:
        out.append({
            "id": eid_value(lv.Id),
            "name": get_element_name(lv),
            "elevation_ft": safe_round(lv.Elevation),
        })
    out.sort(key=lambda l: l["elevation_ft"] if l["elevation_ft"] is not None else 0)
    return out


def nearest_level(z_ft, levels):
    """Name of the level with elevation closest to z (within 6 inches)."""
    if not levels:
        return None
    best_name = None
    best_diff = float("inf")
    for lv in levels:
        if lv["elevation_ft"] is None:
            continue
        d = abs(lv["elevation_ft"] - z_ft)
        if d < best_diff:
            best_diff = d
            best_name = lv["name"]
    if best_diff <= 0.5:
        return best_name
    return None


def available_bar_types():
    col = FilteredElementCollector(doc).OfClass(RebarBarType)
    out = []
    for bt in col:
        try:
            diam_ft = bt.BarNominalDiameter
        except AttributeError:
            diam_ft = None
        out.append({
            "name": get_element_name(bt),
            "nominal_diameter_in": safe_round(diam_ft * FT_TO_IN, 4) if diam_ft else None,
        })
    out.sort(key=lambda x: x["name"])
    return out


def available_hook_types():
    col = FilteredElementCollector(doc).OfClass(RebarHookType)
    out = []
    for ht in col:
        out.append({"name": get_element_name(ht)})
    out.sort(key=lambda x: x["name"])
    return out


def column_types_in_use(entries):
    """Dump every column type used by at least one dumped column, once.

    Keyed by string-form type_id (JSON object keys must be strings). Per-column
    records carry `type_id` so the agent can cross-reference into this map
    without duplicating type-level parameters on every column.
    """
    seen = {}
    for e in entries:
        try:
            type_id = e["_inst"].GetTypeId()
        except Exception:
            continue
        v = eid_value(type_id)
        if v is None or v == -1:
            continue
        key = str(v)
        if key in seen:
            continue
        sym = doc.GetElement(type_id)
        if sym is None:
            continue
        try:
            fam = sym.Family
            fam_name = get_element_name(fam) if fam is not None else None
        except Exception:
            fam_name = None
        seen[key] = {
            "id": v,
            "family": fam_name,
            "type": get_element_name(sym),
            "parameters": dump_parameters(sym),
        }
    return seen


def available_column_family_types():
    col = (FilteredElementCollector(doc)
           .OfCategory(BuiltInCategory.OST_StructuralColumns)
           .WhereElementIsElementType())
    out = []
    for sym in col:
        try:
            fam_name = get_element_name(sym.Family)
        except Exception:
            fam_name = None
        out.append({
            "id": eid_value(sym.Id),
            "family": fam_name,
            "type": get_element_name(sym),
        })
    out.sort(key=lambda x: (x["family"] or "", x["type"] or ""))
    return out


# ──────────────────────────────────────────────────────────────────────────────
# Main
# ──────────────────────────────────────────────────────────────────────────────

def collect_columns():
    """First pass — gather every structural column with its geometry. Returns a
    list of dicts with private fields (_inst, _geom) that the second pass uses."""
    warnings = []
    entries = []
    col = (FilteredElementCollector(doc)
           .OfCategory(BuiltInCategory.OST_StructuralColumns)
           .WhereElementIsNotElementType())

    for inst in col:
        if not isinstance(inst, FamilyInstance):
            continue
        try:
            geom = column_geometry(inst)
        except Exception as ex:
            warnings.append("Column id={0}: skipped ({1})".format(eid_value(inst.Id), ex))
            continue
        geom["_inst"] = inst

        mark = get_param_string(inst, BuiltInParameter.ALL_MODEL_MARK)
        if mark is not None:
            mark = mark.strip() or None

        try:
            fam_name = get_element_name(inst.Symbol.Family)
        except Exception:
            fam_name = None
        try:
            type_name = get_element_name(inst.Symbol)
        except Exception:
            type_name = None

        entries.append({
            "_inst": inst,
            "_geom": geom,
            "mark": mark,
            "family": fam_name,
            "type": type_name,
        })

    return entries, warnings


def _splice_hints(this_g, this_is_round, above, slab_above_elem):
    """Decide which SpliceForm makes sense at this column's top, taking non-coaxial
    stacks and the ACI 1:6 crank limit into account.

    Possible form values:
      "Straight"  — same-size column above (or only minor inset within tolerance).
                    Plugin: SpliceForm=Straight.
      "Cranked"   — smaller / inset upper column, and ACI 1:6 slope is achievable.
                    Plugin: SpliceForm=Cranked + SpliceUpperInset = max face inset.
      "Bent"      — no upper column, but a slab above is present.
                    Plugin: SpliceForm=Bent.
      "StraightFromLowerColumn" — upper column exists but Cranked is geometrically
                                   infeasible (slope > 1:6, or upper overhangs lower).
                                   Plugin has no first-class form for this; engineer
                                   must place straight dowel-style starters from
                                   inside the lower column up into the upper column.
                                   See `needs_starters_from_lower_column` flag.
      null        — top of building, no continuation needed.
    """
    out = {
        "form_hint": None,
        "upper_inset_in": None,
        "max_inward_inset_in": None,
        "non_coaxial": False,
        "overhang_faces": [],
        "cranked_check": None,
        "needs_starters_from_lower_column": False,
        "notes": [],
    }

    if above is None:
        out["form_hint"] = "Bent" if slab_above_elem is not None else None
        if slab_above_elem is None:
            out["notes"].append(
                "Top of building (no column above, no slab above). No splice required.")
        return out

    pose = above.get("relative") or {}
    off_x = pose.get("offset_local_x_in") or 0.0
    off_y = pose.get("offset_local_y_in") or 0.0
    if abs(off_x) > 0.05 or abs(off_y) > 0.05:
        out["non_coaxial"] = True
        out["notes"].append(
            "Upper column is not coaxial (offset {0:+.2f}\" / {1:+.2f}\" in this column's local frame).".format(
                off_x, off_y))

    if not pose.get("axis_aligned", True):
        out["notes"].append(
            "Upper column is rotated {0:+.2f}° relative to this one — per-face insets "
            "not computed. Engineer to verify splice geometry manually.".format(
                pose.get("relative_rotation_deg") or 0.0))

    overhangs = overhang_faces(above)
    if overhangs:
        out["overhang_faces"] = overhangs
        out["notes"].append(
            "Upper column overhangs this one on face(s): {0}. Cranked splice impossible on those sides.".format(
                ", ".join("{0} by {1}\"".format(o["face"], o["overhang_in"]) for o in overhangs)))

    max_inward = max_inward_inset(above)
    out["max_inward_inset_in"] = safe_round(max_inward, 3) if max_inward is not None else None

    # Decide form.
    if max_inward is None:
        # Mixed section types (rect/round) or non-axis-aligned rotation — can't auto-decide.
        out["form_hint"] = None
        out["notes"].append(
            "Cannot compare upper and lower geometry automatically (mixed section types or "
            "non-axis-aligned rotation). Engineer to choose splice form manually based on "
            "the upper column's dimensions in `column_above`.")
        return out

    if max_inward <= 0.05:
        # Same size (or upper bigger/same on every face) — straight continuation.
        out["form_hint"] = "Straight"
        if overhangs:
            out["notes"].append(
                "Upper column is the same size or larger on every face; use Straight splice.")
        return out

    # Smaller upper column → try Cranked, fall back to StraightFromLowerColumn.
    cranked = assess_cranked(this_g, max_inward)
    out["cranked_check"] = cranked
    out["upper_inset_in"] = out["max_inward_inset_in"]

    if cranked is None or cranked.get("feasible_aci_1_to_6", False):
        out["form_hint"] = "Cranked"
        if cranked is not None and cranked.get("available_slope_1_to_n") is not None:
            out["notes"].append(
                "Cranked feasible: slope 1:{0:.1f} (ACI limit 1:6).".format(
                    cranked["available_slope_1_to_n"]))
    else:
        out["form_hint"] = "StraightFromLowerColumn"
        out["needs_starters_from_lower_column"] = True
        out["notes"].append(
            "Cranked NOT feasible: would need vertical {0}\" at 1:6 slope but only ~{1}\" "
            "available (column height {2}\" minus {3}\" lap/bend budget). "
            "Place straight dowel-style starter bars from inside the LOWER column up into the "
            "UPPER column instead. (Plugin lacks a first-class form for this — engineer to "
            "handle manually, or use Straight with bars repositioned to upper cage footprint.)".format(
                cranked["min_required_vertical_in_at_1to6"],
                cranked["available_vertical_in"],
                safe_round(this_g["height_ft"] * FT_TO_IN, 1),
                CRANK_LAP_BUDGET_IN))

    return out


def build_record(entry, levels, all_entries):
    inst = entry["_inst"]
    g = entry["_geom"]

    g["_inst"] = inst
    above, below = find_neighbouring_columns(g, all_entries)

    slab_below_elem, slab_below_kind = find_slab_below(g)
    slab_above_elem = find_slab_above(g)

    section = g["section"]
    width_in = safe_round(g["width_ft"] * FT_TO_IN, 3)
    depth_in = safe_round(g["depth_ft"] * FT_TO_IN, 3)
    is_round = section == "Round"

    # Hints — descriptive only, just to make the LLM's job easier. The LLM still
    # owns the final decision (see assignments-csv-guide.md §6).
    hints = {
        "is_ground_level": (below is None and slab_below_elem is not None),
        "is_roof_level": (above is None),
        "foundation": {
            "has_slab_below": slab_below_elem is not None,
            "is_structural_foundation": (slab_below_kind == "StructuralFoundation"),
            # Needs dowels from the foundation slab when the column sits on a slab
            # below AND has no continuing column below it.
            "needs_dowels": (slab_below_elem is not None and below is None),
            # Drives the CSV field DowelOnlyFoundation: leave True for proper
            # structural foundations; set False when the "foundation" is modelled
            # as an OST_Floors element.
            "dowel_only_foundation_recommended": (slab_below_kind == "StructuralFoundation"),
        },
        "splice": _splice_hints(
            this_g=g,
            this_is_round=is_round,
            above=above,
            slab_above_elem=slab_above_elem,
        ),
    }

    base_lvl = nearest_level(g["base_center"].Z, levels)
    top_lvl = nearest_level(g["top_z_ft"], levels)

    base_lvl_param = get_param_string(inst, BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)
    top_lvl_param = get_param_string(inst, BuiltInParameter.FAMILY_TOP_LEVEL_PARAM)

    # Engineering schedule notes live in the Comments parameter — first-class
    # input for the agent. Also surface Revit's native rebar cover overrides
    # (per-face RebarCoverType ElementId), so the agent uses the engineer's
    # actual cover values instead of the plugin's 1.5" default.
    comments = get_param_string(inst, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)

    record = {
        "element_id": eid_value(inst.Id),
        "mark": entry["mark"],
        "family": entry["family"],
        "type": entry["type"],
        "type_id": eid_value(inst.GetTypeId()),
        "section": section,
        "width_in": width_in if not is_round else None,
        "depth_in": depth_in if not is_round else None,
        "diameter_in": width_in if is_round else None,
        "rotation_deg": safe_round(g["rotation_deg"], 2),
        "faces": faces_block(g),
        "comments": comments,
        "rebar_cover": {
            "top": cover_info(inst, "CLEAR_COVER_TOP"),
            "bottom": cover_info(inst, "CLEAR_COVER_BOTTOM"),
            "other": cover_info(inst, "CLEAR_COVER_OTHER"),
        },
        "base": {
            "level_name": base_lvl_param or base_lvl,
            "elevation_ft": safe_round(g["base_center"].Z),
            "x_ft": safe_round(g["base_center"].X),
            "y_ft": safe_round(g["base_center"].Y),
        },
        "top": {
            "level_name": top_lvl_param or top_lvl,
            "elevation_ft": safe_round(g["top_z_ft"]),
        },
        "height_ft": safe_round(g["height_ft"], 4),
        "context": {
            "slab_below": slab_info(slab_below_elem, slab_below_kind, "below") if slab_below_elem else None,
            "slab_above": slab_info(slab_above_elem, "Floor", "above") if slab_above_elem else None,
            "column_above": above,
            "column_below": below,
        },
        "hints": hints,
        "parameters": dump_parameters(inst),
    }
    return record


def default_output_path():
    title = getattr(doc, "Title", "model") or "model"
    base = "{0}_columns.json".format(title.replace(".rvt", ""))
    try:
        doc_path = doc.PathName
        if doc_path:
            return os.path.join(os.path.dirname(doc_path), base)
    except Exception:
        pass
    return os.path.join(os.path.expanduser("~"), base)


def main():
    if doc is None:
        forms.alert("No active document.", exitscript=True)

    entries, warnings = collect_columns()
    if not entries:
        forms.alert("No structural columns found in this document.", exitscript=True)

    levels = levels_list()
    bar_types = available_bar_types()
    hook_types = available_hook_types()
    fam_types = available_column_family_types()

    duplicates = {}
    for e in entries:
        m = e["mark"]
        if not m:
            continue
        duplicates.setdefault(m, []).append(eid_value(e["_inst"].Id))
    for mark, ids in duplicates.items():
        if len(ids) > 1:
            warnings.append("Duplicate Mark '{0}' on columns: {1}".format(mark, ids))

    records = []
    for e in entries:
        try:
            records.append(build_record(e, levels, entries))
        except Exception as ex:
            warnings.append("Column id={0}: failed to serialise ({1})".format(
                eid_value(e["_inst"].Id), ex))

    no_mark = [r["element_id"] for r in records if not r["mark"]]
    if no_mark:
        warnings.append(
            "{0} column(s) have no Mark parameter — they will need a Mark in Revit "
            "before assignments.csv can address them. ids: {1}".format(
                len(no_mark), no_mark[:20] + (["…"] if len(no_mark) > 20 else [])))

    dump = {
        "schema_version": SCHEMA_VERSION,
        "generated_at": datetime.datetime.now().isoformat(),
        "document": {
            "title": getattr(doc, "Title", None),
            "path": getattr(doc, "PathName", None) or None,
        },
        "units_note":
            "Section dimensions are in inches. Elevations and plan offsets are in "
            "feet (Revit internal). Use these directly when filling assignments.csv "
            "(see assignments-csv-guide.md).",
        "levels": levels,
        "available_rebar_bar_types": bar_types,
        "available_rebar_hook_types": hook_types,
        "available_column_family_types": fam_types,
        "column_types_in_use": column_types_in_use(entries),
        "warnings": warnings,
        "columns": records,
    }

    default_path = default_output_path()
    out_path = forms.save_file(
        file_ext="json",
        default_name=os.path.basename(default_path),
        title="Save column dump as…",
    )
    if not out_path:
        return

    with io.open(out_path, "w", encoding="utf-8") as f:
        f.write(json.dumps(dump, indent=2, ensure_ascii=False, sort_keys=False,
                           default=_json_default))

    output.print_md("### Dumped {0} columns".format(len(records)))
    output.print_md("**File:** `{0}`".format(out_path))
    if warnings:
        output.print_md("**Warnings ({0}):**".format(len(warnings)))
        for w in warnings[:25]:
            output.print_md("- " + w)
        if len(warnings) > 25:
            output.print_md("- … {0} more".format(len(warnings) - 25))
    output.print_md(
        "\nHand `{0}` to Claude and ask it to produce `assignments.csv` per "
        "`assignments-csv-guide.md`.".format(os.path.basename(out_path)))


if __name__ == "__main__":
    try:
        main()
    except Exception:
        forms.alert(
            "Column dump failed:\n\n" + traceback.format_exc(),
            exitscript=True)
