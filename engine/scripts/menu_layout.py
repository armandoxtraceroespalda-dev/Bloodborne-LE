# SPDX-License-Identifier: GPL-2.0-or-later
"""Expand native settings frames without scaling fonts or sharing mutable art.

Uses the original bbport GFX serializer. Edited movies remain local game cache.
"""
import math
import struct
import hud_layout as swf
import quit_menu as quit


def stretch_y(matrix, ratio, anchor):
    """Scale a placement vertically around a parent-space anchor, including flips."""
    scale, rotate, (x, y) = matrix
    sx, sy = scale or (65536, 65536)
    # SWF RotateSkew0 contributes to the Y coordinate; RotateSkew1 to X.
    if rotate is not None:
        rotate = (round(rotate[0]*ratio), rotate[1])
    return ((sx, round(sy*ratio)), rotate, (x, round(anchor+(y-anchor)*ratio)))


def expand_panel(data, name, growth, *, divider_growth=None):
    """Clone the section's frame and grow only its settings area downward."""
    _, sprites, _ = swf.definitions(data)
    root = quit.placements_in(data, swf.stage_start(data), len(data))
    if name not in root:
        raise ValueError(f'Missing native panel: {name}')
    timeline = sprites[root[name][3]]
    placements = quit.placements_in(data, *timeline)
    frame_place = placements.get(3)
    status = placements.get('StatusBar')
    if not frame_place or not status or frame_place[3] not in sprites:
        raise ValueError(f'Unexpected settings frame: {name}')
    frame_timeline = sprites[frame_place[3]]
    frame = quit.placements_in(data, *frame_timeline)
    # Verified Controls frame: background, lower corner/lines, vertical fill.
    required = (1, 11, 14, 25, 33, 41, 49)
    if not all(depth in frame for depth in required):
        raise ValueError(f'Unexpected settings frame decorations: {name}')
    edits = {}
    for depth in (25, 33, 49):
        delta = divider_growth if depth == 25 and divider_growth is not None else growth
        quit.shift_depth(data, frame_timeline, depth, delta, edits)
    # The interior is a pair of identical texture clips: one top-left and
    # one mirrored bottom-right. Moving the mirrored clip alone separates
    # the two textures. Apply one vertical transform to BOTH placements.
    texture = frame[11]
    if texture[3] != frame[14][3]:
        raise ValueError(f'Unexpected mirrored settings textures: {name}')
    top, bottom = quit.swf_shape_y(data, texture[3])
    texture_scale = texture[4][0] or (65536, 65536)
    texture_height = (bottom-top)*texture_scale[1]/65536
    texture_top = texture[4][2][1] + top*texture_scale[1]/65536
    if texture_height <= 0 or texture_height+growth <= 0:
        raise ValueError('Invalid settings texture height')
    ratio = (texture_height+growth)/texture_height
    for code, _, body, _ in swf.tags(data, *frame_timeline):
        if code not in (26, 70) or quit.depth_of(data, body, code) not in (11, 14):
            continue
        _, span, _ = swf.place_fields(data, body, code)
        if span:
            edits[body] = stretch_y(swf.read_matrix(data, span[0])[0], ratio, texture_top)
    # Grow the opaque backing and central fill while keeping their local
    # top edges fixed. Keep each animation frame's own scale/translation.
    for depth in (1, 41):
        _, _, _, character, matrix = frame[depth]
        top, bottom = quit.swf_shape_y(data, character)
        scale = matrix[0] or (65536, 65536)
        height = (bottom-top)*scale[1]/65536
        if height <= 0 or height+growth <= 0:
            raise ValueError('Invalid settings frame bounds')
        ratio = (height+growth)/height
        for code, _, body, _ in swf.tags(data, *frame_timeline):
            if code not in (26, 70) or quit.depth_of(data, body, code) != depth:
                continue
            _, span, _ = swf.place_fields(data, body, code)
            if span:
                current = swf.read_matrix(data, span[0])[0]
                local_scale = current[0] or (65536, 65536)
                anchor = current[2][1] + top*local_scale[1]/65536
                edits[body] = stretch_y(current, ratio, anchor)
    # Clone the frame. Other sections still reference the untouched original.
    shapes, sprites, bitmaps = swf.definitions(data)
    new_id = max(set(shapes)|set(sprites)|set(bitmaps))+1
    rewritten = swf.rewrite_timeline(data, *frame_timeline, edits, {})
    clone = swf.tag(39, struct.pack('<HH', new_id, struct.unpack_from('<H', data, frame_timeline[0]-2)[0]) + rewritten)
    # Update every placement of this section's frame and its help/status anchor.
    status_depth = quit.depth_of(data, status[1], status[0])
    quit.shift_depth(data, timeline, status_depth, growth, edits := {})
    replaces = {}
    frame_depth = quit.depth_of(data, frame_place[1], frame_place[0])
    for code, _, body, length in swf.tags(data, *timeline):
        if code in (26, 70) and quit.depth_of(data, body, code) == frame_depth:
            cid, _, _ = swf.place_fields(data, body, code)
            if cid is not None:
                raw = bytearray(data[body:body+length])
                struct.pack_into('<H', raw, 4 if code == 70 else 3, new_id)
                replaces[body] = swf.tag(code, raw)
    # The serializer's insert/after hooks preserve nested sizes and all timings.
    section = bytearray()
    for code, head, at, length in swf.tags(data, *timeline):
        if at in replaces:
            section += replaces[at]
        elif at in edits:
            _, span, _ = swf.place_fields(data, at, code)
            section += swf.tag(code, data[at:span[0]] + swf.write_matrix(edits[at]) + data[span[1]:at+length])
        else:
            section += data[head:at+length]
    section = bytes(section)
    section_id = root[name][3]
    return replace_sprite(data, section_id, section, clone)


