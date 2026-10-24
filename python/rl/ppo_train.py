"""Standalone PPO trainer (PyTorch) for the Communication Drone navigation and landing policies.

This is a self-contained re-implementation of the training loop that `mlagents-learn` runs for us
(python/rl/train.sh). It talks to the same headless Unity builds (Builds/NavTraining, Builds/LandTraining) through the
low-level `mlagents_envs` API, reads the same per-run YAML files in configs/, and exports ONNX models with the same
tensor names as ML-Agents, so the exported policy drops straight into unity/Assets/Resources/Models/ and is used by
PolicyRunner.cs in missions and the live demo.

The policies reported in the project were trained with `mlagents-learn`; this file makes every part of that training
explicit and readable in one place:

    observation normalisation (running mean / variance, baked into the exported model)
    Gaussian actor + separate critic, MLP (hidden_units x num_layers, Swish), state-independent log-sigma
    action clipping exactly like ML-Agents:  env_action = clamp(a, -3, 3) / 3
    per-agent trajectories cut at time_horizon, GAE(lambda) with value bootstrapping on truncation / interruption
    PPO clipped surrogate + clipped value loss + entropy bonus, advantage normalisation
    linear or constant schedules for learning rate, beta and epsilon
    curriculum lessons from `environment_parameters` (smoothed mean reward >= threshold after min_lesson_length)
    custom stats from the agents (Nav/Success, Land/Error, ...) via the stats side channel -> TensorBoard + console
    checkpoints, --resume, --initialize-from (fine-tuning, e.g. land_ppo_v3 from land_ppo_v2), ONNX export

usage (Unity Editor must be closed while builds are made; training itself needs no Editor):
    ./ubuild Builder.BuildNav
    .venv/bin/python python/rl/ppo_train.py --config configs/nav_ppo_v1.yaml --run-id nav_torch_v1 \
        --env Builds/NavTraining/NavTraining.x86_64 --arenas 32 --seed 1

    # landing, fine-tuned from an earlier run with the measured detector noise
    .venv/bin/python python/rl/ppo_train.py --config configs/land_ppo_v3.yaml --run-id land_torch_v3 \
        --env Builds/LandTraining/LandTraining.x86_64 --arenas 32 --initialize-from results/torch_runs/land_torch_v2

    # export a checkpoint only, and install it for the mission / demo
    .venv/bin/python python/rl/ppo_train.py --config configs/nav_ppo_v1.yaml --run-id nav_torch_v1 \
        --export-only --install-as nav_torch_v1

outputs: results/torch_runs/<run-id>/{checkpoint.pt, <Behavior>.onnx, config.yaml, events.* (TensorBoard), log.csv}
"""
import argparse
import copy
import csv
import math
import os
import shutil
import signal
import sys
import time
from collections import defaultdict, deque
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple

import numpy as np
import torch
import torch.nn as nn
import yaml

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "../.."))
MODELS_DIR = os.path.join(ROOT, "unity/Assets/Resources/Models")
MODEL_EXPORT_VERSION = 3  # ML-Agents 2.x+ model API (what the Unity package expects)
EPS = 1e-8


# ============================================================================================ configuration
@dataclass
class Hyper:
    batch_size: int = 1024
    buffer_size: int = 10240
    learning_rate: float = 3e-4
    beta: float = 5e-3
    epsilon: float = 0.2
    lambd: float = 0.95
    num_epoch: int = 3
    learning_rate_schedule: str = "linear"
    beta_schedule: Optional[str] = None
    epsilon_schedule: Optional[str] = None


@dataclass
class Network:
    normalize: bool = True
    hidden_units: int = 128
    num_layers: int = 2


@dataclass
class Lesson:
    name: str
    value: float
    measure: str = "reward"
    threshold: Optional[float] = None
    min_lesson_length: int = 100
    signal_smoothing: bool = True


@dataclass
class TrainerConfig:
    behavior: str
    hyper: Hyper
    network: Network
    gamma: float = 0.99
    max_steps: int = 500000
    time_horizon: int = 64
    summary_freq: int = 50000
    checkpoint_interval: int = 500000
    constants: Dict[str, float] = field(default_factory=dict)
    curricula: Dict[str, List[Lesson]] = field(default_factory=dict)


