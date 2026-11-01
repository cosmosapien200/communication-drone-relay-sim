# Theory DA2: Code implementing the base paper (PPO)

`ppo_train.py` implements PPO (Schulman et al., 2017) from scratch in PyTorch and trains it on the project's two
drone tasks. It does not use the ML-Agents trainer; only the `mlagents_envs` API is used, to talk to the Unity simulator.

## What is implemented from the paper
| Paper | Code in `ppo_train.py` |
|---|---|
| Clipped surrogate L_CLIP | `PPOTrainer.update` (`pol_loss`) |
| Value loss + entropy bonus | `PPOTrainer.update` (`val_loss`, `entropy`) |
| Truncated GAE(λ) | `gae()` |
| N parallel actors, T timesteps | `UnityVecEnv`, `time_horizon` in the main loop |
| K epochs of minibatch Adam | `PPOTrainer.update` |

Added for practical training: observation normalisation, clipped value loss, curriculum lessons, checkpoints and ONNX export.

## Results on held-out seeds (100 episodes per condition)
Training time on a 16-thread CPU: navigation 5 M steps in 647 s, landing 1.5 M steps in 149 s.

| Navigation level | 1 | 2 | 3 | 4 |
|---|---|---|---|---|
| Success | 98 % | 99 % | 90 % | 75 % |

| Landing gust (m/s) | 0 | 1.5 | 3 | 4.5 | 6 |
|---|---|---|---|---|---|
| Success | 100 % | 100 % | 98 % | 97 % | 98 % |
| Touchdown error (m) | 0.07 | 0.07 | 0.08 | 0.09 | 0.14 |

Curves: `results/training_curves.png`. Logs, trained models and `summary.json` are in `results/`.

## Run
The environment is a compiled Unity build (about 140 MB each), so it is not in this zip; `env_reference/` has the agent
source. Builds come from the DA3 project with `./ubuild Builder.BuildNav` and `./ubuild Builder.BuildLand`.

```bash
python ppo_train.py --config configs/nav_ppo_v1.yaml --run-id nav_base --env Builds/NavTraining/NavTraining.x86_64 --arenas 32 --max-steps 5000000
python ppo_train.py --config configs/land_ppo_v2.yaml --run-id land_base --env Builds/LandTraining/LandTraining.x86_64 --arenas 32 --max-steps 1500000
```
Paths are relative to the project root. Requires Python 3.10, torch, mlagents_envs, pyyaml.
