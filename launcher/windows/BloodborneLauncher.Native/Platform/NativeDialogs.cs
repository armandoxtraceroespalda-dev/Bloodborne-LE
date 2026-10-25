using System.Runtime.InteropServices;
using System.Text;

namespace BloodborneLauncher.Native.Platform;
internal static class NativeDialogs
{
    [DllImport("ole32.dll", ExactSpelling = true)]
    static extern int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
    [DllImport("ole32.dll")]
    static extern void CoTaskMemFree(nint p);
    public static unsafe string? Folder(nint owner, string title)
    {
        // IFileOpenDialog, FOS_PICKFOLDERS: the normal Explorer dialog, including its address bar.
        Guid clsid = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7"), iid = new("D57C7288-D4AD-4768-BE02-9D969532D960");
        Marshal.ThrowExceptionForHR(CoCreateInstance(in clsid, 0, 1, in iid, out nint dialog));
        nint item = 0, path = 0;
        try
        {
            var v = *(nint**)dialog;
            uint options;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, uint*, int> )v[10])(dialog, &options));
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, uint, int> )v[9])(dialog, options | 0x20u | 0x40u | 0x800u));
            fixed (char* text = title)
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, char*, int> )v[17])(dialog, text));
            int result = ((delegate* unmanaged[Stdcall]<nint, nint, int> )v[3])(dialog, owner);
            if (result == unchecked((int)0x800704C7))
                return null;
            Marshal.ThrowExceptionForHR(result);
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint*, int> )v[20])(dialog, &item));
            var shell = *(nint**)item;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int> )shell[5])(item, 0x80058000, &path));
            return Marshal.PtrToStringUni(path);
        }
        finally
        {
            if (path != 0)
                CoTaskMemFree(path);
            if (item != 0)
                Marshal.Release(item);
            Marshal.Release(dialog);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    sealed class OpenFile
    {
        public int Size = Marshal.SizeOf<OpenFile>();
        public nint Owner, Instance;
        public string Filter = "JSON files\0*.json\0All files\0*.*\0\0";
        public nint CustomFilter;
        public int MaxCustomFilter, FilterIndex = 1;
        public StringBuilder File = new(32768);
        public int MaxFile = 32768;
        public nint FileTitle;
        public int MaxFileTitle;
        public string? InitialDirectory, Title;
        public uint Flags;
        public short Offset, Extension;
        public string DefaultExtension = "json";
        public nint Data, Hook, Template, Reserved;
        public int Reserved2, FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetOpenFileName([In, Out] OpenFile f);
    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetSaveFileName([In, Out] OpenFile f);
    public static string? File(nint owner, bool save, string title, string? filter = null, string extension = "json")
    {
        var f = new OpenFile
        {
            Owner = owner,
            Title = title,
            Filter = filter ?? "JSON files\0*.json\0All files\0*.*\0\0",
            DefaultExtension = extension,
            Flags = 0x80008 | (save ? 2u : 0x1000u)
        };
        return (save ? GetSaveFileName(f) : GetOpenFileName(f)) ? f.File.ToString() : null;
    }

    [DllImport("user32.dll")]
    static extern bool OpenClipboard(nint window);
    [DllImport("user32.dll")]
    static extern bool CloseClipboard();
    [DllImport("user32.dll")]
    static extern bool EmptyClipboard();
    [DllImport("user32.dll")]
    static extern nint SetClipboardData(uint format, nint data);
    [DllImport("kernel32.dll")]
    static extern nint GlobalAlloc(uint flags, nuint count);
    [DllImport("kernel32.dll")]
    static extern nint GlobalLock(nint p);
    [DllImport("kernel32.dll")]
    static extern bool GlobalUnlock(nint p);
    [DllImport("kernel32.dll")]
    static extern nint GlobalFree(nint p);
    [DllImport("user32.dll")]
    static extern nint GetClipboardData(uint format);
    public static string Paste(nint owner)
    {
        if (!OpenClipboard(owner))
            return "";
        nint handle = 0, data = 0;
        try
        {
            handle = GetClipboardData(13);
            if (handle == 0)
                return "";
            data = GlobalLock(handle);
            return data == 0 ? "" : Marshal.PtrToStringUni(data) ?? "";
        }
        finally
        {
            if (data != 0)
                GlobalUnlock(handle);
            CloseClipboard();
        }
    }

    public static void Copy(nint window, string text)
    {
        if (!OpenClipboard(window))
            throw new InvalidOperationException("Clipboard is busy.");
        nint memory = 0;
        try
        {
            var bytes = Encoding.Unicode.GetBytes(text + '\0');
            memory = GlobalAlloc(2, (nuint)bytes.Length);
            if (memory == 0)
                throw new OutOfMemoryException();
            var target = GlobalLock(memory);
            if (target == 0)
                throw new InvalidOperationException("Cannot lock clipboard memory.");
            try
            {
                Marshal.Copy(bytes, 0, target, bytes.Length);
            }
            finally
            {
                GlobalUnlock(memory);
            }

            EmptyClipboard();
            if (SetClipboardData(13, memory) == 0)
                throw new InvalidOperationException("Cannot write clipboard.");
            memory = 0;
        }
        finally
        {
            if (memory != 0)
                GlobalFree(memory);
            CloseClipboard();
        }
    }
}
