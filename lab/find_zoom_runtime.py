"""只读搜索当前地图实际使用的五档缩放数组"""
import ctypes as c,struct,time,sys,json
from pathlib import Path
class Region(c.Structure):_fields_=[('base',c.c_void_p),('allocation',c.c_void_p),('ap',c.c_uint32),('part',c.c_uint16),('size',c.c_size_t),('state',c.c_uint32),('protect',c.c_uint32),('kind',c.c_uint32)]
k=c.WinDLL('kernel32',use_last_error=True);k.OpenProcess.argtypes=[c.c_uint32,c.c_int,c.c_uint32];k.OpenProcess.restype=c.c_void_p
k.VirtualQueryEx.argtypes=[c.c_void_p,c.c_void_p,c.POINTER(Region),c.c_size_t];k.VirtualQueryEx.restype=c.c_size_t
k.ReadProcessMemory.argtypes=[c.c_void_p,c.c_void_p,c.c_void_p,c.c_size_t,c.POINTER(c.c_size_t)]
pid=int(sys.argv[1]);h=k.OpenProcess(0x410,False,pid)
if not h:raise OSError(c.get_last_error())
needle=struct.pack('<ffff',16.,8.,4.,2.);buf=c.create_string_buffer(4*1024*1024);n=c.c_size_t();regions=[];cursor=0;matches=[];done=0;last=time.monotonic();start=last
try:
 while True:
  r=Region()
  if not k.VirtualQueryEx(h,c.c_void_p(cursor),c.byref(r),c.sizeof(r)):break
  base=r.base or 0;end=base+r.size
  if r.state==0x1000 and not r.protect&0x100 and r.protect&255 in (2,4,8,32,64,128):regions.append((base,r.size))
  if end<=cursor:break
  cursor=end
 total=sum(z for _,z in regions)
 for base,size in regions:
  pos=base;end=base+size
  while pos<end:
   count=min(len(buf),end-pos)
   if k.ReadProcessMemory(h,c.c_void_p(pos),buf,count,c.byref(n)):
    raw=buf.raw[:n.value];idx=raw.find(needle)
    while idx>=0:
     addr=pos+idx;tail=c.create_string_buffer(4)
     if addr+140<end and k.ReadProcessMemory(h,c.c_void_p(addr+112),tail,4,c.byref(n)) and abs(struct.unpack('<f',tail.raw)[0]-.7)<1e-5:
      rec={'address':hex(addr),'five':[struct.unpack_from('<f',raw if idx+i+4<=len(raw) else bytes())[0] for i in []]}
      block=c.create_string_buffer(140)
      if k.ReadProcessMemory(h,c.c_void_p(addr),block,140,c.byref(n)):
       rec['values']=[struct.unpack_from('<f',block.raw,i*28)[0] for i in range(5)]
       if rec not in matches:matches.append(rec);print(rec,flush=True)
     idx=raw.find(needle,idx+1)
   step=count-15 if pos+count<end else count;pos+=step;done+=step
   if time.monotonic()-last>5:print('progress',round(done/total*100,1),'seconds',round(time.monotonic()-start,1),flush=True);last=time.monotonic()
 result={'pid':pid,'matches':matches,'seconds':time.monotonic()-start};Path('reports/zoom-runtime-candidates.json').write_text(json.dumps(result,indent=2),encoding='utf-8');print(result,flush=True)
finally:k.CloseHandle(h)
