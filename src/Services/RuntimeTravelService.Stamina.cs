using System;
using System.IO;

namespace EnshroudedTravel {
public sealed partial class RuntimeTravelService {
    private const int StaminaSubtractRva = 0x2F0D59;
    private const int StaminaFunctionRva = 0x2F0B70;
    private const int StaminaFunctionLength = 0x248;
    private const string StaminaFunctionHash = "21bddaeb2c5e23ee1f347d28ded19bbf78b5e0aeb8dcf29fd5354bd7dff11be1";
    private const byte NativeStaminaOpcode = 0x29;
    private const byte UnlimitedStaminaOpcode = 0x39;
    private long staminaSubtractAddress;
    private bool ownsStaminaChange;
    public bool StaminaReady { get; private set; }
    public bool StaminaEnabled { get; private set; }
    public bool StaminaNeedsRestore { get { return ownsStaminaChange; } }
    public string StaminaStatus { get; private set; }

    private void EnsureStaminaAddress() {
        // 以当前游戏模块基址计算体力统一扣除指令地址
        if(staminaSubtractAddress==0 && process!=null)
            staminaSubtractAddress=process.MainModule.BaseAddress.ToInt64()+StaminaSubtractRva;
    }
    private bool ReadStaminaState() {
        // 只接受当前版本原生和已禁用扣除两种完整指令状态
        if(process==null || process.HasExited)throw new IOException("GAME_EXITED：游戏已退出");
        EnsureStaminaAddress();
        int offset=StaminaSubtractRva-StaminaFunctionRva+1;
        byte[] code=ReadAt(staminaSubtractAddress-(StaminaSubtractRva-StaminaFunctionRva),StaminaFunctionLength);
        if(code==null)throw new IOException("STAMINA_CODE_UNREADABLE：无法读取体力结算代码");
        byte opcode=code[offset];
        if(opcode!=NativeStaminaOpcode && opcode!=UnlimitedStaminaOpcode)
            throw new IOException("STAMINA_CODE_CHANGED：体力扣除指令已变化");
        code[offset]=NativeStaminaOpcode;
        using(var stream=new MemoryStream(code))
            if(TravelService.Hash(stream)!=StaminaFunctionHash)
                throw new IOException("STAMINA_CONTEXT_CHANGED：体力结算函数不匹配，已停止操作");
        return opcode==UnlimitedStaminaOpcode;
    }
    private void RefreshStaminaState() {
        // 独立刷新体力状态，不影响其他功能定位
        try {
            StaminaEnabled=ReadStaminaState(); StaminaReady=true;
            StaminaStatus=StaminaEnabled?"已开启 · 统一体力消耗不扣除（可能影响其他实体）":"已关闭 · 保持原生体力消耗";
        } catch(Exception ex) {
            StaminaReady=false; StaminaEnabled=false;
            if(StaminaStatus!=ex.Message)Log("无限体力读取失败："+ex.Message);
            StaminaStatus=ex.Message;
        }
    }
    public void ToggleStamina(bool enable) {
        // 切换统一体力扣除指令，当前版本会影响所有执行该结算的实体
        if(enable && !StaminaReady)throw new IOException("STAMINA_CODE_NOT_FOUND：体力结算代码尚未定位");
        bool current=ReadStaminaState();
        if(current==enable) {StaminaEnabled=current;if(!enable)ownsStaminaChange=false;return;}
        IntPtr writer=Native.OpenProcess(0x438,false,process.Id);
        if(writer==IntPtr.Zero)throw Native.Error("WRITE_ACCESS：无法修改体力结算代码");
        try {
            if(process.HasExited || ReadStaminaState()!=current)
                throw new IOException("PROCESS_CHANGED：操作前游戏状态已变化");
            bool previousOwnership=ownsStaminaChange;
            ownsStaminaChange=true;
            try {
                WriteStaminaOpcode(writer,enable?UnlimitedStaminaOpcode:NativeStaminaOpcode);
                StaminaEnabled=ReadStaminaState();
                if(StaminaEnabled!=enable)throw new IOException("STAMINA_VERIFY_FAILED：无限体力状态回读不一致");
                ownsStaminaChange=StaminaEnabled;
            } catch(Exception error) {
                try {
                    WriteStaminaOpcode(writer,current?UnlimitedStaminaOpcode:NativeStaminaOpcode);
                    if(ReadStaminaState()!=current)throw new IOException("STAMINA_ROLLBACK_VERIFY：体力恢复回读不一致");
                    StaminaEnabled=current;ownsStaminaChange=previousOwnership;
                } catch(Exception restoreError) {
                    StaminaReady=false;
                    throw new AggregateException("STAMINA_RESTORE_FAILED：体力指令恢复失败，请保留窗口",error,restoreError);
                }
                throw;
            }
            StaminaReady=true;
            StaminaStatus=StaminaEnabled?"已开启 · 统一体力消耗不扣除（可能影响其他实体）":"已关闭 · 保持原生体力消耗";
            Log(enable?"已开启无限体力：统一体力消耗不再扣除，可能影响其他实体；不含其他独立扣除路径":"已关闭无限体力，体力消耗已恢复");
        } finally {Native.CloseHandle(writer);}
    }
    private void WriteStaminaOpcode(IntPtr writer,byte opcode) {
        // 单字节将 SUB 改为相同操作数的 CMP，保留指令长度、标志及线程指令边界
        long location=staminaSubtractAddress+1;
        WriteProtected(writer,location,new[]{opcode},0x40);
        if(!DamageNative.FlushInstructionCache(writer,new IntPtr(location),new UIntPtr(1)))
            throw Native.Error("STAMINA_CACHE_FLUSH：体力指令缓存刷新失败");
        byte[] actual=ReadAt(location,1);
        if(actual==null || actual[0]!=opcode)throw new IOException("STAMINA_WRITE_VERIFY：体力指令回读不一致");
    }
    private void ReleaseStamina() {
        // 游戏进程结束后清空体力状态
        staminaSubtractAddress=0;StaminaReady=false;StaminaEnabled=false;ownsStaminaChange=false;
        StaminaStatus="等待游戏启动";
    }
}
}
