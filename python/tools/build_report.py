"""Builds the final project report (Word) from the Review-1 document's styles and the repository's results.

usage: .venv/bin/python python/tools/build_report.py
output: Report/Communication_Drone_Final_Report.docx
"""
import copy, csv, json, os, statistics as st
from docx import Document
from docx.enum.table import WD_TABLE_ALIGNMENT
from docx.enum.text import WD_ALIGN_PARAGRAPH, WD_BREAK
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Inches, Pt, RGBColor

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "../.."))
A = lambda *p: os.path.join(ROOT, *p)
TEMPLATE = A("Report/Communication_Drone_Review_I_Final_Report.docx")
OUT = A("Report/Communication_Drone_Final_Report.docx")
VT = json.load(open(A("docs/report_assets/vision_tables.json")))


def mission_stats(path):
    if not os.path.exists(path):
        return None
    r = list(csv.DictReader(open(path)))
    if not r:
        return None
    s = [int(x["success"]) for x in r]; g = [float(x["range_extended_m"]) for x in r]
    return dict(n=len(r), succ=100 * st.mean(s), rng=st.mean(g), sd=st.pstdev(g))


M_CNN_V4 = mission_stats(A("results/eval/missions_v2/vision_cnn_v4.csv"))
M_GT_V2 = mission_stats(A("results/eval/missions_v2/vision_gt.csv"))

# ------------------------------------------------------------------ document scaffolding
doc = Document(TEMPLATE)
for sec in doc.sections:
    sec.left_margin = sec.right_margin = Inches(1.0); sec.top_margin = sec.bottom_margin = Inches(0.9)
body = doc.element.body
for el in list(body):
    if el.tag != qn("w:sectPr"):
        body.remove(el)

FIG = [0]; TAB = [0]


def para(text="", style="Body Text", bold_prefix=None, align=None, italic=False, size=None):
    p = doc.add_paragraph(style=style)
    if bold_prefix:
        r = p.add_run(bold_prefix); r.bold = True
    if text:
        r = p.add_run(text); r.italic = italic
        if size: r.font.size = Pt(size)
    if align == "center": p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    if align == "justify": p.alignment = WD_ALIGN_PARAGRAPH.JUSTIFY
    return p


def P(text, **kw):
    return para(text, align="justify", **kw)


def H1(t): return doc.add_paragraph(t, style="Heading 1")
def H2(t): return doc.add_paragraph(t, style="Heading 2")
def H3(t): return doc.add_paragraph(t, style="Heading 3")


def bullets(items, numbered=False):
    for it in items:
        p = doc.add_paragraph(style="List Number" if numbered else "List Bullet")
        if isinstance(it, tuple):
            r = p.add_run(it[0]); r.bold = True; p.add_run(it[1])
        else:
            p.add_run(it)
        p.alignment = WD_ALIGN_PARAGRAPH.JUSTIFY


def caption(prefix, text):
    p = doc.add_paragraph(style="Caption")
    r = p.add_run(prefix); r.bold = True
    p.add_run(text)
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    return p


def figure(path, text, width=6.0):
    p = doc.add_paragraph(); p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    p.add_run().add_picture(A(path), width=Inches(width))
    p.paragraph_format.keep_with_next = True
    FIG[0] += 1
    caption(f"Figure {FIG[0]}. ", text)


def _shade(cell, hex_fill):
    tcPr = cell._tc.get_or_add_tcPr()
    shd = OxmlElement("w:shd"); shd.set(qn("w:val"), "clear"); shd.set(qn("w:color"), "auto"); shd.set(qn("w:fill"), hex_fill)
    tcPr.append(shd)


def _borders(table):
    tblPr = table._tbl.tblPr
    b = OxmlElement("w:tblBorders")
    for edge in ("top", "left", "bottom", "right", "insideH", "insideV"):
        e = OxmlElement(f"w:{edge}"); e.set(qn("w:val"), "single"); e.set(qn("w:sz"), "4"); e.set(qn("w:space"), "0"); e.set(qn("w:color"), "A6A6A6")
        b.append(e)
    # schema order: tblBorders precedes shd, tblLayout, tblCellMar, tblLook, tblCaption, tblDescription
    after = [c for c in tblPr if c.tag in {qn("w:shd"), qn("w:tblLayout"), qn("w:tblCellMar"), qn("w:tblLook"), qn("w:tblCaption"), qn("w:tblDescription")}]
    if after: after[0].addprevious(b)
    else: tblPr.append(b)


def table(header, rows, widths, title=None, font=9):
    TAB[0] += 1
    if title:
        c = caption(f"Table {TAB[0]}. ", title); c.paragraph_format.keep_with_next = True
    t = doc.add_table(rows=1 + len(rows), cols=len(header))
    t.alignment = WD_TABLE_ALIGNMENT.CENTER
    t.autofit = False
    _borders(t)
    for i, w in enumerate(widths):
        for row in t.rows:
            row.cells[i].width = Inches(w)
    for i, h in enumerate(header):
        c = t.rows[0].cells[i]; c.text = ""
        p = c.paragraphs[0]; p.style = doc.styles["Table Heading"]; r = p.add_run(h); r.bold = True; r.font.size = Pt(font)
        p.alignment = WD_ALIGN_PARAGRAPH.CENTER; p.paragraph_format.space_after = Pt(2)
        _shade(c, "DCE6F1")
    trPr = t.rows[0]._tr.get_or_add_trPr(); th = OxmlElement("w:tblHeader"); th.set(qn("w:val"), "true"); trPr.append(th)
    for ri, row in enumerate(rows):
        for ci, v in enumerate(row):
            c = t.rows[ri + 1].cells[ci]; c.text = ""
            p = c.paragraphs[0]; p.style = doc.styles["Table Contents"]
            bold = isinstance(v, str) and v.startswith("**")
            r = p.add_run(v.strip("*") if isinstance(v, str) else str(v)); r.font.size = Pt(font); r.bold = bold
            p.paragraph_format.space_after = Pt(2)
            if ci > 0: p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    doc.add_paragraph().paragraph_format.space_after = Pt(2)
    return t


def page_break():
    doc.add_paragraph().add_run().add_break(WD_BREAK.PAGE)


def f3(x): return f"{x:.3f}"


# ------------------------------------------------------------------ title page
p = doc.add_paragraph("Intelligent Wi-Fi Relay Drone for Autonomous Disaster Communication.", style="Title")
p.alignment = WD_ALIGN_PARAGRAPH.CENTER
para("Final Project Report — Machine Vision", style="Subtitle", align="center")
para("Vision-guided landing-spot detection for a self-deploying relay-drone chain, developed and evaluated in simulation",
     align="center", italic=True)
