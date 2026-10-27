using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Reflection;
using System.Windows.Forms;
using EnshroudedTravel;
class RuntimeVerify {
    static readonly string root=AppDomain.CurrentDomain.BaseDirectory;
    static readonly List<string> results=new List<string>();
    static void Check(bool ok,string label) {
        // 断言界面操作产生的真实结果
        if(!ok)throw new Exception(label);results.Add("通过："+label);Console.WriteLine(results.Last());
    }
    static IEnumerable<Control> All(Control parent) {
        // 查找生产界面中的实际按钮
        foreach(Control child in parent.Controls){yield return child;foreach(var nested in All(child))yield return nested;}
    }
    static void Wait(Func<bool> condition) {
        // 驱动界面消息直到操作完成，限定全内存扫描等待时间
        var watch=Stopwatch.StartNew();
        while(watch.Elapsed.TotalSeconds<180){Application.DoEvents();if(condition())return;Thread.Sleep(20);}
        throw new Exception("界面操作等待超时");
    }
    [STAThread] static int Main() {
        // 只在用户已经确认的备份世界中调用真实生产界面
        var files=LocalConfiguration.CreateService(root);
        var paths=new[]{Path.Combine(files.Save,"remote/character0.ksd"),Path.Combine(files.Save,"remote/world0.ksd"),Path.Combine(files.Game,"enshrouded.kfc"),Path.Combine(files.Game,"enshrouded.kfc_resources")};
        var before=paths.ToDictionary(p=>p,p=>TravelService.HashFile(p));
        MainForm form=null;RuntimeTravelService runtime=null;
        try {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            form=new MainForm(files);
            runtime=(RuntimeTravelService)typeof(MainForm).GetField("runtime",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
            form.Show();
            var enable=All(form).OfType<Button>().Single(x=>x.Text=="启用传送");
            var disable=All(form).OfType<Button>().Single(x=>x.Text=="关闭并恢复");
            Wait(()=>enable.Enabled);Check(runtime.Ready&&!runtime.Enabled,"真实游戏自动定位成功，不依赖固定地址");
            enable.PerformClick();Wait(()=>runtime.Enabled&&disable.Enabled);Check(!enable.Enabled,"界面启用写入真实内存并更新按钮");
            disable.PerformClick();Wait(()=>!runtime.Enabled&&enable.Enabled);Check(!runtime.NeedsRestore,"界面关闭恢复完整注册表");
            using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(root,"reports/ui-runtime.png"));}
            enable.PerformClick();Wait(()=>runtime.Enabled&&disable.Enabled);form.Close();Wait(()=>form.IsDisposed);
            Check(!runtime.NeedsRestore,"启用状态正常关闭窗口完成恢复");
            using(var reconnect=new RuntimeTravelService(files)){reconnect.Poll(true,CancellationToken.None);Check(reconnect.Ready&&!reconnect.Enabled,"重新创建连接独立扫描并确认原始内存状态");}
            Check(before.All(x=>TravelService.HashFile(x.Key)==x.Value),"原角色原世界及两个磁盘资源均未改变");
            results.Add("待验证：真正退出并重新启动游戏后的自动重新连接");
            File.WriteAllLines(Path.Combine(root,"reports/tests-runtime.txt"),results);return 0;
        } catch(Exception ex){Console.WriteLine(ex);File.WriteAllText(Path.Combine(root,"reports/runtime-test-failure.txt"),ex.ToString());return 1;}
        finally {if(runtime!=null)runtime.Restore();if(form!=null&&!form.IsDisposed){form.Close();Wait(()=>form.IsDisposed);}}
    }
}
