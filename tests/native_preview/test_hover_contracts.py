#!/usr/bin/env python3
"""Verify the hover path cannot rebuild the retained settings scene."""
import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]/'launcher/windows/BloodborneLauncher.Native'
def method(text,name):
 start=text.index('void '+name+'(');start=text.index('{',start);depth=1
 for end in range(start+1,len(text)):
  if text[end]=='{':depth+=1
  elif text[end]=='}':
   depth-=1
   if not depth:return text[start:end+1]
 raise AssertionError('Unbalanced method '+name)
class HoverContracts(unittest.TestCase):
 def test_hover_updates_only_interaction_state(self):
  source=(ROOT/'NativeWindow.cs').read_text()
  hover=method(source,'UpdateHover')
  self.assertNotIn('Rebuild(',hover);self.assertNotIn('Build(',hover)
  self.assertIn('_pointer.Move(',hover)
 def test_tooltip_is_separate_from_settings_scene(self):
  source=(ROOT/'NativeWindow.cs').read_text()
  tip=method(source,'BuildTooltip')
  self.assertIn('_tooltipOps',tip);self.assertNotIn('Rebuild(',tip)
  self.assertIn('_canvas.PrepareScene(_renderOps, _scroll)',source)
 def test_paint_uses_cached_directwrite_layouts(self):
  source=(ROOT/'Rendering/Canvas.cs').read_text()
  paint=method(source,'Paint')
  self.assertIn('DrawTextLayout(',paint);self.assertNotIn('CreateTextLayout(',paint)
  self.assertNotIn('_target.DrawText(',paint)
  self.assertIn('for (int index',paint)
 def test_no_working_set_or_gc_trimming_in_hover_or_rendering(self):
  source='\n'.join(p.read_text() for p in ROOT.rglob('*.cs') if p.name != 'NavigationMemoryReclaimer.cs')
  self.assertNotIn('GC.Collect(',source);self.assertNotIn('EmptyWorkingSet(',source)
if __name__=='__main__':unittest.main()