def load_config(path: str, behavior: Optional[str] = None) -> TrainerConfig:
    """Reads an ML-Agents trainer YAML (the files in configs/) into a TrainerConfig."""
    raw = yaml.safe_load(open(path))
    behaviors = raw["behaviors"]
    if behavior is None:
        if len(behaviors) != 1:
            raise ValueError(f"{path} defines {list(behaviors)}; pass --behavior")
        behavior = next(iter(behaviors))
    b = behaviors[behavior]
    if b.get("trainer_type", "ppo") != "ppo":
        raise ValueError("only trainer_type: ppo is supported")
    hp = b.get("hyperparameters", {})
    hyper = Hyper(**{k: hp[k] for k in Hyper.__dataclass_fields__ if k in hp})
    ns = b.get("network_settings", {})
    network = Network(**{k: ns[k] for k in Network.__dataclass_fields__ if k in ns})
    ext = b.get("reward_signals", {}).get("extrinsic", {})
    cfg = TrainerConfig(behavior=behavior, hyper=hyper, network=network, gamma=ext.get("gamma", 0.99),
                        max_steps=int(b.get("max_steps", 500000)), time_horizon=int(b.get("time_horizon", 64)),
                        summary_freq=int(b.get("summary_freq", 50000)),
                        checkpoint_interval=int(b.get("checkpoint_interval", 500000)))
    if ext.get("strength", 1.0) != 1.0:
        print(f"note: extrinsic strength {ext['strength']} is applied as a reward scale")
    cfg.reward_scale = float(ext.get("strength", 1.0))
    for name, spec in (raw.get("environment_parameters") or {}).items():
        if isinstance(spec, dict) and "curriculum" in spec:
            lessons = []
            for les in spec["curriculum"]:
                cc = les.get("completion_criteria") or {}
                if cc and cc.get("behavior", behavior) != behavior:
                    raise ValueError(f"curriculum {name} is driven by another behavior ({cc['behavior']})")
                if cc.get("measure", "reward") not in ("reward", "progress"):
                    raise ValueError(f"unsupported completion measure {cc.get('measure')}")
                lessons.append(Lesson(name=les.get("name", f"lesson{len(lessons)}"), value=float(_scalar(les["value"])),
                                      measure=cc.get("measure", "reward"), threshold=cc.get("threshold"),
                                      min_lesson_length=int(cc.get("min_lesson_length", 100)),
                                      signal_smoothing=bool(cc.get("signal_smoothing", True))))
            cfg.curricula[name] = lessons
        else:
            cfg.constants[name] = float(_scalar(spec))
    return cfg


def _scalar(v):
    """Environment parameters may be plain numbers or {sampler_type: constant, sampler_parameters: {value: x}}."""
    if isinstance(v, dict):
        if v.get("sampler_type", "constant") != "constant":
            raise ValueError("only constant environment parameters are supported")
        return v["sampler_parameters"]["value"]
    return v


def schedule(kind: Optional[str], start: float, end: float, step: int, max_steps: int) -> float:
    """ML-Agents-style decay: constant, or linear from `start` to `end` over max_steps."""
    if kind in (None, "constant"):
        return start
    frac = min(1.0, step / max(1, max_steps))
    return start + (end - start) * frac


# ============================================================================================ networks
class RunningNorm(nn.Module):
    """Running mean / variance of the observations (Welford / parallel update), as ML-Agents' `normalize: true`."""

    def __init__(self, size: int):
        super().__init__()
        self.register_buffer("count", torch.tensor(1.0))
        self.register_buffer("mean", torch.zeros(size))
        self.register_buffer("var", torch.ones(size))

    @torch.no_grad()
    def update(self, x: torch.Tensor):
        n = x.shape[0]
        if n == 0:
            return
        bmean, bvar = x.mean(0), x.var(0, unbiased=False)
        tot = self.count + n
        delta = bmean - self.mean
        new_mean = self.mean + delta * n / tot
        m2 = self.var * self.count + bvar * n + delta.pow(2) * self.count * n / tot
        self.mean.copy_(new_mean); self.var.copy_(m2 / tot); self.count.copy_(tot)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        return torch.clamp((x - self.mean) / torch.sqrt(self.var + EPS), -5.0, 5.0)


def mlp(inp: int, hidden: int, layers: int) -> nn.Sequential:
    mods, d = [], inp
    for _ in range(layers):
        lin = nn.Linear(d, hidden)
        nn.init.kaiming_normal_(lin.weight, nonlinearity="linear"); nn.init.zeros_(lin.bias)
        mods += [lin, nn.SiLU()]  # SiLU == Swish, the ML-Agents default activation
        d = hidden
    return nn.Sequential(*mods)