def replace_sprite(data, character, body, extra):
    """Replace one top-level sprite body; insert its private frame before it."""
    start = swf.stage_start(data)
    output = bytearray(data[:start])
    found = False
    for code, head, at, length in swf.tags(data, start, len(data)):
        if code == 39 and struct.unpack_from('<H', data, at)[0] == character:
            output += extra + swf.tag(39, data[at:at+4] + body)
            found = True
        else:
            output += data[head:at+length]
    if not found:
        raise ValueError('Settings sprite was not found')
    struct.pack_into('<I', output, 4, len(output))
    return bytes(output)


def expand_option_panels(data):
    for name, growth in (('ControllSetting', 2400), ('PCGraphics', 2100),
                         ('PCKeys', 2400), ('PCEffects', 1600), ('PCColour', -2200), ('PCCamera', -3300)):
        # Graphics gained a second footer row, not a taller options list.
        data = expand_panel(data, name, growth, divider_growth=1600 if name == "PCGraphics" else None)
    return resolution_popup(data)


def black_rectangle(character, bounds):
    """Define a solid RGBA shape; no external textures or game art required."""
    left, right, top, bottom = bounds
    rect = swf.Writer()
    bits = swf.signed_bits(*bounds)
    rect.unsigned(bits, 5)
    for value in bounds:
        rect.unsigned(value & ((1 << bits)-1), bits)
    path = swf.Writer()
    path.unsigned(1, 4)  # one fill bit
    path.unsigned(0, 4)  # no line styles
    path.unsigned(5, 6)  # move and select FillStyle1
    bits = swf.signed_bits(left, top)
    path.unsigned(bits, 5)
    path.unsigned(left & ((1 << bits)-1), bits)
    path.unsigned(top & ((1 << bits)-1), bits)
    path.unsigned(1, 1)
    for dx, dy in ((right-left, 0), (0, bottom-top), (left-right, 0), (0, top-bottom)):
        bits = max(2, swf.signed_bits(dx, dy))
        path.unsigned(3, 2)  # straight edge
        path.unsigned(bits-2, 4)
        path.unsigned(1, 1)  # general edge
        path.unsigned(dx & ((1 << bits)-1), bits)
        path.unsigned(dy & ((1 << bits)-1), bits)
    path.unsigned(0, 6)  # EndShape
    return swf.tag(32, struct.pack('<H', character) + rect.bytes() +
                   b'\x01\x00\x00\x00\x00\xff\x00' + path.bytes())


