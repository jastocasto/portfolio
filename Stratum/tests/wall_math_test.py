"""
Executable specification for Stratum's wall arithmetic.

This is a line-for-line port of the maths in

    Core/WallAssembly.cs      BaselineStation, StationOf, CoreIndex
    Modeling/WallSolver.cs    OffsetOfStation, LayerRanges, FaceOffsets
    Core/Units.cs             FormatInches, TryParseInches

so the invariants the plug-in is built on can be checked without Rhino, a
compiler or Windows. It exists because the central promise of Stratum - that
thickening a layer moves the face on that layer's side of the core and nothing
else - is arithmetic, and arithmetic can be proved rather than eyeballed.

Run it with:  python3 tests/wall_math_test.py

If you change the justification maths in C#, change it here too and re-run.
"""
import math

CORE_CENTER, EXT_FACE, INT_FACE, WALL_CENTER, EXT_CORE, INT_CORE = 'CoreCenter','ExteriorFace','InteriorFace','WallCenter','ExteriorCore','InteriorCore'

class Layer:
    def __init__(self, name, t, core=False, enabled=True, func='Finish'):
        self.name, self.t, self.core, self.enabled, self.func = name, t, core, enabled, func

class Asm:
    def __init__(self, layers): self.layers = layers
    @property
    def active(self): return [l for l in self.layers if l.enabled]
    @property
    def total(self): return sum(max(0.0, l.t) for l in self.active)
    @property
    def core_index(self):
        if not self.layers: return -1
        for i,l in enumerate(self.layers):
            if l.enabled and l.core: return i
        for i,l in enumerate(self.layers):
            if l.enabled and l.func=='Structure': return i
        best, bi = -1e30, -1
        for k,l in enumerate(self.layers):
            if not l.enabled: continue
            if l.t > best: best, bi = l.t, k
        return bi
    def station_of(self, idx):
        u = 0.0
        for i in range(min(idx, len(self.layers))):
            if not self.layers[i].enabled: continue
            u += max(0.0, self.layers[i].t)
        return u
    def baseline_station(self, j):
        core, total = self.core_index, self.total
        if j == EXT_FACE: return 0.0
        if j == INT_FACE: return total
        if j == WALL_CENTER: return total*0.5
        if j == EXT_CORE: return self.station_of(core) if core>=0 else total*0.5
        if j == INT_CORE: return (self.station_of(core)+max(0.0,self.layers[core].t)) if core>=0 else total*0.5
        return (self.station_of(core)+max(0.0,self.layers[core].t)*0.5) if core>=0 else total*0.5

def offset_of_station(a, j, flipped, u, i2m=1.0):
    u0 = a.baseline_station(j); d = -1.0 if flipped else 1.0
    return d*(u0-u)*i2m

def layer_ranges(a, j, flipped, i2m=1.0):
    out=[]; u=0.0
    for i,l in enumerate(a.layers):
        if not l.enabled: continue
        t = max(0.0, l.t)
        if t <= 1e-9: continue
        o1 = offset_of_station(a,j,flipped,u,i2m); o2 = offset_of_station(a,j,flipped,u+t,i2m)
        out.append((i, l.name, min(o1,o2), max(o1,o2))); u += t
    return out

def face_offsets(a,j,flipped,i2m=1.0):
    return offset_of_station(a,j,flipped,0.0,i2m), offset_of_station(a,j,flipped,a.total,i2m)

def fmt_in(v):
    neg = v<0; v=abs(v); whole=int(math.floor(v+1e-9)); frac=v-whole
    num=int(round(frac*32.0)); den=32
    if num==32: whole+=1; num=0
    while num>0 and num%2==0: num//=2; den//=2
    if num==0: s=str(whole)
    elif whole==0: s=f"{num}/{den}"
    else: s=f"{whole}-{num}/{den}"
    return ('-' if neg else '')+s+'"'

def parse_in(text):
    if not text or not text.strip(): return None
    s=text.strip().lower().replace('"','').replace('in','').strip()
    if s.endswith('mm'):
        try: return float(s[:-2].strip())/25.4
        except: return None
    if s.endswith('cm'):
        try: return float(s[:-2].strip())/2.54
        except: return None
    total=0.0
    if "'" in s:
        tick=s.index("'")
        try: total += float(s[:tick].strip())*12.0
        except: pass
        s=s[tick+1:].lstrip('- ')
    if not s: return total
    s=s.replace('-',' ')
    for part in s.split():
        if '/' in part:
            fp=part.split('/')
            if len(fp)==2:
                try:
                    n,d=float(fp[0]),float(fp[1])
                    if abs(d)>1e-9: total+=n/d
                    else: return None
                except: return None
            else: return None
        else:
            try: total+=float(part)
            except: return None
    return total

# ---------------- the W1 assembly from CatalogDefaults ----------------
def W1(ply=0.4375, gyp=0.5):
    return Asm([
        Layer("fiber cement", 0.3125, func='Cladding'),
        Layer("furring",      0.75,   func='Furring'),
        Layer("WRB",          0.01,   func='Membrane'),
        Layer("polyiso",      2.0,    func='Insulation'),
        Layer("sheathing",    ply,    func='Sheathing'),
        Layer("2x6 stud",     5.5,    core=True, func='Structure'),
        Layer("smart VB",     0.012,  func='Membrane'),
        Layer("gypsum",       gyp,    func='Finish'),
    ])

