using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace EnshroudedTravel {
public sealed partial class RuntimeTravelService {
    private const int StorageRecipeAnchorOffset = 8392;
    private const int StorageRecipeAnchorLength = 40;
    private const string StorageRecipeTemplateHash = "c84f3c37567a9713888511fb7d611981e214a68a42ef1da4a086dddfa97d0d15";
    private static readonly int[] StorageRecipeCountOffsets = {
        434932, 434956,
        435060, 435108, 435156,
        435260, 435308, 435356,
        435460, 435508, 435556,
        435660, 435708, 435756, 435804, 435852,
        435960, 436008, 436056, 436104, 436152,
        436260, 436308, 436356, 436404, 436452
    };
    private static readonly int[] StorageRecipeKnowledgeOffsets = {
        8472, 8640, 8808, 8976, 9144, 9312, 9480
    };
    private readonly byte[] storageRecipeTemplate;
    private long storageRecipeAddress;
    private bool storageRecipeConflict, ownsStorageUpgradeChange;
    public bool StorageUpgradeReady { get; private set; }
    public bool StorageUpgradeEnabled { get; private set; }
    public bool StorageUpgradeNeedsRestore { get { return ownsStorageUpgradeChange; } }
    public string StorageUpgradeStatus { get; private set; }

    private static byte[] LoadStorageRecipeTemplate(string root) {
        // 载入并核验箱子容量升级配方模板
        string path=Path.Combine(root,"payload","storage-recipes-original.bin");
        if(!File.Exists(path))throw new IOException("STORAGE_TEMPLATE_MISSING：缺少箱子升级配方模板");
        if(TravelService.HashFile(path)!=StorageRecipeTemplateHash)
            throw new IOException("STORAGE_TEMPLATE_INVALID：箱子升级配方模板校验失败");
        byte[] data=File.ReadAllBytes(path);
        foreach(int offset in StorageRecipeCountOffsets) {
            if(offset<0 || offset+4>data.Length || BitConverter.ToUInt32(data,offset)==0)
                throw new IOException("STORAGE_TEMPLATE_INVALID：箱子升级配方材料数量不符");
        }
        foreach(int offset in StorageRecipeKnowledgeOffsets) {
            if(offset<0 || offset+12>data.Length || BitConverter.ToUInt32(data,offset)==0)
                throw new IOException("STORAGE_TEMPLATE_INVALID：箱子升级配方等级条件不符");
        }
        return data;
    }
    private byte[] BuildStorageRecipeAnchor() {
        // 截取稳定的配方身份字段作为资源定位锚点
        byte[] anchor=new byte[StorageRecipeAnchorLength];
        Buffer.BlockCopy(storageRecipeTemplate,StorageRecipeAnchorOffset,anchor,0,anchor.Length);
        return anchor;
    }
    private bool MatchesStorageRecipeState(byte[] data,bool enabled) {
        // 只允许目标材料数量和等级查询在原值与零值之间切换
        if(data==null || data.Length!=storageRecipeTemplate.Length)return false;
        for(int i=0;i<data.Length;i++) {
            if(Array.IndexOf(StorageRecipeCountOffsets,i)>=0)continue;
            if(IsStorageRecipeKnowledgeByte(i))continue;
            if(data[i]!=storageRecipeTemplate[i])return false;
        }
        foreach(int offset in StorageRecipeCountOffsets) {
            uint value=BitConverter.ToUInt32(data,offset);
            uint expected=enabled?0:BitConverter.ToUInt32(storageRecipeTemplate,offset);
            if(value!=expected)return false;
        }
        foreach(int offset in StorageRecipeKnowledgeOffsets) {
            bool allZero=true,original=true;
            for(int i=0;i<12;i++) {
                if(data[offset+i]!=0)allZero=false;
                if(data[offset+i]!=storageRecipeTemplate[offset+i])original=false;
            }
            if(enabled?!allZero:!original)return false;
        }
        return true;
    }
    private static bool IsStorageRecipeKnowledgeByte(int index) {
        // 判断当前字节是否属于箱子升级配方的等级查询
        foreach(int offset in StorageRecipeKnowledgeOffsets) {
            if(index>=offset && index<offset+12)return true;
        }
        return false;
    }
    private bool ValidateStorageRecipeRegistry(long location) {
        // 完整核验单个箱子升级配方注册表，排除其他相似资源
        byte[] data=ReadAt(location,storageRecipeTemplate.Length);
        return MatchesStorageRecipeState(data,false) || MatchesStorageRecipeState(data,true);
    }
    private bool ReadStorageUpgradeState() {
        // 读取配方锚点、材料数量和等级查询，确认当前开关状态
        if(storageRecipeAddress==0)throw new IOException("STORAGE_NOT_FOUND：箱子升级配方尚未定位");
        if(process==null || process.HasExited)throw new IOException("GAME_EXITED：游戏已退出");
        byte[] anchor=ReadAt(storageRecipeAddress+StorageRecipeAnchorOffset,StorageRecipeAnchorLength);
        if(anchor==null)throw new IOException("STORAGE_CHANGED：箱子升级配方已变化");
        for(int i=0;i<anchor.Length;i++) {
            if(anchor[i]!=storageRecipeTemplate[StorageRecipeAnchorOffset+i])
                throw new IOException("STORAGE_CHANGED：箱子升级配方已变化");
        }
        bool? state=null;
        foreach(int offset in StorageRecipeCountOffsets) {
            byte[] raw=ReadAt(storageRecipeAddress+offset,4);
            if(raw==null)throw new IOException("STORAGE_CHANGED：箱子升级配方已变化");
            uint value=BitConverter.ToUInt32(raw,0);
            uint original=BitConverter.ToUInt32(storageRecipeTemplate,offset);
            bool current=value==0;
            if(value!=0 && value!=original)
                throw new IOException("STORAGE_COUNT_CHANGED：箱子升级材料数量已变化");
            if(state.HasValue && state.Value!=current)
                throw new IOException("STORAGE_STATE_CHANGED：箱子升级配方状态不一致");
            state=current;
        }
        foreach(int offset in StorageRecipeKnowledgeOffsets) {
            byte[] raw=ReadAt(storageRecipeAddress+offset,12);
            if(raw==null)throw new IOException("STORAGE_CHANGED：箱子升级配方已变化");
            bool allZero=true,original=true;
            for(int i=0;i<12;i++) {
                if(raw[i]!=0)allZero=false;
                if(raw[i]!=storageRecipeTemplate[offset+i])original=false;
            }
            if(!allZero && !original)
                throw new IOException("STORAGE_LEVEL_CHANGED：箱子升级等级条件已变化");
            bool current=allZero;
            if(state.HasValue && state.Value!=current)
                throw new IOException("STORAGE_STATE_CHANGED：箱子升级配方状态不一致");
            state=current;
        }
        return state.HasValue && state.Value;
    }
    private void RefreshStorageUpgradeState() {
        // 箱子升级状态独立刷新，不影响传送和缩放
        if(storageRecipeAddress==0)return;
        try {
            StorageUpgradeEnabled=ReadStorageUpgradeState();StorageUpgradeReady=true;
            StorageUpgradeStatus=StorageUpgradeEnabled?"已启用 · 增大箱子无材料与等级限制":"已关闭 · 增大箱子恢复材料与等级要求";
        } catch(Exception ex) {
            StorageUpgradeReady=false;StorageUpgradeEnabled=false;
            StorageUpgradeStatus="箱子升级配方状态已变化 · 请重新定位";
            Log("箱子升级配方读取失败："+ex.Message);
        }
    }
    private void SearchStorageRecipeBuffer(byte[] buffer,int available,long bufferAddress,byte[] anchor,int[] skip) {
        // 在同一资源缓冲区内查找箱子升级配方注册表
        if(storageRecipeConflict || available<anchor.Length)return;
        for(int i=0;i<=available-anchor.Length;) {
            int j=anchor.Length-1;
            while(j>=0 && buffer[i+j]==anchor[j])j--;
            if(j<0 && bufferAddress+i>=StorageRecipeAnchorOffset) {
                long candidate=bufferAddress+i-StorageRecipeAnchorOffset;
                if(ValidateStorageRecipeRegistry(candidate)) {
                    if(storageRecipeAddress!=0 && storageRecipeAddress!=candidate) {
                        storageRecipeConflict=true;StorageUpgradeReady=false;StorageUpgradeEnabled=false;storageRecipeAddress=0;
                        StorageUpgradeStatus="发现多份箱子升级配方 · 未修改游戏";
                        Log(StorageUpgradeStatus);
                        return;
                    }
                    storageRecipeAddress=candidate;
                    RefreshStorageUpgradeState();
                }
                i+=anchor.Length;
            } else i+=skip[buffer[i+anchor.Length-1]];
        }
    }
    private void ScanStorageRecipeRegions(bool readOnly) {
        // 优先扫描只读资源区，必要时再扫描其他私有可读区
        byte[] anchor=BuildStorageRecipeAnchor();
        int[] skip=Enumerable.Repeat(anchor.Length,256).ToArray();
        for(int i=0;i<anchor.Length-1;i++)skip[anchor[i]]=anchor.Length-1-i;
        byte[] buffer=new byte[8*1024*1024];
        long cursor=0;
        Native.Region region;
        while(Native.VirtualQueryEx(handle,new IntPtr(cursor),out region,(UIntPtr)Marshal.SizeOf(typeof(Native.Region)))!=UIntPtr.Zero) {
            if(storageRecipeConflict || storageRecipeAddress!=0)return;
            long start=region.BaseAddress.ToInt64(),length=checked((long)region.RegionSize.ToUInt64());
            if(length<=0 || start>long.MaxValue-length)break;
            long end=start+length; uint access=region.Protect&255;
            bool readable=region.State==0x1000 && (region.Protect&0x100)==0 && region.Type==0x20000
                && (access==2 || access==4 || access==8 || access==0x40 || access==0x80);
            bool protectionMatches=readOnly?access==2:access!=2;
            if(readable && protectionMatches && length>=anchor.Length) {
                for(long pos=start;pos<end;) {
                    int count=(int)Math.Min(buffer.Length,end-pos);UIntPtr read;
                    if(Native.ReadProcessMemory(handle,new IntPtr(pos),buffer,(UIntPtr)count,out read)) {
                        int available=(int)read.ToUInt64();
                        SearchStorageRecipeBuffer(buffer,available,pos,anchor,skip);
                    }
                    long step=pos+count<end?Math.Max(1,count-anchor.Length+1):count;
                    pos+=step;
                }
            }
            cursor=end;
        }
    }
    private void LocateStorageUpgrade() {
        // 首次使用时定位配方注册表，常规只读资源区约五百兆字节
        if(storageRecipeAddress!=0 || storageRecipeConflict)return;
        if(process==null || process.HasExited)throw new IOException("GAME_EXITED：游戏已退出");
        StorageUpgradeStatus="正在定位箱子升级配方";
        Log(StorageUpgradeStatus);
        ScanStorageRecipeRegions(true);
        if(storageRecipeAddress==0 && !storageRecipeConflict)ScanStorageRecipeRegions(false);
        if(storageRecipeConflict)throw new IOException("STORAGE_AMBIGUOUS：发现多份箱子升级配方，未修改游戏");
        if(storageRecipeAddress==0)throw new IOException("STORAGE_NOT_FOUND：未找到箱子升级配方，请进入世界后重试");
        StorageUpgradeReady=true;
    }
    public void ToggleStorageUpgrade(bool enable) {
        // 只切换箱子容量升级配方的材料数量和等级条件，不修改附魔和普通制作
        LocateStorageUpgrade();
        bool current=ReadStorageUpgradeState();
        if(current==enable) {
            StorageUpgradeEnabled=current;StorageUpgradeReady=true;
            if(!enable)ownsStorageUpgradeChange=false;
            return;
        }
        IntPtr writer=Native.OpenProcess(0x438,false,process.Id);
        if(writer==IntPtr.Zero)throw Native.Error("WRITE_ACCESS：无法修改箱子升级配方");
        try {
            if(process.HasExited || ReadStorageUpgradeState()!=current)
                throw new IOException("PROCESS_CHANGED：操作前游戏状态已变化");
            ownsStorageUpgradeChange=enable;
            foreach(int offset in StorageRecipeCountOffsets) {
                uint value=enable?0:BitConverter.ToUInt32(storageRecipeTemplate,offset);
                WriteProtected(writer,storageRecipeAddress+offset,BitConverter.GetBytes(value),4);
            }
            foreach(int offset in StorageRecipeKnowledgeOffsets) {
                byte[] value=new byte[12];
                if(!enable)Buffer.BlockCopy(storageRecipeTemplate,offset,value,0,value.Length);
                WriteProtected(writer,storageRecipeAddress+offset,value,4);
            }
            StorageUpgradeEnabled=ReadStorageUpgradeState();StorageUpgradeReady=true;
            if(StorageUpgradeEnabled!=enable)throw new IOException("VERIFY_FAILED：箱子升级状态回读不一致");
            ownsStorageUpgradeChange=StorageUpgradeEnabled;
            StorageUpgradeStatus=StorageUpgradeEnabled?"已启用 · 增大箱子无材料与等级限制":"已关闭 · 增大箱子恢复材料与等级要求";
            Log(enable?"已启用免费增大箱子，容量升级不再消耗材料或受等级限制":"已关闭免费增大箱子，材料和等级要求已恢复");
        } finally {Native.CloseHandle(writer);}
    }
    private void ReleaseStorageUpgrade() {
        // 游戏进程结束后清空箱子升级配方状态
        storageRecipeAddress=0;storageRecipeConflict=false;StorageUpgradeReady=false;StorageUpgradeEnabled=false;
        ownsStorageUpgradeChange=false;StorageUpgradeStatus="等待游戏启动";
    }
}
}