class ActorCritic(nn.Module):
    """Gaussian policy with a state-independent log-sigma and a separate value network (no shared trunk)."""

    def __init__(self, obs_size: int, act_size: int, net: Network):
        super().__init__()
        self.obs_size, self.act_size = obs_size, act_size
        self.normalize = net.normalize
        self.norm = RunningNorm(obs_size)
        self.actor_body = mlp(obs_size, net.hidden_units, net.num_layers)
        self.mu = nn.Linear(net.hidden_units, act_size)
        nn.init.xavier_uniform_(self.mu.weight, gain=0.2); nn.init.zeros_(self.mu.bias)
        self.log_sigma = nn.Parameter(torch.zeros(act_size))
        self.critic_body = mlp(obs_size, net.hidden_units, net.num_layers)
        self.value_head = nn.Linear(net.hidden_units, 1)
        nn.init.xavier_uniform_(self.value_head.weight, gain=1.0); nn.init.zeros_(self.value_head.bias)

    def _n(self, obs):
        return self.norm(obs) if self.normalize else obs

    def dist(self, obs) -> torch.distributions.Normal:
        x = self._n(obs)
        mu = self.mu(self.actor_body(x))
        sigma = torch.exp(torch.clamp(self.log_sigma, -20.0, 2.0)).expand_as(mu)
        return torch.distributions.Normal(mu, sigma)

    def value(self, obs) -> torch.Tensor:
        return self.value_head(self.critic_body(self._n(obs))).squeeze(-1)

    @torch.no_grad()
    def act(self, obs: torch.Tensor, deterministic: bool = False):
        d = self.dist(obs)
        a = d.mean if deterministic else d.sample()
        return a, d.log_prob(a).sum(-1), self.value(obs)


def env_action(raw: np.ndarray) -> np.ndarray:
    """What the environment receives (identical to ML-Agents' action clipping)."""
    return np.clip(raw, -3.0, 3.0) / 3.0


class ExportWrapper(nn.Module):
    """ONNX graph with the ML-Agents tensor names. PolicyRunner.cs reads `deterministic_continuous_actions`;
    ML-Agents' own BehaviorParameters additionally checks version_number / memory_size / action shape."""

    def __init__(self, model: ActorCritic):
        super().__init__()
        self.model = copy.deepcopy(model).cpu().eval()
        self.version_number = nn.Parameter(torch.tensor([float(MODEL_EXPORT_VERSION)]), requires_grad=False)
        self.memory_size = nn.Parameter(torch.tensor([0.0]), requires_grad=False)
        self.act_shape = nn.Parameter(torch.tensor([float(model.act_size)]), requires_grad=False)

    def forward(self, obs_0):
        d = self.model.dist(obs_0)
        stochastic = d.mean + d.stddev * torch.randn_like(d.mean)
        return (self.version_number, self.memory_size, torch.clamp(stochastic, -3, 3) / 3, self.act_shape,
                torch.clamp(d.mean, -3, 3) / 3)


def export_onnx(model: ActorCritic, path: str):
    w = ExportWrapper(model)
    dummy = torch.zeros(1, model.obs_size)
    names = ["version_number", "memory_size", "continuous_actions", "continuous_action_output_shape",
             "deterministic_continuous_actions"]
    torch.onnx.export(w, (dummy,), path, opset_version=13, input_names=["obs_0"], output_names=names,
                      dynamic_axes={"obs_0": {0: "batch"}, "continuous_actions": {0: "batch"},
                                    "deterministic_continuous_actions": {0: "batch"}})
    return path


# ============================================================================================ rollout storage
@dataclass
class Trajectory:
    obs: List[np.ndarray] = field(default_factory=list)
    act: List[np.ndarray] = field(default_factory=list)
    logp: List[float] = field(default_factory=list)
    val: List[float] = field(default_factory=list)
    rew: List[float] = field(default_factory=list)
    ep_return: float = 0.0
    ep_length: int = 0


class UpdateBuffer:
    """Processed transitions (with advantages and returns) waiting for the next PPO update."""

    KEYS = ("obs", "act", "logp", "val", "adv", "ret")

    def __init__(self):
        self.data = {k: [] for k in self.KEYS}

    def __len__(self):
        return len(self.data["obs"])

    def add(self, **kw):
        for k in self.KEYS:
            self.data[k].extend(kw[k])

    def tensors(self, device) -> Dict[str, torch.Tensor]:
        return {k: torch.as_tensor(np.asarray(v, dtype=np.float32), device=device) for k, v in self.data.items()}

    def clear(self):
        for k in self.KEYS:
            self.data[k].clear()


