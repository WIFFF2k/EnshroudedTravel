using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
namespace EnshroudedTravel {
static class Program {
    [STAThread]
    static void Main() {
        // 防止同时打开两个资源控制器
        bool created;
        using(var mutex=new Mutex(true,@"Local\EnshroudedTravel26082781",out created)) {
            if(!created){ShowExistingWindow();return;}
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string root=AppDomain.CurrentDomain.BaseDirectory;
            var service=new TravelService(root,"C:/Games/Enshrouded","C:/Steam/userdata/LOCAL_ACCOUNT/1203620");
            Application.Run(new MainForm(service));
        }
    }
    private static void ShowExistingWindow() {
        // 包含隐藏窗口在内查找同路径实例，重复启动时恢复并前置已有主窗口
        int currentId=Process.GetCurrentProcess().Id;
        string currentPath=Application.ExecutablePath;
        bool found=false;
        EnumWindows(delegate(IntPtr window,IntPtr state) {
            uint pid;
            GetWindowThreadProcessId(window,out pid);
            if(pid==(uint)currentId)return true;
            var title=new StringBuilder(128);
            GetWindowText(window,title,title.Capacity);
            if(title.ToString()!="雾锁王国修改器")return true;
            try {
                using(var existing=Process.GetProcessById((int)pid)) {
                    if(!string.Equals(existing.MainModule.FileName,currentPath,StringComparison.OrdinalIgnoreCase))return true;
                }
            } catch(ArgumentException) {return true;}
            catch(System.ComponentModel.Win32Exception) {return true;}
            ShowWindowAsync(window,9);
            SetForegroundWindow(window);
            found=true;
            return false;
        },IntPtr.Zero);
        if(!found)MessageBox.Show("助手进程正在运行，但主窗口尚未就绪，请稍后再次打开");
    }
    private delegate bool WindowCallback(IntPtr window,IntPtr state);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(WindowCallback callback,IntPtr state);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window,StringBuilder text,int capacity);
    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr window,int command);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
}
}
