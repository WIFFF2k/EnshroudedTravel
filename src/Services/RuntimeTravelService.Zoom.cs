using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;

namespace EnshroudedTravel {
public sealed partial class RuntimeTravelService {
    private const int ZoomArrayOffset = 93796;
    private const int ZoomScaleOffset = 93908;
    private const int ZoomAnchorLength = 16;
    private const int ZoomCodeRva = 0xC2E483;
    private const byte NativeZoomCount = 4;
    private const byte ExtendedZoomCount = 5;
    private const int OriginalZoomBits = 0x3F333333;
    private const int ReducedZoomBits = 0x3EE66666;
    private const string ZoomTemplateHash = "90d21609dacfdc79ada0096762ca9cd45e4e0d654753f144e9603dc6ee9e7cbd";
    private const string ZoomCacheVersion = "zoom-v2";
    private readonly byte[] zoomTemplate;
    private readonly List<long> zoomAddresses = new List<long>();
    private long zoomCodeAddress;
    private bool ownsZoomChange;
    public bool ZoomReady { get; private set; }
    public bool ZoomEnabled { get; private set; }
    public string ZoomStatus { get; private set; }

    private static byte[] LoadZoomTemplate() {
        // 载入并核验当前版本唯一的地图界面资源模板
        byte[] data=LoadBundledTemplate("zoom-original.bin",ZoomTemplateHash);
        if(data.Length<=ZoomScaleOffset+4 || BitConverter.ToInt32(data,ZoomScaleOffset)!=OriginalZoomBits)
            throw new IOException("ZOOM_TEMPLATE_INVALID：地图缩放模板缺少相机最远档位");
        return data;
    }
    private byte[] BuildZoomAnchor() {
        // 截取不含最远档位的前四档，兼容开启和关闭两种内存状态
        byte[] anchor=new byte[ZoomAnchorLength];
        Buffer.BlockCopy(zoomTemplate,ZoomArrayOffset,anchor,0,anchor.Length);
        return anchor;
    }
    private void LoadZoomCache() {
        // 同进程缓存只用于候选地址，仍须核验完整地图资源
        if(process==null)return;
        EnsureZoomCodeAddress();
        string cache=Path.Combine(files.Root,"runtime-zoom-location.txt");
        if(!File.Exists(cache))return;
        string[] fields=File.ReadAllLines(cache);
        long start;int pid,count;
        if(fields.Length<5 || fields[0]!=ZoomCacheVersion || fields[1]!=TravelService.ExeHash
            || !int.TryParse(fields[2],out pid) || !long.TryParse(fields[3],out start)
            || !int.TryParse(fields[4],out count) || fields.Length!=5+count
            || pid!=process.Id || start!=process.StartTime.ToUniversalTime().Ticks)return;
        var candidates=new List<long>();
        for(int i=0;i<count;i++) {
            long cached;
            if(!long.TryParse(fields[5+i],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out cached)
                || cached<=0 || candidates.Contains(cached)) {
                ZoomStatus="地图缩放的旧位置已失效 · 正在重新定位";
                return;
            }
            candidates.Add(cached);
        }
        for(int i=candidates.Count-1;i>=0;i--) {
            if(ValidateZoomBundle(candidates[i]))continue;
            Log("已忽略失效的地图缩放缓存地址："+candidates[i].ToString("X"));
            candidates.RemoveAt(i);
        }
        if(candidates.Count==0){
            ZoomStatus="地图缩放的旧位置已失效 · 正在重新定位";
            return;
        }
        zoomAddresses.Clear();zoomAddresses.AddRange(candidates);
        RefreshZoomState();
    }
    private void SearchZoomBuffer(byte[] buffer,int available,long bufferAddress,byte[] anchor,int[] skip) {
        // 跳跃搜索特征并收集全部完整匹配的地图缩放资源
        if(available<anchor.Length)return;
        for(int i=0;i<=available-anchor.Length;) {
            int j=anchor.Length-1;
            while(j>=0 && buffer[i+j]==anchor[j])j--;
            if(j<0 && bufferAddress+i>=ZoomArrayOffset) {
                long candidate=bufferAddress+i-ZoomArrayOffset;
                if(ValidateZoomBundle(candidate)) {
                    if(!zoomAddresses.Contains(candidate))zoomAddresses.Add(candidate);
                }
                i+=anchor.Length;
            } else i+=skip[buffer[i+anchor.Length-1]];
        }
    }
    private void EnsureZoomCodeAddress() {
        // 以当前游戏模块基址计算相机档位限制指令地址
        if(zoomCodeAddress==0 && process!=null)zoomCodeAddress=process.MainModule.BaseAddress.ToInt64()+ZoomCodeRva;
    }
    private bool ValidateZoomBundle(long location) {
        // 完整比对资源内容，仅允许新增档位处于原版或扩展状态
        byte[] data=ReadAt(location,zoomTemplate.Length);
        if(data==null || data.Length!=zoomTemplate.Length)return false;
        int scale=BitConverter.ToInt32(data,ZoomScaleOffset);
        if(scale!=OriginalZoomBits && scale!=ReducedZoomBits)return false;
        for(int i=0;i<data.Length;i++) {
            if(i>=ZoomScaleOffset && i<ZoomScaleOffset+4)continue;
            if(data[i]!=zoomTemplate[i])return false;
        }
        return true;
    }
    private bool ReadZoomState() {
        // 每次操作前后都重新核验完整资源、第五档和相机档位限制
        if(zoomAddresses.Count==0)throw new IOException("ZOOM_NOT_FOUND：地图缩放数据尚未定位");
        EnsureZoomCodeAddress();
        if(zoomCodeAddress==0)throw new IOException("ZOOM_CODE_NOT_FOUND：地图缩放代码尚未定位");
        RemoveExpiredZoomCopies();
        bool? bundleEnabled=null;
        foreach(long location in zoomAddresses) {
            byte[] data=ReadAt(location,zoomTemplate.Length);
            if(data==null || data.Length!=zoomTemplate.Length)throw new IOException("ZOOM_UNREADABLE：无法读取地图缩放数据");
            int scale=BitConverter.ToInt32(data,ZoomScaleOffset);
            if(scale!=OriginalZoomBits && scale!=ReducedZoomBits)throw new IOException("ZOOM_CHANGED：地图缩放数据已变化，未继续写入");
            for(int i=0;i<data.Length;i++) {
                if(i>=ZoomScaleOffset && i<ZoomScaleOffset+4)continue;
                if(data[i]!=zoomTemplate[i])throw new IOException("ZOOM_CHANGED：地图缩放资源内容已变化，未继续写入");
            }
            bool current=scale==ReducedZoomBits;
            if(bundleEnabled.HasValue && bundleEnabled.Value!=current)
                throw new IOException("ZOOM_COPY_CHANGED：地图缩放副本状态不一致，未继续写入");
            bundleEnabled=current;
        }
        byte[] code=ReadAt(zoomCodeAddress,1);
        if(code==null || (code[0]!=NativeZoomCount && code[0]!=ExtendedZoomCount))
            throw new IOException("ZOOM_CODE_CHANGED：地图缩放档位代码已变化，未继续写入");
        bool codeEnabled=code[0]==ExtendedZoomCount;
        if(bundleEnabled.Value!=codeEnabled)throw new IOException("ZOOM_PATCH_CHANGED：地图缩放代码与资源状态不一致，请重新打开修改器");
        return bundleEnabled.Value;
    }
    private void RefreshZoomState() {
        // 缩放状态独立刷新，不影响已经可用的传送定位
        if(zoomAddresses.Count==0){ZoomReady=false;return;}
        try {
            ZoomEnabled=ReadZoomState();ZoomReady=true;
            ZoomStatus=(ZoomEnabled?"已启用 · 缩放档位为 16/8/4/2/0.45":"已关闭 · 原生缩放档位为 16/8/4/2")
                +" · "+zoomAddresses.Count+" 份配置";
        } catch(Exception ex) {
            ZoomReady=false;ZoomEnabled=false;
            ZoomStatus="地图缩放状态已变化 · 请重新打开修改器";
            Log("地图缩放读取失败："+ex.Message);
        }
    }
    private void RemoveExpiredZoomCopies() {
        // 资源可能在扫描期间卸载并复用地址，仅保留身份仍与完整模板一致的副本
        // 第五档单独验证，不能将未知档位当成地址失效而跳过报错
        var live=new List<long>();
        foreach(long location in zoomAddresses) {
            byte[] data=ReadAt(location,zoomTemplate.Length);
            bool same=data!=null;
            if(same)for(int i=0;i<data.Length;i++) {
                if(i>=ZoomScaleOffset && i<ZoomScaleOffset+4)continue;
                if(data[i]!=zoomTemplate[i]){same=false;break;}
            }
            if(same)live.Add(location);
        }
        if(live.Count==0)throw new IOException("ZOOM_UNLOADED：地图缩放资源已卸载，请打开地图后重新连接助手");
        if(live.Count==zoomAddresses.Count)return;
        int expired=zoomAddresses.Count-live.Count;
        zoomAddresses.Clear();zoomAddresses.AddRange(live);
        Log("已排除 "+expired+" 份失效缩放地址，保留 "+live.Count+" 份完整配置");
    }
    public void ToggleZoom(bool enable) {
        // 同时切换第五档数值和相机档位数量，完整回读通过后才确认成功
        if(!ZoomReady || zoomAddresses.Count==0)throw new IOException("ZOOM_NOT_FOUND：请重新打开修改器");
        bool current=ReadZoomState();
        if(current==enable) {
            ZoomEnabled=current;
            if(!enable)ownsZoomChange=false;
            return;
        }
        IntPtr writer=Native.OpenProcess(0x438,false,process.Id);
        if(writer==IntPtr.Zero)throw Native.Error("WRITE_ACCESS：无法修改地图缩放内存");
        try {
            if(process.HasExited || ReadZoomState()!=current)throw new IOException("PROCESS_CHANGED：操作前游戏状态已变化");
            EnsureZoomCodeAddress();
            if(zoomCodeAddress==0)throw new IOException("ZOOM_CODE_NOT_FOUND：地图缩放代码尚未定位");
            ownsZoomChange=true;
            if(enable) {
                foreach(long location in zoomAddresses)
                    WriteProtected(writer,location+ZoomScaleOffset,BitConverter.GetBytes(0.45f),4);
                WriteProtected(writer,zoomCodeAddress,new[]{ExtendedZoomCount},0x40);
            } else {
                WriteProtected(writer,zoomCodeAddress,new[]{NativeZoomCount},0x40);
                foreach(long location in zoomAddresses)
                    WriteProtected(writer,location+ZoomScaleOffset,BitConverter.GetBytes(0.7f),4);
            }
            ZoomEnabled=ReadZoomState();ZoomReady=true;
            if(ZoomEnabled!=enable)throw new IOException("VERIFY_FAILED：地图缩放状态回读不一致");
            ownsZoomChange=ZoomEnabled;
            ZoomStatus=(ZoomEnabled?"已启用 · 缩放档位为 16/8/4/2/0.45":"已关闭 · 原生缩放档位为 16/8/4/2")
                +" · "+zoomAddresses.Count+" 份配置";
            Log(enable?"已开启更小缩放，地图缩放档位为 16/8/4/2/0.45":"已关闭更小缩放，地图缩放档位已恢复");
        } finally {Native.CloseHandle(writer);}
    }
    private void WriteZoomCache() {
        // 缓存绑定版本、进程和启动时间，新游戏进程不会沿用旧地址
        string cache=Path.Combine(files.Root,"runtime-zoom-location.txt");
        var lines=new List<string> {
            ZoomCacheVersion,
            TravelService.ExeHash,
            process.Id.ToString(),
            process.StartTime.ToUniversalTime().Ticks.ToString(),
            zoomAddresses.Count.ToString()
        };
        foreach(long address in zoomAddresses)lines.Add(address.ToString("X"));
        File.WriteAllLines(cache,lines);
    }
    private void ReleaseZoom() {
        // 游戏进程结束后清空全部缩放定位状态
        zoomAddresses.Clear();zoomCodeAddress=0;ZoomReady=false;ZoomEnabled=false;ownsZoomChange=false;
        ZoomStatus="等待游戏启动";
    }
}
}
