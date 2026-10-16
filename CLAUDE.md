# Communication Drone — agent instructions

SPEC.md is the source of truth. Re-read the relevant section before starting each work package.

## Roles
- You (the main session, Opus) are the lead: plan, integrate, debug, review, design rewards, interpret training results, and make architecture decisions.
- Delegate bounded implementation tasks (one module against a fixed interface from SPEC.md Section 11) to the `implementer` subagent.
- Delegate running evaluations, reading TensorBoard logs and producing plots to the `evaluator` subagent.
- Never delegate RL debugging, reward design or interface changes.

## Hard rules
1. Build all scenes, prefabs and maps from C# code (editor scripts or runtime generators). Use Unity MCP only for inspection, console errors, play-mode tests and screenshots, never as the way to construct content.
2. Interfaces in SPEC.md Section 11 are frozen. Changing one requires stopping and reporting.
3. Verify every milestone visually: render screenshots and read them before calling anything done.
4. Never change the reward function and the curriculum in the same training run. One config file per run, committed with its seed and results.
5. No system-wide installs, no sudo, no pacman. Project-local uv environment only.
6. Commit to git after every completed task with a clear message.
7. Measure before planning: record real training throughput (steps/s) and use it to re-plan step budgets.
8. If stuck on the same error for more than about 45 minutes of attempts, stop, write the attempts and hypotheses to docs/blockers.md, and report.

## Checkpoints: stop and report
At each one, write docs/checkpoint-N.md (what was done, numbers, screenshots, open issues), commit, and stop for human review:
1. Bootstrap complete (Section 15.3)
2. Interfaces frozen; WiFi heatmap working; measured steps per second
3. First navigation training run on curriculum level 1 (reward curve + success rate)
4. Vision dataset generated and segmentation model trained (IoU per class)
5. First end-to-end single-drone mission on a level-2 map
6. Multi-drone chain on held-out maps with the real detector
7. Evaluation results, demo video, report draft
