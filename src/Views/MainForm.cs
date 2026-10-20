using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
namespace EnshroudedTravel {
public sealed class MainForm : Form {
    private readonly TravelService service;
    private readonly RuntimeTravelService runtime;
    private readonly Label connection = new Label(), backup = new Label();
    private readonly Button enable = new Button(), zoom = new Button(), shroud = new Button(), save = new Button(), refresh = new Button();
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
        service=instance; runtime=new RuntimeTravelService(service); Text="雾锁王国修改器";
        Font=new Font("Microsoft YaHei UI",10F);
        BackColor=Color.FromArgb(24,29,37); ForeColor=Color.FromArgb(230,233,239);
        AutoScaleMode=AutoScaleMode.None; AutoSize=false; ClientSize=new Size(804,681);
        FormBorderStyle=FormBorderStyle.FixedSingle; MaximizeBox=false; StartPosition=FormStartPosition.CenterScreen;
        var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=8,Padding=new Padding(24),AutoSize=false};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach(int height in new[]{48,42,60,108,36,36}) layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        layout.Controls.Add(new Label {Text="雾锁王国修改器",Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,19,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft},0,0);
        int row=1; foreach(var label in new[]{connection,backup}) {label.Dock=DockStyle.Fill; label.TextAlign=ContentAlignment.MiddleLeft; label.AutoEllipsis=true; layout.Controls.Add(label,0,row++);}
        var actions=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=4,RowCount=2,Margin=Padding.Empty};
        // 三个按钮列保持固定宽度，末列保留右侧空白
        for(int i=0;i<3;i++)actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,189));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<2;i++)actions.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var buttons=new[]{save,refresh,enable,zoom,shroud};
        var cells=new[]{0,0,1,0,0,1,1,1,2,1};
        for(int i=0;i<buttons.Length;i++){var b=buttons[i]; b.Dock=DockStyle.Fill; b.Margin=new Padding(0,4,10,4); b.FlatStyle=FlatStyle.Flat; b.BackColor=Color.FromArgb(49,61,77); b.ForeColor=ForeColor; actions.Controls.Add(b,cells[i*2],cells[i*2+1]);}
        enable.Text="启用传送"; enable.BackColor=Color.FromArgb(21,103,91); save.Text="备份存档"; refresh.Text="校验状态"; zoom.Text="开启更小缩放"; shroud.Text="允许瘴气中传送";
        layout.Controls.Add(actions,0,3);
        log.Multiline=true; log.ReadOnly=true; log.ScrollBars=ScrollBars.Vertical; log.Dock=DockStyle.Fill; log.BackColor=Color.FromArgb(17,21,28); log.ForeColor=ForeColor; log.BorderStyle=BorderStyle.FixedSingle;
        location.Dock=DockStyle.Fill;location.TextAlign=ContentAlignment.MiddleLeft;location.AutoEllipsis=true;location.Text="等待游戏启动后自动寻找传送数据";
        layout.Controls.Add(location,0,4);
        var searchRow=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,120));
        progress.Dock=DockStyle.Fill;progress.Margin=new Padding(0,5,12,5);progress.Maximum=1000;
        cancel.Text="取消定位";cancel.Dock=DockStyle.Fill;cancel.FlatStyle=FlatStyle.Flat;cancel.BackColor=refresh.BackColor;cancel.Enabled=false;
        cancel.Click+=delegate {cancellation.Cancel();cancel.Enabled=false;location.Text="正在取消，当前读取结束后停止";};
        searchRow.Controls.Add(progress,0,0);searchRow.Controls.Add(cancel,1,0);layout.Controls.Add(searchRow,0,5);
        layout.Controls.Add(log,0,6);
        layout.Controls.Add(new Label {Text="适配：Build 25988036 · 仅修改内存 · 正常关闭助手时恢复",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Color.Silver},0,7); Controls.Add(layout);
        service.Log=AddLog; runtime.Log=AddLog;
        runtime.Progress=new Progress<LocationProgress>(ShowProgress);
        enable.Click+=async delegate {
            bool turnOn=!(runtime.Enabled||runtime.NeedsRestore);
            await Run(delegate {runtime.Toggle(turnOn);},turnOn?"正在启用传送":"正在关闭传送");
        };
        zoom.Click+=async delegate {
            bool turnOn=!runtime.ZoomEnabled;
            await Run(delegate {runtime.ToggleZoom(turnOn);},turnOn?"正在开启更小缩放":"正在关闭更小缩放");
        };
        shroud.Click+=async delegate {
            bool turnOn=!runtime.ShroudTravelEnabled;
            await Run(delegate {runtime.ToggleShroudTravel(turnOn);},turnOn?"正在允许瘴气中传送":"正在恢复瘴气传送限制");
        };
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
        if(!runtime.Ready)location.Text=message;
        active=Task.Run(action);
        try {await active;lastError="";}
        catch(OperationCanceledException){if(!closing)AddLog("定位已取消");}
        catch(Exception ex){if(lastError!=ex.Message)AddLog(ex.Message);lastError=ex.Message;}
        finally {
            busy=false;
            if(!IsDisposed&&!Disposing){
                cancel.Enabled=false;progress.Style=ProgressBarStyle.Continuous;
                if(runtime.Ready&&runtime.ZoomReady&&runtime.ShroudTravelReady){progress.Value=1000;location.Text="定位完成，可以使用传送、缩放与瘴气传送开关";}
                else if(runtime.Ready&&runtime.ZoomReady){progress.Value=1000;location.Text="传送与缩放已就绪 · "+runtime.ShroudTravelStatus;}
                else if(runtime.Ready){progress.Value=1000;location.Text=string.IsNullOrEmpty(runtime.ZoomStatus)?"传送已就绪，地图缩放尚未就绪":"传送已就绪 · "+runtime.ZoomStatus;}
                else {progress.Value=0;location.Text=runtime.Status;}
                UpdateState();
            }
        }
    }
    private void ShowProgress(LocationProgress update) {
        // 在界面线程显示真实扫描比例、字节量和经过时间
        if(!busy||closing||IsDisposed||Disposing||cancellation.IsCancellationRequested)return;
        connection.Text=ConnectionText(update.Connection);
        cancel.Enabled=true;
        if(update.Total>0) {
            progress.Style=ProgressBarStyle.Continuous;
            progress.Value=(int)Math.Min(1000,update.Completed*1000.0/update.Total);
            location.Text=string.Format("{0} · {1:0.0}% · {2:0.00}/{3:0.00} GB · 已用 {4:0} 秒",update.Message,update.Completed*100.0/update.Total,update.Completed/1073741824.0,update.Total/1073741824.0,update.Seconds);
        } else {progress.Style=ProgressBarStyle.Marquee;location.Text=update.Message;}
    }
    private static string ConnectionText(string value) {
        // 仅精简连接展示，不改变服务状态
        return value.Replace(" · 支持运行中切换","");
    }
    private void UpdateState() {
        // 游戏运行时允许内存开关，完整存档备份仍要求退出
        connection.Text=ConnectionText(runtime.Connection);
        backup.Text="最近备份："+service.LastBackup;
        bool canClose=runtime.Enabled||runtime.NeedsRestore;
        enable.Text=canClose?"关闭传送":"启用传送";
        enable.Enabled=!busy&&!closing&&(runtime.Ready||canClose);
        zoom.Text=runtime.ZoomEnabled?"关闭更小缩放":"开启更小缩放";
        zoom.BackColor=runtime.ZoomEnabled?Color.FromArgb(21,103,91):Color.FromArgb(49,61,77);
        zoom.Enabled=!busy&&!closing&&runtime.ZoomReady;
        shroud.Text=runtime.ShroudTravelEnabled?"恢复瘴气限制":"允许瘴气中传送";
        shroud.BackColor=runtime.ShroudTravelEnabled?Color.FromArgb(21,103,91):Color.FromArgb(49,61,77);
        shroud.Enabled=!busy&&!closing&&(runtime.ShroudTravelReady||runtime.ShroudTravelNeedsRestore);
        save.Enabled=!busy&&!closing&&!runtime.Running;refresh.Enabled=!busy&&!closing;
    }
}
}
