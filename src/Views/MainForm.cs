using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace EnshroudedTravel {
public sealed class MainForm : Form {
    private readonly TravelService service;
    private readonly Label connection = new Label(), state = new Label(), backup = new Label();
    private readonly Button enable = new Button(), disable = new Button(), save = new Button(), refresh = new Button();
    private readonly TextBox log = new TextBox();
    private readonly Timer timer = new Timer();
    private bool busy;
    private string verifiedState = "正在校验资源";
    public MainForm(TravelService instance) {
        // 按自适应行列布局排列状态和按钮
        service=instance; Text="灵火神殿传送助手 · 单人";
        Font=new Font("Microsoft YaHei UI",10F);
        BackColor=Color.FromArgb(24,29,37); ForeColor=Color.FromArgb(230,233,239);
        MinimumSize=new Size(740,570); Size=new Size(820,640); StartPosition=FormStartPosition.CenterScreen;
        var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=8,Padding=new Padding(24)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach(int height in new[]{48,42,42,60,54,62}) layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        layout.Controls.Add(new Label {Text="灵火神殿地图传送",Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,19,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft},0,0);
        int row=1; foreach(var label in new[]{connection,state,backup}) {label.Dock=DockStyle.Fill; label.TextAlign=ContentAlignment.MiddleLeft; label.AutoEllipsis=true; layout.Controls.Add(label,0,row++);}
        var actions=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=Padding.Empty};
        foreach(var b in new[]{enable,disable,save,refresh}) {actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25)); b.Dock=DockStyle.Fill; b.Margin=new Padding(0,4,10,4); b.FlatStyle=FlatStyle.Flat; b.BackColor=Color.FromArgb(49,61,77); b.ForeColor=ForeColor; actions.Controls.Add(b);}
        enable.Text="启用传送"; enable.BackColor=Color.FromArgb(21,103,91); disable.Text="关闭并恢复"; save.Text="备份存档"; refresh.Text="重新校验";
        layout.Controls.Add(actions,0,4);
        layout.Controls.Add(new Label {Dock=DockStyle.Fill,Text="切换前请退出游戏，随后重新启动"+Environment.NewLine+"仅更改灵火精华地点的地图传送开关；实际落点尚待游戏内验证",TextAlign=ContentAlignment.MiddleLeft,ForeColor=Color.FromArgb(240,199,120)},0,5);
        log.Multiline=true; log.ReadOnly=true; log.ScrollBars=ScrollBars.Vertical; log.Dock=DockStyle.Fill; log.BackColor=Color.FromArgb(17,21,28); log.ForeColor=ForeColor; log.BorderStyle=BorderStyle.FixedSingle;
        layout.Controls.Add(log,0,6);
        layout.Controls.Add(new Label {Text="适配：Build 25988036 · 原存档只复制，不自动恢复",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Color.Silver},0,7); Controls.Add(layout);
        service.Log=AddLog;
        enable.Click+=async delegate {await Run(delegate {service.Toggle(true);});};
        disable.Click+=async delegate {await Run(delegate {service.Toggle(false);});};
        save.Click+=async delegate {await Run(delegate {service.BackupSaves();});};
        refresh.Click+=async delegate {await Run(delegate {});};
        Shown+=async delegate {await Run(delegate {});};
        timer.Interval=1000; timer.Tick+=delegate {RefreshConnection();}; timer.Start();
        FormClosing+=delegate(object sender,FormClosingEventArgs e) {if(busy){e.Cancel=true; AddLog("正在完成文件操作，请等待校验结束");}};
        FormClosed+=delegate {timer.Stop(); timer.Dispose();};
        AddLog("游戏目录："+service.Game); AddLog("存档目录："+service.Save);
    }
    private void AddLog(string text) {
        // 把后台操作消息调度到界面线程
        if(IsDisposed||Disposing)return;
        if(InvokeRequired){BeginInvoke(new Action<string>(AddLog),text); return;}
        log.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+text+Environment.NewLine);
    }
    private async Task Run(Action action) {
        // 完成操作后重新读取文件状态，避免虚假成功
        if(busy) return; busy=true; RefreshConnection(); state.Text="正在校验，请稍候…";
        try {verifiedState=await Task.Run(delegate {action(); return service.State();}); AddLog("资源状态："+verifiedState);}
        catch(Exception ex) {verifiedState="操作未完成 · 请查看记录"; AddLog(ex.Message);}
        finally {busy=false; if(!IsDisposed&&!Disposing){state.Text="传送状态："+verifiedState; backup.Text="最近备份："+service.LastBackup; RefreshConnection();}}
    }
    private void RefreshConnection() {
        // 自动发现进程，游戏运行时锁定操作
        connection.Text=service.Connection();
        var processes=service.GameProcesses(); bool running=processes.Length>0; foreach(var p in processes)p.Dispose();
        enable.Enabled=!busy&&!running&&verifiedState=="已关闭";
        disable.Enabled=!busy&&!running&&(verifiedState.StartsWith("已启用")||verifiedState=="恢复待处理");
        save.Enabled=!busy&&!running; refresh.Enabled=!busy;
    }
}
}
