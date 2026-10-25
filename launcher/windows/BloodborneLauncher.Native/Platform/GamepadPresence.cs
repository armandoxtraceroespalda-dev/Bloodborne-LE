using System.Runtime.InteropServices;
namespace BloodborneLauncher.Native.Platform;
internal static class GamepadPresence
{
    [StructLayout(LayoutKind.Sequential)] struct XInputState
    {
        public uint Packet; public ushort Buttons; public byte LeftTrigger,RightTrigger;
        public short LeftX,LeftY,RightX,RightY;
    }
    [StructLayout(LayoutKind.Sequential)] struct RawDevice { public nint Handle; public uint Type; }
    [DllImport("xinput1_4.dll")] static extern uint XInputGetState(uint index,out XInputState state);
    [DllImport("user32.dll")] static extern unsafe uint GetRawInputDeviceList(RawDevice* devices,ref uint count,uint stride);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint GetRawInputDeviceInfo(nint device,uint command,nint data,ref uint size);
    static bool _noXinput;
    public static unsafe bool Any()
    {
        if(!OperatingSystem.IsWindows())return false;
        if(!_noXinput)
        {
            try { for(uint i=0;i<4;i++)if(XInputGetState(i,out _)==0)return true; }
            catch(DllNotFoundException){_noXinput=true;}
            catch(EntryPointNotFoundException){_noXinput=true;}
        }
        uint count=0,stride=(uint)sizeof(RawDevice);
        if(GetRawInputDeviceList(null,ref count,stride)==uint.MaxValue || count==0 || count>256)return false;
        RawDevice* devices=stackalloc RawDevice[(int)count];
        uint found=GetRawInputDeviceList(devices,ref count,stride);
        if(found==uint.MaxValue)return false;
        byte* info=stackalloc byte[32];
        for(int i=0;i<(int)found;i++)
        {
            if(devices[i].Type!=2)continue;
            uint size=32;*(uint*)info=32;
            if(GetRawInputDeviceInfo(devices[i].Handle,0x2000000b,(nint)info,ref size)==uint.MaxValue)continue;
            ushort page=*(ushort*)(info+20),usage=*(ushort*)(info+22);
            if(page==1 && usage is 4 or 5 or 8)return true;
        }
        return false;
    }
}
