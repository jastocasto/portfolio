#! python 3
# -*- coding: utf-8 -*-
"""
Run the Stratum rig from the Rhino command line and leave the report on disk.

WHY THIS EXISTS
---------------
`run_python` over the MCP bridge stopped answering on 2026-09-13 (every call,
including `print(1+1)`, returns "An error occurred invoking 'run_python'"),
while `run_command`, `list_objects` and `get_viewport_image` all still work.
So the rig needs a path in that does not go through the script bridge:

    -_RunPythonScript "C:\\Users\\casto\\NUBIM\\Stratum\\tests\\run_rig.py"

That is a plain scripted command, no dialog. The `#! python 3` line on top is
what makes Rhino 8 hand the file to CPython rather than IronPython 2 - rig.py
uses f-strings and will not parse under IronPython.

Everything rig.py prints lands in tests/_last-run.txt, which is then read off
the machine with device_stage_files. Nothing is lost because the command line
only returned "Done.".
"""

import io
import os
import sys
import traceback
import contextlib

HERE = os.path.dirname(__file__)
RIG = os.path.join(HERE, "rig.py")
OUT = os.path.join(HERE, "_last-run.txt")

buf = io.StringIO()
try:
    with contextlib.redirect_stdout(buf), contextlib.redirect_stderr(buf):
        src = open(RIG, "r", encoding="utf-8").read()
        g = {"__name__": "__rig__", "__file__": RIG}
        try:
            g["__rhino_doc__"] = __rhino_doc__          # noqa: F821
        except NameError:
            import Rhino
            g["__rhino_doc__"] = Rhino.RhinoDoc.ActiveDoc
        exec(compile(src, RIG, "exec"), g)
except Exception:
    buf.write("\n*** run_rig.py caught an exception ***\n")
    buf.write(traceback.format_exc())

text = buf.getvalue()
with open(OUT, "w", encoding="utf-8") as f:
    f.write(text)

# Also to the command line, so a human watching Rhino sees it.
sys.stdout.write(text)
