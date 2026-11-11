using System;
using System.IO;
using System.Diagnostics;
using System.Threading;

namespace EnshroudedTravel {
public sealed partial class RuntimeTravelService {
    private const int DayNightRootRva = 0x2006CC0;
    private const int DayNightRootVtableRva = 0x1D68530;
    private const int DayNightSessionVtableRva = 0x1D68EA8;
    private const long ClockDay = 86400000000000L;
    public bool DayNightReady { get; private set; }
    public string DayNightStatus { get; private set; }

    private sealed class DayNightSnapshot {
        public long World, Address, Clock, Origin, Offset, Dawn, Dusk, Day, Night, Display;
        public float Scale;
        public uint Revision;
        public bool IsDay;
    }
    private byte[] ReadDayNightBytes(long location,int length) {
        // 定向读取已核实的字段，短读立即停止
        byte[] data=ReadAt(location,length);
        if(data==null)throw new IOException("DAY_NIGHT_UNREADABLE：无法读取昼夜数据，请等待世界加载完成");
        return data;
    }
    private long ReadDayNightPointer(long location) {
        // 空指针表示尚未进入世界，不搜索其他内存作为替代
        long value=BitConverter.ToInt64(ReadDayNightBytes(location,8),0);
        if(value==0)throw new IOException("DAY_NIGHT_NO_WORLD：请先进入游戏世界");
        if(value<0x10000 || value>=0x0000800000000000L || (value&7)!=0)
            throw new IOException("DAY_NIGHT_POINTER_CHANGED：昼夜对象入口已变化，请重新连接");
        return value;
    }
    private long ResolveDayNightWorld() {
        // 固定入口经过根对象、会话类型和世界自引用三重核验
        if(process==null || process.HasExited)throw new IOException("GAME_EXITED：游戏已退出");
        long module=process.MainModule.BaseAddress.ToInt64();
        long root=ReadDayNightPointer(module+DayNightRootRva);
        if(ReadDayNightPointer(root)!=module+DayNightRootVtableRva)
            throw new IOException("DAY_NIGHT_ROOT_CHANGED：当前游戏状态不支持昼夜调整");
        long session=ReadDayNightPointer(root+0x108);
        if(ReadDayNightPointer(session)!=module+DayNightSessionVtableRva)
            throw new IOException("DAY_NIGHT_SESSION_CHANGED：请进入本地单人世界后调整昼夜");
        long world=ReadDayNightPointer(session+0x1B8);
        if(ReadDayNightPointer(world+0x466A68)!=world+0x466A90)
            throw new IOException("DAY_NIGHT_WORLD_CHANGED：世界对象已经变化，请等待加载完成");
        if(BitConverter.ToInt32(ReadDayNightBytes(world+0x466B0C,4),0)!=12)
            throw new IOException("DAY_NIGHT_LOADING：世界正在加载，暂时不能调整昼夜");
        return world;
    }
    private DayNightSnapshot ReadDayNightSnapshot() {
        // 每次从根指针重取世界和时间对象，不缓存堆地址或猜测字段
        long world=ResolveDayNightWorld();
        long location=ReadDayNightPointer(world+0x4671E0);
        byte[] data=ReadDayNightBytes(location,0x70);
        var state=new DayNightSnapshot {
            World=world,Address=location,Clock=ReadDayNightPointer(world+0x1D0),
            Origin=BitConverter.ToInt64(data,0x10),Offset=BitConverter.ToInt64(data,0x18),
            Scale=BitConverter.ToSingle(data,0x20),Revision=BitConverter.ToUInt32(data,0x24),
            Dawn=BitConverter.ToInt64(data,0x28),Dusk=BitConverter.ToInt64(data,0x30),
            Day=BitConverter.ToInt64(data,0x38),Night=BitConverter.ToInt64(data,0x40),
            Display=BitConverter.ToInt64(data,0x48),IsDay=data[0x58]!=0
        };
        if(float.IsNaN(state.Scale) || float.IsInfinity(state.Scale) || state.Scale<0
            || state.Origin<0 || state.Offset<0 || state.Dawn<0 || state.Dusk<=state.Dawn
            || state.Dusk>=ClockDay || state.Day<=0 || state.Night<=0
            || state.Day>long.MaxValue-state.Night || state.Display<0 || state.Display>=ClockDay
            || data[0x58]>1 || data[0x59]!=0)
            throw new IOException("DAY_NIGHT_LAYOUT_CHANGED：昼夜字段不符合当前版本，未修改时间");
        if(ResolveDayNightWorld()!=world || ReadDayNightPointer(world+0x4671E0)!=location)
            throw new IOException("DAY_NIGHT_WORLD_CHANGED：世界正在切换，请稍后重试");
        return state;
    }
    private long ReadRawDayNightTime(DayNightSnapshot state) {
        // 复用游戏时钟算法：帧时长乘帧号，再应用时间基准、倍率与偏移
        byte[] clock=ReadDayNightBytes(state.Clock+0xE17E80,32);
        long step=BitConverter.ToInt64(clock,0);
        uint frame=BitConverter.ToUInt32(clock,28);
        if(step<=0 || frame==0)throw new IOException("DAY_NIGHT_CLOCK_NOT_READY：世界时钟尚未就绪");
        long elapsed=checked(step*frame-state.Origin);
        float seconds=(float)(elapsed*1e-9);
        long result=checked(state.Offset+(long)((double)(seconds*state.Scale)*1e9));
        if(result<0)throw new IOException("DAY_NIGHT_CLOCK_CHANGED：世界时钟发生变化，请稍后重试");
        return result;
    }
    private void SetDayNightStatus(bool ready,string message) {
        // 只在状态变化时记录，避免每秒重复输出同一个错误
        bool changed=DayNightReady!=ready || DayNightStatus!=message;
        DayNightReady=ready;DayNightStatus=message;
        if(changed)Log(message);
    }
    private void RefreshDayNightState() {
        // 进入世界后自动定位，卸载后自动禁用，不依赖传送冷扫描
        try {
            DayNightSnapshot state=ReadDayNightSnapshot();
            SetDayNightStatus(true,"昼夜已就绪 · 白天 "+FormatDayNightDuration(state.Day)+" · 黑夜 "+FormatDayNightDuration(state.Night));
        } catch(IOException ex) {SetDayNightStatus(false,ex.Message);}
    }
    private static string FormatDayNightDuration(long ticks) {
        // 将昼夜持续时长按分钟和秒显示
        long seconds=ticks/1000000000L;
        return (seconds/60)+"分"+(seconds%60).ToString("00")+"秒";
    }
    private static bool SameDayNightState(DayNightSnapshot before,DayNightSnapshot after) {
        // 写入前后确认世界身份和影响时间换算的配置没有变化
        return before.World==after.World && before.Address==after.Address && before.Clock==after.Clock
            && before.Origin==after.Origin && before.Scale==after.Scale
            && before.Dawn==after.Dawn && before.Dusk==after.Dusk
            && before.Day==after.Day && before.Night==after.Night;
    }
    private void JumpDayNight(bool daytime) {
        // 单次调整时间偏移并通知游戏刷新，保留基准时钟、倍率和昼夜设置
        DayNightSnapshot state=ReadDayNightSnapshot();
        long now=ReadRawDayNightTime(state);
        long cycle=checked(state.Day+state.Night);
        long nightSpan=ClockDay-(state.Dusk-state.Dawn);
        long dawnPhase=(long)((decimal)state.Night*state.Dawn/nightSpan);
        long phase=daytime?checked(dawnPhase+state.Day/2):checked(dawnPhase+state.Day+state.Night/2)%cycle;
        long target=checked(now-now%cycle+phase);
        // 当前周期早段可能需要负偏移，使用下一周期的同一时段保持合法偏移
        long offset=checked(state.Offset+target-now);
        if(offset<0)offset=checked(offset+cycle);
        long display=daytime?state.Dawn+(state.Dusk-state.Dawn)/2:(state.Dusk+nightSpan/2)%ClockDay;
        IntPtr writer=Native.OpenProcess(0x438,false,process.Id);
        if(writer==IntPtr.Zero)throw Native.Error("DAY_NIGHT_WRITE_ACCESS：无法调整游戏时间");
        try {
            DayNightSnapshot current=ReadDayNightSnapshot();
            if(!SameDayNightState(state,current) || current.Offset!=state.Offset || current.Revision!=state.Revision)
                throw new IOException("DAY_NIGHT_CONCURRENT_CHANGE：游戏正在更新时间，请再次点击");
            WriteProtected(writer,state.Address+0x18,BitConverter.GetBytes(offset),4);
            current=ReadDayNightSnapshot();
            if(!SameDayNightState(state,current) || current.Offset!=offset || current.Revision!=state.Revision)
                throw new IOException("DAY_NIGHT_CONCURRENT_CHANGE：写入期间游戏更新了时间，已停止后续操作");
            WriteProtected(writer,state.Address+0x24,BitConverter.GetBytes(unchecked(state.Revision+1)),4);
            var watch=Stopwatch.StartNew();
            while(watch.ElapsedMilliseconds<2000) {
                current=ReadDayNightSnapshot();
                if(!SameDayNightState(state,current) || current.Offset!=offset)
                    throw new IOException("DAY_NIGHT_OVERRIDDEN：游戏重新同步了时间，调整未完成，请稍后重试");
                long distance=Math.Abs(current.Display-display);
                distance=Math.Min(distance,ClockDay-distance);
                if(current.IsDay==daytime && distance<300000000000L) {
                    Log(daytime?"已调整到白天，游戏将继续正常计时":"已调整到黑夜，游戏将继续正常计时");
                    return;
                }
                Thread.Sleep(25);
            }
            throw new IOException("DAY_NIGHT_NOT_APPLIED：游戏尚未显示目标时段，请关闭暂停菜单后重试");
        } catch(IOException ex) {SetDayNightStatus(false,ex.Message);throw;}
        finally {Native.CloseHandle(writer);}
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
        // 游戏进程结束后清空状态，下一次重新读取固定入口
        DayNightReady=false;DayNightStatus="等待游戏启动";
    }
}
}
