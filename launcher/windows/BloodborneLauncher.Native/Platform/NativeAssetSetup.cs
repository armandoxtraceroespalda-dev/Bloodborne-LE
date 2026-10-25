using System.Runtime.InteropServices;
using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
using W=BloodborneLauncher.Native.Platform.Win32;

namespace BloodborneLauncher.Native.Platform;

/// <summary>Asset-free first-run UI; the themed renderer starts only after extraction succeeds.</summary>
internal sealed class NativeAssetSetup:IProgress<GameAssetExtractor.Step>,IDisposable
{
    const uint ProgressMessage=0x8007,CompletedMessage=0x8008;
    static readonly W.WindowProc Procedure=WindowProcedure;
    static NativeAssetSetup? _current;
    static bool _registered;
    readonly CancellationTokenSource _cancel=new();
    readonly object _progressLock=new();
    GameAssetExtractor.Step _step=new(0,"Loading...");
    nint _window,_label,_progress,_button;
    Task? _task;
    bool _finished;

    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool SetWindowText(nint h,string text);
    [DllImport("user32.dll")]static extern nint SendMessage(nint h,uint message,nint w,nint l);
    [DllImport("user32.dll")]static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]static extern bool EnableWindow(nint h,bool enabled);
    [StructLayout(LayoutKind.Sequential)]struct Controls{public uint Size,Classes;}
    [DllImport("comctl32.dll")]static extern bool InitCommonControlsEx(ref Controls controls);

    internal static bool Ensure(ProfileSession profile)
    {
        if(NativeAssetPreparation.IsReady(profile.Assets))return true;
        string? game=GameAssetExtractor.FindGameRoot(Environment.ExpandEnvironmentVariables(profile.Str("game_dir")));
        while(true)
        {
            if(game==null)
            {
                string? selected=NativeDialogs.Folder(0,UiText.T("Select the Bloodborne game folder"));
                if(selected==null)return false;
                game=GameAssetExtractor.FindGameRoot(selected);
                if(game==null){W.MessageBox(0,UiText.T("Select the Bloodborne folder containing eboot.bin and dvdroot_ps4/menu."),"Bloodborne Launcher",0x10);continue;}
            }
            try
            {
                using var setup=new NativeAssetSetup();
                if(!setup.Run(game,profile.Assets))return false;
                profile.RememberGameRoot(game);return true;
            }
            catch(Exception error)
            {
                if(W.MessageBox(0,error.GetBaseException().Message+"\n\n"+UiText.T("Retry asset extraction?"),"Bloodborne Launcher",0x15)!=4)return false;
                game=null;
            }
        }
    }
    bool Run(string game,string output)
    {
        var controls=new Controls{Size=(uint)Marshal.SizeOf<Controls>(),Classes=0x20};
        if(!InitCommonControlsEx(ref controls))throw new IOException("Could not initialize progress controls.");
        if(!_registered)
        {
            var cls=new W.WindowClass{Size=(uint)Marshal.SizeOf<W.WindowClass>(),Proc=Procedure,Instance=W.GetModuleHandle(null),Cursor=W.LoadCursor(0,32512),Background=6,Name="BloodborneAssetSetup"};
            if(W.RegisterClassEx(ref cls)==0)throw new IOException("Could not register asset setup window.");
            _registered=true;
        }
        _current=this;
        _window=W.CreateWindowEx(0,"BloodborneAssetSetup","Bloodborne Launcher",0x00C80000,
            (GetSystemMetrics(0)-500)/2,(GetSystemMetrics(1)-180)/2,500,180,0,0,W.GetModuleHandle(null),0);
        if(_window==0)throw new IOException("Could not create asset setup window.");
        _label=W.CreateWindowEx(0,"STATIC",UiText.T("Loading..."),0x50000000,20,18,450,30,_window,0,0,0);
        _progress=W.CreateWindowEx(0,"msctls_progress32","",0x50000000,20,55,450,22,_window,0,0,0);
        _button=W.CreateWindowEx(0,"BUTTON",UiText.T("Cancel"),0x50010000,360,94,110,28,_window,1,0,0);
        W.ShowWindow(_window,5);
        _task=Task.Run(()=>NativeAssetPreparation.PrepareAsync(game,output,this,_cancel.Token));
        _=_task.ContinueWith(_=>W.PostMessage(_window,CompletedMessage,0,0),CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
        while(!_finished)
        {
            int result=W.GetMessage(out var message,0,0,0);
            if(result<=0){_cancel.Cancel();break;}
            W.TranslateMessage(ref message);W.DispatchMessage(ref message);
        }
        try{_task.GetAwaiter().GetResult();return true;}
        catch(OperationCanceledException){return false;}
    }
    public void Report(GameAssetExtractor.Step step)
    {lock(_progressLock)_step=step;W.PostMessage(_window,ProgressMessage,0,0);}
    void Cancel()
    {_cancel.Cancel();EnableWindow(_button,false);SetWindowText(_label,UiText.T("Cancelling..."));}
    static nint WindowProcedure(nint h,uint message,nint w,nint l)
    {
        var setup=_current;
        if(setup==null)return W.DefWindowProc(h,message,w,l);
        if(message==0x10 || message==0x111 && ((long)w&0xffff)==1){setup.Cancel();return 0;}
        if(message==ProgressMessage)
        {
            GameAssetExtractor.Step step;lock(setup._progressLock)step=setup._step;
            SetWindowText(setup._label,UiText.T(step.Message));SendMessage(setup._progress,0x402,step.Percent,0);return 0;
        }
        if(message==CompletedMessage){setup._finished=true;W.DestroyWindow(h);setup._window=0;return 0;}
        return W.DefWindowProc(h,message,w,l);
    }
    public void Dispose()
    {
        _cancel.Cancel();
        if(_window!=0)W.DestroyWindow(_window);
        _window=0;_current=null;_cancel.Dispose();
    }
}
