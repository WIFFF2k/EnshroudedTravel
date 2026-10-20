using System;
using System.Threading;
using System.Windows.Forms;
namespace EnshroudedTravel {
static class Program {
    [STAThread]
    static void Main() {
        // 防止同时打开两个资源控制器
        bool created;
        using(var mutex=new Mutex(true,@"Local\EnshroudedTravel25988036",out created)) {
            if(!created){MessageBox.Show("传送助手已经打开");return;}
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string root=AppDomain.CurrentDomain.BaseDirectory;
            var service=new TravelService(root,"C:/Games/Enshrouded","C:/Steam/userdata/LOCAL_ACCOUNT/1203620");
            Application.Run(new MainForm(service));
        }
    }
}
}
