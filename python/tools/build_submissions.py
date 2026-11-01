"""RL subject: per-student DA1/DA2/DA3 packages -> submissions/<REG>_<NAME>/<REG>_DAn.zip

usage: .venv/bin/python python/tools/build_submissions.py <path to base paper pdf>
"""
import os, shutil, subprocess, sys
from docx import Document
from docx.shared import Inches, Pt, RGBColor

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "../.."))
SUB = os.path.join(ROOT, "submissions")
STUDENTS = [("LOKESHWARAN P", "23BAI1098"), ("ANNAMALAI SRINIVAS", "23BAI1006"), ("ADITHYA ANNAMALAI", "23BAI1033")]
NAVY = RGBColor(0x14, 0x21, 0x3D)
PAPER = sys.argv[1]
TITLE = "Intelligent Wi-Fi Relay Drone for Autonomous Disaster Communication"


def heading(d, t, size=12.5):
    p = d.add_paragraph(); r = p.add_run(t); r.bold = True; r.font.size = Pt(size); r.font.color.rgb = NAVY; r.font.name = "Cambria"
    p.paragraph_format.space_before = Pt(8); p.paragraph_format.space_after = Pt(3)


def grid(d, rows, widths):
    t = d.add_table(rows=len(rows), cols=len(rows[0])); t.style = "Table Grid"
    for i, row in enumerate(rows):
        for j, v in enumerate(row):
            c = t.cell(i, j); c.width = Inches(widths[j]); c.text = ""
            r = c.paragraphs[0].add_run(v); r.font.size = Pt(10); r.bold = i == 0
            c.paragraphs[0].paragraph_format.space_after = Pt(0)


def start(da, what, name, reg):
    d = Document(); s = d.sections[0]
    s.left_margin = s.right_margin = Inches(0.9); s.top_margin = s.bottom_margin = Inches(0.7)
    st = d.styles["Normal"]; st.font.name = "Calibri"; st.font.size = Pt(11); st.paragraph_format.space_after = Pt(4)
    heading(d, f"Theory {da}: {what}", 17)
    d.add_paragraph(f"Reinforcement Learning  |  {name}, {reg}")
    d.add_paragraph(f"Project: {TITLE}")
    return d


def to_pdf(d, folder, name="main"):
    path = os.path.join(folder, name + ".docx"); d.save(path)
    subprocess.run(["soffice", "--headless", "--convert-to", "pdf", "--outdir", folder, path], capture_output=True, check=True)
    os.remove(path)


def main_da1(folder, name, reg):
    d = start("DA1", "Base Paper and Presentation", name, reg)
    heading(d, "Base paper")
    d.add_paragraph("J. Schulman, F. Wolski, P. Dhariwal, A. Radford, O. Klimov, \"Proximal Policy Optimization Algorithms\", arXiv:1707.06347, 2017.")
    heading(d, "Why this paper")
    d.add_paragraph("The project uses autonomous drones that fly unknown streets, land under gusts and form a Wi-Fi relay chain after a disaster. "
                    "Both flight skills are continuous-control problems trained in a parallel simulator, which is the setting PPO was designed for: "
                    "the stability of trust-region methods using only first-order optimisation.")
    heading(d, "Contents of this submission")
    grid(d, [("File", "Description"), ("base_paper_PPO_Schulman_2017.pdf", "The base paper"),
             ("DA1_PPO_Base_Paper_Presentation.pptx", "11 slides: motivation, clipped objective, full loss and GAE, algorithm, the paper's results, why PPO suits this project, and the plan for DA2 and DA3"),
             ("main.pdf", "This introduction")], [2.9, 3.9])
    to_pdf(d, folder)


