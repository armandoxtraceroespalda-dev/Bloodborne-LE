using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Interaction;
using BloodborneLauncher.Native.Platform;
using BloodborneLauncher.Native.Backend;
using W=BloodborneLauncher.Native.Platform.Win32;
namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    readonly MenuGamepadState _menuGamepad=new();
    bool _launcherPadReady,_padScrolling;
    double _nextPadProbe,_lastPadPoll;
    string _padPort=""; int _padScope=-1;
    void StopLauncherPad(){_launcherPadReady=false;_padScrolling=false;_nextPadProbe=0;_menuGamepad.Reset();SdlPad.Stop();}
    void PollLauncherPad(double now)
    {
        if(_benchmark || _game?.Running==true || !_active || _closed)return;
        if(!W.IsWindowEnabled(_window)){if(_launcherPadReady)StopLauncherPad();return;}
        string port=_profile.Str("port_dir");
        if(port!=_padPort){_padPort=port;_nextPadProbe=0;}
        if(!_launcherPadReady)
        {
            if(now<_nextPadProbe)return;
            _nextPadProbe=now+2;
            if(!GamepadPresence.Any() || !SdlPad.Available)return;
            _launcherPadReady=true;_menuGamepad.Reset();_lastPadPoll=now;
        }
        var state=SdlPad.ReadMenuInput();
        if(state.Connected==0){StopLauncherPad();_nextPadProbe=now+5;return;}
        var command=_menuGamepad.Update(state,now);_padScrolling=command.Scroll!=0;
        float elapsed=(float)Math.Clamp(now-_lastPadPoll,0,.1);_lastPadPoll=now;
        if(_padCapture!=null || _dialogClosing || _switchAt>=0)return;
        if(command.Pressed!=0 || command.Direction!=0 || command.Scroll!=0)
        {
            bool show=!_usingPadNavigation;_usingPadNavigation=true;
            if(show && PadFocusRect is { } cue)Invalidate(cue);
        }
        if(_binding!=null || _textCommit!=null || _editing!=null)
        {
            if((command.Pressed&2)!=0)Key(0x1b);
            else if(_binding==null && (command.Pressed&1)!=0)Key(0x0d);
            return;
        }
        nint editFocus=GetFocus();
        foreach(var editor in _textEditors)
            if(editor.Key!=GlobalSearchKey && editor.Key!="output-view" && editor.Value.Window==editFocus)
            {
                if((command.Pressed&3)!=0){W.SetFocus(_window);if((command.Pressed&2)!=0)_audio.PlayBack();return;}
                if(command.Direction!=0)W.SetFocus(_window);
                break;
            }
        if(_textEditors.TryGetValue(GlobalSearchKey,out var global) && editFocus==global.Window)
        {
            if((command.Pressed&1)!=0){GlobalSearchKeyDown(0x0d);return;}
            if(command.Direction!=0)W.SetFocus(_window);
        }
        int scope=_messagePrompt!=null?100:_resetPrompt!=null?101:_recommendation!=null?102:_popup!=null?103:_page;
        if(scope!=_padScope)
        {
            _padScope=scope;
            int initial=_hits.FindIndex(h=>h.Id=="controls-reset-cancel" || h.Id=="recommend-cancel" || h.Id=="message-2" || h.Id=="message-7");
            if(initial<0 && _page==0 && scope==0)initial=_hits.FindIndex(h=>h.Id=="play");
            if(initial<0)initial=_hits.FindIndex(h=>PadHit(h) && h.Bounds.Left>=_side);
            if(initial<0)initial=_hits.FindIndex(h=>PadHit(h));
            if(initial>=0)SetFocus(initial);
        }
        if((command.Pressed&2)!=0)
        {
            if(_popup!=null){Key(0x1b);_audio.PlayBack();}
            else if(_page==0 && _globalSearchActive){GlobalSearchKeyDown(0x1b);_audio.PlayBack();}
            else ReturnToStart();
            return;
        }
        if((command.Pressed&(1u<<3))!=0 && _messagePrompt==null && _resetPrompt==null && _recommendation==null)
        {
            if(_page!=0){_settingDestination=new(0,GlobalSearchKey,""," ");Switch(0);}
            else FocusGlobalSearch();
            return;
        }
        if(_popup!=null)
        {
            if(command.Direction is 1 or 2) { Key(command.Direction==1?0x26:0x28); }
            if((command.Pressed&1)!=0)Key(0x0d);
            return;
        }
        if ((_messagePrompt!=null || _recommendation!=null) && command.Direction is 1 or 2)
        {
            if(_messagePrompt!=null)ScrollMessage((command.Direction==1?-1:1)*48*_scale);
            else ScrollRecommendations((command.Direction==1?-1:1)*48*_scale);
        }
        else if(command.Direction!=0)
        {
            if(_focus>=0 && _focus<_hits.Count && _hits[_focus].Option?.Kind==OptionKind.Slider && command.Direction is 3 or 4)
                Key(command.Direction==3?0x25:0x27);
            else MovePadFocus(command.Direction);
        }
        if((command.Pressed&1)!=0 && _focus>=0 && _focus<_hits.Count && PadHit(_hits[_focus]))
        {
            if(_hits[_focus].Id!="mute")_audio.Play(MenuSoundCue.Change);
            Run(()=>_hits[_focus].Action?.Invoke(.5f));
        }
        if(_messagePrompt==null && _resetPrompt==null && _recommendation==null && (command.Pressed&((1u<<9)|(1u<<10)))!=0)
        {
            int[] order=_cheats.Unlocked?new[]{0,2,1,3,4,9,5,6,7,8}:new[]{0,2,1,3,4,5,6,7,8};
            int current=Array.IndexOf(order,_page),delta=(command.Pressed&(1u<<10))!=0?1:-1;
            _audio.Play(MenuSoundCue.Select);
            Switch(order[(current+delta+order.Length)%order.Length]);
        }
        if(command.Scroll!=0)
        {
            float amount=command.Scroll*elapsed*650*_scale;
            if(_messagePrompt!=null)ScrollMessage(amount);
            else if(_recommendation!=null)ScrollRecommendations(amount);
            else if(_page==8 && _moreTab==1)ScrollOutputWheelDelta((int)(-command.Scroll*120));
            else SetScroll(_scroll+amount);
        }
    }
    bool PadHit(Hit hit)
    {
        if(!hit.Enabled || hit.Id is "min" or "max" or "close" or "scroll" or "output-text" or "output-scroll" || hit.Id.StartsWith("help-",StringComparison.Ordinal))return false;
        if(_messagePrompt!=null)return hit.Id.StartsWith("message-",StringComparison.Ordinal);
        if(_resetPrompt!=null)return hit.Id is "controls-reset-cancel" or "controls-reset-accept";
        if(_recommendation!=null)return hit.Id.StartsWith("recommend-",StringComparison.Ordinal);
        if(_popup!=null)return hit.Id.StartsWith("choice",StringComparison.Ordinal);
        if(_page==0 && _globalSearchActive && _globalResults.Count>0)return hit.Id==GlobalSearchKey || hit.Id.StartsWith("settings-result-",StringComparison.Ordinal);
        return true;
    }
    void MovePadFocus(int direction)
    {
        int current=_focus>=0 && _focus<_hits.Count?_focus:-1;
        if(current<0){int first=_hits.FindIndex(PadHit);if(first>=0)SetFocus(first);return;}
        var origin=ScreenRect(_hits[current].Bounds,_hits[current].Scrollable).Center;
        int next=-1;float best=float.MaxValue;
        for(int i=0;i<_hits.Count;i++)
        {
            var hit=_hits[i];if(i==current || !PadHit(hit))continue;
            var delta=ScreenRect(hit.Bounds,hit.Scrollable).Center-origin;
            float forward=direction==1?-delta.Y:direction==2?delta.Y:direction==3?-delta.X:delta.X;
            if(forward<=2)continue;
            float cross=direction is 1 or 2?Math.Abs(delta.X):Math.Abs(delta.Y);
            float score=forward+cross*2;
            if(score<best){best=score;next=i;}
        }
        if(next<0)return;
        W.SetFocus(_window);SetFocus(next);_audio.Play(MenuSoundCue.Hover);
        var candidate=_hits[next];
        if(candidate.Scrollable)
        {
            var screen=ScreenRect(candidate.Bounds,true);
            if(screen.Top<_viewport.Top)SetScroll(_scroll+screen.Top-_viewport.Top-10*_scale);
            else if(screen.Bottom>_viewport.Bottom)SetScroll(_scroll+screen.Bottom-_viewport.Bottom+10*_scale);
        }
    }
}
