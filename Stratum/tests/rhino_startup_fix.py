#! python3
# rhino_startup_fix.py — ONE-TIME SETTINGS FIX. This one DOES change things.
#
# Run in Rhino 8: ScriptEditor -> open -> Run.
#
# It does exactly two things, both reversible, and prints before/after:
#   1. Clears the startup template, so Rhino opens a genuinely blank model.
#   2. Finds plug-ins whose load protection makes Rhino ask on startup, and
#      sets them to load silently — so Rhino reaches a usable window without
#      waiting on modal dialogs.
#
# Why: a modal dialog at startup blocks Rhino's MCP plug-in from advertising
# itself, so an agent driving Rhino sees "no Rhino running" until the dialogs
# are dismissed by hand. And a template-loaded document is not a clean test bed.
#
# It touches no geometry and no document.

import traceback
import Rhino
import System

OUT = []
def w(s=""):
    OUT.append(str(s)); print(s)

w("RHINO STARTUP FIX")
w("Rhino %s" % Rhino.RhinoApp.Version)
w()

# ---------------------------------------------------------------- 1. template
w("=" * 68)
w("1. STARTUP TEMPLATE")
w("=" * 68)
try:
    fs = Rhino.ApplicationSettings.FileSettings
    before = fs.TemplateFile
    w("before : %r" % (before or "(none)"))
    if before:
        fs.TemplateFile = ""
        w("after  : %r" % (fs.TemplateFile or "(none — Rhino will open blank)"))
        w()
        w("Reverted by setting it back to the path above, or in")
        w("Tools > Options > Files.")
    else:
        w("already blank — nothing to do")
except Exception:
    w("FAILED:\n" + traceback.format_exc())
    w("Set it by hand instead: Tools > Options > Files > template file.")

# ------------------------------------------------------- 2. load protection
w()
w("=" * 68)
w("2. PLUG-INS THAT ASK ON STARTUP")
w("=" * 68)
try:
    import Rhino.PlugIns as P

    installed = P.PlugIn.GetInstalledPlugIns()
    w("%d plug-ins installed" % len(installed))
    w()

    # Find the load-protection API without assuming its exact shape.
    import System.Reflection as R
    tp = P.PlugIn
    lp_get = None
    lp_set = None
    for m in tp.GetMethods(R.BindingFlags.Public | R.BindingFlags.Static):
        n = m.Name.lower()
        if "loadprotection" in n:
            ps = m.GetParameters()
            sig = ", ".join("%s %s" % (p.ParameterType.Name, p.Name) for p in ps)
            w("   API found: %s %s(%s)" % (m.ReturnType.Name, m.Name, sig))
            if n.startswith("get"):
                lp_get = m
            elif n.startswith("set"):
                lp_set = m
    w()

    asked = []
    for gid, nm in installed.items():
        state = None
        if lp_get is not None:
            try:
                state = lp_get.Invoke(None, System.Array[System.Object]([gid]))
            except Exception:
                state = None
        info = P.PlugIn.GetPlugInInfo(gid)
        loaded = info.IsLoaded if info else None
        if state is not None and str(state).lower() not in ("loadsilently", "0", "none"):
            asked.append((gid, nm, state, loaded))

    if not asked:
        w("None reported as asking. If Rhino still prompts, the prompt is coming")
        w("from the plug-in itself rather than Rhino's load protection — note")
        w("which two they are and say so.")
    else:
        w("These ask on startup:")
        for gid, nm, state, loaded in asked:
            w("   %-34s state=%s loaded=%s" % (nm, state, loaded))
        w()
        if lp_set is None:
            w("No setter available — change them by hand in")
            w("Tools > Options > Plug-ins (right-click a plug-in).")
        else:
            for gid, nm, state, loaded in asked:
                try:
                    ps = lp_set.GetParameters()
                    if ps.Length == 2 and ps[1].ParameterType.Name.lower() == "boolean":
                        lp_set.Invoke(None, System.Array[System.Object]([gid, True]))
                    else:
                        # enum-typed second parameter: pick the "silent" member
                        et = ps[1].ParameterType
                        target = None
                        for vn in System.Enum.GetNames(et):
                            if "silent" in vn.lower():
                                target = System.Enum.Parse(et, vn)
                        if target is None:
                            raise RuntimeError("no silent value in %s" % et.Name)
                        lp_set.Invoke(None, System.Array[System.Object]([gid, target]))
                    w("   set to load silently: %s" % nm)
                except Exception as e:
                    w("   could not change %s: %s" % (nm, e))
            w()
            w("Takes effect next time Rhino starts.")
except Exception:
    w("FAILED:\n" + traceback.format_exc())

# ---------------------------------------------------------------- report
w()
w("=" * 68)
try:
    import os
    here = os.path.dirname(os.path.abspath(__file__))
    dest = os.path.join(here, "rhino-startup-fix-report.txt")
    with open(dest, "w", encoding="utf-8") as f:
        f.write("\n".join(OUT))
    print("WROTE: %s" % dest)
except Exception:
    print("could not write report:\n" + traceback.format_exc())