fails=[]
total=[0]
def check(label, cond, detail=""):
    total[0]+=1
    print(("PASS  " if cond else "FAIL  ")+label+(("   "+detail) if detail else ""))
    if not cond: fails.append(label)

print("=== 1. layer ranges are contiguous, ordered, and sum to total ===")
for j in [CORE_CENTER, EXT_FACE, INT_FACE, WALL_CENTER, EXT_CORE, INT_CORE]:
    for flip in (False, True):
        r = layer_ranges(W1(), j, flip)
        widths = sum(hi-lo for _,_,lo,hi in r)
        srt = sorted(r, key=lambda x: x[2])
        gaps = all(abs(srt[i][3]-srt[i+1][2])<1e-12 for i in range(len(srt)-1))
        check(f"{j:13s} flip={flip!s:5s} contiguous & total",
              abs(widths - W1().total)<1e-9 and gaps,
              f"sum={widths:.4f} total={W1().total:.4f}")

print()
print("=== 2. CoreCenter: the core's centre line sits exactly on the baseline ===")
for ply in (0.4375, 0.5, 0.75, 1.5):
    for gyp in (0.5, 0.625, 1.25):
        a=W1(ply,gyp); r=layer_ranges(a, CORE_CENTER, False)
        core=[x for x in r if x[1]=="2x6 stud"][0]
        mid=(core[2]+core[3])/2
        check(f"ply={ply} gyp={gyp}: core midpoint on baseline", abs(mid)<1e-12, f"mid={mid:+.6f}")

print()
print("=== 3. thickening EXTERIOR sheathing pushes the exterior face out only ===")
e0,i0 = face_offsets(W1(0.5,0.5), CORE_CENTER, False)
e1,i1 = face_offsets(W1(0.75,0.5), CORE_CENTER, False)
check("exterior face moves out exactly 1/4in", abs((e1-e0)-0.25)<1e-12, f"{e0:+.4f} -> {e1:+.4f}")
check("interior face does NOT move",          abs(i1-i0)<1e-12,        f"{i0:+.4f} -> {i1:+.4f}")

print()
print("=== 4. thickening INTERIOR gypsum pushes the interior face in only ===")
e2,i2 = face_offsets(W1(0.5,1.25), CORE_CENTER, False)
check("interior face moves in exactly 3/4in", abs((i0-i2)-0.75)<1e-12, f"{i0:+.4f} -> {i2:+.4f}")
check("exterior face does NOT move",          abs(e2-e0)<1e-12,        f"{e0:+.4f} -> {e2:+.4f}")

print()
print("=== 5. ExteriorFace justification holds the exterior face at 0 ===")
for ply in (0.4375,0.75,1.5):
    e,_ = face_offsets(W1(ply), EXT_FACE, False)
    check(f"ply={ply}: exterior face pinned at 0", abs(e)<1e-12, f"e={e:+.6f}")

print()
print("=== 6. flipping mirrors every offset about the baseline ===")
ra=layer_ranges(W1(), CORE_CENTER, False); rb=layer_ranges(W1(), CORE_CENTER, True)
ok=all(abs(ra[k][2]+rb[k][3])<1e-12 and abs(ra[k][3]+rb[k][2])<1e-12 for k in range(len(ra)))
check("flip is an exact mirror", ok)

print()
print("=== 7. disabled layers drop out without moving the core ===")
a=W1(); a.layers[0].enabled=False   # remove the cladding
r=layer_ranges(a, CORE_CENTER, False); core=[x for x in r if x[1]=="2x6 stud"][0]
check("core still centred after disabling cladding", abs((core[2]+core[3])/2)<1e-12)
check("total thickness dropped by the cladding", abs(a.total-(W1().total-0.3125))<1e-12)

print()
print("=== 8. inch formatting / parsing round-trips ===")
for v in [0.5,0.625,0.4375,0.75,3.5,5.5,7.625,0.3125,11.875,0.03125,96.0,0.0]:
    s=fmt_in(v); back=parse_in(s)
    check(f"{v:>9} -> {s:>12} -> {back}", back is not None and abs(back-v)<1e-9)
for txt,exp in [("3/4",0.75),("5 1/2",5.5),("5-1/2",5.5),("0.75",0.75),("1'-6\"",18.0),
                ("2'",24.0),("140mm",140/25.4),("8'",96.0),("garbage",None)]:
    got=parse_in(txt)
    ok = (got is None and exp is None) or (got is not None and exp is not None and abs(got-exp)<1e-9)
    check(f"parse {txt!r:12} -> {got}", ok, f"expected {exp}")

print()
if fails:
    print(f"{len(fails)} of {total[0]} CHECKS FAILED:")
    for f in fails: print("   -", f)
    raise SystemExit(1)
print(f"ALL {total[0]} CHECKS PASSED")
