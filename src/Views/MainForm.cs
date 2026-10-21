using System;
using System.Drawing;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
namespace EnshroudedTravel {
public sealed class MainForm : Form {
    private static readonly Color ButtonBackColor = Color.FromArgb(49,61,77);
    private static readonly Color ButtonActiveBackColor = Color.FromArgb(21,103,91);
    private static readonly Color ButtonTextColor = Color.FromArgb(230,233,239);
    private static readonly Color ButtonBorderColor = Color.FromArgb(83,97,114);
    private readonly TravelService service;
    private readonly RuntimeTravelService runtime;
    private readonly Label connection = new Label(), backup = new Label();
    private readonly Button enable = new Button(), zoom = new Button(), shroud = new Button(), storage = new Button(), save = new Button(), enableAll = new Button();
    private readonly List<FeatureSwitch> featureSwitches = new List<FeatureSwitch>();
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
        // 四个按钮列保持固定宽度，完整使用操作区宽度
        for(int i=0;i<4;i++)actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,189));
        for(int i=0;i<2;i++)actions.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var buttons=new[]{enableAll,save,enable,zoom,shroud,storage};
        var cells=new[]{0,0,3,0,0,1,1,1,2,1,3,1};
        for(int i=0;i<buttons.Length;i++){var b=buttons[i]; b.Dock=DockStyle.Fill; b.Margin=new Padding(0,4,10,4); ApplyButtonStyle(b,false); actions.Controls.Add(b,cells[i*2],cells[i*2+1]);}
        enable.Text="启用传送"; save.Text="备份存档"; enableAll.Text="启用全部"; zoom.Text="开启更小缩放"; shroud.Text="允许瘴气中传送"; storage.Text="免费增大箱子";
        layout.Controls.Add(actions,0,3);
        log.Multiline=true; log.ReadOnly=true; log.ScrollBars=ScrollBars.Vertical; log.Dock=DockStyle.Fill; log.BackColor=Color.FromArgb(17,21,28); log.ForeColor=ForeColor; log.BorderStyle=BorderStyle.FixedSingle;
        location.Dock=DockStyle.Fill;location.TextAlign=ContentAlignment.MiddleLeft;location.AutoEllipsis=true;location.Text="等待游戏启动后自动寻找传送数据";
        layout.Controls.Add(location,0,4);
        var searchRow=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,120));
        progress.Dock=DockStyle.Fill;progress.Margin=new Padding(0,5,12,5);progress.Maximum=1000;
        cancel.Text="取消定位";cancel.Dock=DockStyle.Fill;ApplyButtonStyle(cancel,false);cancel.Enabled=false;
        cancel.Click+=delegate {cancellation.Cancel();cancel.Enabled=false;location.Text="正在取消，当前读取结束后停止";};
        searchRow.Controls.Add(progress,0,0);searchRow.Controls.Add(cancel,1,0);layout.Controls.Add(searchRow,0,5);
        layout.Controls.Add(log,0,6);
        layout.Controls.Add(new Label {Text="适配：Build 26082781 · 仅修改内存 · 正常关闭助手时恢复",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Color.Silver},0,7); Controls.Add(layout);
        service.Log=AddLog; runtime.Log=AddLog;
        runtime.Progress=new Progress<LocationProgress>(ShowProgress);
        RegisterFeature("传送", delegate {return runtime.Enabled||runtime.NeedsRestore;}, delegate {return runtime.Ready;}, delegate {runtime.Toggle(true);});
        RegisterFeature("更小缩放", delegate {return runtime.ZoomEnabled;}, delegate {return runtime.ZoomReady;}, delegate {runtime.ToggleZoom(true);});
        RegisterFeature("瘴气传送", delegate {return runtime.ShroudTravelEnabled;}, delegate {return runtime.ShroudTravelReady;}, delegate {runtime.ToggleShroudTravel(true);});
        RegisterFeature("免费增大箱子", delegate {return runtime.StorageUpgradeEnabled;}, delegate {return runtime.Ready;}, delegate {runtime.ToggleStorageUpgrade(true);});
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
        storage.Click+=async delegate {
            bool turnOn=!runtime.StorageUpgradeEnabled;
            await Run(delegate {runtime.ToggleStorageUpgrade(turnOn);},turnOn?"正在启用免费箱子升级":"正在关闭免费箱子升级");
        };
        save.Click+=async delegate {
            if(runtime.Running) {
                AddLog("备份存档已禁用：请先正常退出游戏，完全关闭后再备份");
                return;
            }
            if(busy) {
                AddLog("正在执行其他操作，请稍后再备份存档");
                return;
            }
            await Run(delegate {service.BackupSaves();},"正在备份存档");
        };
        enableAll.Click+=async delegate {await Run(EnableAllFeatures,"正在启用全部功能");};
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
    private void RegisterFeature(string name,Func<bool> enabled,Func<bool> ready,Action enable) {
        // 注册可由“启用全部”统一开启的功能
        featureSwitches.Add(new FeatureSwitch(name,enabled,ready,enable));
    }
    private void EnableAllFeatures() {
        // 依次启用所有已注册但尚未开启的功能
        if(!runtime.Ready)runtime.Poll(true,cancellation.Token);
        if(!runtime.Ready)throw new IOException("GAME_NOT_READY：请进入游戏世界后点击启用全部");
        var errors=new List<Exception>();
        int changed=0;
        foreach(var feature in featureSwitches) {
            if(feature.Enabled())continue;
            if(!feature.Ready()) {
                var error=new IOException(feature.Name+"尚未就绪");
                errors.Add(error);AddLog(error.Message);continue;
            }
            try {feature.Enable();changed++;}
            catch(Exception ex) {
                var error=new IOException(feature.Name+"启用失败："+ex.Message,ex);
                errors.Add(error);AddLog(error.Message);
            }
        }
        if(errors.Count==0) {
            AddLog(changed==0?"全部功能已经启用":"已启用全部功能");
            return;
        }
        if(errors.Count==1)throw errors[0];
        throw new AggregateException("ENABLE_ALL：部分功能启用失败",errors);
    }
    private sealed class FeatureSwitch {
        public readonly string Name;
        public readonly Func<bool> Enabled;
        public readonly Func<bool> Ready;
        public readonly Action Enable;
        public FeatureSwitch(string name,Func<bool> enabled,Func<bool> ready,Action enable) {
            Name=name;Enabled=enabled;Ready=ready;Enable=enable;
        }
    }
    private static void ApplyButtonStyle(Button button,bool active) {
        // 统一所有按钮的底色、文字、边框和交互状态
        Color background=active?ButtonActiveBackColor:ButtonBackColor;
        button.FlatStyle=FlatStyle.Flat;
        button.UseVisualStyleBackColor=false;
        button.BackColor=background;
        button.ForeColor=ButtonTextColor;
        button.TextAlign=ContentAlignment.MiddleCenter;
        button.FlatAppearance.BorderColor=ButtonBorderColor;
        button.FlatAppearance.BorderSize=1;
        button.FlatAppearance.MouseOverBackColor=ControlPaint.Light(background,0.12f);
        button.FlatAppearance.MouseDownBackColor=ControlPaint.Dark(background,0.12f);
    }
    private void UpdateState() {
        // 游戏运行时允许内存开关，完整存档备份仍要求退出
        connection.Text=ConnectionText(runtime.Connection);
        backup.Text="最近备份："+service.LastBackup;
        bool canClose=runtime.Enabled||runtime.NeedsRestore;
        enable.Text=canClose?"关闭传送":"启用传送";
        ApplyButtonStyle(enable,canClose);
        enable.Enabled=!busy&&!closing&&(runtime.Ready||canClose);
        zoom.Text=runtime.ZoomEnabled?"关闭更小缩放":"开启更小缩放";
        ApplyButtonStyle(zoom,runtime.ZoomEnabled);
        zoom.Enabled=!busy&&!closing&&runtime.ZoomReady;
        shroud.Text=runtime.ShroudTravelEnabled?"恢复瘴气限制":"允许瘴气中传送";
        ApplyButtonStyle(shroud,runtime.ShroudTravelEnabled);
        shroud.Enabled=!busy&&!closing&&(runtime.ShroudTravelReady||runtime.ShroudTravelNeedsRestore);
        storage.Text=runtime.StorageUpgradeEnabled?"关闭免费箱子升级":"免费增大箱子";
        ApplyButtonStyle(storage,runtime.StorageUpgradeEnabled);
        storage.Enabled=!busy&&!closing&&(runtime.Ready||runtime.StorageUpgradeNeedsRestore);
        save.Enabled=!closing;enableAll.Enabled=!busy&&!closing;
    }
}
}