def gae(rew: np.ndarray, val: np.ndarray, bootstrap: float, gamma: float, lam: float) -> Tuple[np.ndarray, np.ndarray]:
    """Generalised advantage estimation over one trajectory segment; `bootstrap` is V(s_T) (0 if terminal)."""
    adv = np.zeros_like(rew)
    last = 0.0
    nxt = bootstrap
    for t in reversed(range(len(rew))):
        delta = rew[t] + gamma * nxt - val[t]
        last = delta + gamma * lam * last
        adv[t] = last
        nxt = val[t]
    return adv, adv + val


# ============================================================================================ curriculum
class Curriculum:
    """Lesson progression per environment parameter, mirroring ML-Agents' EnvironmentParameterManager:
    after `min_lesson_length` episodes in a lesson, the (optionally smoothed) mean episode reward of the last
    `min_lesson_length` episodes must reach the threshold to move to the next lesson."""

    def __init__(self, cfg: TrainerConfig, channel):
        self.cfg, self.channel = cfg, channel
        self.lesson = {k: 0 for k in cfg.curricula}
        self.smoothed = {k: 0.0 for k in cfg.curricula}
        self.rewards = {k: deque(maxlen=max(1, ls[0].min_lesson_length)) for k, ls in cfg.curricula.items()}
        self.episodes_in_lesson = {k: 0 for k in cfg.curricula}

    def apply(self):
        for k, v in self.cfg.constants.items():
            self.channel.set_float_parameter(k, v)
        for k, lessons in self.cfg.curricula.items():
            self.channel.set_float_parameter(k, lessons[self.lesson[k]].value)

    def set_lessons(self, lessons: Dict[str, int]):
        for k, i in lessons.items():
            if k in self.lesson:
                self.lesson[k] = min(i, len(self.cfg.curricula[k]) - 1)
                self._reset_window(k)

    def _reset_window(self, k):
        les = self.cfg.curricula[k][self.lesson[k]]
        self.rewards[k] = deque(maxlen=max(1, les.min_lesson_length))
        self.episodes_in_lesson[k] = 0

    def episode_done(self, ep_return: float, step: int, max_steps: int) -> List[str]:
        """Records one finished episode; returns the names of parameters whose lesson advanced."""
        changed = []
        for k, lessons in self.cfg.curricula.items():
            i = self.lesson[k]
            les = lessons[i]
            self.rewards[k].append(ep_return)
            self.episodes_in_lesson[k] += 1
            if i == len(lessons) - 1 or les.threshold is None:
                continue
            if les.measure == "progress":
                measure = step / max(1, max_steps)
            else:
                if self.episodes_in_lesson[k] < les.min_lesson_length:
                    continue
                measure = float(np.mean(self.rewards[k]))
                if les.signal_smoothing:
                    measure = 0.25 * self.smoothed[k] + 0.75 * measure
                    self.smoothed[k] = measure
            if measure >= les.threshold:
                self.lesson[k] = i + 1
                self._reset_window(k)
                self.channel.set_float_parameter(k, lessons[i + 1].value)
                changed.append(k)
        return changed

    def describe(self) -> str:
        return ", ".join(f"{k}={ls[self.lesson[k]].name}({ls[self.lesson[k]].value:g})" for k, ls in self.cfg.curricula.items())


