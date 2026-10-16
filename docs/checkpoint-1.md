# Checkpoint 1 — Bootstrap complete

- Python: uv env `.venv`, Python 3.10.12 (mlagents 1.1.0 requires >=3.10.1,<=3.10.12), torch 2.5.1+cu121, CUDA available on RTX 3060.
- Unity: 6000.6.4f1, project `unity/` created in batch mode, ML-Agents 4.1.0 + AI Assistant 2.20.0-pre.2 (official MCP).
- MCP: relay registered as `unity-mcp` (Claude Code local config). Because MCP servers added mid-session are not loaded
  until restart, `python/tools/unity_mcp.py` talks to the same relay over stdio. Verified: tools/list, console logs,
  scene hierarchy (`Main Camera`, `Directional Light`), C# execution.
- Smoke test: `SmokeTest.Run` (editor script, also usable with `-executeMethod`) builds a floor + cube scene, saves
  `Assets/Scenes/Smoke.unity`, renders `docs/screenshots/smoke.png` (checked visually).

Open issues: btrfs snapshot not verifiable without sudo; desktop is Wayland (Editor runs on XWayland).
