"""Keep opt-in diagnostics and allocation-free native repaint paths explicit."""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'launcher/windows'
NATIVE = ROOT / 'BloodborneLauncher.Native'


class OptimizationContracts(unittest.TestCase):
    def test_periodic_diagnostics_are_opt_in(self):
        source = (NATIVE / 'NativeWindow.cs').read_text()
        self.assertIn('_diagnostics = _benchmark || n >= 0', source)
        self.assertIn('if (_diagnostics && now - _lastSample >= 2)', source)
        self.assertIn('if (!_diagnostics) return;', source)

    def test_native_repaint_avoids_capturing_linq(self):
        source = (NATIVE / 'NativeText.cs').read_text()
        self.assertNotIn('.Values.Any(', source)
        self.assertNotIn('.Keys.Where(', source)
        self.assertIn('_staleTextEditors.Clear()', source)

    def test_optional_assets_are_not_probed_on_every_frame(self):
        source = (NATIVE / 'Rendering/Canvas.cs').read_text()
        self.assertIn('if (_missingImages.Contains(name)) return null;', source)
        self.assertIn('_missingImages.Add(name)', source)
        self.assertIn('_missingImages.Clear()', source)

    def test_audio_is_pinned_outside_the_compacting_heap(self):
        source = (NATIVE / 'Platform/MenuAudio.cs').read_text()
        self.assertIn('pinned: true', source)
        self.assertIn('clip.Data.Free()', source)

    def test_saved_playtime_does_not_construct_regex_parsers(self):
        source = (ROOT / 'BloodborneLauncher/Core/SessionStats.cs').read_text()
        self.assertIn('private static class Patterns', source)
        self.assertNotIn('List<double>', source)
        observe = source[source.index('public void Observe'):source.index('public void Finish')]
        self.assertEqual(source.count('Patterns.'), observe.count('Patterns.'))

    def test_log_rollover_checks_the_retained_text(self):
        source = (NATIVE / 'Backend/TextSplice.cs').read_text()
        self.assertIn('SequenceEqual(next.AsSpan(0, kept))', source)
        native = (NATIVE / 'NativeText.cs').read_text()
        self.assertIn('TextSplice.TryRollingAppend(', native)
        self.assertIn('editor.RemovedOutputCharacters = removedCharacters', native)


if __name__ == '__main__':
    unittest.main()
