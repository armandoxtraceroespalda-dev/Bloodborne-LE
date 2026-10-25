using BloodborneLauncher.Core;
namespace BloodborneLauncher.Native.Interaction;
internal readonly record struct PadCommands(uint Pressed,int Direction,float Scroll);
internal sealed class MenuGamepadState
{
    uint _held; int _direction; bool _primed; double _repeat;
    public bool Busy => _held!=0 || _direction!=0;
    public void Reset(){_primed=false;_held=0;_direction=0;}
    public PadCommands Update(SdlPad.MenuInput input,double now)
    {
        if(input.Connected==0){Reset();return default;}
        int direction=(input.Buttons&(1u<<11))!=0?1:(input.Buttons&(1u<<12))!=0?2:
            (input.Buttons&(1u<<13))!=0?3:(input.Buttons&(1u<<14))!=0?4:
            Math.Abs((int)input.LeftY)>16000?(input.LeftY<0?1:2):Math.Abs((int)input.LeftX)>16000?(input.LeftX<0?3:4):0;
        if(!_primed){_held=input.Buttons;_direction=direction;_repeat=now+.35;_primed=true;return default;}
        uint pressed=input.Buttons&~_held;_held=input.Buttons;
        int move=0;
        if(direction!=_direction){_direction=direction;_repeat=now+.35;move=direction;}
        else if(direction!=0 && now>=_repeat){move=direction;_repeat=now+.09;}
        return new(pressed,move,Math.Abs((int)input.RightY)>12000?input.RightY/32768f:0);
    }
}