# ============================================================================================ environment
class UnityVecEnv:
    """One or more Unity processes, each with N training arenas (agents) of the same behavior.
    Agents are keyed by (env index, agent id); ids are only unique within one Unity process."""

    def __init__(self, path: str, num_envs: int, arenas: int, seed: int, base_port: int, behavior: str,
                 graphics: bool, extra_args: List[str], timeout: int):
        from mlagents_envs.environment import UnityEnvironment
        from mlagents_envs.side_channel.environment_parameters_channel import EnvironmentParametersChannel
        from mlagents_envs.side_channel.stats_side_channel import StatsSideChannel
        self.envs, self.params, self.stats = [], [], []
        for i in range(num_envs):
            p, s = EnvironmentParametersChannel(), StatsSideChannel()
            env = UnityEnvironment(file_name=path, worker_id=i, base_port=base_port, seed=seed + i,
                                   no_graphics=not graphics, side_channels=[p, s], timeout_wait=timeout,
                                   additional_args=["-arenas", str(arenas)] + extra_args)
            self.envs.append(env); self.params.append(p); self.stats.append(s)
        for env in self.envs:
            env.reset()
        names = list(self.envs[0].behavior_specs.keys())
        match = [n for n in names if n.split("?")[0] == behavior]
        if not match:
            raise RuntimeError(f"behavior {behavior} not found in the build (has {names})")
        self.name = match[0]
        spec = self.envs[0].behavior_specs[self.name]
        if len(spec.observation_specs) != 1 or len(spec.observation_specs[0].shape) != 1:
            raise RuntimeError("expected a single vector observation")
        if spec.action_spec.discrete_size:
            raise RuntimeError("expected continuous actions only")
        self.obs_size = int(spec.observation_specs[0].shape[0])
        self.act_size = int(spec.action_spec.continuous_size)

    def set_param(self, key: str, value: float):
        for p in self.params:
            p.set_float_parameter(key, value)

    def set_float_parameter(self, key, value):  # channel-like interface for Curriculum
        self.set_param(key, value)

    def get_steps(self):
        """Returns per-env (decision_steps, terminal_steps)."""
        return [env.get_steps(self.name) for env in self.envs]

    def set_actions(self, i: int, actions: np.ndarray):
        from mlagents_envs.base_env import ActionTuple
        self.envs[i].set_actions(self.name, ActionTuple(continuous=actions.astype(np.float32)))

    def step(self):
        for env in self.envs:
            env.step()

    def custom_stats(self) -> Dict[str, List[float]]:
        out = defaultdict(list)
        for s in self.stats:
            for k, vals in s.get_and_reset_stats().items():
                out[k].extend(v for v, _ in vals)
        return out

    def close(self):
        for env in self.envs:
            try:
                env.close()
            except Exception:
                pass