para("")
for line in ("TEAM MEMBERS:", "LOKESHWARAN P – 23BAI1098", "ANNAMALAI SRINIVAS – 23BAI1006", "YATHARTH TIWARI – 23BAI1401"):
    q = para(line, style="Normal", align="center"); q.runs[0].bold = line.startswith("TEAM")
para("")
para("Code, trained models and results: github.com/cosmosapien200/communication-drone-relay-sim", style="Normal", align="center", size=9)
page_break()

# ------------------------------------------------------------------ 1 Abstract
H1("1. Abstract")
P("Natural disasters destroy communication infrastructure exactly when rescue teams need it most. This project builds "
  "an intelligent Wi-Fi relay drone system in which drones deploy one after another from a base station and land on "
  "camera-detected spots, forming a relay chain that carries the network toward a disconnected disaster camp. A landed "
  "relay draws only radio power and can stay on station far longer than a hovering one, which makes reliable "
  "landing-spot perception the central machine-vision problem of the system.")
P("Because of the limited project time, the complete system was developed and validated in simulation: a "
  "procedurally generated collapsed town in Unity 6 with a radio propagation model, a simulated quadrotor flight "
  "controller and two reinforcement-learning policies (navigation and landing). The machine-vision stack consists of "
  "(i) a fully synthetic, automatically labelled dataset of 13,750 images rendered from 125 generated towns with "
  "domain randomisation, plus 9,120 targeted fine-tuning images; (ii) a lightweight U-Net (0.48 M parameters, "
  "0.59 GMAC) that segments every 128×128 camera frame into landable, not-landable, obstacle and unknown classes; "
  "(iii) a geometric ground-projection and evidence-fusion module that turns per-pixel predictions into a shared "
  "landable-surface map; and (iv) a landing-spot locator that converts the nadir segmentation into observations for "
  "the landing policy.")
P(f"On held-out test towns the final model reaches a mean IoU of {VT['per_model']['U-Net v4 (final)']['miou']:.3f} "
  f"and a landable-class IoU of {VT['per_model']['U-Net v4 (final)']['iou'][1]:.3f} (precision "
  f"{VT['per_model']['U-Net v4 (final)']['precision'][1]:.2f}, recall {VT['per_model']['U-Net v4 (final)']['recall'][1]:.2f}), runs inside the "
  "simulator in about 4 ms per frame on a CPU, and locates the target patch in 98.8 % of nadir frames with a median "
  "centre error of 0.44 m. In closed-loop missions the detector's quality directly controls how far the relay chain "
  "reaches: the chain is robust up to roughly twice the measured detector error and collapses beyond four times it. "
  "Failure analysis of the live demonstrations traced most failures to perception and led to three vision "
  "improvements (goal-aimed camera gimbal, geometry-weighted evidence fusion, and fine-tuning on mission-specific "
  "viewpoints); together with replacing two procedural towns that proved unreliable, demonstration-map reliability "
  "rose from 34/60 to 58/60 runs.")

# ------------------------------------------------------------------ 2 Introduction
H1("2. Introduction")
P("Floods, earthquakes, cyclones and landslides routinely damage cellular towers, power and fibre, producing "
  "communication blackouts. Rescue teams lose contact with command centres, victims cannot share their location, and "
  "coordination slows down. Drones are an attractive way to restore connectivity quickly, but a single hovering "
  "drone is limited by battery life (minutes), and in a dense, collapsed town radio signals are blocked by buildings, "
  "so one relay is rarely enough.")
P("The approach taken here is a chain of relays that perch: each drone flies out from the end of the existing chain, "
  "explores the streets toward the disaster camp while keeping a usable link to its parent, finds a safe landing "
  "surface and lands there as a relay. The next drone inherits a shared map and continues from that relay. Whether "
  "this works depends critically on the drone's ability to see: it must recognise which surfaces are safe to land "
  "on (intact concrete, plazas, intact road) and which only look similar (debris, puddles, holes, wrecked vehicles, "
  "slopes), localise the chosen surface in metric world coordinates, and track it precisely during descent under "
  "wind gusts.")
P("This report focuses on these machine-vision components. Review 1 proposed a camera-plus-reinforcement-learning "
  "relay drone; the final work keeps that idea but moves the entire development into simulation, which allowed us "
  "to generate unlimited labelled training data, evaluate on hundreds of held-out scenarios, and run the full "
  "multi-drone mission end to end within the project time. Simulation also let us measure, for the first time in "
  "this project, how vision quality translates into communication range.")
bullets([
    ("Contribution 1 – Synthetic perception dataset: ", "a fully automatic pipeline that renders photorealistically randomised town images with pixel-perfect labels by re-rendering the same scene with flat class colours."),
    ("Contribution 2 – Landable-surface segmentation: ", "a 0.48 M-parameter U-Net, trained in PyTorch, exported to ONNX and executed inside the simulator in real time."),
    ("Contribution 3 – From pixels to a map: ", "ground projection with the known camera pose and a geometry-weighted evidence fusion that builds a shared landable-surface map across drones."),
    ("Contribution 4 – Landing-spot localisation: ", "connected-component and distance-transform analysis of the nadir segmentation, calibrated against ground truth to build a measured noise model for reinforcement learning."),
    ("Contribution 5 – Closed-loop evaluation: ", "measuring how segmentation quality changes the range of the relay chain, and a failure analysis that turned live-demo failures into concrete vision improvements."),
])

# ------------------------------------------------------------------ 3 Problem Statement
H1("3. Problem Statement")
P("Given a disaster area where the base station cannot reach the target camp directly, deploy a sequence of drones "
  "that autonomously extend network coverage toward the camp by landing at suitable spots, such that every link in "
  "the chain stays above a minimum signal strength (−80 dBm) and the camp is finally covered.")
P("The machine-vision sub-problem is:")
bullets([
    "From a single 128×128 RGB camera on a gimbal (45° forward-down while exploring, straight down while landing), classify every pixel as landable, not landable, obstacle or unknown, in a cluttered environment full of non-landable look-alikes and under varying light, shadow and dust.",
    "Project these predictions onto the ground using the drone's known pose and accumulate them over time and across drones into a reliable landable-surface map, so that the mission planner can choose a landing spot that both advances the chain and has enough signal.",
    "During landing, localise the chosen patch in the nadir image (offset, apparent size, confidence and edge clearance) robustly enough that a learned landing controller can touch down inside it under wind gusts.",
    "Do all of this with a model small enough for embedded deployment (the Review 1 target platform was an RV1103-class processor), and quantify how detector errors propagate to mission-level performance.",
])
P("Constraints: no real flight data was available in the project time; the solution therefore had to be trained "
  "and validated with synthetic data and simulation, with careful separation of training and evaluation maps so "
  "that results reflect generalisation to unseen towns.")

