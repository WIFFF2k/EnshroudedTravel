"""只读定位当前进程中的完整地图界面资源"""
import ctypes as c
from ctypes import wintypes as w
import json, struct, time, sys
from pathlib import Path

class Region(c.Structure):
    _fields_=[('base',c.c_void_p),('allocation',c.c_void_p),('allocation_protect',w.DWORD),('partition',w.WORD),('size',c.c_size_t),('state',w.DWORD),('protect',w.DWORD),('kind',w.DWORD)]

k=c.WinDLL('kernel32',use_last_error=True)
k.OpenProcess.argtypes=[w.DWORD,w.BOOL,w.DWORD];k.OpenProcess.restype=c.c_void_p
k.ReadProcessMemory.argtypes=[c.c_void_p,c.c_void_p,c.c_void_p,c.c_size_t,c.POINTER(c.c_size_t)]
k.VirtualQueryEx.argtypes=[c.c_void_p,c.c_void_p,c.POINTER(Region),c.c_size_t];k.VirtualQueryEx.restype=c.c_size_t
k.CloseHandle.argtypes=[c.c_void_p]
pid=int(sys.argv[1]);h=k.OpenProcess(0x410,False,pid)
if not h:raise OSError(c.get_last_error())
template=Path('lab/zoom-export/zoom-bundle-1.bin').read_bytes()
array_field=60856
rel,count=struct.unpack_from('<II',template,array_field)
anchor=array_field+rel
print('zoom array',anchor,'values',[struct.unpack_from('<f',template,anchor+i*28)[0] for i in range(count)],flush=True)
pattern=template[anchor:anchor+140]
buf=c.create_string_buffer(4*1024*1024); n=c.c_size_t();cursor=0;regions=[];found=[];done=0;start=time.monotonic();last=start
try:
    while True:
        r=Region()
        if not k.VirtualQueryEx(h,c.c_void_p(cursor),c.byref(r),c.sizeof(r)):break
        end=(r.base or 0)+r.size
        if r.state==0x1000 and not r.protect&0x100 and r.protect&255 in (2,4,8,32,64,128):regions.append((r.base,r.size))
        if end<=cursor:break
        cursor=end
    total=sum(s for _,s in regions)
    for base,size in regions:
        pos=base;end=base+size
        while pos<end:
            length=min(len(buf),end-pos)
            if k.ReadProcessMemory(h,c.c_void_p(pos),buf,length,c.byref(n)):
                raw=buf.raw[:n.value];i=raw.find(pattern)
                while i>=0:
                    addr=pos+i-anchor;data=c.create_string_buffer(len(template))
                    if k.ReadProcessMemory(h,c.c_void_p(addr),data,len(template),c.byref(n)) and data.raw==template:
                        if addr not in found:found.append(addr);print('完整资源匹配',hex(addr),flush=True)
                    i=raw.find(pattern,i+1)
            step=length-len(pattern)+1 if pos+length<end else length
            pos+=step;done+=step
            if time.monotonic()-last>5:
                print('进度',round(done/total*100,1),'耗时',round(time.monotonic()-start,1),flush=True);last=time.monotonic()
    result={'pid':pid,'matches':[hex(a) for a in found],'array_offset':anchor,'last_scale_offset':anchor+112,'seconds':time.monotonic()-start}
    Path('reports/zoom-location.json').write_text(json.dumps(result,indent=2),encoding='utf-8');print(result,flush=True)
finally:k.CloseHandle(h)