# ============================================================================================ PPO trainer
class PPOTrainer:
    def __init__(self, cfg: TrainerConfig, obs_size: int, act_size: int, device: torch.device):
        self.cfg, self.device = cfg, device
        self.model = ActorCritic(obs_size, act_size, cfg.network).to(device)
        self.opt = torch.optim.Adam(self.model.parameters(), lr=cfg.hyper.learning_rate, eps=1e-7)
        self.buffer = UpdateBuffer()
        self.step = 0
        self.updates = 0

    # ---- schedules
    def lr(self):
        return schedule(self.cfg.hyper.learning_rate_schedule, self.cfg.hyper.learning_rate, 1e-10, self.step, self.cfg.max_steps)

    def beta(self):
        return schedule(self.cfg.hyper.beta_schedule or self.cfg.hyper.learning_rate_schedule, self.cfg.hyper.beta, 1e-5, self.step, self.cfg.max_steps)

    def epsilon(self):
        return schedule(self.cfg.hyper.epsilon_schedule or self.cfg.hyper.learning_rate_schedule, self.cfg.hyper.epsilon, 0.1, self.step, self.cfg.max_steps)

    # ---- acting
    def policy(self, obs: np.ndarray):
        o = torch.as_tensor(obs, dtype=torch.float32, device=self.device)
        a, logp, v = self.model.act(o)
        return a.cpu().numpy(), logp.cpu().numpy(), v.cpu().numpy()

    @torch.no_grad()
    def value(self, obs: np.ndarray) -> np.ndarray:
        return self.model.value(torch.as_tensor(obs, dtype=torch.float32, device=self.device)).cpu().numpy()

    # ---- trajectory processing
    def process(self, traj: Trajectory, bootstrap: float):
        """Turns a finished or truncated segment into advantage/return samples."""
        rew = np.asarray(traj.rew, dtype=np.float32) * getattr(self.cfg, "reward_scale", 1.0)
        val = np.asarray(traj.val, dtype=np.float32)
        adv, ret = gae(rew, val, bootstrap, self.cfg.gamma, self.cfg.hyper.lambd)
        self.buffer.add(obs=traj.obs, act=traj.act, logp=traj.logp, val=traj.val, adv=adv.tolist(), ret=ret.tolist())
        if self.cfg.network.normalize:
            self.model.norm.update(torch.as_tensor(np.asarray(traj.obs, dtype=np.float32), device=self.device))

    def ready(self) -> bool:
        return len(self.buffer) >= self.cfg.hyper.buffer_size

    # ---- optimisation
    def update(self) -> Dict[str, float]:
        h = self.cfg.hyper
        lr, beta, eps = self.lr(), self.beta(), self.epsilon()
        for g in self.opt.param_groups:
            g["lr"] = lr
        b = self.buffer.tensors(self.device)
        n = (len(self.buffer) // h.batch_size) * h.batch_size or len(self.buffer)
        stats = defaultdict(list)
        for _ in range(h.num_epoch):
            perm = torch.randperm(len(self.buffer), device=self.device)[:n]
            for s in range(0, n, h.batch_size):
                idx = perm[s:s + h.batch_size]
                obs, act, old_logp, old_v, adv, ret = (b[k][idx] for k in ("obs", "act", "logp", "val", "adv", "ret"))
                adv = (adv - adv.mean()) / (adv.std() + 1e-10)
                d = self.model.dist(obs)
                logp = d.log_prob(act).sum(-1)
                ratio = torch.exp(logp - old_logp)
                pol_loss = -torch.min(ratio * adv, torch.clamp(ratio, 1 - eps, 1 + eps) * adv).mean()
                v = self.model.value(obs)
                v_clip = old_v + torch.clamp(v - old_v, -eps, eps)
                val_loss = torch.max((ret - v).pow(2), (ret - v_clip).pow(2)).mean()
                entropy = d.entropy().sum(-1).mean()
                loss = pol_loss + 0.5 * val_loss - beta * entropy
                self.opt.zero_grad(set_to_none=True)
                loss.backward()
                self.opt.step()
                with torch.no_grad():
                    approx_kl = (old_logp - logp).mean().item()
                    clipfrac = ((ratio - 1).abs() > eps).float().mean().item()
                stats["Losses/Policy Loss"].append(pol_loss.item())
                stats["Losses/Value Loss"].append(val_loss.item())
                stats["Policy/Entropy"].append(entropy.item())
                stats["Policy/ApproxKL"].append(approx_kl)
                stats["Policy/ClipFraction"].append(clipfrac)
        self.buffer.clear()
        self.updates += 1
        out = {k: float(np.mean(v)) for k, v in stats.items()}
        out.update({"Policy/Learning Rate": lr, "Policy/Beta": beta, "Policy/Epsilon": eps,
                    "Policy/Sigma": float(torch.exp(self.model.log_sigma).mean())})
        return out

    # ---- persistence
    def save(self, path: str, extra: dict):
        torch.save({"model": self.model.state_dict(), "opt": self.opt.state_dict(), "step": self.step,
                    "updates": self.updates, **extra}, path)

    def load(self, path: str, weights_only: bool = False) -> dict:
        ck = torch.load(path, map_location=self.device, weights_only=False)
        self.model.load_state_dict(ck["model"])
        if not weights_only:
            self.opt.load_state_dict(ck["opt"]); self.step = ck["step"]; self.updates = ck.get("updates", 0)
        return ck


# ============================================================================================ logging
class Logger:
    def __init__(self, run_dir: str):
        try:
            from torch.utils.tensorboard import SummaryWriter
            self.tb = SummaryWriter(run_dir)
        except Exception:  # tensorboard missing: console + CSV only
            self.tb = None
        self.csv_path = os.path.join(run_dir, "log.csv")
        self.rows: List[dict] = []
        self.t0 = time.time()

    def scalars(self, step: int, d: Dict[str, float]):
        if self.tb:
            for k, v in d.items():
                self.tb.add_scalar(k, v, step)

    def summary(self, step: int, d: Dict[str, float], lesson: str):
        d = {"step": step, "time_s": round(time.time() - self.t0, 1), **{k: round(v, 5) for k, v in d.items()}}
        self.rows.append(d)
        keys = sorted({k for r in self.rows for k in r}, key=lambda k: (k not in ("step", "time_s"), k))
        with open(self.csv_path, "w", newline="") as f:
            w = csv.DictWriter(f, fieldnames=keys); w.writeheader(); w.writerows(self.rows)
        sps = step / max(1e-6, time.time() - self.t0)
        msg = (f"[{step:>10,d}] {d['time_s']:>7.0f}s  {sps:6.0f} steps/s  "
               f"reward {d.get('Environment/Cumulative Reward', float('nan')):7.3f}  "
               f"len {d.get('Environment/Episode Length', float('nan')):6.1f}")
        for k in sorted(d):
            if k.endswith("/Success"):
                msg += f"  {k.split('/')[0].lower()} success {100 * d[k]:5.1f} %"
        print(msg + (f"  | {lesson}" if lesson else ""), flush=True)

    def close(self):
        if self.tb:
            self.tb.close()


# ============================================================================================ main loop
def train(args):
    cfg = load_config(args.config, args.behavior)
    if args.max_steps:
        cfg.max_steps = args.max_steps
    run_dir = os.path.join(ROOT, args.results_dir, args.run_id)
    ck_path = os.path.join(run_dir, "checkpoint.pt")
    if os.path.exists(ck_path) and not (args.resume or args.force or args.export_only):
        sys.exit(f"{run_dir} exists; use --resume to continue or --force to overwrite")
    os.makedirs(run_dir, exist_ok=True)
    shutil.copy(args.config, os.path.join(run_dir, "config.yaml"))
    torch.manual_seed(args.seed); np.random.seed(args.seed)
    device = torch.device(args.device)
    torch.set_num_threads(args.torch_threads)

    if args.export_only:
        return export_only(args, cfg, run_dir, ck_path, device)

    env = UnityVecEnv(args.env, args.num_envs, args.arenas, args.seed, args.base_port, cfg.behavior,
                      args.graphics, args.env_args, args.timeout)
    print(f"behavior {env.name}: {env.obs_size} observations, {env.act_size} actions, "
          f"{args.num_envs} env(s) x {args.arenas} arenas")
    trainer = PPOTrainer(cfg, env.obs_size, env.act_size, device)
    curriculum = Curriculum(cfg, env)
    if args.resume:
        ck = trainer.load(ck_path)
        curriculum.set_lessons(ck.get("lessons", {}))
        print(f"resumed at step {trainer.step:,}")
    elif args.initialize_from:
        src = args.initialize_from
        src = src if src.endswith(".pt") else os.path.join(ROOT, src, "checkpoint.pt")
        trainer.load(src, weights_only=True)
        print(f"initialised weights (incl. observation normaliser) from {src}")
    curriculum.apply()
    log = Logger(run_dir)

    stop = {"flag": False}
    signal.signal(signal.SIGINT, lambda *_: stop.__setitem__("flag", True))  # Ctrl-C: save and export cleanly

    trajs: Dict[Tuple[int, int], Trajectory] = defaultdict(Trajectory)
    pending: Dict[Tuple[int, int], Tuple[np.ndarray, np.ndarray, float, float]] = {}  # last obs/act/logp/val per agent
    ep_returns, ep_lengths = [], []
    next_summary = (trainer.step // cfg.summary_freq + 1) * cfg.summary_freq
    next_ckpt = (trainer.step // cfg.checkpoint_interval + 1) * cfg.checkpoint_interval
    update_stats: Dict[str, float] = {}

    def finish_segment(key, traj: Trajectory, bootstrap: float):
        if traj.obs:
            trainer.process(traj, bootstrap)
        trajs[key] = Trajectory(ep_return=traj.ep_return, ep_length=traj.ep_length)

    while trainer.step < cfg.max_steps and not stop["flag"]:
        for ei, (dec, term) in enumerate(env.get_steps()):
            # --- agents whose episode ended since the last decision
            for j, aid in enumerate(term.agent_id):
                key = (ei, int(aid))
                tr = trajs[key]
                if key in pending:
                    o, a, lp, v = pending.pop(key)
                    r = float(term.reward[j])
                    tr.obs.append(o); tr.act.append(a); tr.logp.append(lp); tr.val.append(v); tr.rew.append(r)
                    tr.ep_return += r; tr.ep_length += 1
                    trainer.step += 1
                interrupted = bool(term.interrupted[j])  # max-step / timeout: not a true terminal state
                boot = float(trainer.value(term.obs[0][j:j + 1])[0]) if interrupted else 0.0
                ret, length = tr.ep_return, tr.ep_length
                finish_segment(key, tr, boot)
                trajs[key] = Trajectory()
                ep_returns.append(ret); ep_lengths.append(length)
                for k in curriculum.episode_done(ret, trainer.step, cfg.max_steps):
                    print(f"curriculum: {k} -> {curriculum.describe()}", flush=True)
            # --- agents requesting a decision
            if len(dec) == 0:
                continue
            obs = dec.obs[0]
            raw, logp, val = trainer.policy(obs)
            for j, aid in enumerate(dec.agent_id):
                key = (ei, int(aid))
                tr = trajs[key]
                if key in pending:  # reward for the previous action of this (still running) agent
                    o, a, lp, v = pending.pop(key)
                    r = float(dec.reward[j])
                    tr.obs.append(o); tr.act.append(a); tr.logp.append(lp); tr.val.append(v); tr.rew.append(r)
                    tr.ep_return += r; tr.ep_length += 1
                    trainer.step += 1
                    if len(tr.obs) >= cfg.time_horizon:  # truncate the segment, bootstrap from the current state
                        finish_segment(key, tr, float(val[j]))
                pending[key] = (obs[j].copy(), raw[j].copy(), float(logp[j]), float(val[j]))
            env.set_actions(ei, env_action(raw))
        env.step()

        if trainer.ready():
            update_stats = trainer.update()
            log.scalars(trainer.step, update_stats)

        if trainer.step >= next_summary:
            next_summary += cfg.summary_freq
            summ = dict(update_stats)
            if ep_returns:
                summ["Environment/Cumulative Reward"] = float(np.mean(ep_returns))
                summ["Environment/Episode Length"] = float(np.mean(ep_lengths))
                summ["Environment/Episodes"] = float(len(ep_returns))
            for k, v in env.custom_stats().items():
                if v:
                    summ[k] = float(np.mean(v))
            for i, k in enumerate(cfg.curricula):
                summ[f"Environment/Lesson Number/{k}"] = float(curriculum.lesson[k])
            log.scalars(trainer.step, summ)
            log.summary(trainer.step, summ, curriculum.describe())
            ep_returns, ep_lengths = [], []

        if trainer.step >= next_ckpt:
            next_ckpt += cfg.checkpoint_interval
            trainer.save(ck_path, {"lessons": dict(curriculum.lesson), "obs_size": env.obs_size, "act_size": env.act_size})
            snap = os.path.join(run_dir, f"{cfg.behavior}-{trainer.step}.onnx")
            export_onnx(trainer.model, snap)
            print(f"checkpoint at {trainer.step:,} -> {snap}", flush=True)

    env.close()
    trainer.save(ck_path, {"lessons": dict(curriculum.lesson), "obs_size": env.obs_size, "act_size": env.act_size})
    out = export_onnx(trainer.model, os.path.join(run_dir, f"{cfg.behavior}.onnx"))
    log.close()
    print(f"done: {trainer.step:,} steps, final lessons {curriculum.describe()}\n  checkpoint {ck_path}\n  model {out}")
    if args.install_as:
        install(out, args.install_as)


def export_only(args, cfg, run_dir, ck_path, device):
    ck = torch.load(ck_path, map_location=device, weights_only=False)
    model = ActorCritic(ck["obs_size"], ck["act_size"], cfg.network).to(device)
    model.load_state_dict(ck["model"])
    out = export_onnx(model, os.path.join(run_dir, f"{cfg.behavior}.onnx"))
    print(f"exported {out} (step {ck['step']:,})")
    if args.install_as:
        install(out, args.install_as)


def install(onnx_path: str, name: str):
    """Copies the model next to nav_v1.onnx / land_v3.onnx so MissionRunner (-navModel / -landModel) can load it."""
    dst = os.path.join(MODELS_DIR, f"{name}.onnx")
    shutil.copy(onnx_path, dst)
    print(f"installed {dst}  (Unity imports it on next refresh; use -navModel {name} or -landModel {name})")


def parse_args(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0], formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--config", required=True, help="trainer YAML, e.g. configs/nav_ppo_v1.yaml")
    ap.add_argument("--run-id", required=True)
    ap.add_argument("--behavior", default=None, help="behavior name if the YAML defines several")
    ap.add_argument("--env", default=None, help="Unity build, e.g. Builds/NavTraining/NavTraining.x86_64")
    ap.add_argument("--num-envs", type=int, default=1, help="Unity processes")
    ap.add_argument("--arenas", type=int, default=32, help="training arenas (agents) per Unity process")
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--base-port", type=int, default=5005)
    ap.add_argument("--timeout", type=int, default=120, help="seconds to wait for Unity to start")
    ap.add_argument("--graphics", action="store_true", help="run the build with rendering (slow)")
    ap.add_argument("--env-args", nargs="*", default=[], help="extra command-line arguments for the build")
    ap.add_argument("--max-steps", type=int, default=0, help="override max_steps from the YAML")
    ap.add_argument("--results-dir", default="results/torch_runs")
    ap.add_argument("--resume", action="store_true")
    ap.add_argument("--force", action="store_true", help="overwrite an existing run directory")
    ap.add_argument("--initialize-from", default=None, help="run dir or checkpoint.pt to fine-tune from")
    ap.add_argument("--export-only", action="store_true", help="only export the run's checkpoint to ONNX")
    ap.add_argument("--install-as", default=None, help="copy the final ONNX to unity/Assets/Resources/Models/<name>.onnx")
    ap.add_argument("--device", default="cpu", help="RL is CPU-bound here (small MLPs); cpu is recommended")
    ap.add_argument("--torch-threads", type=int, default=4)
    args = ap.parse_args(argv)
    if not args.export_only and not args.env:
        ap.error("--env is required for training")
    if args.env and not os.path.isabs(args.env):
        args.env = os.path.join(ROOT, args.env)
    if not os.path.isabs(args.config):
        args.config = os.path.join(ROOT, args.config)
    return args


if __name__ == "__main__":
    train(parse_args())