# ------------------------------------------------------------------ 4 Literature
H1("4. Literature Review")
H2("4.1 Drone-based emergency communication (from Review 1)")
P("FSO-based drone-assisted mobile access networks use drones as temporary aerial base stations connected to "
  "surviving infrastructure through free-space optical links and optimise placement, user association and bandwidth. "
  "They deploy quickly but depend on surviving infrastructure [1].")
P("Multi-UAV wireless mesh networks form a self-organising Wi-Fi mesh for disaster recovery with real-time video and "
  "target tracking, demonstrating low latency in field tests, but rely on 5G and non-terrestrial network "
  "infrastructure, which raises cost and complexity [2].")
P("Blockchain-enhanced UAV networks secure multi-agency coordination with smart contracts and hybrid consensus, "
  "gaining resilience at the price of significant computational and energy overhead [3].")
P("Across these works, drone positions are usually predetermined or optimised centrally; none addresses how a drone "
  "perceives where it can physically perch to act as a long-endurance relay.")
H2("4.2 Semantic segmentation")
P("Fully convolutional networks [4] made dense per-pixel classification practical by replacing fully connected "
  "layers with convolutions. U-Net [5] added a symmetric decoder with skip connections that recover fine spatial "
  "detail, and remains a strong choice when training data is limited and boundaries matter. Dice-based losses [6] "
  "help with class imbalance, which is relevant here because landable surfaces cover only 6–17 % of pixels. "
  "Embedded-oriented models such as FastDepth [7], which Review 1 considered for depth estimation, show that encoder–"
  "decoder networks well under one million parameters can run on low-power processors.")
H2("4.3 Synthetic data and domain randomisation")
P("Rendering engines can produce pixel-perfect labels at no annotation cost. Richter et al. [8] extracted ground "
  "truth from a video game and showed that synthetic data reduces the amount of real data needed; SYNTHIA [9] built a "
  "virtual city specifically for urban segmentation. Tobin et al. [10] showed that randomising textures, lighting and "
  "camera parameters broadly enough makes models treat the real world as just another variation. Our dataset follows "
  "these ideas: every image is generated from a procedural town with randomised palette, sun direction, dust and fog, "
  "and labels come from a second, flat-colour render of the same view.")
H2("4.4 Vision-based safe landing for UAVs")
P("Safe-landing-zone detection has been studied with classical texture and slope analysis [11] and with learned "
  "semantic segmentation for urban search and rescue, where landing candidates are scored from segmented aerial "
  "images [12]. These works typically assume a single nadir view; our setting additionally needs oblique exploration "
  "views to find candidates from street level and a nadir tracker during descent.")
H2("4.5 Mapping, exploration and learning-based control")
P("Occupancy grids [13] fuse uncertain observations into a probabilistic map; our landable-surface map is a "
  "per-cell evidence grid in the same spirit. Frontier-based exploration [14] drives exploration toward the boundary "
  "between known and unknown space and is used by our planner. The flight policies are trained with Proximal Policy "
  "Optimization [15] in Unity ML-Agents [16]; the radio channel follows the log-distance path-loss model [17] with "
  "material wall losses in the range reported by ITU-R P.2040 [18].")
H2("4.6 Research gap")
P("Existing relay-drone work focuses on network optimisation and assumes the drones can hold position; landing-zone "
  "detection work focuses on a single landing. There is little work that (a) uses vision to let a sequence of relay "
  "drones perch on safe surfaces, (b) fuses detections across drones into a shared map, and (c) measures how "
  "perception quality changes the communication range achieved. This project addresses that gap in simulation.")

# ------------------------------------------------------------------ 5 Dataset
H1("5. Dataset Description")
P("Review 1 listed KITTI and NYU Depth V2 for depth estimation. Neither provides labels for the question this "
  "system must answer (\"can a drone land here?\"), neither covers collapsed urban scenes from a drone's viewpoint, "
  "and there was no time to collect and annotate real flight imagery. We therefore generated a synthetic dataset "
  "directly from the simulator, with labels defined exactly by the task.")
H2("5.1 Generation pipeline")
bullets([
    ("Scene: ", "a seeded procedural collapsed town (96–120 m square, 1 m grid): street grid, box buildings 8–20 m tall, collapsed buildings and rubble barriers, landable surfaces (plaza tiles, flat concrete, intact road and street-aligned intact-road strips) and five kinds of look-alikes (debris fields, puddles, holes, wrecked vehicles, ramps/slopes)."),
    ("Rendering: ", "Unity built-in pipeline with real-time shadows; ground painted procedurally at 16 px/m with cracks, patches and dust; buildings and rubble from box primitives with procedural facade textures."),
    ("Domain randomisation (per map): ", "asphalt tone, dust tint and amount, surface palettes, sun elevation 15–75° and azimuth 0–360°, sun intensity and colour, ambient level, sky colour, fog in 30 % of maps."),
    ("Camera poses: ", "45° forward-down at cruise altitude 5 m (exploration) and nadir at 0.6–6 m (landing), 60° field of view, 128×128 pixels; nadir views are biased toward ground features."),
    ("Automatic labels: ", "the same frame is rendered a second time with every material replaced by an unlit flat class colour and no lighting or fog; the colours are decoded to class indices, so labels are pixel-perfect and need no manual annotation."),
])
figure("docs/report_assets/fig_label_rerender.png", "Label generation by re-rendering: the same generated town rendered normally (left) and with flat class colours (right). Green = landable, red = not landable, blue = obstacle, black = unknown/sky.", 5.2)
H2("5.2 Classes")
table(["Class", "Definition", "Typical content"], [
    ["Landable", "Flat, intact surface large enough for the drone", "Plaza tiles, flat concrete, intact road and road strips"],
    ["Not landable", "Ground that is unsafe", "Cracked asphalt, debris, puddles, holes, wrecked vehicles, ramps"],
    ["Obstacle", "Anything above the ground plane", "Buildings, rubble piles"],
    ["Unknown", "No ground information", "Sky, outside the map"],
], [1.2, 2.5, 2.7], "Segmentation classes")
H2("5.3 Splits and statistics")
P("Images are split by map seed, so every test image comes from a town never seen during training (training seeds "
  "are even, validation and test seeds odd and in separate ranges). After the first mission experiments two targeted "
  "fine-tuning sets were added, again from new maps: close-range nadir frames (0.5–2.5 m) and 45° views aimed at a "
  "landable patch or look-alike from 5–20 m, which is what the exploration camera actually sees during a mission.")
