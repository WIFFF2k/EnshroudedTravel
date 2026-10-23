using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;

namespace EnshroudedTravel {
public sealed partial class RuntimeTravelService {
    private const int IngameTimeSize = 0x70;
    private const int IngameTimeCurrentOffset = 0x10;
    private const int IngameTimePreviousOffset = 0x18;
    private const int IngameTimeScaleOffset = 0x20;
    private const int IngameTimeDayDurationOffset = 0x28;
    private const int IngameTimeNightDurationOffset = 0x30;
    private const int IngameTimeDayBoundaryOffset = 0x38;
    private const int IngameTimeNightBoundaryOffset = 0x40;
    private const int IngameTimeFullDayOffset = 0x48;
    private const long IngameTimeFullDayTicks = 43200000000000L;
    private const string DayNightCacheVersion = "daynight-v1";
    private readonly List<long> dayNightAddresses = new List<long>();
    private int dayNightTimeOffset;
    private long dayNightDuration, nightDuration;
    public bool DayNightReady { get; private set; }
    public string DayNightStatus { get; private set; }

    private static bool MatchesIngameTime(byte[] data) {
        // 按完整结构核验昼夜时长和固定边界，排除相似数值片段
        if(data==null || data.Length!=IngameTimeSize)return false;
        float scale;
        long day,night,dayBoundary,nightBoundary,fullDay;
        try {
            scale=BitConverter.ToSingle(data,IngameTimeScaleOffset);
            day=BitConverter.ToInt64(data,IngameTimeDayDurationOffset);
            night=BitConverter.ToInt64(data,IngameTimeNightDurationOffset);
            dayBoundary=BitConverter.ToInt64(data,IngameTimeDayBoundaryOffset);
            nightBoundary=BitConverter.ToInt64(data,IngameTimeNightBoundaryOffset);
            fullDay=BitConverter.ToInt64(data,IngameTimeFullDayOffset);
        } catch(ArgumentException) { return false; }
        return scale>0 && scale<1000
            && day>0 && night>0
            && dayBoundary==day && nightBoundary==night
            && fullDay==IngameTimeFullDayTicks;
    }
    private bool ValidateIngameTime(long location) {
        // 完整读取单个时间对象，不允许短读或字段组合通过
        return MatchesIngameTime(ReadAt(location,IngameTimeSize));
    }
    private void LoadDayNightCache() {
        // 同进程缓存只作为候选，仍须通过完整结构核验
        if(process==null)return;
        string cache=Path.Combine(files.Root,"runtime-daynight-location.txt");
        if(!File.Exists(cache))return;
        string[] fields=File.ReadAllLines(cache);
        long start,address;
        int pid;
        if(fields.Length!=5 || fields[0]!=DayNightCacheVersion || fields[1]!=TravelService.ExeHash
            || !int.TryParse(fields[2],out pid) || !long.TryParse(fields[3],out start)
            || !long.TryParse(fields[4],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out address)
            || pid!=process.Id || start!=process.StartTime.ToUniversalTime().Ticks || address<=0)return;
        if(ValidateIngameTime(address)) {
            dayNightAddresses.Clear();dayNightAddresses.Add(address);
            DayNightStatus="昼夜时间地址复核通过";
        }
    }
    private void WriteDayNightCache() {
        // 缓存绑定版本、进程、启动时间和已核验地址
        if(dayNightAddresses.Count==0 || process==null)return;
        File.WriteAllLines(Path.Combine(files.Root,"runtime-daynight-location.txt"),new[]{
            DayNightCacheVersion,
            TravelService.ExeHash,
            process.Id.ToString(),
            process.StartTime.ToUniversalTime().Ticks.ToString(),
            dayNightAddresses[0].ToString("X")
        });
    }
    private void SearchDayNightBuffer(byte[] buffer,int available,long bufferAddress) {
        // 用固定的十二小时边界寻找游戏时间对象的主结构
        byte[] marker=BitConverter.GetBytes(IngameTimeFullDayTicks);
        int start=0;
        while(start<=available-marker.Length) {
            int found=Array.IndexOf<byte>(buffer,marker[0],start,available-start);
            if(found<0 || found+marker.Length>available)return;
            bool match=true;
            for(int i=1;i<marker.Length;i++) {
                if(buffer[found+i]!=marker[i]) {match=false;break;}
            }
            if(match && found>=IngameTimeFullDayOffset) {
                long candidate=bufferAddress+found-IngameTimeFullDayOffset;
                if(ValidateIngameTime(candidate) && !dayNightAddresses.Contains(candidate))
                    dayNightAddresses.Add(candidate);
            }
            start=found+1;
        }
    }
    private void LocateActiveDayNight() {
        // 通过短时间观察当前时间字段，排除只读配置和旧对象
        var beforeCurrent=new List<long>();
        var beforePrevious=new List<long>();
        for(int i=dayNightAddresses.Count-1;i>=0;i--) {
            byte[] data=ReadAt(dayNightAddresses[i],IngameTimeSize);
            if(!MatchesIngameTime(data)) {
                dayNightAddresses.RemoveAt(i);continue;
            }
            beforeCurrent.Add(BitConverter.ToInt64(data,IngameTimeCurrentOffset));
            beforePrevious.Add(BitConverter.ToInt64(data,IngameTimePreviousOffset));
        }
        beforeCurrent.Reverse();beforePrevious.Reverse();
        if(dayNightAddresses.Count==0)
            throw new IOException("DAY_NIGHT_NOT_FOUND：未找到昼夜时间对象，请进入世界后重新打开修改器");
        System.Threading.Thread.Sleep(1200);
        var active=new List<long>();
        int detectedOffset=0;
        long detectedDay=0,detectedNight=0;
        for(int i=0;i<dayNightAddresses.Count;i++) {
            byte[] data=ReadAt(dayNightAddresses[i],IngameTimeSize);
            if(!MatchesIngameTime(data))continue;
            long current=BitConverter.ToInt64(data,IngameTimeCurrentOffset);
            long previous=BitConverter.ToInt64(data,IngameTimePreviousOffset);
            bool currentChanged=current!=beforeCurrent[i] && Math.Abs(current-beforeCurrent[i])<60000000000L;
            bool previousChanged=previous!=beforePrevious[i] && Math.Abs(previous-beforePrevious[i])<60000000000L;
            if(currentChanged==previousChanged)continue;
            int offset=currentChanged?IngameTimeCurrentOffset:IngameTimePreviousOffset;
            if(detectedOffset!=0 && detectedOffset!=offset)
                throw new IOException("DAY_NIGHT_LAYOUT_CHANGED：昼夜时间字段不一致，未修改时间");
            long day=BitConverter.ToInt64(data,IngameTimeDayDurationOffset);
            long night=BitConverter.ToInt64(data,IngameTimeNightDurationOffset);
            if(detectedDay!=0 && (detectedDay!=day || detectedNight!=night))
                throw new IOException("DAY_NIGHT_LAYOUT_CHANGED：昼夜时长副本不一致，未修改时间");
            detectedDay=day;detectedNight=night;detectedOffset=offset;
            active.Add(dayNightAddresses[i]);
        }
        if(active.Count==0)
            throw new IOException("DAY_NIGHT_NOT_RUNNING：昼夜时间尚未推进，请关闭游戏菜单并保持世界运行后重试");
        dayNightAddresses.Clear();dayNightAddresses.AddRange(active);
        dayNightDuration=detectedDay;nightDuration=detectedNight;dayNightTimeOffset=detectedOffset;
        DayNightReady=true;
        DayNightStatus="已就绪 · 白天 "+FormatDuration(dayNightDuration)+" · 黑夜 "+FormatDuration(nightDuration);
        WriteDayNightCache();
        Log(DayNightStatus);
    }
    private static string FormatDuration(long ticks) {
        // 将纳秒时长转换为界面的分钟和秒
        long seconds=ticks/1000000000L;
        return (seconds/60)+"分"+(seconds%60).ToString("00")+"秒";
    }
    private void RefreshDayNightState() {
        // 定位成功后重新核验对象，不影响其他功能状态
        if(dayNightAddresses.Count==0) {
            DayNightReady=false;
            DayNightStatus="尚未找到昼夜时间";
            return;
        }
        try {
            foreach(long location in dayNightAddresses) {
                byte[] data=ReadAt(location,IngameTimeSize);
                if(!MatchesIngameTime(data))
                    throw new IOException("DAY_NIGHT_CHANGED：昼夜时间对象已变化");
            }
            if(!DayNightReady)LocateActiveDayNight();
        } catch(Exception ex) {
            DayNightReady=false;DayNightStatus="昼夜时间状态不可用";
            Log("昼夜时间读取失败："+ex.Message);
        }
    }
    private long ReadDayNightTime(long location) {
        // 严格读取当前游戏时间字段
        byte[] raw=ReadAt(location+dayNightTimeOffset,8);
        if(raw==null || raw.Length!=8)throw new IOException("DAY_NIGHT_UNREADABLE：无法读取当前游戏时间");
        return BitConverter.ToInt64(raw,0);
    }
    private void JumpDayNight(bool daytime) {
        // 将当前周期直接调整到白天或黑夜的中段，并核验写入结果
        if(!DayNightReady)throw new IOException("DAY_NIGHT_NOT_READY：昼夜时间尚未就绪");
        var previous=new List<long>();
        var targets=new List<long>();
        foreach(long location in dayNightAddresses) {
            long current=ReadDayNightTime(location);
            long cycle=dayNightDuration+nightDuration;
            if(cycle<=0)throw new IOException("DAY_NIGHT_CHANGED：昼夜周期无效");
            long cycleStart=(current/cycle)*cycle;
            long target=daytime?cycleStart+dayNightDuration/2:cycleStart+dayNightDuration+nightDuration/2;
            previous.Add(current);targets.Add(target);
        }
        IntPtr writer=Native.OpenProcess(0x438,false,process.Id);
        if(writer==IntPtr.Zero)throw Native.Error("WRITE_ACCESS：无法调整游戏时间");
        try {
            for(int i=0;i<dayNightAddresses.Count;i++)
                WriteProtected(writer,dayNightAddresses[i]+dayNightTimeOffset,BitConverter.GetBytes(targets[i]),4);
            System.Threading.Thread.Sleep(250);
            for(int i=0;i<dayNightAddresses.Count;i++) {
                long current=ReadDayNightTime(dayNightAddresses[i]);
                if(Math.Abs(current-targets[i])>15000000000L) {
                    for(int restore=0;restore<dayNightAddresses.Count;restore++)
                        WriteProtected(writer,dayNightAddresses[restore]+dayNightTimeOffset,BitConverter.GetBytes(previous[restore]),4);
                    throw new IOException("DAY_NIGHT_VERIFY_FAILED：游戏覆盖了时间写入，请重新定位");
                }
            }
            Log(daytime?"已将游戏时间调整到白天":"已将游戏时间调整到黑夜");
        } finally {Native.CloseHandle(writer);}
    }
    public void JumpToDay() {
        // 点击式调整到白天，不参与启用全部
        JumpDayNight(true);
    }
    public void JumpToNight() {
        // 点击式调整到黑夜，不参与启用全部
        JumpDayNight(false);
    }
    private void ReleaseDayNight() {
        // 游戏进程结束后清空昼夜时间定位状态
        dayNightAddresses.Clear();dayNightTimeOffset=0;dayNightDuration=0;nightDuration=0;
        DayNightReady=false;DayNightStatus="等待游戏启动";
    }
}
}