def resolution_popup(data):
    """Give only Graphics' resolution popup an opaque backing and topmost row.

    A black background alone cannot cover siblings at higher display depths.
    Promote the resolution row across all animation updates/removals as well.
    Clone its widget chain so vanilla language lists keep their original art.
    """
    shapes, sprites, bitmaps = swf.definitions(data)
    root = quit.placements_in(data, swf.stage_start(data), len(data))
    section = root['PCGraphics'][3]
    panel = quit.placements_in(data, *sprites[section])
    row = panel['Item_8_0']
    path = [row[3]]
    for name in ('Widgets', 'DropdownList', 'ItemList'):
        path.append(quit.placements_in(data, *sprites[path[-1]])[name][3])
    backing = quit.placements_in(data, *sprites[path[-1]])[1]
    def bounds(character):
        if character in shapes:
            for code, _, at, _ in swf.tags(data, swf.stage_start(data), len(data)):
                if code in swf.SHAPE_TAGS and struct.unpack_from('<H', data, at)[0] == character:
                    reader = swf.Reader(data, at+2)
                    bits = reader.unsigned(5)
                    return tuple(reader.signed(bits) for _ in range(4))
        points = []
        if character not in sprites:
            raise ValueError('Unexpected popup background character')
        for depth, placement in quit.placements_in(data, *sprites[character]).items():
            if not isinstance(depth, int):
                continue
            left, right, top, bottom = bounds(placement[3])
            scale, rotate, (x, y) = placement[4]
            sx, sy = scale or (65536, 65536)
            ry, rx = rotate or (0, 0)
            points.extend((x+(sx*xx+rx*yy)/65536, y+(ry*xx+sy*yy)/65536)
                          for xx in (left, right) for yy in (top, bottom))
        if not points:
            raise ValueError('Empty popup background')
        return (min(p[0] for p in points), max(p[0] for p in points),
                min(p[1] for p in points), max(p[1] for p in points))
    left, right, top, bottom = bounds(backing[3])
    # Match the original popup's transformed artwork, including its border.
    next_id = max(set(shapes)|set(sprites)|set(bitmaps))+1
    shape = next_id
    extra = black_rectangle(shape, (math.floor(left), math.ceil(right), math.floor(top), math.ceil(bottom)))
    mapping = {old: next_id+1+i for i, old in enumerate(reversed(path))}
    mapping[backing[3]] = shape
    for old in reversed(path):
        body = bytearray()
        for code, head, at, length in swf.tags(data, *sprites[old]):
            raw = bytearray(data[at:at+length])
            if code in (26, 70):
                cid, _, _ = swf.place_fields(data, at, code)
                if cid in mapping:
                    struct.pack_into('<H', raw, 4 if code == 70 else 3, mapping[cid])
            body += swf.tag(code, raw)
        frames = struct.unpack_from('<H', data, sprites[old][0]-2)[0]
        extra += swf.tag(39, struct.pack('<HH', mapping[old], frames)+body)
    depth = quit.depth_of(data, row[1], row[0])
    highest = max(quit.depth_of(data, at, code)
                  for code, _, at, _ in swf.tags(data, *sprites[section]) if code in (26, 70))
    body = bytearray()
    for code, _, at, length in swf.tags(data, *sprites[section]):
        raw = bytearray(data[at:at+length])
        if code in (26, 70) and quit.depth_of(data, at, code) == depth:
            struct.pack_into('<H', raw, 1+(code == 70), highest+1)
            cid, _, _ = swf.place_fields(data, at, code)
            if cid is not None:
                if cid != row[3]:
                    raise ValueError('Resolution row changes character during animation')
                struct.pack_into('<H', raw, 4 if code == 70 else 3, mapping[cid])
        elif code == 28 and struct.unpack_from('<H', raw)[0] == depth:
            struct.pack_into('<H', raw, 0, highest+1)
        body += swf.tag(code, raw)
    return replace_sprite(data, section, body, extra)
