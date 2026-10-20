using System;
using System.IO;
using System.Globalization;

namespace EnshroudedTravel {
public sealed partial class RuntimeTravelService {
    private const int ZoomArrayOffset = 93364;
    private const int ZoomScaleOffset = 93448;
    private const int ZoomAnchorLength = 16;
    private const int OriginalZoomBits = 0x40000000;
    private const int ReducedZoomBits = 0x3ECCCCCD;
    private const string ZoomTemplateHash = "316d5476d609fe1e4e8c280733daf218a8cc3d8c6a2861717400417a79b694a8";
    private readonly byte[] zoomTemplate;
    private long zoomAddress;
    private bool zoomConflict, ownsZoomChange;
    public bool ZoomReady { get; private set; }
    public bool ZoomEnabled { get; private set; }
    public string ZoomStatus { get; private set; }

    private static byte[] LoadZoomTemplate(string root) {
        // 载入并核验当前版本唯一的地图界面资源模板
        string path=Path.Combine(root,"payload","zoom-original.bin");
        if(!File.Exists(path))throw new IOException("ZOOM_TEMPLATE_MISSING：缺少地图缩放定位模板");
        if(TravelService.HashFile(path)!=ZoomTemplateHash)throw new IOException("ZOOM_TEMPLATE_INVALID：地图缩放定位模板校验失败");
        byte[] data=File.ReadAllBytes(path);
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
        string cache=Path.Combine(files.Root,"runtime-zoom-location.txt");
        if(!File.Exists(cache))return;
        string[] fields=File.ReadAllLines(cache);
        long cached,start;int pid;
        if(fields.Length!=4 || fields[0]!=TravelService.ExeHash || !int.TryParse(fields[1],out pid)
            || !long.TryParse(fields[2],out start)
            || !long.TryParse(fields[3],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out cached)
            || cached<=0 || pid!=process.Id || start!=process.StartTime.ToUniversalTime().Ticks)return;
        if(!ValidateZoomBundle(cached)){
            ZoomStatus="地图缩放的旧位置已失效 · 正在重新定位";
            return;
        }
        zoomAddress=cached;
        RefreshZoomState();
    }
    private void SearchZoomBuffer(byte[] buffer,int available,long bufferAddress,byte[] anchor,int[] skip) {
        // 跳跃搜索唯一特征，再用完整资源排除其他相似数据
        if(zoomConflict || available<anchor.Length)return;
        for(int i=0;i<=available-anchor.Length;) {
            int j=anchor.Length-1;
            while(j>=0 && buffer[i+j]==anchor[j])j--;
            if(j<0 && bufferAddress+i>=ZoomArrayOffset) {
                long candidate=bufferAddress+i-ZoomArrayOffset;
                if(ValidateZoomBundle(candidate)) {
                    if(zoomAddress!=0 && zoomAddress!=candidate) {
                        zoomConflict=true;ZoomReady=false;ZoomEnabled=false;zoomAddress=0;
                        ZoomStatus="发现多份地图缩放配置 · 未修改游戏";
                        Log(ZoomStatus);
                        return;
                    }
                    zoomAddress=candidate;
                    RefreshZoomState();
                }
                i+=anchor.Length;
            } else i+=skip[buffer[i+anchor.Length-1]];
        }
    }
    private bool ValidateZoomBundle(long location) {
        // 完整比对资源内容，仅允许最远缩放档位处于原版或五倍状态
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
        // 每次操作前后都重新核验完整资源与最远档位
        if(zoomAddress==0)throw new IOException("ZOOM_NOT_FOUND：地图缩放数据尚未定位");
        byte[] data=ReadAt(zoomAddress,zoomTemplate.Length);
        if(data==null || data.Length!=zoomTemplate.Length)throw new IOException("ZOOM_UNREADABLE：无法读取地图缩放数据");
        int scale=BitConverter.ToInt32(data,ZoomScaleOffset);
        if(scale!=OriginalZoomBits && scale!=ReducedZoomBits)throw new IOException("ZOOM_CHANGED：地图缩放数据已变化，未继续写入");
        for(int i=0;i<data.Length;i++) {
            if(i>=ZoomScaleOffset && i<ZoomScaleOffset+4)continue;
            if(data[i]!=zoomTemplate[i])throw new IOException("ZOOM_CHANGED：地图缩放资源内容已变化，未继续写入");
        }
        return scale==ReducedZoomBits;
    }
    private void RefreshZoomState() {
        // 缩放状态独立刷新，不影响已经可用的传送定位
        if(zoomAddress==0)return;
        try {
            ZoomEnabled=ReadZoomState();ZoomReady=true;
            ZoomStatus=ZoomEnabled?"已启用 · 最远视野为原版五倍":"已关闭 · 最远档位已恢复";
        } catch(Exception ex) {
            ZoomReady=false;ZoomEnabled=false;
            ZoomStatus="地图缩放状态已变化 · 请重新校验";
            Log("地图缩放读取失败："+ex.Message);
        }
    }
    public void ToggleZoom(bool enable) {
        // 仅修改最远档缩放值，完整资源回读通过后才确认成功
        if(!ZoomReady || zoomAddress==0)throw new IOException("ZOOM_NOT_FOUND：请重新校验地图缩放数据");
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
            uint old;IntPtr target=new IntPtr(zoomAddress+ZoomScaleOffset);
            if(!Native.VirtualProtectEx(writer,target,(UIntPtr)4,4,out old))throw Native.Error("PAGE_ACCESS：无法修改地图缩放页权限");
            try {
                ownsZoomChange=true;
                byte[] value=BitConverter.GetBytes(enable?0.4f:2.0f);
                UIntPtr written;
                if(!Native.WriteProcessMemory(writer,target,value,(UIntPtr)value.Length,out written) || written.ToUInt64()!=(ulong)value.Length)
                    throw Native.Error("WRITE_FAILED：地图缩放值写入失败");
            } finally {
                uint ignored;
                if(!Native.VirtualProtectEx(writer,target,(UIntPtr)4,old,out ignored))throw Native.Error("PROTECTION_RESTORE：恢复地图缩放页权限失败");
            }
            ZoomEnabled=ReadZoomState();ZoomReady=true;
            if(ZoomEnabled!=enable)throw new IOException("VERIFY_FAILED：地图缩放状态回读不一致");
            ownsZoomChange=ZoomEnabled;
            ZoomStatus=ZoomEnabled?"已启用 · 最远视野为原版五倍":"已关闭 · 最远档位已恢复";
            Log(enable?"已开启更小缩放，重新打开地图后最远视野为原版五倍":"已关闭更小缩放，地图最远档位已恢复");
        } finally {Native.CloseHandle(writer);}
    }
    private void WriteZoomCache() {
        // 缓存绑定版本、进程和启动时间，新游戏进程不会沿用旧地址
        string cache=Path.Combine(files.Root,"runtime-zoom-location.txt");
        File.WriteAllLines(cache,new[]{TravelService.ExeHash,process.Id.ToString(),process.StartTime.ToUniversalTime().Ticks.ToString(),zoomAddress.ToString("X")});
    }
    private void ReleaseZoom() {
        // 游戏进程结束后清空全部缩放定位状态
        zoomAddress=0;zoomConflict=false;ZoomReady=false;ZoomEnabled=false;ownsZoomChange=false;
        ZoomStatus="等待游戏启动";
    }
}
}