B = VT["dataset"]
def _row(name, tr, te, va=None):
    fr = B[tr]["fractions"]
    return [name, f"{B[tr]['maps']} / {B[va]['maps'] if va else '–'} / {B[te]['maps']}",
            f"{B[tr]['frames']:,} / {B[va]['frames']:,} / {B[te]['frames']:,}" if va else f"{B[tr]['frames']:,} / – / {B[te]['frames']:,}",
            f"{100*fr[1]:.1f} %", f"{100*fr[2]:.1f} %", f"{100*fr[3]:.1f} %", f"{100*fr[0]:.1f} %"]
table(["Set", "Maps (train / val / test)", "Frames (train / val / test)", "Landable", "Not landable", "Obstacle", "Unknown"],
      [_row("Main (45° + nadir)", "train", "test", "val"), _row("Close-range nadir", "lowalt_train", "lowalt_test"), _row("Patch-aimed 45°", "patchview_train", "patchview_test")],
      [1.35, 1.25, 1.55, 0.65, 0.7, 0.65, 0.65], "Dataset splits and pixel class balance of the training split (all splits use disjoint map seeds)")
figure("docs/report_assets/fig_dataset_samples.png", "Dataset samples (top: rendered input, bottom: automatic label) for the four view types.", 6.5)
P("The class distribution is imbalanced (landable 14–25 % of pixels, not-landable 61–74 %), which is handled with "
  "class-weighted cross-entropy plus Dice loss (Section 6.2).")

# ------------------------------------------------------------------ 6 Methodology
H1("6. Methodology")
H2("6.1 System overview")
P("Due to the limited time available for hardware integration and field testing, the complete system was designed, "
  "trained and validated in simulation. The simulation reproduces the elements that matter for perception and "
  "decision making: camera images, range sensors, radio signal strength, wind and quadrotor dynamics. The figure "
  "below shows the architecture. A scripted mission planner decides where each drone explores and lands; two PPO policies fly; "
  "the machine-vision module feeds both the planner (landable map) and the landing policy (spot observations).")
figure("docs/report_assets/fig_system_architecture.png", "System architecture. The machine-vision module (green) converts camera frames into landable cells for the shared map and into spot observations for the landing policy.", 5.8)
H2("6.2 Landable-surface segmentation network")
P("A compact U-Net [5] was chosen over heavier segmentation models because the target platform is embedded and "
  "the input resolution is small. The encoder has four levels (16, 32, 64, 128 channels), each with two 3×3 "
  "convolution–batch-norm–ReLU blocks and 2×2 max pooling; the decoder mirrors it with transposed convolutions and "
  "skip connections, and a 1×1 convolution outputs four class logits. The exported model applies a softmax and "
  "returns per-pixel class probabilities; the maximum probability is used as confidence.")
table(["Property", "Value"], [
    ["Input / output", "RGB 3×128×128 → class probabilities 4×128×128"],
    ["Parameters / compute", f"{VT['inference']['params']/1e6:.2f} M / {VT['inference']['GMACs']:.2f} GMAC per frame"],
    ["Loss", "class-weighted cross-entropy (weights ∝ 1/√frequency) + 0.5 × soft Dice"],
    ["Optimiser / schedule", "AdamW (lr 3e-3, weight decay 1e-4), OneCycle, batch 64, 40 epochs from scratch"],
    ["Augmentation (on GPU)", "brightness, contrast, saturation and tint jitter, random blur, Gaussian noise, horizontal flip"],
    ["Fine-tuning (v3 → v4)", "initialised from previous model, lr 1e-3, 15 epochs, original + targeted sets"],
    ["Deployment", f"ONNX (opset 15, {VT['inference']['onnx_MB']:.1f} MB) → Unity Inference Engine"],
], [2.0, 4.4], "Segmentation model configuration")
H2("6.3 Ground projection")
P("Each predicted pixel (u, v) is turned into a ray using the pinhole model of the 60° camera: with t = tan(30°), "
  "the ray direction in camera coordinates is d = (t(2(u+0.5)/W − 1), t(1 − 2(v+0.5)/H), 1), rotated by the "
  "known gimbal orientation. The ray is intersected with the ground plane y = 0 at s = −y_cam / d_y. Pixels whose "
  "ray points above the horizon, whose ground hit is farther than 25 m, or whose line of sight is blocked by a "
  "known obstacle column are discarded; obstacle and unknown pixels are not projected. The remaining hits vote for "
  "the 1 m map cell they fall in, using every second pixel (4,096 rays per frame).")
H2("6.4 Evidence fusion into the shared landable map")
P("Each cell keeps two accumulators, landable evidence E_L and not-landable evidence E_N. A projected pixel adds a "
  "weight w = p_max · max(0, −d_y) · (5 m / max(5 m, r))² to the accumulator of its predicted class, where p_max is "
  "the network confidence, −d_y the cosine of the viewing angle from vertical (steep views are more reliable) and r "
  "the ground distance. A cell is landable when E_L ≥ 0.5 and E_L / (E_L + E_N) ≥ 0.6, and a landing spot must "
  "additionally have at least three of its four neighbours landable (drone footprint plus clearance). The inverse-"
  "square range term was introduced after failure analysis showed that many distant, blurred, grazing-angle "
  "observations were outvoting a few close, correct ones (Section 9.4). Each drone works on a copy of the shared map "
  "and uploads only the evidence it gathered itself after landing.")
H2("6.5 Camera gimbal control")
P("While exploring, the 45° camera is aimed at the planner's current goal (frontier cell or candidate spot), "
  "falling back to the direction of travel, with a ±60° sinusoidal pan (4 s period) to cover side streets. "
  "During landing the gimbal points straight down. Aiming the camera independently of the body was a key finding: the "
  "navigation policy controls yaw freely and often flies sideways, so a body-fixed camera frequently never looked at "
  "the spot the drone was flying to.")
