"""PreToolUse hook: refuse script and shader edits while Unity is in Play mode.

Unity writes .claude/unity.playing when Play starts and removes it when Play ends
(Assets/Backrooms/Scripts/Editor/AgentPlayGuard.cs). While that file exists, a change to a
.cs / .shader / .hlsl / .compute / .cginc / .asmdef file under Assets or Packages is refused:
Unity recompiles on the change and the running session breaks.

Exit 0 = allowed. Exit 2 = refused (stderr is shown to Claude).
"""
import json
import os
import re
import sys

ROOT = os.environ.get("CLAUDE_PROJECT_DIR") or os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MARKER = os.path.join(ROOT, ".claude", "unity.playing")
CODE = re.compile(r"\.(cs|shader|hlsl|compute|cginc|asmdef|shadergraph)\b", re.I)
WRITES = re.compile(r"(sed\s+-i|perl\s+-[a-z]*i|\btee\b|>>?\s*\S*\.(cs|shader|hlsl|compute)|Set-Content|Out-File|Add-Content|git\s+(checkout|restore|stash|apply|reset))", re.I)


def unity_running():
    try:
        import subprocess
        out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq Unity.exe", "/NH"],
                             capture_output=True, text=True, timeout=5).stdout
        return "Unity.exe" in out
    except Exception:
        return True


def refuse(what):
    sys.stderr.write(
        "Refused: Unity is in Play mode (.claude/unity.playing exists) and this would change " + what + ". "
        "Stop Play first (manage_editor stop, only if it is an agent session holding .claude/unity.lock; "
        "otherwise it is the owner's session: ask them), then edit.\n")
    sys.exit(2)


def main():
    if not os.path.exists(MARKER):
        return
    try:
        data = json.load(sys.stdin)
    except Exception:
        return
    if not unity_running():
        try:
            os.remove(MARKER)
        except OSError:
            pass
        return
    tool = data.get("tool_name", "")
    inp = data.get("tool_input", {}) or {}
    if tool in ("Edit", "Write", "NotebookEdit"):
        path = str(inp.get("file_path", "")).replace("\\", "/")
        if CODE.search(path) and ("/Assets/" in path or "/Packages/" in path):
            refuse("a script or shader (" + os.path.basename(path) + ")")
    elif tool in ("Bash", "PowerShell"):
        cmd = str(inp.get("command", ""))
        if WRITES.search(cmd) and CODE.search(cmd):
            refuse("scripts or shaders from the shell")
        for script in re.findall(r"python[0-9.]*\s+(?:-\S+\s+)*[\"']?([^\"'\s]+\.py)", cmd):
            try:
                text = open(script.replace("/e/", "E:/").replace("/f/", "F:/").replace("/c/", "C:/"),
                            encoding="utf-8", errors="ignore").read()
            except OSError:
                continue
            if CODE.search(text) and ("Assets/" in text or "Packages/" in text) and re.search(r"\.write\(|open\([^)]*['\"]w", text):
                refuse("scripts or shaders through " + os.path.basename(script))
    elif tool.startswith("mcp__unity__"):
        refuse("scripts through the Unity tools")


if __name__ == "__main__":
    main()
