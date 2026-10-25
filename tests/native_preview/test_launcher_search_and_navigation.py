"""Guard lazy localized search, controller ownership and retained-row rendering."""
from pathlib import Path
import unittest,json
ROOT=Path(__file__).resolve().parents[2]/'launcher/windows/BloodborneLauncher.Native'
class LauncherSearchAndNavigation(unittest.TestCase):
 def source(self,path):return (ROOT/path).read_text()
 def test_rendering_reads_use_a_cached_model_and_mutations_invalidate_it(self):
  source=self.source('Backend/NativeServices.cs')
  self.assertIn('if (_readSettings != null) return _readSettings;',source)
  self.assertIn('copy.SetNode(pair.Key, pair.Value)',source)
  self.assertIn('InvalidateSettingsSnapshot();',source)
  self.assertIn('EnginePreset.Current(ReadSettings())',source)
  self.assertIn('InvalidateSettingsSnapshot();',self.source('Backend/UpstreamBoundary.cs'))
 def test_search_is_lazy_localized_and_does_not_construct_views(self):
  model=self.source('Backend/SettingsSearchIndex.cs')
  self.assertIn('UiText.T(title)',model)
  self.assertIn('NormalizationForm.FormD',model)
  self.assertIn('if (page==9 && !cheats) continue;',model)
  self.assertIn('PageCatalog.Build(3,profile,device)',model)
  self.assertNotIn('new NativeWindow',model)
  view=self.source('NativeSettingsSearch.cs')
  self.assertIn('_settingsIndexLocale==UiText.Locale',view)
  self.assertNotIn('_profile.Set(',view)
  self.assertIn('DrawGlobalSearchResults()',self.source('NativeWindow.cs'))
  self.assertIn('overview.Y + 15 * _scale',self.source('NativeWindow.cs'))
 def test_search_edit_supports_native_unicode_placeholder_and_focus(self):
  text=self.source('NativeText.cs')
  self.assertIn('UiText.T("Search settings...")',text)
  self.assertIn('GlobalSearchKeyDown((int)w)',text)
  self.assertIn('_focusGlobalAfterSync=false; W.SetFocus',text)
  for locale in ['es','fr','de','it','pt-BR','ru','ja']:
   messages=json.loads((ROOT.parent/'BloodborneLauncher/Localization'/(locale+'.json')).read_text())
   self.assertTrue(messages['Search settings...'])
   self.assertTrue(messages['No matching settings'])
   self.assertIn('{0}',messages['Select {0} to view this setting.'])
 def test_pad_navigation_stops_for_game_inactive_and_native_owned_dialogs(self):
  source=self.source('NativeGamepadNavigation.cs')
  self.assertIn('_game?.Running==true || !_active || _closed',source)
  self.assertIn('!W.IsWindowEnabled(_window)',source)
  self.assertIn('_padCapture!=null || _dialogClosing || _switchAt>=0',source)
  self.assertIn('StopLauncherPad();',self.source('NativeGame.cs'))
  self.assertIn('StopLauncherPad();',self.source('NativeWindow.cs'))
  self.assertIn('GamepadPresence.Any()',source)
 def test_pad_navigation_has_no_held_set_or_poll_timer(self):
  source=(ROOT.parent/'BloodborneLauncher/Core/SdlPad.cs').read_text()
  method=source.split('public static MenuInput ReadMenuInput()',1)[1].split('public static HashSet<string> Held()',1)[0]
  self.assertNotIn('new HashSet',method)
  self.assertNotIn('Held()',method)
  self.assertIn('_nextDeviceScan = now + 750',source)
  self.assertNotIn('SetTimer',self.source('NativeGamepadNavigation.cs'))
 def test_slider_drag_updates_only_its_retained_row_and_final_value(self):
  row=self.source('NativeSliderRows.cs')
  self.assertIn('_ops[row.First+i]=_sliderOps[i]',row)
  self.assertIn('_hits[row.Hit]=_sliderHits[0]',row)
  self.assertNotIn('Build();',row)
  window=self.source('NativeWindow.cs')
  self.assertIn('if(RefreshSliderRow(o))return;',window)
  self.assertIn('FlushSliderDrag();\n                        _drag = null;',window)
  self.assertIn('_sliderPending = fraction',window)
 def test_cleanup_has_a_coalescing_cooldown_and_waits_for_input(self):
  source=self.source('Platform/NavigationMemoryReclaimer.cs')
  self.assertIn('minSpacingMilliseconds:750',source)
  self.assertIn('now-_lastStarted<_minSpacing',source)
  readiness=self.source('NativeLogRefresh.cs')
  self.assertIn('!_globalSearchActive && !_menuGamepad.Busy && !_padScrolling',readiness)
  self.assertIn('navigationCollectionMs = _navigationMemory.CollectionMilliseconds',self.source('NativeWindow.cs'))
if __name__=='__main__':unittest.main()