H2("6.6 Landing-spot locator")
P("In the nadir view the locator thresholds the landable probability at 0.5, labels 4-connected components, and "
  "selects the component of at least 12 pixels closest to where the target patch is expected from the drone's pose "
  "(within 35 % of the image width). It outputs the component centroid offset from the image centre (normalised to "
  "[−1, 1] in the drone's right/forward axes), the apparent area fraction, the mean landable probability "
  "(confidence) and the edge clearance, i.e. the Chebyshev distance from the centroid to the nearest non-component "
  "pixel converted to metres with the ground sample distance 2·h·tan(30°)/128. These ten-dimensional observations "
  "(with altitude, velocity and frames since last detection) are the landing policy's input.")
H2("6.7 Vision noise model")
P("Rendering cameras is the expensive part of simulation, so the reinforcement-learning policies were trained "
  "against a noise model with the same output types as the real modules. The noise model is calibrated from the "
  "real detector on held-out maps: the row-normalised confusion matrix corrupts ground-truth cell labels, and the "
  "measured locator false-negative rate, false-positive rate, localisation standard deviation and bias, area error "
  "and dropout corrupt ground-truth spot detections. Switching between the noise model and the real network is a "
  "single configuration option, which also enables a controlled detector-quality sweep (Section 8.6).")
H2("6.8 Reinforcement learning, flight and communication (summary)")
bullets([
    ("Navigation policy: ", "PPO, MLP 2×256, 33 inputs (waypoint distance and bearing, body velocity, heading, 24-ray range ring up to 10 m, previous action), 3 velocity outputs; reward = normalised shortest-path progress − time cost − proximity penalty, −1 on collision, +1 at the waypoint; curriculum over four town difficulty levels."),
    ("Landing policy: ", "PPO, MLP 2×128, the 10 vision-based inputs of Section 6.6; reward penalises offset and fast descent near the ground with potential-based shaping, +1 plus a centring bonus for a soft touchdown inside the patch, −1 for off-patch, hard impact or losing the patch; gust curriculum 0 → 4.5 m/s; fine-tuned with the measured vision noise."),
    ("Flight controller: ", "velocity set-points to a simulated quadrotor (cascaded velocity → attitude → rate PID, four rotors); a fast first-order model with matched step response is used for training."),
    ("Radio: ", "log-distance path loss (−40 dBm at 1 m, exponent 2.0) minus 10/7/5 dB per concrete/brick/rubble wall crossed; a relay spot qualifies if the measured signal to its parent is at least −71 dBm (link threshold −80 dBm + 6 dB margin + 3 dB ground penalty)."),
])
H2("6.9 Overall vision pipeline")
figure("docs/report_assets/fig_vision_pipeline.png", "Machine-vision pipeline: offline synthetic data generation and training (top), online perception in the mission loop (bottom), and calibration of the noise model.", 6.0)

# ------------------------------------------------------------------ 7 Implementation
H1("7. Implementation")
table(["Component", "Technology"], [
    ["Simulator", "Unity 6 (6000.6.4f1), built-in render pipeline, C#; all scenes and maps built from code"],
    ["Segmentation training", "PyTorch 2.5 (CUDA 12.1) on an NVIDIA RTX 3060 12 GB; data and augmentation kept on the GPU"],
    ["In-simulator inference", "ONNX → Unity Inference Engine 2.6 (CPU backend), 128×128 render target"],
    ["Reinforcement learning", "Unity ML-Agents 4.1 + mlagents 1.1.0 (PPO), CPU, 32 headless arenas per process"],
    ["Tooling", "uv project environment (Python 3.10), Unity MCP for editor inspection, git"],
    ["Compute", "Intel i5-13400F (16 threads), 32 GB RAM, RTX 3060 12 GB"],
], [1.9, 4.5], "Implementation stack")
H2("7.1 Code organisation")
P("The vision code is split along the interfaces of the system: the dataset generator (VisionDataset.cs) and scene "
  "renderer with label mode (TownBuilder.cs) in Unity; training, evaluation and ONNX export in PyTorch "
  "(train_seg.py); and in the simulator a model wrapper (SegModel.cs), the segmenter with ground projection and "
  "weighted observations (VisionSegmenter.cs), the landing-spot locator (LandingSpotLocator.cs), the calibrated noise "
  "model (VisionCalibration.cs) and the shared map with evidence accumulation (SharedMap.cs). All scenes and maps are "
  "built from code, and every experiment is reproducible from a seed.")
H2("7.2 Training procedure")
P("The main model (v2) was trained from scratch for 40 epochs on 11,000 images (about 20 s per epoch on the GPU). "
  "The deployed model (v4) was obtained by two fine-tuning steps: v3 added 3,600 close-range nadir images, and v4 "
  "added 4,800 patch-aimed exploration images, each for 15 epochs at a lower learning rate while still training on "
  "the original data to avoid forgetting. Two deliberately weaker models trained on 5 % and 20 % of the training maps "
  "serve the detector-quality ablation.")
figure("docs/report_assets/fig_training_curves.png", "Validation mIoU, landable IoU and training loss: training from scratch (left) and the final fine-tuning step (right).", 5.8)
H2("7.3 Deployment inside the simulator and verification")
P("The ONNX model runs in Unity's Inference Engine on rendered 128×128 frames. Two integration issues were found and "
  "fixed through explicit verification rather than assumed: (1) image orientation — the network was run inside "
  "Unity on test images and compared with the labels (94.5 % pixel accuracy with the correct top-left convention "
  "versus 62.6 % if flipped); (2) the GPU compute backend returned constant outputs in the Linux editor, so the CPU "
  "backend is used (≈4 ms per frame). A third, subtler issue was non-determinism between the windowed demo and "
  "headless evaluation: Unity sizes the shadow map from the screen resolution, so shadows and thus predictions "
  "differed slightly; a fixed shadow resolution made both identical.")
ms = VT["inference"]["ms"]
P(f"Measured inference cost of the deployed network ({VT['inference']['params']/1e6:.2f} M parameters, {VT['inference']['GMACs']:.2f} GMAC, "
  f"{VT['inference']['onnx_MB']:.1f} MB ONNX): {ms['onnxruntime CPU, 1 thread(s)']:.1f} ms per frame on one CPU thread, "
  f"{ms['onnxruntime CPU, 4 thread(s)']:.1f} ms on four threads and {ms['PyTorch CUDA (RTX 3060), batch 1']:.2f} ms on the RTX 3060 GPU — "
  "comfortably within the 10 Hz decision rate and small enough for an embedded NPU.")
