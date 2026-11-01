# Theory DA3: Final code with the proposed novelty

Read `DA3_Contribution.pdf` first: one page on what this work adds beyond the base paper.

`code/` is the complete project: the Unity simulator (`unity/Assets/Scripts`, `unity/Assets/Editor`), training and
evaluation (`python/`, `configs/`), all trained models (`unity/Assets/Resources/Models`), result tables and the demo video
(`docs/`). `DEMO.md` explains the live demo. `demo_video.mp4` is a recorded run.

## Run
```bash
uv sync
./ubuild Builder.BuildDemo
./demo
```
Requires Unity 6000.6.4f1. Training: `python/rl/train.sh` (ML-Agents, used for the reported results) or `python/rl/ppo_train.py`
(from-scratch PPO, DA2). Evaluation: `python/eval/`.
