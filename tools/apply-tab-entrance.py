#!/usr/bin/env python3
"""Animate book/content only on navigation to or from Start."""
from pathlib import Path
import os
import sys

ROOT = Path(__file__).resolve().parents[1]
NATIVE = ROOT / 'launcher/windows/BloodborneLauncher.Native'
MARKER = '// Tab entrance: move content upward only during fade-in.'


def replace_once(text, old, new):
    if text.count(old) != 1:
        raise RuntimeError('Unexpected launcher source layout; no files were changed.')
    return text.replace(old, new, 1)


def patch_window(text):
    if MARKER in text:
        old = 'if (_page != 0 && _pending < 0 && _switchAt >= 0)'
        new = 'if (_bookTransition && _page != 0 && _pending < 0 && _switchAt >= 0)'
        if old in text:
            return patch_exit(replace_once(text, old, new))
        if new in text:
            return patch_exit(text)
        raise RuntimeError('Unexpected tab entrance layout; no files were changed.')
    text = replace_once(text, '        RevealSettingResult();\n        SyncNativeText();',
'''        // Tab entrance: move content upward only during fade-in.
        // A cubic ease-out settles at the existing position without rebuilding the scene.
        float entranceY = 0;
        if (_bookTransition && _page != 0 && _pending < 0 && _switchAt >= 0)
        {
            float remaining = 1 - fade;
            entranceY = 12 * _scale * remaining * remaining * remaining;
        }

        RevealSettingResult();
        SyncNativeText();''')
    return patch_exit(replace_once(text,
        'navigationOpacity:(_messagePrompt!=null || _resetPrompt!=null || _recommendation!=null)?_dialogFade.Value(_clock.Elapsed.TotalSeconds):1);',
        'navigationOpacity:(_messagePrompt!=null || _resetPrompt!=null || _recommendation!=null)?_dialogFade.Value(_clock.Elapsed.TotalSeconds):1, contentOffsetY:entranceY);'))


def patch_exit(text):
    marker = '// Returning to Start: slide the outgoing book/content down as it fades.'
    if marker in text:
        return text
    return replace_once(text,
        '        }\n\n        RevealSettingResult();\n        SyncNativeText();',
'''        }
        // Returning to Start: slide the outgoing book/content down as it fades.
        else if (_pending == 0 && _page != 0 && _switchAt >= 0)
        {
            float progress = (float)Math.Clamp((_clock.Elapsed.TotalSeconds - _switchAt) / .18, 0, 1);
            entranceY = 12 * _scale * progress * progress * progress;
        }

        RevealSettingResult();
        SyncNativeText();''')


def patch_canvas(text):
    if 'float contentOffsetY = 0)' in text:
        if '!op.Fixed && !op.Surface && !op.Dialog ? contentOffsetY : 0' in text:
            text = replace_once(text,
                '!op.Fixed && !op.Surface && !op.Dialog ? contentOffsetY : 0',
                '!op.Fixed && !op.Dialog ? contentOffsetY : 0')
            return text.replace('// Animate page content only: wallpaper, sidebar, book and dialogs stay fixed.',
                                '// Animate page content and book together; wallpaper, sidebar and dialogs stay fixed.')
        if '!op.Fixed && !op.Dialog ? contentOffsetY : 0' in text:
            return text
        raise RuntimeError('Unexpected tab entrance layout; no files were changed.')
    text = replace_once(text, 'float navigationOpacity = 1)',
                        'float navigationOpacity = 1, float contentOffsetY = 0)')
    text = replace_once(text,
        '            var b = ScrollGeometry.Screen(op.Bounds, op.Scrollable, scroll);\n            if (!op.VisibleFor',
'''            var b = ScrollGeometry.Screen(op.Bounds, op.Scrollable, scroll);
            // Animate page content and book together; wallpaper, sidebar and dialogs stay fixed.
            float entranceY = !op.Fixed && !op.Dialog ? contentOffsetY : 0;
            b = new Rect(b.X, b.Y + entranceY, b.Width, b.Height);
            if (!op.VisibleFor''')
    return replace_once(text,
        '            if (localClip is Rect local && !Intersects(b, local))',
'''            if (localClip is Rect movingClip)
                localClip = new Rect(movingClip.X, movingClip.Y + entranceY, movingClip.Width, movingClip.Height);
            if (localClip is Rect local && !Intersects(b, local))''')


def main():
    paths = [NATIVE / 'NativeWindow.cs', NATIVE / 'Rendering/Canvas.cs']
    originals = [path.read_bytes() for path in paths]
    # Validate both edits before writing either source. Preserve CRLF checkouts.
    modified = []
    for original, patch in zip(originals, [patch_window, patch_canvas]):
        text = original.decode('utf-8-sig').replace('\r\n', '\n')
        updated = patch(text)
        newline = '\r\n' if b'\r\n' in original else '\n'
        data = updated.replace('\n', newline).encode('utf-8')
        if original.startswith(b'\xef\xbb\xbf'):
            data = b'\xef\xbb\xbf' + data
        modified.append(data)
    try:
        for path, data in zip(paths, modified):
            if path.read_bytes() != data:
                temporary = path.with_suffix('.cs.entrance-tmp')
                temporary.write_bytes(data)
                os.replace(temporary, path)
    except OSError:
        for path, original in zip(paths, originals):
            path.write_bytes(original)
        raise
    print('Start transitions: upward fade-in, downward fade-out; other transitions keep fade only.')


if __name__ == '__main__':
    try:
        main()
    except (OSError, UnicodeError, RuntimeError) as error:
        sys.exit(str(error))