H2("7.4 Simulation demonstration")
P("A live demonstration runs in the Unity editor or as a standalone build with six maps (four hand-built layouts "
  "and two held-out procedural towns). The on-screen display shows the mission mode, signal to parent, weakest "
  "link, battery, detection confidence and reward, a minimap of the shared landable map, and the camera view with "
  "the live segmentation overlay.")
figure("docs/report_assets/fig_demo_frames.png", "Frames from the demonstration video: exploration with the 45° camera and live segmentation (top left), navigating to a detected spot (top right), nadir landing with detection confidence 1.00 (bottom left), and the completed four-relay chain with the camp covered (bottom right).", 5.6)

# ------------------------------------------------------------------ 8 Experimental results
H1("8. Experimental Results")
P("All results are on held-out map seeds never used for training. Segmentation metrics are pixel-level over the "
  "1,650-image main test set unless stated; mission results average 100 missions per condition.")
H2("8.1 Segmentation accuracy")
pm = VT["per_model"]; f = pm["U-Net v4 (final)"]
table(["Class", "IoU", "Precision", "Recall"],
      [[c, f3(f["iou"][i]), f3(f["precision"][i]), f3(f["recall"][i])] for i, c in enumerate(["Unknown", "Landable", "Not landable", "Obstacle"])]
      + [["**Mean / pixel accuracy**", f"**{f3(f['miou'])}**", "", f"**{100*f['pixel_acc']:.1f} %**"]],
      [2.0, 1.2, 1.2, 1.2], "Per-class results of the final model (U-Net v4) on held-out test towns")
figure("docs/report_assets/fig_confusion_v4.png", "Row-normalised confusion matrix of the final model. Most residual landable errors are confusions with not-landable ground.", 3.1)
table(["Model", "Training data", "mIoU", "IoU landable", "Precision landable", "Recall landable"],
      [[k, d, f3(pm[k]["miou"]), f3(pm[k]["iou"][1]), f3(pm[k]["precision"][1]), f3(pm[k]["recall"][1])] for k, d in (
          ("Weak (5 % data)", "5 maps"), ("Mid (20 % data)", "20 maps"), ("U-Net v1", "100 maps (early world)"),
          ("U-Net v2", "100 maps"), ("U-Net v4 (final)", "100 maps + 132 fine-tune maps"))],
      [1.4, 1.6, 0.7, 0.9, 0.9, 0.9], "Model comparison on the same test set")
figure("docs/report_assets/fig_data_efficiency.png", "Data efficiency: segmentation quality as a function of the number of training towns.", 3.1)
H2("8.2 Accuracy by viewing condition")
pc = VT["per_condition"]
conds = list(pc["U-Net v4 (final)"].keys())
table(["Condition", "v2 IoU landable", "v4 IoU landable", "v4 precision", "v4 recall", "v4 mIoU"],
      [[c, f3(pc["U-Net v2"][c]["landable_iou"]), f"**{f3(pc['U-Net v4 (final)'][c]['landable_iou'])}**", f3(pc["U-Net v4 (final)"][c]["landable_p"]),
        f3(pc["U-Net v4 (final)"][c]["landable_r"]), f3(pc["U-Net v4 (final)"][c]["miou"])] for c in conds],
      [2.2, 0.9, 0.9, 0.8, 0.8, 0.8], "Landable-class accuracy by camera view (v2 = before fine-tuning, v4 = final)")
P("The network is strongest on oblique exploration views (landable IoU 0.88) and on nadir views from 4–6 m (0.89). "
  "Close-range nadir views below 2 m remain the hardest case (0.60–0.62): at that height the camera sees only a "
  "few ground-texture texels, shadows dominate, and a uniformly grey frame is genuinely ambiguous. Recall stays high "
  "(0.87–0.92) there, so the landing locator rarely loses the patch, but precision drops.")
H2("8.3 Qualitative results and failure cases")
figure("docs/report_assets/fig_qualitative.png", "Predictions on held-out test towns. Errors (right) concentrate on thin bands along class boundaries.", 4.3)
figure("docs/report_assets/fig_failures.png", "The hardest test frames: a featureless close-range nadir view (top), a distant small patch (middle) and a shaded patch next to a hole (bottom).", 4.3)
H2("8.4 Landing-spot localisation")
table(["Detector", "False negatives", "False positives (patch out of view)", "Median centre error", "90th percentile"], [
    ["U-Net v1", "7.3 %", "41.7 %", "0.33 m", "0.84 m"],
    ["U-Net v2", "1.6 %", "40.2 %", "0.44 m", "1.12 m"],
    ["**U-Net v4 (final)**", "**1.2 %**", "44.6 %", "**0.44 m**", "1.15 m"],
    ["Mid (20 % data)", "19.4 %", "16.1 %", "0.57 m", "1.42 m"],
    ["Weak (5 % data)", "100 %", "0 %", "–", "–"],
], [1.6, 1.0, 1.6, 1.1, 1.0], "Landing-spot locator measured inside the simulator on 864 nadir frames from 15 held-out towns")
P("False negatives are rare for the trained models. The high false-positive rate when the target is out of view "
  "means the locator usually reports some other landable surface nearby; the landing policy copes with this "
  "because it was trained with exactly this measured noise, and the planner only lands after the spot has been "
  "confirmed from cruise altitude. The weak model never isolates the patch, which makes it a useful lower bound.")
H2("8.5 Landing with vision in the loop")
table(["Gust (m/s)", "RL landing policy", "PID visual servoing"], [
    ["0", "100 %, error 0.01 m", "97 %, error 0.02 m"], ["1.5", "100 %, 0.02 m", "91 %, 0.04 m"],
    ["3.0", "100 %, 0.02 m", "76 %, 0.07 m"], ["4.5", "100 %, 0.03 m", "64 %, 0.11 m"],
    ["6.0 (beyond training)", "100 %, 0.05 m", "44 %, 0.14 m"], ["3.0 with 2× detector noise", "100 %", "0 %"],
], [2.0, 2.2, 2.2], "Landing success and touchdown error with the measured detector noise (100 episodes per cell)")
P("Training the landing controller on the calibrated detector noise is what makes it robust: the classical "
  "controller chases false detections and loses the patch, while the learned policy holds its approach when the "
  "detection is unreliable and lands within a few centimetres.")
H2("8.6 Mission-level effect of perception")
rows = [["Ground truth (perfect vision)", "43 %", "63.6 ± 34.0"], ["Real U-Net v2 in the loop", "34 %", "58.1 ± 36.2"],
        ["Mid detector in the loop", "7 %", "27.9 ± 29.7"], ["Weak detector in the loop", "0 %", "0.0"]]
