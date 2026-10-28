using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Globalization;
using System.Collections.Generic;

namespace EnshroudedTravel {
public sealed class LocationProgress {
    public string Connection, Message;
    public long Completed, Total, Unreadable;
    public double Seconds;
    public bool Scanning;
}
public sealed partial class RuntimeTravelService : IDisposable {
    private const string RegistryHash = "da6cb531da4c25df7f0bb03ee316018b9cf5356afa09220f3fcce6032c9c7a53";
    private readonly TravelService files;
    private readonly byte[] original;
    private readonly int[] travelOffsets;
    private Process process;
    private IntPtr handle;
    private long address, logPosition;
    private string logTail = "";
    private int attemptedPid;
    private long attemptedStart;
    private bool ownsChange;
    public bool Ready { get; private set; }
    public bool Enabled { get; private set; }
    public bool Running { get; private set; }
    public bool NeedsRestore { get { return ownsChange; } }
    public string Connection { get; private set; }
    public string Status { get; private set; }
    public Action<string> Log = delegate {};
    public IProgress<LocationProgress> Progress;
    public RuntimeTravelService(TravelService instance) {
        // 载入与当前版本绑定的完整注册表模板
        files=instance;
        original=LoadBundledTemplate("registry-original.bin",RegistryHash);
        travelOffsets=BuildTravelOffsets(original);
        zoomTemplate=LoadZoomTemplate();
        storageRecipeTemplate=LoadStorageRecipeTemplate();
        Connection="未连接 · 等待游戏启动"; Status="等待游戏启动"; DayNightStatus="等待游戏启动"; DamageBoostStatus="等待游戏启动";
    }
    public void Poll(bool retry,CancellationToken cancel) {
        // 同一进程复用已验证地址，新进程必须重新定位
        if(retry && process!=null && !Ready && !ownsChange) {ReleaseExited();attemptedPid=0;}
        if(process!=null && !process.HasExited) {
            Running=true;
            if(ownsChange && SessionEnded()) { Toggle(false); Log("离开世界，已关闭传送"); }
            try { Enabled=ReadState(); Ready=true; }
            catch { Ready=false; Status="目标数据已变化 · 请查看记录"; throw; }
            RefreshZoomState();
            RefreshShroudTravelState();
            Status=Enabled?"已启用 · 多类目标可传送":"已关闭 · 可直接启用";
            RefreshDayNightState();
            RefreshDamageBoostState();
            return;
        }
        ReleaseExited();
        var candidates=Process.GetProcessesByName("enshrouded");
        try {
            Running=candidates.Length>0;
            if(!Running) { attemptedPid=0; Status="等待游戏启动"; Connection="未连接 · 等待游戏启动"; return; }
            if(candidates.Length!=1) throw new IOException("MULTIPLE_GAMES：检测到多个游戏，请仅运行一个实例");
            var candidate=candidates[0]; long started=candidate.StartTime.ToUniversalTime().Ticks;
            Connection="已发现 · PID "+candidate.Id;
            if(!retry && candidate.Id==attemptedPid && started==attemptedStart) return;
            attemptedPid=candidate.Id; attemptedStart=started;
            Report("已发现游戏，正在校验版本");
            string path=candidate.MainModule.FileName;
            if(!string.Equals(Path.GetFullPath(path),Path.Combine(files.Game,"enshrouded.exe"),StringComparison.OrdinalIgnoreCase)) throw new IOException("GAME_PATH：游戏运行目录与配置不一致");
            if(TravelService.HashFile(path)!=TravelService.ExeHash) throw new IOException("VERSION_MISMATCH：当前游戏版本尚未适配");
            if(files.State()!="已关闭") throw new IOException("DISK_PATCH：磁盘资源不是原版，请退出游戏后恢复旧补丁");
            process=Process.GetProcessById(candidate.Id);
            if(process.StartTime.ToUniversalTime().Ticks!=started) throw new IOException("PROCESS_CHANGED：游戏已经重新启动");
            handle=Native.OpenProcess(0x410,false,process.Id);
            if(handle==IntPtr.Zero) throw Native.Error("PROCESS_ACCESS：无法读取游戏进程");
            Status="正在定位传送数据"; Log("正在定位传送数据");
            var watch=Stopwatch.StartNew();
            address=FindRegistry(cancel);
            if(address==0) throw new IOException("REGISTRY_MISSING：尚未找到传送数据，进入世界后点击启用全部");
            Enabled=ReadState(); ownsChange=false; Ready=true;
            RefreshZoomState();
            RefreshShroudTravelState();
            RefreshDayNightState();
            RefreshDamageBoostState();
            ResetLog();
            Connection="已连接 · PID "+process.Id+" · 支持运行中切换";
            Status=Enabled?"已启用 · 多类目标传送":"已关闭 · 可直接启用";
            Log("已核验完整注册表，定位耗时 "+watch.Elapsed.TotalSeconds.ToString("0.00")+" 秒");
            Log("已加载 "+travelOffsets.Length+" 类地图传送目标");
            if(ZoomReady)Log("已定位地图缩放数据，可扩展为五档缩放");
            else if(!string.IsNullOrEmpty(ZoomStatus))Log(ZoomStatus);
            if(ShroudTravelReady)Log("已定位瘴气传送限制");
            else if(!string.IsNullOrEmpty(ShroudTravelStatus))Log(ShroudTravelStatus);
            if(DayNightReady)Log("已定位昼夜时间，可使用白天与黑夜调整");
            else if(!string.IsNullOrEmpty(DayNightStatus))Log(DayNightStatus);
            if(DamageBoostReady)Log("已定位命中伤害结算，开启后将记录实际触发数值");
            else if(!string.IsNullOrEmpty(DamageBoostStatus))Log(DamageBoostStatus);
        } catch(OperationCanceledException) {
            if(!ownsChange)ReleaseExited();
            Ready=false;Status="定位已取消 · 正在等待自动重试";throw;
        } catch {
            if(!ownsChange) ReleaseExited();
            Ready=false; Status="连接未完成 · 请查看记录"; throw;
        } finally { foreach(var p in candidates)p.Dispose(); }
    }
    private long FindRegistry(CancellationToken cancel) {
        // 同次游戏缓存仅作为候选地址，始终复核完整注册表
        cancel.ThrowIfCancellationRequested();Report("正在核对本次游戏的定位缓存");
        string cache=Path.Combine(files.Root,"runtime-location.txt");
        LoadZoomCache();
        long found=0;
        if(File.Exists(cache)) {
            string[] fields=File.ReadAllLines(cache);long cached,start;int pid;
            if(fields.Length==4 && fields[0]==TravelService.ExeHash && int.TryParse(fields[1],out pid) && long.TryParse(fields[2],out start) && long.TryParse(fields[3],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out cached) && cached>0 && pid==process.Id && start==process.StartTime.ToUniversalTime().Ticks) {
                byte[] existing=ReadAt(cached);
                if(existing!=null && MatchesKnownTravelState(existing)){
                    found=cached;
                    Log("本次游戏传送地址复核通过");
                    if(zoomAddresses.Count!=0){Log("本次游戏地图缩放地址复核通过");return cached;}
                    Log("传送缓存已复用，继续定位地图缩放数据");
                }
            }
        }
        // 先核验完整只读资源区，缺失时才搜索已观察到临时资源副本的私有读写区
        const int anchor=1960,width=32;
        byte[] pattern=original.Skip(anchor).Take(width).ToArray();
        int[] skip=Enumerable.Repeat(width,256).ToArray();
        for(int i=0;i<width-1;i++)skip[pattern[i]]=width-1-i;
        byte[] zoomAnchor=BuildZoomAnchor();
        int[] zoomSkip=Enumerable.Repeat(zoomAnchor.Length,256).ToArray();
        for(int i=0;i<zoomAnchor.Length-1;i++)zoomSkip[zoomAnchor[i]]=zoomAnchor.Length-1-i;
        bool searchZoom=zoomAddresses.Count==0,searchTravel=found==0;
        byte[] buffer=new byte[8*1024*1024]; long cursor=0;
        var regions=new List<Native.Region>();long total=0,done=0,unreadable=0;
        var writableRegions=new List<Native.Region>();long writableTotal=0;
        Report("正在统计私有资源内存区域");
        Native.Region region;
        while(Native.VirtualQueryEx(handle,new IntPtr(cursor),out region,(UIntPtr)Marshal.SizeOf(typeof(Native.Region)))!=UIntPtr.Zero) {
            cancel.ThrowIfCancellationRequested();
            long start=region.BaseAddress.ToInt64(),length=checked((long)region.RegionSize.ToUInt64());
            if(length<=0 || start>long.MaxValue-length) break;
            long end=start+length; uint access=region.Protect&255;
            if(region.State==0x1000 && region.Type==0x20000 && (region.Protect&0x100)==0
                && (access==2||access==4||access==8)) {
                if(access==2){regions.Add(region);total+=length;}
                else {writableRegions.Add(region);writableTotal+=length;}
            }
            cursor=end;
        }
        var elapsed=Stopwatch.StartNew();long lastReport=-250;
        Log("优先核验只读资源区 "+(total/1048576.0).ToString("0.0")+" MB；私有读写区 "+(writableTotal/1048576.0).ToString("0.0")+" MB 仅在资源缺失时搜索");
        for(int phase=0;phase<2;phase++) {
        if(phase==1) {
            searchTravel=found==0;searchZoom=zoomAddresses.Count==0;
            if(!searchTravel && !searchZoom)break;
            regions=writableRegions;total=writableTotal;done=0;
            Log("只读区缺少"+(searchTravel?"传送":"")+(searchZoom?"地图缩放":"")+"资源，继续核验私有读写区");
        }
        foreach(var selected in regions) {
                long start=selected.BaseAddress.ToInt64(),end=start+checked((long)selected.RegionSize.ToUInt64());
                for(long pos=start;pos<end;) {
                    cancel.ThrowIfCancellationRequested();
                    int count=(int)Math.Min(buffer.Length,end-pos); UIntPtr read;
                    if(Native.ReadProcessMemory(handle,new IntPtr(pos),buffer,(UIntPtr)count,out read)) {
                        int available=(int)read.ToUInt64();
                        for(int i=0;searchTravel && i<=available-width;) {
                            int j=width-1;while(j>=0 && buffer[i+j]==pattern[j])j--;
                            if(j<0 && pos+i>=anchor) {
                                long candidate=pos+i-anchor;byte[] data=ReadAt(candidate);
                                if(data!=null && MatchesKnownTravelState(data)) {
                                    if(found!=0 && found!=candidate)throw new IOException("REGISTRY_AMBIGUOUS：发现多个注册表，未修改内存");
                                    found=candidate;
                                }
                            }
                            i+=skip[buffer[i+width-1]];
                        }
                        if(searchZoom)SearchZoomBuffer(buffer,available,pos,zoomAnchor,zoomSkip);
                    } else {unreadable+=count;}
                    long step=pos+count<end?Math.Max(1,count-width+1):count;
                    pos+=step;done+=step;
                    if(elapsed.ElapsedMilliseconds-lastReport>=250 || done==total) {
                        lastReport=elapsed.ElapsedMilliseconds;
                        string targets=searchTravel?(searchZoom?"传送与地图缩放":"传送"):"地图缩放";
                        Report("正在核验"+(phase==0?"只读资源区":"私有读写区")+" · "+targets,done,total,unreadable,elapsed.Elapsed.TotalSeconds,true);
                    }
                }
        }
        }
        cancel.ThrowIfCancellationRequested();
        if(unreadable>0)Log("扫描期间有 "+(unreadable/1048576.0).ToString("0.0")+" MB 内存变为不可读");
        Report("扫描完成，正在核验定位结果",done,total,unreadable,elapsed.Elapsed.TotalSeconds,false);
        if(found!=0)File.WriteAllLines(cache,new[]{TravelService.ExeHash,process.Id.ToString(),process.StartTime.ToUniversalTime().Ticks.ToString(),found.ToString("X")});
        if(zoomAddresses.Count!=0){
            RefreshZoomState();if(ZoomReady)WriteZoomCache();
        } else ZoomStatus="尚未找到地图缩放 · 打开地图后重新打开修改器";
        return found;
    }
    private void Report(string message,long done=0,long total=0,long unreadable=0,double seconds=0,bool scanning=false) {
        // 发布实际搜索字节与耗时，不使用按时间递增的虚假进度
        if(Progress!=null)Progress.Report(new LocationProgress {Connection=Connection,Message=message,Completed=done,Total=total,Unreadable=unreadable,Seconds=seconds,Scanning=scanning});
    }
    private byte[] ReadAt(long location) {
        // 严格读取一份完整注册表，不用部分数据推断成功
        return ReadAt(location,original.Length);
    }
    private byte[] ReadAt(long location, int length) {
        // 按调用方要求的完整长度读取，短读视为失败
        byte[] data=new byte[length]; UIntPtr read;
        return Native.ReadProcessMemory(handle,new IntPtr(location),data,(UIntPtr)data.Length,out read) && read.ToUInt64()==(ulong)data.Length?data:null;
    }
    private static void WriteProtected(IntPtr writer,long location,byte[] value,uint protection) {
        // 临时修改目标页权限后完整写入，并在任何结果下恢复原权限
        uint old;
        IntPtr target=new IntPtr(location);
        if(!Native.VirtualProtectEx(writer,target,(UIntPtr)value.Length,protection,out old))throw Native.Error("PAGE_ACCESS：无法修改目标页权限");
        try {
            UIntPtr written;
            if(!Native.WriteProcessMemory(writer,target,value,(UIntPtr)value.Length,out written) || written.ToUInt64()!=(ulong)value.Length)
                throw Native.Error("WRITE_FAILED：目标页写入失败");
        } finally {
            uint ignored;
            if(!Native.VirtualProtectEx(writer,target,(UIntPtr)value.Length,old,out ignored))throw Native.Error("PROTECTION_RESTORE：恢复目标页权限失败");
        }
    }
    private bool ReadState() {
        // 每次点击和状态轮询都重新检查全部目标标记
        if(process==null || process.HasExited) throw new IOException("GAME_EXITED：游戏已退出");
        byte[] data=ReadAt(address);
        if(data==null)throw new IOException("REGISTRY_CHANGED：目标数据已变化，未继续写入");
        if(MatchesTravelState(data,true))return true;
        if(MatchesTravelState(data,false))return false;
        throw new IOException("REGISTRY_CHANGED：目标数据已变化，未继续写入");
    }
    public void Toggle(bool enable) {
        // 逐项切换目标标记的传送字段，并核验完整注册表
        bool current=ReadState();
        if(current==enable) { Enabled=current; if(!enable)ownsChange=false; return; }
        IntPtr writer=Native.OpenProcess(0x438,false,process.Id);
        if(writer==IntPtr.Zero)throw Native.Error("WRITE_ACCESS：无法修改游戏内存");
        try {
            if(process.HasExited || ReadState()!=current)throw new IOException("PROCESS_CHANGED：操作前游戏状态已变化");
            ownsChange=true;
            foreach(int offset in travelOffsets) {
                byte value=enable?(byte)1:original[offset];
                WriteProtected(writer,address+offset,new[]{value},4);
            }
            Enabled=ReadState();
            if(Enabled!=enable)throw new IOException("VERIFY_FAILED：开关回读不一致");
            ownsChange=Enabled; Ready=true;
            Status=Enabled?"已启用 · 多类目标传送":"已关闭 · 已恢复";
            if(enable)ResetLog();
            Log(enable?"已启用灵火神殿、宝石锻炉、远古尖塔、地点和任务感叹号传送":"已关闭，完整注册表已恢复");
        } finally { Native.CloseHandle(writer); }
    }
    private void ResetLog() {
        // 从启用时开始跟踪已确认的世界卸载日志
        logPosition=new FileInfo(Path.Combine(files.Game,"enshrouded.log")).Length; logTail="";
    }
    private bool SessionEnded() {
        // 退出世界时取消开关，不把启用状态带入下个世界
        using(var stream=new FileStream(Path.Combine(files.Game,"enshrouded.log"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
            if(stream.Length<logPosition)return true;
            stream.Position=logPosition; byte[] data=new byte[Math.Min(65536,stream.Length-logPosition)];
            int n=stream.Read(data,0,data.Length);logPosition+=n;
            string text=logTail+System.Text.Encoding.UTF8.GetString(data,0,n);
            bool ended=text.Contains("[play] start destruction step");
            logTail=text.Substring(Math.Max(0,text.Length-100));return ended;
        }
    }
    public void Restore() {
        // 两项修改分别尝试恢复，任一项失败都必须交给界面显示
        var errors=new List<Exception>();
        if(process!=null && !process.HasExited && ownsChange) {
            try {Toggle(false);} catch(Exception ex) {errors.Add(ex);}
        }
        if(process!=null && !process.HasExited && ownsZoomChange) {
            try {ToggleZoom(false);} catch(Exception ex) {errors.Add(ex);}
        }
        if(process!=null && !process.HasExited && ownsShroudTravelChange) {
            try {ToggleShroudTravel(false);} catch(Exception ex) {errors.Add(ex);}
        }
        if(process!=null && !process.HasExited && ownsStorageUpgradeChange) {
            try {ToggleStorageUpgrade(false);} catch(Exception ex) {errors.Add(ex);}
        }
        if(process!=null && !process.HasExited && ownsDamageBoostChange) {
            try {ToggleDamageBoost(false);} catch(Exception ex) {errors.Add(ex);}
        }
        if(errors.Count==1)throw errors[0];
        if(errors.Count>1)throw new AggregateException("MEMORY_RESTORE：多项内存修改均未完全恢复",errors);
        if(process==null || process.HasExited)ReleaseExited();
    }
    private void ReleaseExited() {
        // 释放旧进程句柄，绝不向新进程沿用旧地址
        if(handle!=IntPtr.Zero)Native.CloseHandle(handle); handle=IntPtr.Zero;
        if(process!=null)process.Dispose();process=null;address=0;Ready=false;Enabled=false;ownsChange=false;
        ReleaseZoom();
        ReleaseShroudTravel();
        ReleaseStorageUpgrade();
        ReleaseDayNight();
        ReleaseDamageBoost();
    }
    public void Dispose() {
        // 恢复失败必须交给界面显示，不能静默退出
        Restore();ReleaseExited();
    }
    private static class Native {
        [StructLayout(LayoutKind.Sequential)] internal struct Region {
            public IntPtr BaseAddress,AllocationBase;public uint AllocationProtect;public ushort PartitionId;
            public UIntPtr RegionSize;public uint State,Protect,Type;
        }
        [DllImport("kernel32.dll",SetLastError=true)] internal static extern IntPtr OpenProcess(uint access,bool inherit,int id);
        [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool ReadProcessMemory(IntPtr process,IntPtr address,byte[] buffer,UIntPtr count,out UIntPtr read);
        [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool WriteProcessMemory(IntPtr process,IntPtr address,byte[] buffer,UIntPtr count,out UIntPtr written);
        [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool VirtualProtectEx(IntPtr process,IntPtr address,UIntPtr count,uint protection,out uint old);
        [DllImport("kernel32.dll",SetLastError=true)] internal static extern UIntPtr VirtualQueryEx(IntPtr process,IntPtr address,out Region info,UIntPtr size);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
        internal static Exception Error(string message) {
            // 保留系统错误码供诊断
            return new IOException(message+"（系统错误 "+Marshal.GetLastWin32Error()+"）");
        }
    }
}
}