def main_da2(folder, name, reg):
    d = start("DA2", "Code Implementing the Base Paper", name, reg)
    d.add_paragraph("ppo_train.py implements PPO from scratch in PyTorch and trains it on the project's two drone tasks. The ML-Agents trainer is not used; "
                    "only the mlagents_envs API is used to communicate with the Unity simulator.")
    heading(d, "Paper to code")
    grid(d, [("Paper", "ppo_train.py"), ("Clipped surrogate L_CLIP", "PPOTrainer.update (pol_loss)"), ("Value loss and entropy bonus", "PPOTrainer.update (val_loss, entropy)"),
             ("Truncated GAE(λ)", "gae()"), ("N parallel actors, T timesteps", "UnityVecEnv, time_horizon in the main loop"), ("K epochs of minibatch Adam", "PPOTrainer.update")], [3.0, 3.8])
    d.add_paragraph()
    d.add_paragraph("Added for practical training: observation normalisation, clipped value loss, curriculum lessons, checkpoints and ONNX export.")
    heading(d, "Results on held-out seeds (100 episodes per condition)")
    grid(d, [("Navigation level", "1", "2", "3", "4", ""), ("Success", "98 %", "99 %", "90 %", "75 %", "")], [2.3, 0.9, 0.9, 0.9, 0.9, 0.9])
    d.add_paragraph()
    grid(d, [("Landing gust (m/s)", "0", "1.5", "3", "4.5", "6"), ("Success", "100 %", "100 %", "98 %", "97 %", "98 %"), ("Touchdown error (m)", "0.07", "0.07", "0.08", "0.09", "0.14")], [2.3, 0.9, 0.9, 0.9, 0.9, 0.9])
    d.add_paragraph()
    d.add_paragraph("Training on a 16-thread CPU: navigation 5 M steps in about 11 minutes, landing 1.5 M steps in about 2.5 minutes.")
    d.add_picture(os.path.join(SUB, "DA2/results/training_curves.png"), width=Inches(6.4))
    heading(d, "Running the code")
    d.add_paragraph("The environment is a compiled Unity build (about 140 MB each) and is not included; env_reference/ has the agent source. Builds are made from the project code in DA3 with "
                    "./ubuild Builder.BuildNav and ./ubuild Builder.BuildLand. Requires Python 3.10, torch, mlagents_envs and pyyaml. From the project root:")
    for cmd in ("python ppo_train.py --config configs/nav_ppo_v1.yaml --run-id nav_base --env Builds/NavTraining/NavTraining.x86_64 --arenas 32 --max-steps 5000000",
                "python ppo_train.py --config configs/land_ppo_v2.yaml --run-id land_base --env Builds/LandTraining/LandTraining.x86_64 --arenas 32 --max-steps 1500000"):
        r = d.add_paragraph().add_run(cmd); r.font.name = "Courier New"; r.font.size = Pt(8.5)
    to_pdf(d, folder)


def contribution(folder, name, reg):
    d = Document(os.path.join(SUB, "DA3/DA3_Contribution.docx"))
    for p in d.paragraphs:
        for r in p.runs:
            if "LOKESHWARAN P, 23BAI1098" in r.text:
                r.text = r.text.replace("LOKESHWARAN P, 23BAI1098", f"{name}, {reg}")
    to_pdf(d, folder, "DA3_Contribution")
    d.save(os.path.join(folder, "DA3_Contribution.docx"))


def zip_dir(src, out):
    if os.path.exists(out): os.remove(out)
    subprocess.run(["zip", "-qr", out, "."], cwd=src, check=True)


for name, reg in STUDENTS:
    person = os.path.join(SUB, f"{reg}_{name.replace(' ', '_')}")
    shutil.rmtree(person, ignore_errors=True)
    # DA1
    f = os.path.join(person, "DA1"); os.makedirs(f)
    shutil.copy(PAPER, os.path.join(f, "base_paper_PPO_Schulman_2017.pdf"))
    subprocess.run([sys.executable, os.path.join(ROOT, "python/tools/build_ppt_da1.py")], check=True, capture_output=True,
                   env={**os.environ, "DA1_OUT": os.path.join(f, "DA1_PPO_Base_Paper_Presentation.pptx"), "DA1_STUDENT": f"{name}  {reg}"})
    main_da1(f, name, reg)
    # DA2
    f = os.path.join(person, "DA2")
    shutil.copytree(os.path.join(SUB, "DA2"), f, ignore=shutil.ignore_patterns("README.md"))
    main_da2(f, name, reg)
    # DA3
    f = os.path.join(person, "DA3")
    shutil.copytree(os.path.join(SUB, "DA3"), f, ignore=shutil.ignore_patterns("DA3_Contribution.*"))
    contribution(f, name, reg)
    for da in ("DA1", "DA2", "DA3"):
        zip_dir(os.path.join(person, da), os.path.join(person, f"{reg}_{da}.zip"))
    print("done", person)