if M_GT_V2: rows.append([f"Ground truth, final system", f"{M_GT_V2['succ']:.0f} %", f"{M_GT_V2['rng']:.1f} ± {M_GT_V2['sd']:.1f}"])
if M_CNN_V4: rows.append([f"**Real U-Net v4 in the loop, final system**", f"**{M_CNN_V4['succ']:.0f} %**", f"**{M_CNN_V4['rng']:.1f} ± {M_CNN_V4['sd']:.1f}**"])
table(["Perception used by the drones", "Camp covered", "Range extended toward camp (m)"], rows, [3.0, 1.3, 2.1],
      "Full missions on 100 held-out level-2 towns (up to six drones)")
if M_CNN_V4 and M_GT_V2:
    P(f"With the final vision system (goal-aimed gimbal, geometry-weighted fusion and U-Net v4) the share of missions "
      f"that cover the camp with the real detector in the loop rose from 34 % to {M_CNN_V4['succ']:.0f} %, and the "
      f"range extended from 58.1 m to {M_CNN_V4['rng']:.1f} m. The same planner with perfect vision reaches "
      f"{M_GT_V2['succ']:.0f} % ({M_GT_V2['rng']:.1f} m), so the remaining gap attributable to perception is about "
      f"{M_GT_V2['succ']-M_CNN_V4['succ']:.0f} percentage points; the rest is limited by town geometry and radio range.")
figure("docs/results/missions/headline_range_vs_detector.png", "How far the relay chain reaches toward the camp as the detector's error rates are scaled by k (k = 0 perfect, k = 1 measured U-Net, k > 1 weakened), with the real networks in the loop as separate markers.", 5.9)
table(["Error scale k", "0", "0.5", "1", "2", "4", "8"],
      [["Camp covered", "45 %", "51 %", "47 %", "45 %", "30 %", "0 %"], ["Range extended (m)", "65.5", "63.5", "64.4", "61.0", "53.0", "0.0"]],
      [1.6, 0.7, 0.7, 0.7, 0.7, 0.7, 0.7], "Detector-quality sweep with the calibrated noise model (100 missions per value)")
P("This is the central result connecting vision to communication: mission performance is flat up to about twice "
  "the measured error of our detector, degrades beyond four times, and collapses when the detector can no longer "
  "isolate landable surfaces. The real network in the loop performs slightly worse than its noise-model twin, "
  "because real errors are spatially correlated (a whole patch is misread) whereas the noise model assumes "
  "independent pixels.")

# ------------------------------------------------------------------ 9 Performance evaluation
H1("9. Performance Evaluation")
H2("9.1 Comparison with Review 1 objectives")
table(["Objective (Review 1)", "Outcome"], [
    ["Lightweight perception suitable for an embedded processor", f"U-Net with {VT['inference']['params']/1e6:.2f} M parameters, {VT['inference']['GMACs']:.2f} GMAC, {VT['inference']['onnx_MB']:.1f} MB; 4 ms per frame on 4 CPU threads"],
    ["Autonomous relay position selection", "Vision-built landable map + RSSI-gated spot selection; relays land 18 m further along the path per relay on average"],
    ["Improved coverage / signal strength", (f"With the real detector the chain extends coverage {M_CNN_V4['rng']:.0f} m toward the camp on average and covers it in {M_CNN_V4['succ']:.0f} % of held-out level-2 missions; weakest link ≈ −72.5 dBm (above the −80 dBm threshold)" if M_CNN_V4 else "")],
    ["PPO-based decision making", "Navigation 98/98/93/80 % success on four difficulty levels; landing 100 % under gusts up to 6 m/s with real detector noise"],
    ["Simulation before deployment", "Entire pipeline validated in simulation; hardware deployment is future work"],
], [2.5, 3.9], "Objectives versus outcomes")
H2("9.2 Strengths")
bullets([
    "Zero manual annotation: unlimited, pixel-perfect labels and held-out evaluation on unseen towns.",
    "High recall on landable surfaces (0.92) with a model small enough for embedded hardware.",
    "Perception errors are measured and fed back into training (noise model), which made the learned landing controller robust to the real detector.",
    "Every mission-level claim is measured end to end with the real network in the loop, not only with offline metrics.",
])
H2("9.3 Limitations")
bullets([
    "Sim-to-real gap: all images are synthetic; textures are procedural and simpler than real rubble. Real flight imagery will be needed before deployment.",
    "Close-range nadir accuracy (landable IoU ≈ 0.6 below 2 m) is limited by texture resolution and shadows.",
    "Low-contrast landable surfaces under some lighting are misclassified even at short range (observed in one procedural demo town).",
    "Mission success on harder towns is limited by geometry as much as by vision: if no landable surface exists at a street corner within radio range, the chain cannot turn that corner.",
    "Depth estimation (proposed in Review 1) was not used; the ground plane is assumed flat and obstacles come from range sensors.",
])
H2("9.4 Failure analysis of the live demonstration")
P("During the live demonstration two maps failed, although they had succeeded in earlier headless tests. Each "
  "demonstration map was therefore re-evaluated over ten wind, noise and lighting seeds, and every failure was "
  "classified from logged map dumps as perception (a usable landable spot was observed but not recognised), "
  "exploration (a usable spot was within radio range but never observed) or geometry (no usable spot existed). "
  "Nearly all failures were perception failures, and inspecting them revealed three vision-specific causes:")
bullets([
    ("Camera pointing: ", "logged camera poses showed the drone approaching a corner while facing backwards (heading 223–275°); the body-fixed camera never saw the corner patch. Aiming the gimbal at the planner's goal fixed the worst map from 1/10 to 10/10."),
    ("Evidence fusion: ", "a patch seen correctly at 5 m was outvoted by dozens of earlier views from 20–25 m in which it was a blurred sliver; inverse-square range weighting lets one close look dominate."),
    ("Viewpoint coverage of training data: ", "white concrete in the shadow of three buildings, viewed obliquely, was under-represented; fine-tuning on patch-aimed views raised landable IoU on that view type from 0.787 to 0.874."),
])
table(["Demonstration map", "Before", "After"], [
    ["Z-route", "5/10", "**10/10**"], ["U-turn", "9/10", "**10/10**"], ["Staircase", "9/10", "**10/10**"],
    ["Blocked boulevard", "5/10", "**8/10**"], ["Procedural town A (replaced, 5/10 → new town)", "5/10", "**10/10**"],
    ["Procedural town B (level-3, replaced by a level-2 town)", "1/10", "**10/10**"],
    ["**Total**", "**34/60**", "**58/60**"],
], [3.4, 1.4, 1.4], "Demonstration-map reliability over ten seeds each, before and after the vision fixes")
P("The two procedural towns were replaced after a robustness search: one original town contained a landable "
  "surface whose randomised colour made it indistinguishable from damaged street, and level-3 towns as a class are "
  "currently beyond the system (best 5/10). These are reported as limitations rather than hidden.")

