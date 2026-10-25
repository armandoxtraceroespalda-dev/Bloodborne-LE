"""Regression guards for modal input isolation and relocated environment settings."""
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[2]/'launcher/windows/BloodborneLauncher.Native'
class DialogReorganization(unittest.TestCase):
    def source(self, name): return (ROOT/name).read_text()
    def test_regular_message_boxes_share_the_native_dialog(self):
        pages=self.source('NativePages.cs')
        game=self.source('NativeGame.cs')
        self.assertNotIn('W.MessageBox(',pages)
        self.assertNotIn('W.MessageBox(',game)
        prompt=self.source('NativeMessages.cs')
        self.assertIn('DialogBackdrop(box)',prompt)
        self.assertIn('W.GetMessage(out var message',prompt)
        self.assertIn('W.DispatchMessage(ref message)',prompt)
        self.assertIn('finally',prompt)
        self.assertIn('_messagePrompt = null;',prompt)
        self.assertIn('RefreshDialogSurface();',prompt)
        self.assertIn('("Yes", 6), ("No", 7), ("Cancel", 2)',prompt)
    def test_message_modal_blocks_page_input_and_native_edits(self):
        window=self.source('NativeWindow.cs')
        self.assertIn('if (_messagePrompt != null) { MessageMouse(x, y, down); return; }',window)
        self.assertIn('if (_messagePrompt != null) { MessageKey(key); return; }',window)
        self.assertIn('if (_dialogClosing || _messagePrompt != null) return true;',self.source('NativePages.cs'))
        self.assertIn('_messagePrompt == null',self.source('NativeText.cs'))
        self.assertIn('ScrollMessage(',window)
        self.assertIn('if (_messagePrompt != null) { FinishMessage(2)',self.source('NativeText.cs'))
    def test_busy_notice_is_removed_before_the_result_callback(self):
        worker=self.source('NativePages.cs').split('void CompleteWorker()',1)[1].split('void StoreAccount',1)[0]
        self.assertLess(worker.index('_notice = ""'), worker.index('Run(entry.Action)'))
        self.assertLess(worker.index('_busy = false'), worker.index('Run(entry.Action)'))
    def test_common_backdrop_has_dimmer_and_centered_wrapped_text(self):
        surface=self.source('NativeDialogSurface.cs')
        self.assertIn('Fill(new Rect(0, 0, _width, _height)',surface)
        self.assertIn('new Color4(0, 0, 0, .65f)',surface)
        self.assertIn('new Color4(0, 0, 0, .75f)',surface)
        self.assertIn('Center: true, Wrap: true',surface)
        reset=self.source('NativeControlReset.cs')
        self.assertIn('DialogText(text.Title',reset)
        self.assertIn('DialogText(text.Body',reset)
        self.assertIn('18, Gold, false)',reset)
    def test_achievements_use_neutral_dimmer_locked_text(self):
        source=self.source('ReferencePages.cs')
        self.assertIn('new Color4(.89f, .89f, .87f, earned ? 1 : .46f)',source)
        self.assertIn('new Color4(.82f, .82f, .80f, earned ? .9f : .38f)',source)
        self.assertIn('image + (earned ? "" : "|gray")',source)
    def test_engine_keys_move_without_losing_environment_semantics(self):
        schema=self.source('Backend/UpstreamBoundary.cs')
        self.assertIn('EngineGraphicsOption("BB_ANISO", "Screen")',schema)
        self.assertIn('EngineGraphicsOption("BB_UPSCALER_NO_BLUR", "Upscaler")',schema)
        self.assertIn('return new(section, item.Title, item.Key',schema)
        self.assertIn('First(i => i.Env == key)',schema)
        self.assertIn('i.Group != EngineSwitches.Graphics',self.source('NativePages.cs'))
        controls=self.source('ReferenceControls.cs')
        self.assertIn('group.Key is not ("Engine preset" or "Game fixes") &&',controls)
        self.assertIn('rows[i + 1].Key == "object_motion"',controls)
    def test_about_keeps_only_upstream_link(self):
        source=self.source('NativeMore.cs')
        self.assertIn('https://github.com/Awakenchan/LanternEngine',source)
        self.assertNotIn('ForkRepository',source)
    def test_timeout_wraps_and_reserves_height(self):
        source=self.source('NativeWindow.cs')
        self.assertIn('if (o.Key == "online_sign_timeout") return 106 * _scale;',source)
        self.assertIn('control.Height), colour, text, 17 * _scale, Center: true, Wrap: true',source)
if __name__=='__main__': unittest.main()
