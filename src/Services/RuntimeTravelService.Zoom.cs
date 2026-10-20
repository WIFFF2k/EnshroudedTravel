using System;
using System.IO;
using System.Globalization;

namespace EnshroudedTravel {
public sealed partial class RuntimeTravelService {
    private const int ZoomArrayOffset = 93364;
    private const int ZoomScaleOffset = 93476;
    private const int ZoomAnchorLength = 16;
    private const int ZoomCodeRva = 0xC1CF83;
    private const byte NativeZoomCount = 4;
    private const byte ExtendedZoomCount = 5;
    private const int OriginalZoomBits = 0x3F333333;
    private const int ReducedZoomBits = 0x3F000000;
    private const string ZoomTemplateHash = "316d5476d609fe1e4e8c280733daf218a8cc3d8c6a2861717400417a79b694a8";
    private readonly byte[] zoomTemplate;
    private long zoomAddress, zoomCodeAddress;
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
        EnsureZoomCodeAddress();
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
        if(zoomAddress==0)throw new IOException("ZOOM_NOT_FOUND：地图缩放数据尚未定位");
        EnsureZoomCodeAddress();
        if(zoomCodeAddress==0)throw new IOException("ZOOM_CODE_NOT_FOUND：地图缩放代码尚未定位");
        byte[] data=ReadAt(zoomAddress,zoomTemplate.Length);
        if(data==null || data.Length!=zoomTemplate.Length)throw new IOException("ZOOM_UNREADABLE：无法读取地图缩放数据");
        int scale=BitConverter.ToInt32(data,ZoomScaleOffset);
        if(scale!=OriginalZoomBits && scale!=ReducedZoomBits)throw new IOException("ZOOM_CHANGED：地图缩放数据已变化，未继续写入");
        for(int i=0;i<data.Length;i++) {
            if(i>=ZoomScaleOffset && i<ZoomScaleOffset+4)continue;
            if(data[i]!=zoomTemplate[i])throw new IOException("ZOOM_CHANGED：地图缩放资源内容已变化，未继续写入");
        }
        byte[] code=ReadAt(zoomCodeAddress,1);
        if(code==null || (code[0]!=NativeZoomCount && code[0]!=ExtendedZoomCount))
            throw new IOException("ZOOM_CODE_CHANGED：地图缩放档位代码已变化，未继续写入");
        bool bundleEnabled=scale==ReducedZoomBits;
        bool codeEnabled=code[0]==ExtendedZoomCount;
        if(bundleEnabled!=codeEnabled)throw new IOException("ZOOM_PATCH_CHANGED：地图缩放代码与资源状态不一致，请重新校验");
        return bundleEnabled;
    }
    private void RefreshZoomState() {
        // 缩放状态独立刷新，不影响已经可用的传送定位
        if(zoomAddress==0)return;
        try {
            ZoomEnabled=ReadZoomState();ZoomReady=true;
            ZoomStatus=ZoomEnabled?"已启用 · 缩放档位为 16/8/4/2/0.5":"已关闭 · 原生缩放档位为 16/8/4/2";
        } catch(Exception ex) {
            ZoomReady=false;ZoomEnabled=false;
            ZoomStatus="地图缩放状态已变化 · 请重新校验";
            Log("地图缩放读取失败："+ex.Message);
        }
    }
    public void ToggleZoom(bool enable) {
        // 同时切换第五档数值和相机档位数量，完整回读通过后才确认成功
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
            EnsureZoomCodeAddress();
            if(zoomCodeAddress==0)throw new IOException("ZOOM_CODE_NOT_FOUND：地图缩放代码尚未定位");
            ownsZoomChange=true;
            if(enable) {
                WriteProtected(writer,zoomAddress+ZoomScaleOffset,BitConverter.GetBytes(0.5f),4);
                WriteProtected(writer,zoomCodeAddress,new[]{ExtendedZoomCount},0x40);
            } else {
                WriteProtected(writer,zoomCodeAddress,new[]{NativeZoomCount},0x40);
                WriteProtected(writer,zoomAddress+ZoomScaleOffset,BitConverter.GetBytes(0.7f),4);
            }
            ZoomEnabled=ReadZoomState();ZoomReady=true;
            if(ZoomEnabled!=enable)throw new IOException("VERIFY_FAILED：地图缩放状态回读不一致");
            ownsZoomChange=ZoomEnabled;
            ZoomStatus=ZoomEnabled?"已启用 · 缩放档位为 16/8/4/2/0.5":"已关闭 · 原生缩放档位为 16/8/4/2";
            Log(enable?"已开启更小缩放，地图缩放档位为 16/8/4/2/0.5":"已关闭更小缩放，地图缩放档位已恢复");
        } finally {Native.CloseHandle(writer);}
    }
    private static void WriteProtected(IntPtr writer,long location,byte[] value,uint protection) {
        // 临时修改目标页权限后完整写入，并在任何结果下恢复原权限
        uint old;
        IntPtr target=new IntPtr(location);
        if(!Native.VirtualProtectEx(writer,target,(UIntPtr)value.Length,protection,out old))throw Native.Error("PAGE_ACCESS：无法修改地图缩放页权限");
        try {
            UIntPtr written;
            if(!Native.WriteProcessMemory(writer,target,value,(UIntPtr)value.Length,out written) || written.ToUInt64()!=(ulong)value.Length)
                throw Native.Error("WRITE_FAILED：地图缩放写入失败");
        } finally {
            uint ignored;
            if(!Native.VirtualProtectEx(writer,target,(UIntPtr)value.Length,old,out ignored))throw Native.Error("PROTECTION_RESTORE：恢复地图缩放页权限失败");
        }
    }
    private void WriteZoomCache() {
        // 缓存绑定版本、进程和启动时间，新游戏进程不会沿用旧地址
        string cache=Path.Combine(files.Root,"runtime-zoom-location.txt");
        File.WriteAllLines(cache,new[]{TravelService.ExeHash,process.Id.ToString(),process.StartTime.ToUniversalTime().Ticks.ToString(),zoomAddress.ToString("X")});
    }
    private void ReleaseZoom() {
        // 游戏进程结束后清空全部缩放定位状态
        zoomAddress=0;zoomCodeAddress=0;zoomConflict=false;ZoomReady=false;ZoomEnabled=false;ownsZoomChange=false;
        ZoomStatus="等待游戏启动";
    }
}
}
