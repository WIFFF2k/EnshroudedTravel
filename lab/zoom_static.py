"""核对地图配置偏移在当前版本机器代码中的引用"""
import sys,struct,bisect
from pathlib import Path
sys.path.insert(0,'lab/analysis-deps')
import pefile,capstone
p=pefile.PE('lab/game/enshrouded.exe');base=p.OPTIONAL_HEADER.ImageBase
m=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64)
entries=sorted((e.struct.BeginAddress,e.struct.EndAddress) for e in p.DIRECTORY_ENTRY_EXCEPTION);starts=[e[0] for e in entries]
out=[]
for section in p.sections:
    if not section.Characteristics&0x20000000:continue
    data=section.get_data();rva=section.VirtualAddress
    for offset in [60432,60856,60860]:
        needle=struct.pack('<I',offset);at=-1
        while True:
            at=data.find(needle,at+1)
            if at<0:break
            target=rva+at;idx=bisect.bisect_right(starts,target)-1
            if idx<0:continue
            a,end=entries[idx]
            if target>=end:continue
            ins=list(m.disasm(p.get_data(a,min(end-a,target-a+80)),base+a))
            for j,i in enumerate(ins):
                if i.address-base<=target<i.address-base+i.size and hex(offset)[2:] in i.op_str:
                    out.append('OFFSET '+hex(offset)+' FUNCTION '+hex(a))
                    out.extend(hex(x.address-base)+' '+x.mnemonic+' '+x.op_str for x in ins[max(0,j-8):j+10])
Path('reports/zoom-static.txt').write_text(chr(10).join(out),encoding='utf-8');print(chr(10).join(out))