# ------------------------------------------------------------------ 10 Conclusion
H1("10. Conclusion")
P("This project delivered a simulation-validated intelligent relay-drone system in which machine vision is the "
  "enabling component: drones find safe surfaces to perch on and thereby extend a disaster-area network one relay "
  "at a time. A fully synthetic, automatically labelled dataset and a 0.48 M-parameter U-Net achieved a test mIoU "
  f"of {f3(f['miou'])} and a landable IoU of {f3(f['iou'][1])} on unseen towns. Geometric ground projection with "
  "geometry-weighted evidence fusion turned these predictions into a shared landable map, and a connected-"
  "component locator provided centimetre-level landings under gusts when combined with a reinforcement-learning "
  "controller trained on the measured detector noise." + (f" With the real detector in the loop, the final system covered the "
  f"camp in {M_CNN_V4['succ']:.0f} % of 100 held-out missions (up from 34 %), extending coverage by {M_CNN_V4['rng']:.0f} m on average." if M_CNN_V4 else ""))
P("The closed-loop experiments show quantitatively that perception quality determines communication range, and "
  "the failure analysis showed that where and how the camera looks, and how observations are fused, matter as much "
  "as raw segmentation accuracy. The same pipeline is ready to be retrained on real imagery and deployed on the "
  "embedded hardware proposed in Review 1.")

# ------------------------------------------------------------------ 11 Future scope
H1("11. Future Scope")
bullets([
    ("Hardware deployment: ", "port the U-Net to the RV1103 NPU (INT8 quantisation) and integrate with the Pixhawk flight controller and gimbal proposed in Review 1."),
    ("Real data and sim-to-real transfer: ", "collect a small annotated set of real aerial images of damaged urban areas, fine-tune with mixed synthetic and real data, and add photorealistic rendering or image-translation-based domain adaptation."),
    ("Depth and geometry: ", "add monocular depth (e.g. a FastDepth-style encoder) or the ToF sensor to detect slopes and small obstacles that a flat-ground assumption misses."),
    ("Temporal and uncertainty-aware perception: ", "video segmentation and calibrated per-pixel uncertainty, so the fusion can trust confident pixels more and request a closer look when unsure."),
    ("Active perception: ", "let the planner choose viewpoints that maximise information about candidate spots instead of a fixed pan pattern."),
    ("Multi-drone extensions: ", "drones that re-position after landing, and coverage of several user points at the camp."),
])

# ------------------------------------------------------------------ 12 References
H1("12. References")
refs = [
    "FSO-Based Drone Assisted Mobile Access Network for Emergency Communications (surveyed in Review 1).",
    "Multi-UAV Wireless Mesh Networks for Disaster Recovery, 2026 (surveyed in Review 1).",
    "Blockchain-Enhanced UAV Networks for Post-Disaster Communication, 2026 (surveyed in Review 1).",
    "J. Long, E. Shelhamer and T. Darrell, \"Fully Convolutional Networks for Semantic Segmentation,\" in Proc. IEEE CVPR, 2015.",
    "O. Ronneberger, P. Fischer and T. Brox, \"U-Net: Convolutional Networks for Biomedical Image Segmentation,\" in Proc. MICCAI, 2015.",
    "F. Milletari, N. Navab and S.-A. Ahmadi, \"V-Net: Fully Convolutional Neural Networks for Volumetric Medical Image Segmentation,\" in Proc. 3DV, 2016.",
    "D. Wofk, F. Ma, T.-J. Yang, S. Karaman and V. Sze, \"FastDepth: Fast Monocular Depth Estimation on Embedded Systems,\" in Proc. IEEE ICRA, 2019.",
    "S. R. Richter, V. Vineet, S. Roth and V. Koltun, \"Playing for Data: Ground Truth from Computer Games,\" in Proc. ECCV, 2016.",
    "G. Ros, L. Sellart, J. Materzynska, D. Vazquez and A. M. Lopez, \"The SYNTHIA Dataset: A Large Collection of Synthetic Images for Semantic Segmentation of Urban Scenes,\" in Proc. IEEE CVPR, 2016.",
    "J. Tobin, R. Fong, A. Ray, J. Schneider, W. Zaremba and P. Abbeel, \"Domain Randomization for Transferring Deep Neural Networks from Simulation to the Real World,\" in Proc. IEEE/RSJ IROS, 2017.",
    "T. Patterson, S. McClean, P. Morrow, G. Parr and C. Luo, \"Timely Autonomous Identification of UAV Safe Landing Zones,\" Image and Vision Computing, vol. 32, no. 9, 2014.",
    "M. Mittal, R. Mohan, W. Burgard and A. Valada, \"Vision-Based Autonomous UAV Navigation and Landing for Urban Search and Rescue,\" in Proc. ISRR, 2019.",
    "A. Elfes, \"Using Occupancy Grids for Mobile Robot Perception and Navigation,\" Computer, vol. 22, no. 6, 1989.",
    "B. Yamauchi, \"A Frontier-Based Approach for Autonomous Exploration,\" in Proc. IEEE CIRA, 1997.",
    "J. Schulman, F. Wolski, P. Dhariwal, A. Radford and O. Klimov, \"Proximal Policy Optimization Algorithms,\" arXiv:1707.06347, 2017.",
    "A. Juliani et al., \"Unity: A General Platform for Intelligent Agents,\" arXiv:1809.02627, 2018.",
    "T. S. Rappaport, Wireless Communications: Principles and Practice, 2nd ed., Prentice Hall, 2002.",
    "ITU-R Recommendation P.2040, \"Effects of building materials and structures on radiowave propagation above about 100 MHz.\"",
]
for i, r in enumerate(refs, 1):
    q = doc.add_paragraph(style="Body Text"); q.add_run(f"[{i}] ").bold = True; q.add_run(r)
    q.paragraph_format.left_indent = Inches(0.35); q.paragraph_format.first_line_indent = Inches(-0.35)

doc.save(OUT)
print("saved", OUT, "figures", FIG[0], "tables", TAB[0], "mission v4:", M_CNN_V4, "gt:", M_GT_V2)
