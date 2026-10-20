using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;
using EnshroudedTravel;
class Verify {
    static string root="C:/Workspace/EnshroudedTravel";
    static List<string> results=new List<string>();
    static void Check(bool ok,string label) {
        // 每项断言都验证真实操作后的结果
        if(!ok)throw new Exception("失败："+label);
        results.Add("通过："+label); Console.WriteLine(results.Last());
    }
    static IEnumerable<Control> Controls(Control c) {
        // 遍历实际窗口控件而不是模拟界面状态
        foreach(Control child in c.Controls){yield return child; foreach(var nested in Controls(child))yield return nested;}
    }
    static void Wait(Func<bool> condition) {
        // 驱动消息循环直到实际后台操作完成
        var watch=Stopwatch.StartNew();
        do {Application.DoEvents(); Thread.Sleep(30); if(condition())return;}while(watch.Elapsed.TotalSeconds<30);
        throw new Exception("等待界面状态超时");
    }
    static Dictionary<string,string> SaveHashes(string dir) {
        // 记录每个测试存档文件的完整内容哈希
        return Directory.GetFiles(dir,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>TravelService.HashFile(p));
    }
    [STAThread]
    static int Main(string[] args) {
        // 所有写操作限定在单次创建的独立测试目录
        string run=Path.Combine(root,"lab","test-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(run);
        try {
            string game=Path.Combine(run,"game"),save=Path.Combine(run,"save"),payload=Path.Combine(run,"payload");
            Directory.CreateDirectory(game); Directory.CreateDirectory(save); Directory.CreateDirectory(payload);
            foreach(string n in new[]{"enshrouded.exe","enshrouded.kfc","enshrouded.kfc_resources"})File.Copy(Path.Combine("C:/Games/Enshrouded",n),Path.Combine(game,n));
            foreach(string p in Directory.GetFiles(Path.Combine(root,"payload")))File.Copy(p,Path.Combine(payload,Path.GetFileName(p)));
            string backup=Directory.GetDirectories(Path.Combine(root,"backups"),"*-latest-*",SearchOption.TopDirectoryOnly).OrderByDescending(p=>p).Select(p=>Path.Combine(p,"1203620")).First();
            foreach(string p in Directory.GetFiles(backup,"*",SearchOption.AllDirectories)){string to=Path.Combine(save,p.Substring(backup.Length+1));Directory.CreateDirectory(Path.GetDirectoryName(to));File.Copy(p,to);}
            var hashes=SaveHashes(save); var svc=new TravelService(run,game,save);
            Check(svc.State()=="已关闭","原版游戏副本被正确识别");
            foreach(string name in svc.Names) {
                string path=Path.Combine(game,name),original=path+".original";File.Copy(path,original);
                using(var patch=new Patch(Path.Combine(payload,name+".patch"))) {
                    using(var stream=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))patch.Apply(stream);
                    Check(TravelService.HashFile(path)==patch.Patched,"生产补丁生成正确资源："+name);
                    File.Copy(original,path,true);
                    Check(TravelService.HashFile(path)==patch.Original,"测试资源恢复正确："+name);
                }
            }
            bool running=Process.GetProcessesByName("enshrouded").Length>0;
            if(running) {
                bool blocked=false;try{svc.Toggle(true);}catch(InvalidOperationException e){blocked=e.Message.StartsWith("GAME_RUNNING");}
                Check(blocked && svc.State()=="已关闭","游戏运行时阻止写入");
            }
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            var uiService=running?new TravelService(root,"C:/Games/Enshrouded",save):svc;
            using(var form=new MainForm(uiService)) {
                form.Show();
                Wait(()=>Controls(form).OfType<Label>().Any(c=>c.Text=="传送状态：已关闭"));
                Button enable=Controls(form).OfType<Button>().Single(c=>c.Text=="启用传送");
                Button disable=Controls(form).OfType<Button>().Single(c=>c.Text=="关闭并恢复");
                Button saveButton=Controls(form).OfType<Button>().Single(c=>c.Text=="备份存档");
                if(running) {
                    Wait(()=>Controls(form).OfType<Label>().Any(c=>c.Text.StartsWith("已连接 · PID")));
                    Check(!enable.Enabled&&!disable.Enabled&&!saveButton.Enabled,"真实进程连接后所有写入按钮禁用");
                } else {
                    Check(enable.Enabled,"原版状态允许启用");enable.PerformClick();
                    Wait(()=>Controls(form).OfType<Label>().Any(c=>c.Text.StartsWith("传送状态：已启用")));
                    Wait(()=>disable.Enabled);
                    Check(svc.State().StartsWith("已启用")&&disable.Enabled&&!enable.Enabled,"界面点击启用产生真实补丁并更新状态");
                    Check(Directory.GetFiles(Path.Combine(run,"backups"),"verified.txt",SearchOption.AllDirectories).Length==1,"启用前完成独立存档备份校验");
                    disable.PerformClick();Wait(()=>Controls(form).OfType<Label>().Any(c=>c.Text=="传送状态：已关闭")); Wait(()=>enable.Enabled&& !disable.Enabled);
                    Check(svc.State()=="已关闭"&&enable.Enabled&&!disable.Enabled,"界面关闭恢复完整原始资源");
                }
                form.Size=new Size(820,640);Application.DoEvents();
                using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(root,"reports","ui-"+(running?"running":"closed")+".png"));}
                form.Close();
            }
            File.WriteAllText(Path.Combine(run,"operation.pending"),game+Environment.NewLine+"恢复");
            using(var f=new FileStream(Path.Combine(game,"enshrouded.exe"),FileMode.Open,FileAccess.ReadWrite)){f.Position=f.Length-1;int b=f.ReadByte();f.Position--;f.WriteByte((byte)(b^1));}
            Check(svc.State()=="版本不支持","操作中断标记不能绕过EXE版本校验");
            File.Copy("C:/Games/Enshrouded/enshrouded.exe",Path.Combine(game,"enshrouded.exe"),true);
            using(var f=new FileStream(Path.Combine(game,"enshrouded.kfc"),FileMode.Open,FileAccess.ReadWrite)){f.Position=100;int b=f.ReadByte();f.Position--;f.WriteByte((byte)(b^1));}
            Check(svc.State()=="资源不同 · 已停止操作","操作中断标记不能授权覆盖未知资源");
            Check(hashes.All(p=>File.Exists(p.Key)&&TravelService.HashFile(p.Key)==p.Value)&&hashes.Count==SaveHashes(save).Count,"所有测试存档字节不变");
            results.Add(running?"待验证：游戏退出后的界面完整启用恢复流程":"已验证：游戏退出后的界面完整启用恢复流程");
            results.Add("未验证：游戏内神殿点击传送、落点、未发现地点过滤");
            File.WriteAllLines(Path.Combine(root,"reports","tests-"+(running?"running":"closed")+".txt"),results);
            return 0;
        }catch(Exception ex){Console.WriteLine(ex);File.WriteAllText(Path.Combine(root,"reports","test-failure.txt"),ex.ToString());return 1;}
    }
}
