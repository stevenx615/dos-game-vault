using System.Runtime.InteropServices;
using System.Text;

namespace DOSGameVault;

// Native file/folder pickers use Windows transitions, separate from app dialog animations.
internal sealed class FileBrowserTransitions : IDisposable
{
    private readonly EventCallback callback;
    private readonly IntPtr hook;
    public FileBrowserTransitions()
    {
        callback=OnWindowCreated;
        hook=SetWinEventHook(0x8000,0x8002,IntPtr.Zero,callback,(uint)Environment.ProcessId,0,0);
    }
    private static void OnWindowCreated(IntPtr hook,uint eventType,IntPtr hwnd,int objectId,int childId,uint thread,uint time)
    {
        if(hwnd==IntPtr.Zero || objectId!=0 || childId!=0)return;
        var name=new StringBuilder(64);GetClassName(hwnd,name,name.Capacity);
        if(name.ToString()!="#32770")return;
        var disabled=1;DwmSetWindowAttribute(hwnd,3,ref disabled,sizeof(int));
    }
    public void Dispose() {if(hook!=IntPtr.Zero)UnhookWinEvent(hook);GC.KeepAlive(callback);}
    private delegate void EventCallback(IntPtr hook,uint eventType,IntPtr hwnd,int objectId,int childId,uint thread,uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,EventCallback callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd,StringBuilder name,int count);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
}
