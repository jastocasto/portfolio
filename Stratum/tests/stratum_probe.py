#! python3
# stratum_probe.py — read-only inspection of the loaded Stratum BIM plugin.
#
# Run in Rhino 8: ScriptEditor -> open this file -> Run.
# It draws nothing, changes nothing, and prompts for nothing.
# It writes a report next to itself and prints the path at the end.
#
# Purpose: establish the real API surface and the seeded catalogue, so a
# geometry-driving test can be written against facts instead of guesses.

import os, sys, traceback, datetime

OUT = []
def w(s=""):
    OUT.append(str(s))
    print(s)

def rule(t):
    w(); w("=" * 72); w(t); w("=" * 72)

w("STRATUM PROBE  %s" % datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"))

# ---------------------------------------------------------------- environment
rule("1. ENVIRONMENT")
try:
    import Rhino
    import scriptcontext as sc
    doc = sc.doc
    w("Rhino version   : %s" % Rhino.RhinoApp.Version)
    w("Executable      : %s" % Rhino.RhinoApp.ExeVersion)
    w("Document units  : %s" % doc.ModelUnitSystem)
    w("Document path   : %s" % (doc.Path or "(unsaved)"))
    s = Rhino.DocObjects.ObjectEnumeratorSettings(); s.DeletedObjects = False
    w("Alive objects   : %d" % sum(1 for _ in doc.Objects.GetObjectList(s)))
    w("Layers          : %d" % doc.Layers.Count)
except Exception:
    w("FAILED: " + traceback.format_exc())

# ---------------------------------------------------------------- plugin
rule("2. PLUGIN REGISTRATION")
strat_asm = None
try:
    import Rhino.PlugIns as P
    for gid, nm in P.PlugIn.GetInstalledPlugIns().items():
        low = nm.lower()
        if "stratum" in low or "resdes" in low:
            info = P.PlugIn.GetPlugInInfo(gid)
            w("%-16s loaded=%-5s id=%s" % (nm, info.IsLoaded if info else "?", gid))
            w("                 path=%s" % (info.FileName if info else "?"))
            if info and os.path.exists(info.FileName):
                st = os.stat(info.FileName)
                w("                 %d bytes, %s" % (
                    st.st_size,
                    datetime.datetime.fromtimestamp(st.st_mtime).strftime("%Y-%m-%d %H:%M")))
except Exception:
    w("FAILED: " + traceback.format_exc())

# ---------------------------------------------------------------- assembly
rule("3. LOADED ASSEMBLY")
try:
    import System
    for a in System.AppDomain.CurrentDomain.GetAssemblies():
        try:
            n = a.GetName().Name
        except Exception:
            continue
        if n and n.lower().startswith("stratum"):
            strat_asm = a
            w("Assembly : %s" % a.FullName)
            try:
                w("Location : %s" % a.Location)
            except Exception:
                pass
    if strat_asm is None:
        w("NOT FOUND — Stratum's assembly is not in the AppDomain.")
        w("If section 2 says loaded=True this is a lazy-load: run BimHelp once, then re-run.")
except Exception:
    w("FAILED: " + traceback.format_exc())

# ---------------------------------------------------------------- API surface
rule("4. PUBLIC API SURFACE")
types_by_ns = {}
if strat_asm is not None:
    try:
        for t in strat_asm.GetTypes():
            if not t.IsPublic:
                continue
            ns = t.Namespace or "(none)"
            types_by_ns.setdefault(ns, []).append(t)
        for ns in sorted(types_by_ns):
            w(); w("-- %s" % ns)
            for t in sorted(types_by_ns[ns], key=lambda x: x.Name):
                kind = "enum" if t.IsEnum else ("static class" if t.IsAbstract and t.IsSealed else "class")
                w("   %s %s" % (kind, t.Name))
                if t.IsEnum:
                    w("        values: %s" % ", ".join(str(v) for v in System.Enum.GetNames(t)))
                    continue
                # public static methods are what a script can drive
                import System.Reflection as R
                flags = R.BindingFlags.Public | R.BindingFlags.Static | R.BindingFlags.DeclaredOnly
                for m in t.GetMethods(flags):
                    ps = ", ".join("%s %s" % (p.ParameterType.Name, p.Name) for p in m.GetParameters())
                    w("        static %s %s(%s)" % (m.ReturnType.Name, m.Name, ps))
                # public instance properties tell us the data model
                pflags = R.BindingFlags.Public | R.BindingFlags.Instance | R.BindingFlags.DeclaredOnly
                props = t.GetProperties(pflags)
                if props:
                    w("        props: %s" % ", ".join(
                        "%s:%s" % (p.Name, p.PropertyType.Name) for p in props))
    except Exception:
        w("FAILED: " + traceback.format_exc())
else:
    w("skipped — no assembly")

# ---------------------------------------------------------------- catalogue
rule("5. SEEDED CATALOGUE")
if strat_asm is not None:
    try:
        import System.Reflection as R
        cd = strat_asm.GetType("Stratum.Core.CatalogDefaults") or \
             next((t for t in strat_asm.GetTypes() if t.Name == "CatalogDefaults"), None)
        if cd is None:
            w("CatalogDefaults not found by name; types seen in section 4.")
        else:
            w("CatalogDefaults found: %s" % cd.FullName)
            flags = R.BindingFlags.Public | R.BindingFlags.Static
            for m in cd.GetMethods(flags):
                if m.GetParameters().Length == 0 and m.ReturnType.Name != "Void":
                    w("   trying %s() -> %s" % (m.Name, m.ReturnType.Name))
                    try:
                        res = m.Invoke(None, None)
                        try:
                            items = list(res)
                            w("      %d items" % len(items))
                            for it in items[:60]:
                                bits = []
                                for pn in ("Code", "Name", "ProductName", "ThicknessIn",
                                           "Function", "IsCore", "RPerIn", "RValue",
                                           "CostPerSf", "Layers"):
                                    pi = it.GetType().GetProperty(pn)
                                    if pi is not None:
                                        v = pi.GetValue(it, None)
                                        if pn == "Layers" and v is not None:
                                            try:
                                                v = "%d layers" % len(list(v))
                                            except Exception:
                                                pass
                                        bits.append("%s=%s" % (pn, v))
                                w("      - " + "  ".join(bits) if bits else "      - %s" % it)
                        except TypeError:
                            w("      (not enumerable) %s" % res)
                    except Exception as e:
                        w("      invoke failed: %s" % e)
    except Exception:
        w("FAILED: " + traceback.format_exc())
else:
    w("skipped — no assembly")

# ---------------------------------------------------------------- solver
rule("6. WALLSOLVER — THE CLAIM THE PLUGIN RESTS ON")
w("Expected: with CoreCenter, the structural core's centre sits on the baseline;")
w("thickening an exterior layer moves only the exterior face.")
if strat_asm is not None:
    try:
        ws = next((t for t in strat_asm.GetTypes() if t.Name == "WallSolver"), None)
        if ws is None:
            w("WallSolver not found.")
        else:
            import System.Reflection as R
            flags = R.BindingFlags.Public | R.BindingFlags.Static
            for m in ws.GetMethods(flags):
                ps = ", ".join("%s %s" % (p.ParameterType.Name, p.Name) for p in m.GetParameters())
                w("   static %s %s(%s)" % (m.ReturnType.Name, m.Name, ps))
            w()
            w("Signatures captured. A driving script can now be written against them")
            w("rather than guessed at.")
    except Exception:
        w("FAILED: " + traceback.format_exc())
else:
    w("skipped — no assembly")

# ---------------------------------------------------------------- commands
rule("7. REGISTERED COMMANDS")
try:
    import Rhino
    names = []
    for cn in Rhino.Commands.Command.GetCommandNames(True, True):
        if cn.lower().startswith("bim"):
            names.append(cn)
    w("Bim* commands registered: %d" % len(names))
    for n in sorted(names):
        w("   " + n)
    if not names:
        w("NONE — the plugin is registered but its commands are not available.")
        w("That is the finding; do not proceed to geometry tests.")
except Exception:
    w("FAILED: " + traceback.format_exc())

# ---------------------------------------------------------------- write
rule("REPORT")
try:
    here = os.path.dirname(os.path.abspath(__file__))
except Exception:
    here = os.path.expanduser("~")
dest = os.path.join(here, "stratum-probe-report.txt")
try:
    with open(dest, "w", encoding="utf-8") as f:
        f.write("\n".join(OUT))
    print()
    print("WROTE: %s" % dest)
except Exception:
    print("could not write report: " + traceback.format_exc())
