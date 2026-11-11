// 管理单人世界玩家命中伤害的十倍开关、命中记录及原逻辑恢复
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace EnshroudedTravel {
public sealed partial class RuntimeTravelService {
    private const int DamageBoostCallRva = 0x3DDA73;
    private const int DamageStubSize = 195;
    private long reportedDamageHits;
    private static readonly byte[] NativeDamageBoostCall = {0xE8,0x98,0x82,0x00,0x00};
    private long damageBoostCallAddress;
    private long damageBoostStubAddress;
    private bool ownsDamageBoostChange;
    public bool DamageBoostReady { get; private set; }
    public bool DamageBoostEnabled { get; private set; }
    public string DamageBoostStatus { get; private set; }

    private void EnsureDamageBoostCallAddress() {
        // 以当前游戏模块基址计算命中伤害结算调用地址
        if(damageBoostCallAddress==0 && process!=null)
            damageBoostCallAddress=process.MainModule.BaseAddress.ToInt64()+DamageBoostCallRva;
    }
    private static bool Matches(byte[] left,byte[] right) {
        // 严格比较完整字节序列
        if(left==null || right==null || left.Length!=right.Length)return false;
        for(int i=0;i<left.Length;i++)if(left[i]!=right[i])return false;
        return true;
    }
    private bool ReadDamageBoostState() {
        // 只接受原版调用和指向本功能存根的十倍调用两种状态
        if(process==null || process.HasExited)throw new IOException("GAME_EXITED：游戏已退出");
        EnsureDamageBoostCallAddress();
        if(damageBoostCallAddress==0)throw new IOException("DAMAGE_CODE_NOT_FOUND：伤害提升代码尚未定位");
        byte[] code=ReadAt(damageBoostCallAddress,NativeDamageBoostCall.Length);
        if(Matches(code,NativeDamageBoostCall)) {
            return false;
        }
        if(code==null || code[0]!=0xE8)
            throw new IOException("DAMAGE_CODE_CHANGED：伤害提升代码已变化，请重新打开修改器");
        long target=damageBoostCallAddress+code.Length+BitConverter.ToInt32(code,1);
        byte[] stub=ReadAt(target,DamageStubSize);
        byte[] expected=BuildDamageStub(target);
        if(!Matches(stub,expected))
            throw new IOException("DAMAGE_CODE_CHANGED：伤害提升代码已变化，请重新打开修改器");
        damageBoostStubAddress=target;
        return true;
    }
    private byte[] BuildDamageStub(long stubAddress) {
        // 保留原结算函数及额外使用的寄存器，只放大负的生命变化量
        // ApplyDamage 栈帧：-80h 为上下文，+1A0h 为原始施法者实体，+140h 为目标阵营
        // HasFaction 节点确认上下文 +160h 为阵营组件表，按原生位图、偏移及步长查询
        // 玩家阵营 7 才放大，目标阵营 7、8 排除，治疗不变；仅适用本地单人世界
        // 页内 +300h/+308h 记录结算/放大次数，+310h 对齐写入原值和放大值
        byte[] stub={
            0x48,0x83,0xEC,0x28,0xE8,0,0,0,0,0x48,0x83,0xC4,0x28,0x52,0x41,0x50,
            0x41,0x51,0x41,0x52,0x41,0x53,0xF0,0x48,0xFF,0x05,0xE2,0x02,0x00,0x00,0x85,0xC0,
            0x0F,0x89,0x93,0x00,0x00,0x00,0x4C,0x8B,0x95,0xA0,0x01,0x00,0x00,0x4D,0x85,0xD2,
            0x0F,0x84,0x83,0x00,0x00,0x00,0x4C,0x8B,0x5D,0x80,0x4D,0x8B,0x9B,0x60,0x01,0x00,0x00,
            0x4D,0x85,0xDB,0x74,0x73,0x45,0x0F,0xB7,0x03,0x4D,0x8B,0x5A,0x18,0x4D,0x0F,0xA3,0x03,
            0x73,0x65,0x47,0x0F,0xB7,0x8C,0x43,0x84,0x0A,0x00,0x00,0x47,0x0F,0xB7,0x84,0x43,0x84,
            0x00,0x00,0x00,0x41,0x8B,0x52,0x30,0x4C,0x0F,0xAF,0xCA,0x4D,0x01,0xC1,0x4D,0x03,0x4A,
            0x20,0x41,0x80,0x39,0x07,0x75,0x3E,0x4C,0x8B,0x9D,0x40,0x01,0x00,0x00,0x4D,0x85,0xDB,
            0x74,0x32,0x41,0x80,0x3B,0x07,0x74,0x2C,0x41,0x80,0x3B,0x08,0x74,0x26,0x41,0x89,0xC0,
            0x6B,0xC0,0x0A,0x71,0x05,0x44,0x89,0xC0,0xEB,0x19,0x41,0x89,0xC1,0x49,0xC1,0xE1,0x20,
            0x4D,0x09,0xC8,0x4C,0x89,0x05,0x5F,0x02,0x00,0x00,0xF0,0x48,0xFF,0x05,0x4F,0x02,0x00,0x00,
            0x41,0x5B,0x41,0x5A,0x41,0x59,0x41,0x58,0x5A,0xC3};
        long originalTarget=damageBoostCallAddress+NativeDamageBoostCall.Length+
            BitConverter.ToInt32(NativeDamageBoostCall,1);
        int relative=RelativeDisplacement(originalTarget,stubAddress+9,"DAMAGE_ORIGINAL_CALL_RANGE");
        Buffer.BlockCopy(BitConverter.GetBytes(relative),0,stub,5,4);
        return stub;
    }
    private static int RelativeDisplacement(long target,long origin,string errorCode) {
        // 将相对跳转限制在 32 位有符号范围内，超范围时返回明确错误
        long displacement=target-origin;
        if(displacement<int.MinValue || displacement>int.MaxValue)
            throw new IOException(errorCode+"：跳转目标超出 32 位相对地址范围");
        return (int)displacement;
    }
    private long FindDamageStubAddress(IntPtr writer) {
        // 在调用点附近查找可分配的连续空闲页
        const long reach=0x70000000;
        long low=Math.Max(0x10000L,damageBoostCallAddress-reach);
        long high=damageBoostCallAddress+reach;
        DamageNative.MemoryBasicInformation info;
        UIntPtr infoSize=new UIntPtr((uint)Marshal.SizeOf(typeof(DamageNative.MemoryBasicInformation)));
        if(DamageNative.VirtualQueryEx(writer,new IntPtr(damageBoostCallAddress),out info,infoSize)==UIntPtr.Zero)
            return 0;
        long regionStart=info.BaseAddress.ToInt64();
        long regionSize=checked((long)info.RegionSize.ToUInt64());
        if(regionSize<=0)return 0;
        long above=regionStart+regionSize,below=Math.Max(low,regionStart-1);
        for(int step=0;step<4096 && (above<high || below>=low);step++) {
            long candidateAbove=0,candidateBelow=0;
            if(above<high) {
                info=new DamageNative.MemoryBasicInformation();
                if(DamageNative.VirtualQueryEx(writer,new IntPtr(above),out info,infoSize)!=UIntPtr.Zero) {
                    long start=info.BaseAddress.ToInt64();
                    long size=checked((long)info.RegionSize.ToUInt64());
                    long end=start+size;
                    if(info.State==0x10000 && size>=0x1000) {
                        long candidate=(Math.Max(start,low)+0xFFFF)&~0xFFFFL;
                        if(candidate+0x1000<=end && candidate<high &&
                            DamageBoostStubReachable(candidate))
                            candidateAbove=candidate;
                    }
                    above=Math.Max(end,above+1);
                } else above=high;
            }
            if(below>=low) {
                info=new DamageNative.MemoryBasicInformation();
                if(DamageNative.VirtualQueryEx(writer,new IntPtr(below),out info,infoSize)!=UIntPtr.Zero) {
                    long start=info.BaseAddress.ToInt64();
                    long size=checked((long)info.RegionSize.ToUInt64());
                    long end=start+size;
                    if(info.State==0x10000 && size>=0x1000) {
                        long candidate=(end-0x1000)&~0xFFFFL;
                        if(candidate>=start && candidate>=low &&
                            DamageBoostStubReachable(candidate))
                            candidateBelow=candidate;
                    }
                    below=Math.Min(start-1,below-1);
                } else below=low-1;
            }
            if(candidateAbove!=0 && candidateBelow!=0) {
                return Math.Abs(candidateAbove-damageBoostCallAddress)<=
                    Math.Abs(candidateBelow-damageBoostCallAddress)?candidateAbove:candidateBelow;
            }
            if(candidateAbove!=0)return candidateAbove;
            if(candidateBelow!=0)return candidateBelow;
        }
        return 0;
    }
    private bool DamageBoostStubReachable(long stubAddress) {
        // 同时验证入口调用与原伤害函数调用都能编码为 32 位相对位移
        long returnAddress=damageBoostCallAddress+NativeDamageBoostCall.Length;
        long callDisplacement=stubAddress-returnAddress;
        long originalTarget=returnAddress+BitConverter.ToInt32(NativeDamageBoostCall,1);
        long originalDisplacement=originalTarget-(stubAddress+9);
        return callDisplacement>=int.MinValue && callDisplacement<=int.MaxValue &&
            originalDisplacement>=int.MinValue && originalDisplacement<=int.MaxValue;
    }
    private void RefreshDamageBoostState() {
        // 独立刷新伤害提升状态，不影响其他功能开关
        try {
            DamageBoostEnabled=ReadDamageBoostState();
            DamageBoostReady=true;
            DamageBoostStatus=DamageBoostEnabled?"已开启 · 等待玩家命中记录":"已关闭 · 命中伤害保持原值";
            if(DamageBoostEnabled)ReadDamageHitRecord();
        } catch(Exception ex) {
            DamageBoostReady=false;DamageBoostEnabled=false;
            DamageBoostStatus="伤害提升状态不可用";
            Log("伤害提升读取失败："+ex.Message);
        }
    }
    private void ReadDamageHitRecord() {
        // 每秒读取 24 字节，仅在命中计数变化时报告最近一次结算原值和放大值
        byte[] data=ReadAt(damageBoostStubAddress+0x300,24);
        if(data==null)throw new IOException("DAMAGE_HIT_READ：无法读取命中记录");
        long hits=BitConverter.ToInt64(data,8);
        if(hits==0) {
            DamageBoostStatus="已开启 · 尚未记录玩家伤害（结算次数 "+BitConverter.ToInt64(data,0)+"）";
            return;
        }
        long before=-(long)BitConverter.ToInt32(data,16),after=-(long)BitConverter.ToInt32(data,20);
        DamageBoostStatus="已记录玩家命中 "+before+" → "+after+" · 累计 "+hits+" 次";
        if(hits!=reportedDamageHits) {
            reportedDamageHits=hits;
            Log("伤害结算："+before+" → "+after+" · 累计放大 "+hits+" 次");
        }
    }
    private IntPtr[] SuspendGameThreads() {
        // 暂停全部游戏线程，避免写入多字节调用指令时执行到半条指令
        var handles=new List<IntPtr>();
        process.Refresh();
        try {
            foreach(ProcessThread thread in process.Threads) {
                IntPtr threadHandle=DamageNative.OpenThread(0x0002,false,thread.Id);
                if(threadHandle==IntPtr.Zero)throw DamageNative.Error("DAMAGE_THREAD_ACCESS：无法暂停游戏线程");
                if(DamageNative.SuspendThread(threadHandle)==uint.MaxValue) {
                    DamageNative.CloseHandle(threadHandle);
                    throw DamageNative.Error("DAMAGE_THREAD_SUSPEND：游戏线程暂停失败");
                }
                handles.Add(threadHandle);
            }
            return handles.ToArray();
        } catch {
            ResumeGameThreads(handles);
            throw;
        }
    }
    private static void ResumeGameThreads(List<IntPtr> handles) {
        // 任何写入结果下都恢复已暂停的游戏线程
        var errors=new List<Exception>();
        foreach(IntPtr threadHandle in handles) {
            if(DamageNative.ResumeThread(threadHandle)==uint.MaxValue)
                errors.Add(DamageNative.Error("DAMAGE_THREAD_RESUME：游戏线程恢复失败"));
            DamageNative.CloseHandle(threadHandle);
        }
        if(errors.Count>0)throw new AggregateException("DAMAGE_THREAD_RESTORE：部分游戏线程未能恢复",errors);
    }
    private void PrepareDamageBoostStub(IntPtr writer) {
        // 在暂停线程前准备存根，同一服务连接内复用已核验的代码页
        if(damageBoostStubAddress!=0) {
            if(!Matches(ReadAt(damageBoostStubAddress,DamageStubSize),BuildDamageStub(damageBoostStubAddress)))
                throw new IOException("DAMAGE_STUB_CHANGED：伤害提升代码空间已变化");
            return;
        }
        long requestedStub=FindDamageStubAddress(writer);
        if(requestedStub==0)
            throw new IOException("DAMAGE_STUB_RANGE：调用点附近没有可用的代码空间");
        IntPtr stub=DamageNative.VirtualAllocEx(writer,new IntPtr(requestedStub),
            new UIntPtr(0x1000),DamageNative.MemCommit|DamageNative.MemReserve,
            DamageNative.PageExecuteReadWrite);
        if(stub==IntPtr.Zero)throw DamageNative.Error("DAMAGE_STUB_ALLOC：无法分配伤害提升代码空间");
        long allocated=stub.ToInt64();
        try {
            if(!DamageBoostStubReachable(allocated))
                throw new IOException("DAMAGE_STUB_RANGE：伤害提升存根超出 32 位相对地址范围");
            WriteDamageBoostCode(writer,allocated,BuildDamageStub(allocated));
            damageBoostStubAddress=allocated;
        } catch(Exception ex) {
            // 尚未接入调用点的存根没有执行者，可以直接释放
            if(!DamageNative.VirtualFreeEx(writer,stub,UIntPtr.Zero,DamageNative.MemRelease))
                throw new AggregateException("DAMAGE_STUB_CLEANUP：伤害提升代码空间清理失败",
                    ex,DamageNative.Error("DAMAGE_STUB_RELEASE：代码页释放失败"));
            throw;
        }
    }
    private void WriteDamageBoostCode(IntPtr writer,long location,byte[] code) {
        // 写入后核验全部字节并刷新目标进程指令缓存
        WriteProtected(writer,location,code,DamageNative.PageExecuteReadWrite);
        if(!DamageNative.FlushInstructionCache(writer,new IntPtr(location),new UIntPtr((uint)code.Length)))
            throw DamageNative.Error("DAMAGE_CACHE_FLUSH：伤害提升指令缓存刷新失败");
        if(!Matches(ReadAt(location,code.Length),code))
            throw new IOException("DAMAGE_WRITE_VERIFY：伤害提升代码回读不一致");
    }
    public void ToggleDamageBoost(bool enable) {
        // 准备工作在游戏运行期间完成，仅在替换调用指令时暂停线程
        var elapsed=Stopwatch.StartNew();
        bool current=ReadDamageBoostState();
        if(current==enable) {
            DamageBoostEnabled=current;
            if(!enable)ownsDamageBoostChange=false;
            return;
        }
        IntPtr writer=DamageNative.OpenProcess(0x438,false,process.Id);
        if(writer==IntPtr.Zero)throw DamageNative.Error("WRITE_ACCESS：无法修改伤害提升代码");
        try {
            if(enable)PrepareDamageBoostStub(writer);
            byte[] call=(byte[])NativeDamageBoostCall.Clone();
            if(enable) {
                int relative=RelativeDisplacement(damageBoostStubAddress,
                    damageBoostCallAddress+call.Length,"DAMAGE_CALL_RANGE");
                Buffer.BlockCopy(BitConverter.GetBytes(relative),0,call,1,4);
            }
            var suspended=new List<IntPtr>(SuspendGameThreads());
            try {
                if(process.HasExited || ReadDamageBoostState()!=current)
                    throw new IOException("PROCESS_CHANGED：操作前游戏状态已变化");
                byte[] previousCall=ReadAt(damageBoostCallAddress,call.Length);
                if(previousCall==null)
                    throw new IOException("DAMAGE_CODE_READ：无法读取切换前的伤害调用");
                bool previouslyOwned=ownsDamageBoostChange;
                ownsDamageBoostChange=true;
                try {
                    WriteDamageBoostCode(writer,damageBoostCallAddress,call);
                    DamageBoostEnabled=ReadDamageBoostState();
                    if(DamageBoostEnabled!=enable)
                        throw new IOException("DAMAGE_STATE_VERIFY：伤害提升状态回读不一致");
                    ownsDamageBoostChange=DamageBoostEnabled;
                } catch(Exception ex) {
                    // 写入或回读失败时，在线程恢复前还原完整调用，避免残留半条指令
                    try {
                        WriteDamageBoostCode(writer,damageBoostCallAddress,previousCall);
                        DamageBoostEnabled=current;
                        ownsDamageBoostChange=previouslyOwned;
                    } catch(Exception restoreError) {
                        DamageBoostReady=false;
                        throw new AggregateException("DAMAGE_CALL_RESTORE：伤害调用恢复失败",ex,restoreError);
                    }
                    throw;
                }
            } finally {
                ResumeGameThreads(suspended);
            }
            // 关闭后保留一页存根，已进入存根的线程仍能安全返回，下一次开启直接复用
            DamageBoostReady=true;
            DamageBoostStatus=DamageBoostEnabled?"已开启 · 等待玩家命中记录":"已关闭 · 命中伤害保持原值";
            Log((enable?"已开启伤害提升十倍，等待玩家命中记录":"已关闭伤害提升十倍，命中伤害已恢复")+
                " · 切换耗时 "+elapsed.Elapsed.TotalMilliseconds.ToString("0")+" 毫秒");
        } finally {
            Native.CloseHandle(writer);
        }
    }
    private void ReleaseDamageBoost() {
        // 游戏进程结束后清空伤害提升状态
        damageBoostCallAddress=0;damageBoostStubAddress=0;reportedDamageHits=0;
        DamageBoostReady=false;DamageBoostEnabled=false;ownsDamageBoostChange=false;
        DamageBoostStatus="等待游戏启动";
    }
    private static class DamageNative {
        internal const uint MemCommit=0x1000,MemReserve=0x2000,MemRelease=0x8000;
        internal const uint PageExecuteReadWrite=0x40;
        [StructLayout(LayoutKind.Sequential)]
        internal struct MemoryBasicInformation {
            public IntPtr BaseAddress,AllocationBase;
            public uint AllocationProtect;
            public ushort PartitionId;
            public UIntPtr RegionSize;
            public uint State,Protect,Type;
        }
        [DllImport("kernel32.dll",SetLastError=true)]
        internal static extern IntPtr OpenProcess(uint access,bool inherit,int id);
        [DllImport("kernel32.dll",SetLastError=true)]
        internal static extern IntPtr OpenThread(uint access,bool inherit,int id);
        [DllImport("kernel32.dll",SetLastError=true)]
        internal static extern uint SuspendThread(IntPtr thread);
        [DllImport("kernel32.dll",SetLastError=true)]
        internal static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll",SetLastError=true)]
        internal static extern IntPtr VirtualAllocEx(IntPtr process,IntPtr address,UIntPtr size,uint allocationType,uint protection);
        [DllImport("kernel32.dll",SetLastError=true)]
        internal static extern bool VirtualFreeEx(IntPtr process,IntPtr address,UIntPtr size,uint freeType);
        [DllImport("kernel32.dll",SetLastError=true)]
        internal static extern UIntPtr VirtualQueryEx(IntPtr process,IntPtr address,
            out MemoryBasicInformation info,UIntPtr length);
        [DllImport("kernel32.dll",SetLastError=true)]
        internal static extern bool FlushInstructionCache(IntPtr process,IntPtr address,UIntPtr size);
        [DllImport("kernel32.dll",SetLastError=true)]
        internal static extern bool CloseHandle(IntPtr handle);
        internal static Exception Error(string message) {
            // 保留系统错误码供诊断
            return new IOException(message+"（系统错误 "+Marshal.GetLastWin32Error()+"）");
        }
    }
}
}
