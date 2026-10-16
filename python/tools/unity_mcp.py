"""Minimal stdio MCP client for the official Unity MCP relay.

Used for inspection, console errors, play-mode tests and screenshots, and to invoke
editor scripts (CLAUDE.md rule 1: content is always built by C# code, never by MCP edits).

usage:
  unity_mcp.py list                      list MCP tools
  unity_mcp.py refresh                   refresh assets, wait for compile, print compile errors
  unity_mcp.py exec Class.Method [args]  run a static editor method (string args), print its logs
  unity_mcp.py cs file.cs                run a raw IRunCommand C# file
  unity_mcp.py logs [errors]             console logs
  unity_mcp.py call <tool> '<json>'      raw tool call
"""
import json, subprocess, sys, select, time

RELAY = ["/home/lokesh/.unity/relay/relay_linux", "--mcp"]

TEMPLATE = """using UnityEngine;
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
%s
    }
}
"""


class Client:
    def __init__(self):
        self.p = subprocess.Popen(RELAY, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  stderr=subprocess.DEVNULL, text=True)
        self.i = 0
        self.req("initialize", {"protocolVersion": "2025-06-18", "capabilities": {},
                                "clientInfo": {"name": "claude-code-script", "version": "0.1"}})
        self.send({"jsonrpc": "2.0", "method": "notifications/initialized"})

    def send(self, msg):
        self.p.stdin.write(json.dumps(msg) + "\n")
        self.p.stdin.flush()

    def req(self, method, params, timeout=120):
        self.i += 1
        self.send({"jsonrpc": "2.0", "id": self.i, "method": method, "params": params})
        end = time.time() + timeout
        while time.time() < end:
            r, _, _ = select.select([self.p.stdout], [], [], 1)
            if not r:
                continue
            line = self.p.stdout.readline()
            if not line:
                raise RuntimeError("relay exited")
            msg = json.loads(line)
            if msg.get("id") == self.i:
                if "error" in msg:
                    raise RuntimeError(msg["error"])
                return msg["result"]
        raise TimeoutError(method)

    def tool(self, name, args, timeout=600):
        res = self.req("tools/call", {"name": name, "arguments": args}, timeout=timeout)
        text = "".join(item.get("text", "") for item in res.get("content", []))
        try:
            return json.loads(text)
        except json.JSONDecodeError:
            return {"raw": text}

    def run_cs(self, code, timeout=600):
        return self.tool("Unity_RunCommand", {"Code": code}, timeout)

    def run_body(self, body, timeout=600):
        return self.run_cs(TEMPLATE % body, timeout)

    def wait_idle(self, timeout=600):
        end = time.time() + timeout
        time.sleep(1.0)
        while time.time() < end:
            try:
                r = self.run_body('result.Log("{0}", EditorApplication.isCompiling || EditorApplication.isUpdating);', 60)
                logs = r.get("data", {}).get("executionLogs", "")
                if "False" in logs:
                    return True
            except Exception:
                pass  # relay drops during domain reload
            time.sleep(2.0)
        raise TimeoutError("editor busy")

    def errors(self, since_clear=False):
        r = self.tool("Unity_GetConsoleLogs", {})
        logs = r.get("data", {}).get("logs", [])
        return [l for l in logs if str(l.get("type", l.get("logType", ""))).lower() in ("error", "exception", "assert")]

    def close(self):
        self.p.terminate()


def summarize(r):
    d = r.get("data", r)
    out = []
    if d.get("compilationLogs"):
        out.append("COMPILE: " + d["compilationLogs"])
    if d.get("executionLogs"):
        out.append(d["executionLogs"].replace("\\n", "\n"))
    if not r.get("success", True) or d.get("isExecutionSuccessful") is False:
        err = r.get("error", json.dumps(r))
        # shader-compile warnings during builds are reported as errors by the tool; show only real errors
        lines = [l for l in err.split("\n") if ("[Error]" in l or "[Exception]" in l or "error" in l.lower()) and "Shader warning" not in l]
        out.append("FAILED/WARN: " + ("\n".join(lines[:30]) if lines else err[:300]))
    return "\n".join(out)


def main():
    c = Client()
    try:
        cmd = sys.argv[1]
        if cmd == "list":
            for t in c.req("tools/list", {})["tools"]:
                print(t["name"], "-", (t.get("description") or "")[:110].replace("\n", " "))
        elif cmd == "refresh":
            c.run_body('UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation(); AssetDatabase.Refresh();', 120)
            c.close()
            time.sleep(3)
            c = Client()
            c.wait_idle()
            errs = c.errors()
            for e in errs[-30:]:
                print("ERR:", (e.get("message") or json.dumps(e))[:600])
            print("refresh done, errors:", len(errs))
        elif cmd == "exec":
            c.wait_idle()
            meth = sys.argv[2]
            args = ", ".join(json.dumps(a) for a in sys.argv[3:])
            r = c.run_body(f"{meth}({args}); result.Log(\"exec ok\");", 3600)
            print(summarize(r))
        elif cmd == "cs":
            print(summarize(c.run_cs(open(sys.argv[2]).read())))
        elif cmd == "logs":
            r = c.tool("Unity_GetConsoleLogs", {})
            logs = r.get("data", {}).get("logs", [])
            if len(sys.argv) > 2:
                logs = [l for l in logs if "error" in json.dumps(l).lower() or "exception" in json.dumps(l).lower()]
            for l in logs[-40:]:
                print(json.dumps(l)[:500])
        else:
            args = json.loads(sys.argv[3]) if len(sys.argv) > 3 else {}
            print(json.dumps(c.tool(sys.argv[2], args), indent=1)[:5000])
    finally:
        c.close()


if __name__ == "__main__":
    main()
