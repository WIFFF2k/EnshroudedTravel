"""从已核验的原始和修改资源生成精确版本补丁"""
from pathlib import Path
import hashlib
import json
import struct

ROOT = Path(__file__).resolve().parents[1]
GAME = Path(json.loads((ROOT / 'config.local.json').read_text(encoding='utf-8'))['gameDirectory'])

def digest(data):
    # 为资源生成版本指纹
    return hashlib.sha256(data).hexdigest()

def make_patch(name):
    # 仅记录实际改变的字节区间，不打包完整游戏资源
    old = (GAME / name).read_bytes()
    new = (ROOT / 'lab/game' / name).read_bytes()
    starts = []
    i = 0
    while i < min(len(old), len(new)):
        if old[i] == new[i]:
            i += 1
            continue
        start = i
        last = i
        while i < min(len(old), len(new)) and i - last < 32:
            if old[i] != new[i]:
                last = i
            i += 1
        starts.append((start, new[start:last + 1]))
    if len(new) > len(old):
        starts.append((len(old), new[len(old):]))
    with (ROOT / 'payload' / (name + '.patch')).open('wb') as f:
        f.write(b'ETP1')
        f.write(bytes.fromhex(digest(old)))
        f.write(bytes.fromhex(digest(new)))
        f.write(struct.pack('<qqi', len(old), len(new), len(starts)))
        for offset, data in starts:
            f.write(struct.pack('<qi', offset, len(data)))
            f.write(data)
    return {'name': name, 'original_sha256': digest(old), 'patched_sha256': digest(new), 'original_size': len(old), 'patched_size': len(new), 'segments': len(starts)}

records = [make_patch(n) for n in ['enshrouded.kfc', 'enshrouded.kfc_resources']]
report = {'build_id': '25988036', 'exe_sha256': digest((GAME / 'enshrouded.exe').read_bytes()), 'files': records}
(ROOT / 'reports/payload.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report, indent=2))
