using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Threading;
using System.Diagnostics;
using System.Windows.Forms;
using System.Collections.Generic;
using EnshroudedTravel;
class ProgressVerify {
    static IEnumerable<Control> All(Control c) {
        // 读取实际生产窗体的全部控件
        foreach(Control child in c.Controls){yield return child;foreach(var nested in All(child))yield return nested;}
    }
    static void Wait(Func<bool> ready) {
        // 驱动真实消息循环，等待异步扫描反馈
        var watch=Stopwatch.StartNew();
        while(watch.Elapsed.TotalSeconds<30){Application.DoEvents();if(ready())return;Thread.Sleep(20);}
        throw new Exception("等待界面响应超时");
    }
    [STAThread] static int Main() {
        // 只测试读取、进度、取消和缓存重连，不执行游戏内存写入
        string root=AppDomain.CurrentDomain.BaseDirectory,cache=Path.Combine(root,"runtime-location.txt"),held=cache+".progress-test-"+Guid.NewGuid().ToString("N");
        var results=new List<string>();MainForm form=null;
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        try {
            File.Move(cache,held);
            form=new MainForm(LocalConfiguration.CreateService(root));form.Show();
            var bar=All(form).OfType<ProgressBar>().Single();
            var cancel=All(form).OfType<Button>().Single(x=>x.Text=="取消定位");
            var retry=All(form).OfType<Button>().Single(x=>x.Text=="重新校验");
            var enable=All(form).OfType<Button>().Single(x=>x.Text=="启用传送");
            Wait(()=>bar.Style==ProgressBarStyle.Continuous&&bar.Value>0&&cancel.Enabled);
            if(!All(form).OfType<Label>().Any(x=>x.Text.Contains("已发现")&&x.Text.Contains("PID")))throw new Exception("定位中未显示进程");
            var line=All(form).OfType<Label>().Single(x=>x.Text.Contains(" GB"));results.Add("通过：真实扫描显示进程、字节比例和经过时间："+line.Text);
            using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size));image.Save(Path.Combine(root,"reports/ui-location-progress.png"));}
            var watch=Stopwatch.StartNew();cancel.PerformClick();Wait(()=>retry.Enabled&&!cancel.Enabled);
            if(enable.Enabled||!All(form).OfType<Label>().Any(x=>x.Text.Contains("定位已取消")))throw new Exception("取消后状态不正确");
            results.Add("通过：取消停止扫描，耗时 "+watch.Elapsed.TotalSeconds.ToString("0.00")+" 秒，启用按钮保持禁用");
            File.Move(held,cache);watch.Restart();retry.PerformClick();Wait(()=>enable.Enabled);
            results.Add("通过：取消后重新校验可用，真实缓存连接耗时 "+watch.Elapsed.TotalSeconds.ToString("0.00")+" 秒");
            using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size));image.Save(Path.Combine(root,"reports/ui-location-ready.png"));}
            form.Close();Wait(()=>form.IsDisposed);
            File.WriteAllLines(Path.Combine(root,"reports/tests-progress.txt"),results);foreach(var lineText in results)Console.WriteLine(lineText);return 0;
        }catch(Exception ex){Console.WriteLine(ex);return 1;}
        finally {if(form!=null&&!form.IsDisposed){form.Close();Wait(()=>form.IsDisposed);}if(File.Exists(held)&&!File.Exists(cache))File.Move(held,cache);}
    }
}
