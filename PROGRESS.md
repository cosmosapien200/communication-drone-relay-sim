# Progress log

Updated at every major milestone. Newest first.

## 2026-10-17 — Bootstrap (Section 15.3) — DONE (checkpoint 1)
- [x] Spec read; copied to `SPEC.md`
- [x] git repo, `.gitignore`, layout (`unity/`, `python/`, `configs/`, `docs/`, `results/`)
- [x] `CLAUDE.md` and `.claude/agents/{implementer,evaluator}.md` from spec 15.4/15.5
- [x] uv env: Python 3.10.12, mlagents 1.1.0, torch 2.5.1+cu121 (`cuda.is_available() == True`, RTX 3060), ultralytics 8.4.178
- [x] Unity 6000.6.4f1 project created in batch mode (built-in render pipeline), packages `com.unity.ml-agents@4.1.0`, `com.unity.ai.assistant@2.20.0-pre.2`; 0 compile errors
- [x] Unity MCP relay registered with Claude Code (`unity-mcp`, official relay)
- [x] MCP verified (no approval gate was needed): tool list, console logs, scene hierarchy, C# execution via `python/tools/unity_mcp.py`
- [x] Smoke test: floor+cube scene saved, `docs/screenshots/smoke.png` rendered and checked

### Prerequisite notes
- Filesystem snapshot: root is btrfs but snapshot could not be verified (no sudo) — human to confirm.
- Session is Wayland (XWayland `DISPLAY=:1`), spec asks for X11; Editor launched under XWayland.
- `mlagents` needs `setuptools<70` for `pkg_resources` (added to pyproject).
