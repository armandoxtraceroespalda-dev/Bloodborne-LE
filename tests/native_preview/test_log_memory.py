"""Log updates and scrolling must not recreate the retained launcher page."""
import unittest
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'launcher/windows/BloodborneLauncher.Native'


def body(source, name):
    start = re.search(r'\b(?:void|nint)\s+' + name + r'\(', source).start()
    start = source.index('{', start)
    depth = 1
    for end in range(start + 1, len(source)):
        depth += (source[end] == '{') - (source[end] == '}')
        if not depth:
            return source[start:end + 1]
    raise AssertionError(name)


class LogMemoryContracts(unittest.TestCase):
    def test_new_output_updates_only_native_text(self):
        method = body((ROOT / 'NativeMore.cs').read_text(), 'PollReferenceOutput')
        self.assertNotIn('Rebuild(', method)
        self.assertIn('spec with { Value = _output.Text() }', method)
        self.assertIn('SyncNativeText()', method)

    def test_native_scroll_repaints_only_a_changed_thumb(self):
        source = (ROOT / 'NativeText.cs').read_text()
        method = body(source, 'TextWindowProcCore')
        self.assertNotIn('Invalidate(', method)
        method = body(source, 'ReadOutputScroll')
        self.assertIn('oldOffset != _outputOffset || oldMax != _outputMax', method)
        self.assertIn('InvalidateOutputThumb()', method)
        self.assertNotIn('Invalidate(', body(source, 'ScrollOutputLines'))

    def test_edit_uses_splices_without_retaining_undo_snapshots(self):
        method = body((ROOT / 'NativeText.cs').read_text(), 'UpdateEditorText')
        self.assertIn('TextSplice.Between(editor.Text, text)', method)
        self.assertIn('0xC2, 0,', method)
        self.assertIn('patch.MapSelection(start)', method)
        self.assertIn('0xB, 1, 0', method)

    def test_activity_cleanup_is_not_requested_by_pointer_motion(self):
        source = (ROOT / 'NativeText.cs').read_text()
        method = body(source, 'TextWindowProcCore')
        self.assertNotIn('_logMemory.Changed(', method)
        window = (ROOT / 'NativeWindow.cs').read_text() + (ROOT / 'NativeLogRefresh.cs').read_text()
        self.assertIn('_logMemory.TakeRequest(now,', window)
        self.assertIn('_logMemory.Cancel()', window)


if __name__ == '__main__':
    unittest.main()
