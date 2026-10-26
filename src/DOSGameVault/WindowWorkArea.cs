using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shell;
namespace DOSGameVault;
internal static class WindowWorkArea
{
    public static void Attach(Window window)
    {
        var hwnd=new WindowInteropHelper(window).Handle;
        // Hide the native surface from the compositor until WPF has rendered content.
        // Unlike Opacity=0, cloaking does not switch WPF to a layered window.
        var cloaked=1;
        DwmSetWindowAttribute(hwnd,13,ref cloaked,sizeof(int)); // DWMWA_CLOAK
        var darkMode=1;
        DwmSetWindowAttribute(hwnd,20,ref darkMode,sizeof(int)); // Match native first-paint colors to the app.
        // Avoid DWM animating an intermediate frame before WindowChrome finishes its layout.
        var disableTransitions=1;
        DwmSetWindowAttribute(hwnd,3,ref disableTransitions,sizeof(int)); // DWMWA_TRANSITIONS_FORCEDISABLED
        var roundedCorners=2;
        DwmSetWindowAttribute(hwnd,33,ref roundedCorners,sizeof(int)); // Native rounded corners where supported.
        var noBorder=unchecked((int)0xfffffffe); // DWMWA_COLOR_NONE
        DwmSetWindowAttribute(hwnd,34,ref noBorder,sizeof(int)); // Keep the native border hidden in both activation states.
        var source=HwndSource.FromHwnd(hwnd);
        if(source?.CompositionTarget is {} target)target.BackgroundColor=Color.FromRgb(12,14,27);
        var awaitingFirstFrame=true;
        EventHandler? rendered=null;
        rendered=(_,_)=>
        {
            awaitingFirstFrame=false;window.ContentRendered-=rendered;
            DwmFlush();
            var visible=0;DwmSetWindowAttribute(hwnd,13,ref visible,sizeof(int));
            // Only suppress transitions during the hidden first paint. Windows owns
            // minimize/restore animations after the window is ready.
            var allowTransitions=0;
            DwmSetWindowAttribute(hwnd,3,ref allowTransitions,sizeof(int));
            if(window.ShowActivated)window.Activate();
        };
        window.ContentRendered+=rendered;
        source?.AddHook((IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)=>
        {
            // Cover the initial native surface until WPF has presented its first frame.
            // Leave normal resize/scroll rendering untouched after startup.
            if(message==0x0014 && awaitingFirstFrame && GetClientRect(hwnd,out var client))
            {
                var previous=SetDCBrushColor(wParam,0x001B0E0C);
                FillRect(wParam,ref client,GetStockObject(18)); // DC_BRUSH, owned by Windows.
                SetDCBrushColor(wParam,previous);
                handled=true;return new IntPtr(1);
            }
            return ConstrainMaximizedBounds(window,hwnd,message,wParam,lParam,ref handled);
        });
        // WindowStyle=None removes WS_CAPTION, which Windows needs for native
        // window transitions. Keep the native style while our WM_NCCALCSIZE hook
        // supplies the existing full-client-area title bar and border.
        var style=GetWindowLong(hwnd,-16); // GWL_STYLE
        const int caption=0x00c00000;
        if((style&caption)!=caption)
        {
            SetWindowLong(hwnd,-16,style|caption);
            SetWindowPos(hwnd,IntPtr.Zero,0,0,0,0,0x0037); // FRAMECHANGED, no move/size/z-order/activation.
        }
    }
    public static async Task ShowReadyAsync(Window window)
    {
        var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler rendered=(_,_)=>ready.TrySetResult();
        EventHandler closed=(_,_)=>ready.TrySetCanceled();
        window.ContentRendered+=rendered;window.Closed+=closed;
        try {window.Show();await ready.Task;}
        finally {window.ContentRendered-=rendered;window.Closed-=closed;}
    }
    private static IntPtr ConstrainMaximizedBounds(Window window,IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        // Let Windows update activation without painting its inactive non-client frame.
        if(message==0x0086) {handled=true;return DefWindowProc(hwnd,message,wParam,new IntPtr(-1));}
        if(message==0x0083) {handled=true;return IntPtr.Zero;} // Full client area, without a second WindowChrome frame correction.
        if(message==0x0084)
        {
            var packed=lParam.ToInt64();
            var point=window.PointFromScreen(new Point((short)(packed&0xffff),(short)((packed>>16)&0xffff)));
            var hit=HitTest(window,point);
            if(hit!=0) {handled=true;return new IntPtr(hit);}
        }
        if(message!=0x0024) return IntPtr.Zero; // WM_GETMINMAXINFO
        var monitor=MonitorFromWindow(hwnd,2);
        var info=new MonitorInfo { Size=Marshal.SizeOf<MonitorInfo>() };
        if(monitor==IntPtr.Zero || !GetMonitorInfo(monitor,ref info)) return IntPtr.Zero;
        var bounds=Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(window);
        bounds.MinTrackSize=new PointInt
        {
            X=Math.Max(bounds.MinTrackSize.X,(int)Math.Ceiling(window.MinWidth*dpi.DpiScaleX)),
            Y=Math.Max(bounds.MinTrackSize.Y,(int)Math.Ceiling(window.MinHeight*dpi.DpiScaleY))
        };
        bounds.MaxPosition=new PointInt { X=info.Work.Left-info.Monitor.Left,Y=info.Work.Top-info.Monitor.Top };
        bounds.MaxSize=new PointInt { X=info.Work.Right-info.Work.Left,Y=info.Work.Bottom-info.Work.Top };
        Marshal.StructureToPtr(bounds,lParam,false);
        handled=true;return IntPtr.Zero;
    }
    private static int HitTest(Window window,Point point)
    {
        if(window.WindowState==WindowState.Normal)
        {
            var left=point.X<6;var right=point.X>=window.ActualWidth-6;
            var top=point.Y<6;var bottom=point.Y>=window.ActualHeight-6;
            if(top)return left?13:right?14:12;
            if(bottom)return left?16:right?17:15;
            if(left)return 10;if(right)return 11;
        }
        var caption=window.DataContext is MainViewModel vm ? vm.TitleBarHeight : 76;
        if(point.Y>=caption)return 1;
        var target=window.InputHitTest(point) as DependencyObject;
        while(target!=null && target!=window)
        {
            if((target is IInputElement input && WindowChrome.GetIsHitTestVisibleInChrome(input)) || target is ButtonBase or TextBoxBase or Selector or Slider) return 1;
            target=target is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(target) : LogicalTreeHelper.GetParent(target);
        }
        return 2; // HTCAPTION retains native drag, snap and double-click maximize.
    }
    [StructLayout(LayoutKind.Sequential)] private struct PointInt {public int X,Y;}
    [StructLayout(LayoutKind.Sequential)] private struct RectInt {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo {public PointInt Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize;}
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo {public int Size;public RectInt Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd,int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd,int index,int value);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int objectId);
    [DllImport("gdi32.dll")] private static extern uint SetDCBrushColor(IntPtr hdc,uint color);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr hwnd,out RectInt rect);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr hdc,ref RectInt rect,IntPtr brush);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll",CharSet=CharSet.Auto)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
}
