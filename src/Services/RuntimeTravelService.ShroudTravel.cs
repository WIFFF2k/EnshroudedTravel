using System;
using System.IO;

namespace EnshroudedTravel {
public sealed partial class RuntimeTravelService {
    private const int ShroudTravelCodeRva = 0xE7681E;
    private const byte NativeShroudTravelCode = 0x74;
    private const byte AllowedShroudTravelCode = 0xEB;
    private long shroudTravelCodeAddress;
    private bool ownsShroudTravelChange;
    public bool ShroudTravelReady { get; private set; }
    public bool ShroudTravelEnabled { get; private set; }
    public bool ShroudTravelNeedsRestore { get { return ownsShroudTravelChange; } }
    public string ShroudTravelStatus { get; private set; }

    private void EnsureShroudTravelCodeAddress() {
        // 以当前游戏模块基址计算瘴气传送限制指令地址
        if(shroudTravelCodeAddress==0 && process!=null)
            shroudTravelCodeAddress=process.MainModule.BaseAddress.ToInt64()+ShroudTravelCodeRva;
    }
    private bool ReadShroudTravelState() {
        // 只接受原生和允许两种已知指令状态
        if(process==null || process.HasExited)throw new IOException("GAME_EXITED：游戏已退出");
        EnsureShroudTravelCodeAddress();
        if(shroudTravelCodeAddress==0)throw new IOException("SHROUD_CODE_NOT_FOUND：瘴气传送限制尚未定位");
        byte[] code=ReadAt(shroudTravelCodeAddress,1);
        if(code==null || (code[0]!=NativeShroudTravelCode && code[0]!=AllowedShroudTravelCode))
            throw new IOException("SHROUD_CODE_CHANGED：瘴气传送限制代码已变化");
        return code[0]==AllowedShroudTravelCode;
    }
    private void RefreshShroudTravelState() {
        // 瘴气传送状态独立刷新，不影响传送和缩放
        try {
            ShroudTravelEnabled=ReadShroudTravelState();ShroudTravelReady=true;
            ShroudTravelStatus=ShroudTravelEnabled?"已允许瘴气中传送":"已恢复原生瘴气传送限制";
        } catch(Exception ex) {
            ShroudTravelReady=false;ShroudTravelEnabled=false;
            ShroudTravelStatus="瘴气传送限制状态不可用";
            Log("瘴气传送读取失败："+ex.Message);
        }
    }
    public void ToggleShroudTravel(bool enable) {
        // 仅切换快速传送禁用判断的通用分支，保留落水和下落限制
        if(!ShroudTravelReady)throw new IOException("SHROUD_CODE_NOT_FOUND：请重新打开修改器");
        bool current=ReadShroudTravelState();
        if(current==enable) {
            ShroudTravelEnabled=current;
            if(!enable)ownsShroudTravelChange=false;
            return;
        }
        IntPtr writer=Native.OpenProcess(0x438,false,process.Id);
        if(writer==IntPtr.Zero)throw Native.Error("WRITE_ACCESS：无法修改瘴气传送限制");
        try {
            if(process.HasExited || ReadShroudTravelState()!=current)
                throw new IOException("PROCESS_CHANGED：操作前游戏状态已变化");
            ownsShroudTravelChange=true;
            WriteProtected(writer,shroudTravelCodeAddress,
                new[]{enable?AllowedShroudTravelCode:NativeShroudTravelCode},0x40);
            ShroudTravelEnabled=ReadShroudTravelState();
            if(ShroudTravelEnabled!=enable)throw new IOException("VERIFY_FAILED：瘴气传送状态回读不一致");
            ownsShroudTravelChange=ShroudTravelEnabled;
            ShroudTravelReady=true;
            ShroudTravelStatus=ShroudTravelEnabled?"已允许瘴气中传送":"已恢复原生瘴气传送限制";
            Log(enable?"已允许在瘴气中快速传送":"已恢复原生瘴气快速传送限制");
        } finally {Native.CloseHandle(writer);}
    }
    private void ReleaseShroudTravel() {
        // 游戏进程结束后清空瘴气传送限制状态
        shroudTravelCodeAddress=0;ShroudTravelReady=false;ShroudTravelEnabled=false;ownsShroudTravelChange=false;
        ShroudTravelStatus="等待游戏启动";
    }
}
}
