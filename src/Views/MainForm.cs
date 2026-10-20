using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
namespace EnshroudedTravel {
public sealed class MainForm : Form {
    private readonly TravelService service;
    private readonly RuntimeTravelService runtime;
    private readonly Label connection = new Label(), state = new Label(), backup = new Label();
    private readonly Button enable = new Button(), disable = new Button(), save = new Button(), refresh = new Button();
    private readonly TextBox log = new TextBox();
    private readonly Label location = new Label();
    private readonly ProgressBar progress = new ProgressBar();
    private readonly Button cancel = new Button();
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    private CancellationTokenSource cancellation = new CancellationTokenSource();
    private bool busy, closing, allowClose;
    private Task active = Task.FromResult(0);
    private string lastError = "";
    public MainForm(TravelService instance) {
        // 按自适应行列布局排列状态和按钮
        service=instance; runtime=new RuntimeTravelService(service); Text="灵火神殿传送助手 · 单人 · 运行中开关";
        Font=new Font("Microsoft YaHei UI",10F);
        BackColor=Color.FromArgb(24,29,37); ForeColor=Color.FromArgb(230,233,239);
        MinimumSize=new Size(740,650); Size=new Size(820,720); StartPosition=FormStartPosition.CenterScreen;
        var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=10,Padding=new Padding(24)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach(int height in new[]{48,42,42,60,54,62}) layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,36));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        layout.Controls.Add(new Label {Text="灵火神殿地图传送",Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,19,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft},0,0);
        int row=1; foreach(var label in new[]{connection,state,backup}) {label.Dock=DockStyle.Fill; label.TextAlign=ContentAlignment.MiddleLeft; label.AutoEllipsis=true; layout.Controls.Add(label,0,row++);}
        var actions=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=Padding.Empty};
        foreach(var b in new[]{enable,disable,save,refresh}) {actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25)); b.Dock=DockStyle.Fill; b.Margin=new Padding(0,4,10,4); b.FlatStyle=FlatStyle.Flat; b.BackColor=Color.FromArgb(49,61,77); b.ForeColor=ForeColor; actions.Controls.Add(b);}
        enable.Text="启用传送"; enable.BackColor=Color.FromArgb(21,103,91); disable.Text="关闭并恢复"; save.Text="备份存档"; refresh.Text="重新校验";
        layout.Controls.Add(actions,0,4);
        layout.Controls.Add(new Label {Dock=DockStyle.Fill,Text="无需退出游戏；切换后重新打开地图"+Environment.NewLine+"当前测试仅使用 backup 角色和世界；切回原存档前请关闭传送",TextAlign=ContentAlignment.MiddleLeft,ForeColor=Color.FromArgb(240,199,120)},0,5);
        log.Multiline=true; log.ReadOnly=true; log.ScrollBars=ScrollBars.Vertical; log.Dock=DockStyle.Fill; log.BackColor=Color.FromArgb(17,21,28); log.ForeColor=ForeColor; log.BorderStyle=BorderStyle.FixedSingle;
        location.Dock=DockStyle.Fill;location.TextAlign=ContentAlignment.MiddleLeft;location.AutoEllipsis=true;location.Text="等待游戏启动后自动寻找传送数据";
        layout.Controls.Add(location,0,6);
        var searchRow=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,120));
        progress.Dock=DockStyle.Fill;progress.Margin=new Padding(0,5,12,5);progress.Maximum=1000;
        cancel.Text="取消定位";cancel.Dock=DockStyle.Fill;cancel.FlatStyle=FlatStyle.Flat;cancel.BackColor=refresh.BackColor;cancel.Enabled=false;
        cancel.Click+=delegate {cancellation.Cancel();cancel.Enabled=false;location.Text="正在取消，当前读取结束后停止";};
        searchRow.Controls.Add(progress,0,0);searchRow.Controls.Add(cancel,1,0);layout.Controls.Add(searchRow,0,7);
        layout.Controls.Add(log,0,8);
        layout.Controls.Add(new Label {Text="适配：Build 25988036 · 仅修改内存 · 正常关闭助手时恢复",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Color.Silver},0,9); Controls.Add(layout);
        service.Log=AddLog; runtime.Log=AddLog;
        runtime.Progress=new Progress<LocationProgress>(ShowProgress);
        enable.Click+=async delegate {await Run(delegate {runtime.Toggle(true);},"正在启用传送");};
        disable.Click+=async delegate {await Run(delegate {runtime.Toggle(false);},"正在关闭传送");};
        save.Click+=async delegate {await Run(delegate {service.BackupSaves();},"正在备份存档");};
        refresh.Click+=async delegate {await Run(delegate {runtime.Poll(true,cancellation.Token);},"正在重新校验");};
        Shown+=async delegate {await Run(delegate {runtime.Poll(true,cancellation.Token);},"正在连接与定位"); if(!closing&&!IsDisposed)timer.Start();};
        timer.Interval=1000; timer.Tick+=async delegate {await Run(delegate {runtime.Poll(false,cancellation.Token);},"正在检查连接");};
        FormClosing+=async delegate(object sender,FormClosingEventArgs e) {
            if(allowClose)return; e.Cancel=true; if(closing)return;
            closing=true;timer.Stop();cancellation.Cancel();
            try {await active;}catch(Exception ex){AddLog("操作已结束："+ex.Message);}
            try {await Task.Run(delegate {runtime.Dispose();});allowClose=true;Close();}
            catch(Exception ex){closing=false;AddLog("关闭未完成："+ex.Message);MessageBox.Show(this,"恢复失败，请保留窗口并重试关闭："+ex.Message);}
        };
        FormClosed+=delegate {timer.Stop();timer.Dispose();cancellation.Dispose();};
        UpdateState();
        AddLog("游戏目录："+service.Game); AddLog("存档目录："+service.Save);
    }
    private void AddLog(string text) {
        // 把后台操作消息调度到界面线程
        if(IsDisposed||Disposing)return;
        if(InvokeRequired){BeginInvoke(new Action<string>(AddLog),text); return;}
        log.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+text+Environment.NewLine);
    }
    private async Task Run(Action action,string message) {
        // 串行执行内存操作，等待完成后统一更新按钮状态
        if(busy||closing)return;
        if(cancellation.IsCancellationRequested){cancellation.Dispose();cancellation=new CancellationTokenSource();}
        busy=true;UpdateState();
        if(!runtime.Ready)state.Text=message;
        active=Task.Run(action);
        try {await active;lastError="";}
        catch(OperationCanceledException){if(!closing)AddLog("定位已取消");}
        catch(Exception ex){if(lastError!=ex.Message)AddLog(ex.Message);lastError=ex.Message;}
        finally {
            busy=false;
            if(!IsDisposed&&!Disposing){
                cancel.Enabled=false;progress.Style=ProgressBarStyle.Continuous;
                if(runtime.Ready){progress.Value=1000;location.Text="定位完成，可以使用传送开关";}
                else {progress.Value=0;location.Text=runtime.Status;}
                UpdateState();
            }
        }
    }
    private void ShowProgress(LocationProgress update) {
        // 在界面线程显示真实扫描比例、字节量和经过时间
        if(!busy||closing||IsDisposed||Disposing||cancellation.IsCancellationRequested)return;
        connection.Text=update.Connection;state.Text="传送状态："+update.Message;
        cancel.Enabled=true;
        if(update.Total>0) {
            progress.Style=ProgressBarStyle.Continuous;
            progress.Value=(int)Math.Min(1000,update.Completed*1000.0/update.Total);
            location.Text=string.Format("{0} · {1:0.0}% · {2:0.00}/{3:0.00} GB · 已用 {4:0} 秒",update.Message,update.Completed*100.0/update.Total,update.Completed/1073741824.0,update.Total/1073741824.0,update.Seconds);
        } else {progress.Style=ProgressBarStyle.Marquee;location.Text=update.Message;}
    }
    private void UpdateState() {
        // 游戏运行时允许内存开关，完整存档备份仍要求退出
        connection.Text=runtime.Connection;state.Text="传送状态："+runtime.Status;
        backup.Text="最近备份："+service.LastBackup;
        enable.Enabled=!busy&&!closing&&runtime.Ready&&!runtime.Enabled;
        disable.Enabled=!busy&&!closing&&(runtime.Enabled||runtime.NeedsRestore);
        save.Enabled=!busy&&!closing&&!runtime.Running;refresh.Enabled=!busy&&!closing;
    }
}
}
