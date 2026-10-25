using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Rendering;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;
using W = BloodborneLauncher.Native.Platform.Win32;
namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    const string GlobalSearchKey="global-settings-search";
    SettingsSearchIndex? _settingsIndex;
    string _settingsIndexLocale="", _globalQuery="";
    bool _globalSearchActive, _searchNotice, _focusGlobalAfterSync;
    IReadOnlyList<SettingResult> _globalResults=Array.Empty<SettingResult>();
    Rect _globalSearchBox;
    SettingResult? _settingDestination;
    bool GlobalSearchFocused => _focusGlobalAfterSync ||
        _textEditors.TryGetValue(GlobalSearchKey,out var editor) && GetFocus()==editor.Window;
    void EnsureSettingsIndex()
    {
        if(_settingsIndex!=null && _settingsIndexLocale==UiText.Locale)return;
        _settingsIndex=SettingsSearchIndex.Build(_profile,_cheats.Unlocked);
        _settingsIndexLocale=UiText.Locale;
        _globalResults=_settingsIndex.Find(_globalQuery);
    }
    void ChangeGlobalSearch(string text)
    {
        _globalQuery=text.Length>120?text[..120]:text;
        _globalSearchActive=true;
        EnsureSettingsIndex();
        _globalResults=_settingsIndex!.Find(_globalQuery);
        Rebuild();
    }
    void DrawGlobalSearch(Rect box)
    {
        _globalSearchBox=box;
        if(_globalQuery.Length>0)EnsureSettingsIndex();
        bool focused=GlobalSearchFocused;
        float opacity=focused || _globalQuery.Length>0?1f:.65f;
        HitAt(box,GlobalSearchKey,_=>FocusGlobalSearch());
        Symbol("\uE721",new Rect(box.X+4*_scale,box.Y,20*_scale,box.Height),new Color4(Muted.R,Muted.G,Muted.B,opacity),16);
        if(focused) Fill(new Rect(box.X+25*_scale,box.Y,box.Width-25*_scale,box.Height),new Color4(16/255f,19/255f,21/255f,1));
        else TextAt(_globalQuery.Length==0?UiText.T("Search settings..."):_globalQuery,
            new Rect(box.X+33*_scale,box.Y,Math.Max(1,box.Width-41*_scale),box.Height),18.375f,
            color:new Color4(Text.R,Text.G,Text.B,opacity),detail:true,translate:false);
        QueueNativeText(GlobalSearchKey,new Rect(box.X+25*_scale,box.Y,Math.Max(1,box.Width-25*_scale),box.Height),_globalQuery,false,false,ChangeGlobalSearch);
    }
    void FocusGlobalSearch()
    {
        _focusGlobalAfterSync=true;
        _globalSearchActive=true;EnsureSettingsIndex();Rebuild();
        int hit=_hits.FindIndex(h=>h.Id==GlobalSearchKey);if(hit>=0)SetFocus(hit);
        _focusGlobalAfterSync=true;
    }
    void DrawGlobalSearchResults()
    {
        if(_page!=0 || !_globalSearchActive || _globalQuery.Trim().Length==0)return;
        _fixed=true;_scrollContent=false;_surface=false;_clip=null;
        float width=Math.Min(660*_scale,_width-_globalSearchBox.X-24);
        float row=60*_scale, height=Math.Max(1,_globalResults.Count)*row+12*_scale;
        var box=new Rect(_globalSearchBox.X,_globalSearchBox.Bottom+8*_scale,width,Math.Min(height,_height-_globalSearchBox.Bottom-44));
        Fill(box,new Color4(.035f,.04f,.043f,.98f),6*_scale);
        if(_globalResults.Count==0)
            TextAt("No matching settings",box,16,true,Muted);
        for(int i=0;i<_globalResults.Count;i++)
        {
            var result=_globalResults[i];var bounds=new Rect(box.X+6*_scale,box.Y+6*_scale+i*row,box.Width-12*_scale,row);
            if(bounds.Bottom>box.Bottom)break;
            int trigger=_hits.Count;
            HitAt(bounds,"settings-result-"+i,_=>OpenSettingResult(result));
            Add(new(Ink.ControlSurface,bounds,new Color4(1,1,1,1),Trigger:trigger));
            _glows.Add(Add(new(Ink.Glow,bounds,new Color4(1,1,1,.8f),Trigger:trigger)));
            TextAt(result.Title,new Rect(bounds.X+12*_scale,bounds.Y,bounds.Width-24*_scale,32*_scale),18,translate:false);
            TextAt(result.Path,new Rect(bounds.X+12*_scale,bounds.Y+32*_scale,bounds.Width-24*_scale,23*_scale),12.5f,color:Muted,translate:false);
        }
    }
    void OpenSettingResult(SettingResult result)
    {
        _globalSearchActive=false;
        _globalQuery="";
        _globalResults=Array.Empty<SettingResult>();
        _focusGlobalAfterSync=false;
        W.SetFocus(_window);
        _settingDestination=result;
        if(result.Page==8){_moreTab=result.MoreTab;_subpage="";_pageRows=null;}
        Switch(result.Page);
    }
    void RevealSettingResult()
    {
        if(_settingDestination is not { } result || _switchAt>=0 || _dirty || _page!=result.Page)return;
        _settingDestination=null;
        string key=result.Key.StartsWith("action:",StringComparison.Ordinal)?result.Key[7..]:result.Key;
        if(result.Device.Length>0 && result.Device!=_profile.InputDevice)
        {
            key=result.Device=="controller"?"gamepad":"keyboard";
            _notice=UiText.Format("Select {0} to view this setting.",UiText.T(result.Device=="controller"?"Gamepad":"Keyboard/Mouse"));_searchNotice=true;
        }
        int hit=_hits.FindIndex(h=>h.Id==key || h.Id==result.Key || h.Option?.Key==result.Key);
        if((hit<0 || !_hits[hit].Enabled) && result.Fallback.Length>0)
        {
            hit=_hits.FindIndex(h=>h.Option?.Key==result.Fallback);
            _notice=UiText.T("Enable the relevant option to view this setting.");_searchNotice=true;
        }
        if(hit>=0)
        {
            var entry=_hits[hit];
            if(entry.Scrollable)SetScroll(entry.Bounds.Top-_viewport.Top-24*_scale);
            SetFocus(hit);InvalidateInteraction(hit,true);
        }
        if(key==GlobalSearchKey && _page==0)FocusGlobalSearch();
        if(_searchNotice)Rebuild();
    }
    void MoveGlobalResult(int direction)
    {
        int index=-1;
        if(_focus>=0 && _focus<_hits.Count && _hits[_focus].Id.StartsWith("settings-result-",StringComparison.Ordinal))
            int.TryParse(_hits[_focus].Id[16..],out index);
        index=Math.Clamp(index+direction,0,_globalResults.Count-1);
        int hit=_hits.FindIndex(h=>h.Id=="settings-result-"+index);if(hit>=0)SetFocus(hit);
    }
    bool GlobalSearchKeyDown(int key)
    {
        if(key==0x1B){_globalSearchActive=false;W.SetFocus(_window);Rebuild();return true;}
        if(key==0x0D && _globalResults.Count>0){OpenSettingResult(_globalResults[0]);return true;}
        if(key==0x28 && _globalResults.Count>0)
        {
            W.SetFocus(_window);int hit=_hits.FindIndex(h=>h.Id=="settings-result-0");if(hit>=0)SetFocus(hit);return true;
        }
        return false;
    }
}
