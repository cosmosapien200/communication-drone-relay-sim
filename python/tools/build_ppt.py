"""Project presentation (PowerPoint) -> Report/Communication_Drone_Presentation.pptx

usage: .venv/bin/python python/tools/build_ppt.py
"""
import os
from pptx import Presentation
from pptx.chart.data import CategoryChartData
from pptx.dml.color import RGBColor
from pptx.enum.chart import XL_CHART_TYPE, XL_LEGEND_POSITION, XL_LABEL_POSITION
from pptx.enum.shapes import MSO_SHAPE
from pptx.enum.text import PP_ALIGN, MSO_ANCHOR
from pptx.util import Inches, Pt
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "../.."))
A = lambda *p: os.path.join(ROOT, *p)
OUT = A("Report/Communication_Drone_Presentation.pptx")

NAVY = RGBColor(0x14, 0x21, 0x3D)      # dominant dark
ORANGE = RGBColor(0xF2, 0x7F, 0x1B)    # signal / rescue accent
TEAL = RGBColor(0x1F, 0x8A, 0x70)      # landable green
INK = RGBColor(0x22, 0x2B, 0x38)
MUTED = RGBColor(0x5F, 0x6B, 0x7A)
TINT = RGBColor(0xEE, 0xF2, 0xF7)
WHITE = RGBColor(0xFF, 0xFF, 0xFF)
GREY = RGBColor(0xA6, 0xA6, 0xA6)
HEAD, BODY = "Cambria", "Calibri"

prs = Presentation()
prs.slide_width, prs.slide_height = Inches(13.333), Inches(7.5)
BLANK = prs.slide_layouts[6]
W, H = 13.333, 7.5


def bg(slide, color):
    f = slide.background.fill; f.solid(); f.fore_color.rgb = color


def text(slide, x, y, w, h, runs, size=16, color=INK, font=BODY, bold=False, align=PP_ALIGN.LEFT, anchor=MSO_ANCHOR.TOP, italic=False):
    """runs: str, or list of paragraphs; a paragraph is str or list of (text, {bold, color, size})."""
    tb = slide.shapes.add_textbox(Inches(x), Inches(y), Inches(w), Inches(h))
    tf = tb.text_frame; tf.word_wrap = True; tf.vertical_anchor = anchor
    tf.margin_left = tf.margin_right = Inches(0.05); tf.margin_top = tf.margin_bottom = Inches(0.03)
    paras = runs if isinstance(runs, list) else [runs]
    for i, p in enumerate(paras):
        para = tf.paragraphs[0] if i == 0 else tf.add_paragraph()
        para.alignment = align
        para.space_after = Pt(6)
        for t, o in ([(p, {})] if isinstance(p, str) else p):
            r = para.add_run(); r.text = t
            r.font.name = o.get("font", font); r.font.size = Pt(o.get("size", size)); r.font.bold = o.get("bold", bold)
            r.font.italic = o.get("italic", italic); r.font.color.rgb = o.get("color", color)
    return tb


def bullets(slide, x, y, w, h, items, size=16, color=INK):
    tb = slide.shapes.add_textbox(Inches(x), Inches(y), Inches(w), Inches(h))
    tf = tb.text_frame; tf.word_wrap = True
    for i, it in enumerate(items):
        p = tf.paragraphs[0] if i == 0 else tf.add_paragraph()
        p.space_after = Pt(8)
        head, rest = it if isinstance(it, tuple) else ("", it)
        r = p.add_run(); r.text = "▸  "; r.font.color.rgb = ORANGE; r.font.size = Pt(size); r.font.name = BODY
        if head:
            r = p.add_run(); r.text = head; r.font.bold = True; r.font.size = Pt(size); r.font.color.rgb = color; r.font.name = BODY
        r = p.add_run(); r.text = rest; r.font.size = Pt(size); r.font.color.rgb = color; r.font.name = BODY
    return tb


def card(slide, x, y, w, h, fill=TINT):
    s = slide.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, Inches(x), Inches(y), Inches(w), Inches(h))
    s.adjustments[0] = 0.08; s.fill.solid(); s.fill.fore_color.rgb = fill; s.line.fill.background(); s.shadow.inherit = False
    return s


def stat(slide, x, y, w, value, label, vcolor=ORANGE, lcolor=MUTED, vsize=40):
    text(slide, x, y, w, 0.8, value, size=vsize, color=vcolor, font=HEAD, bold=True)
    text(slide, x, y + 0.82, w, 0.7, label, size=13, color=lcolor)


def picture(slide, path, x, y, w=None, h=None):
    """Fits the image inside the (w, h) box, keeping aspect ratio, centred."""
    iw, ih = Image.open(A(path)).size
    s = min(w / iw, h / ih)
    pw, ph = iw * s, ih * s
    return slide.shapes.add_picture(A(path), Inches(x + (w - pw) / 2), Inches(y + (h - ph) / 2), Inches(pw), Inches(ph))


def cover(slide, path, x, y, w, h):
    """Fills the box with the image, cropping the overflow (like CSS object-fit: cover)."""
    iw, ih = Image.open(A(path)).size
    pic = slide.shapes.add_picture(A(path), Inches(x), Inches(y), Inches(w), Inches(h))
    box, img = w / h, iw / ih
    if img > box:
        c = (1 - box / img) / 2; pic.crop_left = pic.crop_right = c
    else:
        c = (1 - img / box) / 2; pic.crop_top = pic.crop_bottom = c
    return pic


def title(slide, t, sub=None):
    text(slide, 0.6, 0.35, W - 1.2, 0.8, t, size=34, color=NAVY, font=HEAD, bold=True)
    if sub:
        text(slide, 0.6, 1.08, W - 1.2, 0.5, sub, size=16, color=MUTED, italic=True)


def footer(slide, n, dark=False):
    text(slide, W - 1.2, H - 0.45, 0.8, 0.3, str(n), size=11, color=GREY if not dark else MUTED, align=PP_ALIGN.RIGHT)


def chart_style(ch, colors, legend=True, maxv=None, fmt='0"%"'):
    ch.font.size = Pt(12); ch.font.name = BODY; ch.font.color.rgb = INK
    ch.has_legend = legend
    if legend:
        ch.legend.position = XL_LEGEND_POSITION.BOTTOM; ch.legend.include_in_layout = False; ch.legend.font.size = Pt(12)
    va = ch.value_axis; va.has_major_gridlines = True
    va.major_gridlines.format.line.color.rgb = RGBColor(0xDD, 0xDD, 0xDD); va.format.line.fill.background()
    va.tick_labels.font.size = Pt(11); va.tick_labels.font.color.rgb = MUTED; va.tick_labels.number_format = fmt; va.tick_labels.number_format_is_linked = False
    if maxv is not None:
        va.maximum_scale = maxv; va.minimum_scale = 0
    ca = ch.category_axis; ca.tick_labels.font.size = Pt(12); ca.format.line.color.rgb = GREY
    for s, c in zip(ch.plots[0].series, colors):
        f = s.format
        if ch.chart_type in (XL_CHART_TYPE.LINE_MARKERS,):
            f.line.color.rgb = c; f.line.width = Pt(2.75)
            s.marker.format.fill.solid(); s.marker.format.fill.fore_color.rgb = c; s.marker.format.line.color.rgb = c
        else:
            f.fill.solid(); f.fill.fore_color.rgb = c


n = 0
def new(dark=False):
    global n
    n += 1
    s = prs.slides.add_slide(BLANK); bg(s, NAVY if dark else WHITE)
    if n > 1: footer(s, n, dark)
    return s


# ---------------------------------------------------------------- 1 title
s = new(dark=True)
cover(s, "docs/screenshots/demo_level_topdown.png", 7.0, 0, W - 7.0, H)
text(s, 0.7, 1.1, 6.0, 2.6, "Intelligent Wi-Fi Relay Drone for Autonomous Disaster Communication", size=32, color=WHITE, font=HEAD, bold=True)
text(s, 0.7, 3.95, 6.0, 0.9, "A self-deploying chain of relay drones — reinforcement-learned flight, vision-guided landing, developed in simulation",
     size=17, color=RGBColor(0xC9, 0xD3, 0xE3), italic=True)
text(s, 0.7, 5.2, 6.0, 1.3, [[("LOKESHWARAN P", {"bold": True}), ("  23BAI1098", {})], [("ANNAMALAI SRINIVAS", {"bold": True}), ("  23BAI1006", {})],
                              [("YATHARTH TIWARI", {"bold": True}), ("  23BAI1401", {})]], size=14, color=WHITE)
s.notes_slide.notes_text_frame.text = "Introduce the team and the one-line idea: drones that land as Wi-Fi relays to reconnect a disaster camp."

# ---------------------------------------------------------------- 2 problem
s = new()
title(s, "Disasters cut communication off")
bullets(s, 0.6, 1.5, 5.9, 4.5, [
    ("Blackout: ", "floods and earthquakes destroy towers, power and fibre; rescue teams lose contact."),
    ("One drone isn't enough: ", "15–30 min of hover, and buildings block Wi-Fi around every corner."),
    ("Our idea: ", "launch drones one by one; each lands on a safe spot that still hears the previous relay and becomes the next link."),
    ("Landed relay = long endurance: ", "it only powers its radio, not its rotors."),
], size=17)
card(s, 6.9, 1.5, 5.85, 4.9)
cover(s, "docs/report_assets/video/t100.png", 7.05, 1.65, 5.55, 4.0)
text(s, 7.05, 5.75, 5.55, 0.6, "A relay drone landing on a camera-detected safe patch (nadir camera, bottom right).", size=12, color=MUTED)
s.notes_slide.notes_text_frame.text = "Explain why a chain of perching drones beats one hovering drone."

# ---------------------------------------------------------------- 3 architecture
s = new()
title(s, "Planner decides where; policies fly", "Hierarchical autonomy — every step runs at 10 Hz")
picture(s, "docs/report_assets/fig_system_architecture.png", 0.6, 1.6, 12.1, 4.3)
for i, (h_, t_, c) in enumerate([("Mission planner", "rules: frontier exploration, radio-aware landing spot, shared map", NAVY),
                                  ("Two PPO policies", "navigation through unknown streets; landing under gusts", ORANGE),
                                  ("Machine vision", "U-Net segmentation → landable map + landing-spot features", TEAL)]):
    x = 0.6 + i * 4.1
    card(s, x, 6.0, 3.9, 0.95)
    text(s, x + 0.15, 6.05, 3.6, 0.9, [[(h_ + "  ", {"bold": True, "color": c}), (t_, {"size": 13})]], size=14)
s.notes_slide.notes_text_frame.text = "Rules where verification matters; learning where hand-written controllers fail (unknown obstacles, gusts, noisy detections)."

# ---------------------------------------------------------------- 4 simulator
s = new()
title(s, "A Unity simulator built entirely from code")
picture(s, "docs/screenshots/maps/maps_overview.png", 0.5, 1.35, 8.2, 4.2)
text(s, 0.6, 5.65, 8.0, 0.5, "Seeded procedural towns, 4 difficulty levels — training on even seeds, every test on unseen odd seeds.", size=13, color=MUTED)
stat(s, 9.2, 1.5, 3.6, "4 levels", "rubble, collapses, scarce landing spots, wind up to 1.5 m/s gusts")
stat(s, 9.2, 3.1, 3.6, "−80 dBm", "link threshold; log-distance path loss + 10/7/5 dB per wall")
stat(s, 9.2, 4.7, 3.6, "−71 dBm", "needed to land as a relay (≈ 35 m along a clear street)")
s.notes_slide.notes_text_frame.text = "Same grid drives collisions, range sensors and radio; a 3-D textured scene is built from it for the camera."

# ---------------------------------------------------------------- 5 flight model
s = new()
title(s, "Two flight models, one autopilot interface", "Policies send velocity set-points, like a companion computer to PX4")
picture(s, "docs/report_assets/drones/fig_cascaded_pid.png", 0.5, 1.6, 12.3, 3.3)
for i, (v, l) in enumerate([("Full", "1.2 kg quadrotor, 4 rotors, cascaded PID, 500 Hz — evaluation"),
                            ("Fast", "first-order surrogate, 50 Hz — RL training (millions of steps)"),
                            ("0.83 / 0.84 s", "settling time full / fast — matched"),
                            ("0.38 m", "drift in a 3 m/s gust, both models")]):
    x = 0.6 + i * 3.1
    card(s, x, 5.1, 2.9, 1.75)
    stat(s, x + 0.15, 5.15, 2.6, v, l, vsize=28)
s.notes_slide.notes_text_frame.text = "Trained on Fast, tested on Full: 3–6 points lost per flight; fine-tuning on Full is future work."

# ---------------------------------------------------------------- 6 navigation RL
s = new()
title(s, "Navigation PPO: unknown streets, no map")
card(s, 0.6, 1.4, 5.3, 5.4)
bullets(s, 0.8, 1.55, 5.0, 5.2, [
    ("Inputs (33): ", "24 range rays ≤ 10 m, waypoint distance/bearing, velocity, heading, last action"),
    ("Outputs (3): ", "forward, sideways speed ≤ 3 m/s, yaw rate"),
    ("Network: ", "MLP 2 × 256, PPO, γ 0.995"),
    ("Reward: ", "progress along the street path, −1 crash, +1 arrival, small time/wall/jerk costs"),
    ("Curriculum: ", "level 1 → 4; 10.75 M steps in ≈ 35 min"),
], size=15)
cd = CategoryChartData(); cd.categories = ["Level 1", "Level 2", "Level 3", "Level 4"]
cd.add_series("PPO (ours)", (98, 98, 93, 80)); cd.add_series("Potential field", (93, 92, 78, 54)); cd.add_series("Straight line", (84, 64, 39, 18))
gf = s.shapes.add_chart(XL_CHART_TYPE.COLUMN_CLUSTERED, Inches(6.2), Inches(1.4), Inches(6.6), Inches(5.4), cd)
ch = gf.chart; chart_style(ch, [ORANGE, NAVY, GREY], maxv=100)
ch.has_title = True; ch.chart_title.text_frame.text = "Success on unseen towns"; ch.chart_title.text_frame.paragraphs[0].runs[0].font.size = Pt(15)
p = ch.plots[0]; p.gap_width = 60; p.has_data_labels = True; p.data_labels.font.size = Pt(10); p.data_labels.number_format = '0'; p.data_labels.number_format_is_linked = False
p.data_labels.position = XL_LABEL_POSITION.OUTSIDE_END
s.notes_slide.notes_text_frame.text = "Path-progress reward makes detours rewarding. Potential field gets stuck in dead ends (46 % timeouts at level 4)."

# ---------------------------------------------------------------- 7 landing RL
s = new()
title(s, "Landing PPO: gusts and noisy detections")
cd = CategoryChartData(); cd.categories = ["0", "1.5", "3", "4.5", "6"]
cd.add_series("PPO landing", (100, 100, 100, 100, 100)); cd.add_series("PID visual servoing", (97, 91, 76, 64, 44))
gf = s.shapes.add_chart(XL_CHART_TYPE.LINE_MARKERS, Inches(0.5), Inches(1.4), Inches(6.6), Inches(5.4), cd)
ch = gf.chart; chart_style(ch, [ORANGE, NAVY], maxv=110)
ch.has_title = True; ch.chart_title.text_frame.text = "Landing success vs gust amplitude (m/s)"; ch.chart_title.text_frame.paragraphs[0].runs[0].font.size = Pt(15)
ch.value_axis.major_unit = 20
card(s, 7.4, 1.4, 5.4, 5.4)
bullets(s, 7.6, 1.55, 5.1, 5.2, [
    ("Inputs (10): ", "patch offset, area, confidence, clearance from the U-Net + altitude, velocity"),
    ("Reward: ", "offset & fast-descent penalties + potential shaping Φ = −0.1(h + 2·offset); +1 soft touchdown"),
    ("Lesson learned: ", "v1 hovered forever (timeout was cheaper than failing) → fixed in v2"),
    ("Robust: ", "100 % even at 6 m/s (never trained) and 2× detector noise, where PID drops to 0 %"),
], size=15)
s.notes_slide.notes_text_frame.text = "Touchdown error 1–5 cm. Gust curriculum 0→4.5 m/s, then fine-tuned with the measured detector noise."

# ---------------------------------------------------------------- 8 vision
s = new()
title(s, "Vision: which ground is safe to land on?")
picture(s, "docs/report_assets/fig_qualitative.png", 0.5, 1.3, 6.3, 5.7)
stat(s, 7.3, 1.4, 2.6, "0.92", "mean IoU on unseen towns")
stat(s, 10.1, 1.4, 2.6, "0.83", "landable-class IoU")
stat(s, 7.3, 3.0, 2.6, "0.48 M", "U-Net parameters")
stat(s, 10.1, 3.0, 2.6, "≈ 4 ms", "per frame on CPU in Unity")
bullets(s, 7.3, 4.7, 5.5, 2.3, [
    "13,750 synthetic images, labels free by re-rendering in flat class colours",
    "Projected to the ground with the camera pose and fused into a shared map",
], size=15)
s.notes_slide.notes_text_frame.text = "Input | ground truth | prediction | errors. Green landable, red not, blue obstacle."

# ---------------------------------------------------------------- 9 mission
s = new()
title(s, "Mission: explore, pick a spot, land, relay")
picture(s, "docs/report_assets/drones/fig_mission_fsm.png", 0.5, 1.3, 12.3, 3.4)
for i, (h_, t_) in enumerate([("Explore rule", "go to the frontier closest to the camp whose predicted signal is still ≥ −74 dBm"),
                              ("Spot rule", "landable spot furthest toward the camp that still hears the parent at ≥ −71 dBm"),
                              ("Shared map", "next drones reuse what earlier ones saw: exploration 52 s → 30 s")]):
    x = 0.6 + i * 4.1
    card(s, x, 4.95, 3.9, 1.9)
    text(s, x + 0.2, 5.05, 3.5, 1.8, [[(h_, {"bold": True, "color": ORANGE, "size": 17})], t_], size=14)
s.notes_slide.notes_text_frame.text = "This rule is why relays end up at street corners. Random spot: 25 %, nearest spot: 3 %, our rule: 47 %."

# ---------------------------------------------------------------- 10 results
s = new()
title(s, "Results on 100 unseen towns", "Level-2 towns, up to six drones, real U-Net in the loop")
stat(s, 0.7, 1.7, 3.6, "45 %", "missions where the chain reaches the camp (51 % with perfect vision)", vsize=54)
stat(s, 0.7, 3.5, 3.6, "+62 m", "average coverage extended toward the camp", vsize=54)
stat(s, 0.7, 5.3, 3.6, "−72.5 dBm", "weakest link of a chain (threshold −80)", vsize=40)
cd = CategoryChartData()
rows = [("Full system", 47), ("Scripted landing", 42), ("Full quadrotor", 30), ("Random spot", 25), ("Scripted navigation", 9), ("Nearest spot", 3)]
cd.categories = [r[0] for r in rows][::-1]; cd.add_series("Camp covered", [r[1] for r in rows][::-1])
gf = s.shapes.add_chart(XL_CHART_TYPE.BAR_CLUSTERED, Inches(4.8), Inches(1.6), Inches(8.0), Inches(5.3), cd)
ch = gf.chart; chart_style(ch, [NAVY], legend=False, maxv=60)
ch.has_title = True; ch.chart_title.text_frame.text = "Ablations: camp covered (%)"; ch.chart_title.text_frame.paragraphs[0].runs[0].font.size = Pt(15)
p = ch.plots[0]; p.gap_width = 45; p.has_data_labels = True; p.data_labels.font.size = Pt(12); p.data_labels.number_format = '0"%"'; p.data_labels.number_format_is_linked = False
p.data_labels.position = XL_LABEL_POSITION.OUTSIDE_END
pt = p.series[0].points[len(rows) - 1]; pt.format.fill.solid(); pt.format.fill.fore_color.rgb = ORANGE
s.notes_slide.notes_text_frame.text = ("All-or-nothing metric on random towns; the ceiling with perfect vision is 51 %, set by whether a landable spot "
                                       "exists at each corner. Learned navigation is the biggest single contributor (47 % vs 9 %).")

# ---------------------------------------------------------------- 11 demo
s = new()
title(s, "Live demo: six maps, 58/60 reliability")
picture(s, "docs/report_assets/fig_demo_frames.png", 0.5, 1.3, 7.9, 5.6)
bullets(s, 8.7, 1.5, 4.2, 5.2, [
    ("Z-route, U-turn, Staircase, ", "Blocked boulevard + two unseen procedural towns"),
    ("Fixes from failure analysis: ", "camera aimed at the goal, distance-weighted evidence, cautious radio prediction"),
    ("Reliability: ", "34/60 → 58/60 runs over 10 seeds per map"),
    ("Run it: ", "./demo  (keys 1–6, R, Space, V)"),
], size=15)
s.notes_slide.notes_text_frame.text = "Switch to the live demo here: start with Z-route, then Blocked boulevard, then Procedural town A."

# ---------------------------------------------------------------- 12 limits / future
s = new()
title(s, "Limitations and next steps")
card(s, 0.6, 1.4, 5.9, 5.4)
text(s, 0.85, 1.55, 5.4, 0.5, "Limitations", size=20, color=NAVY, font=HEAD, bold=True)
bullets(s, 0.85, 2.15, 5.5, 4.6, [
    "Simulation only; synthetic images and a signal-strength radio model",
    "Trained on the fast model: 47 % → 30 % on the full quadrotor",
    "Dense towns lack landing spots at corners (level 4: 2 %)",
    "2.5-D flight at a fixed 5 m altitude",
], size=15)
card(s, 6.85, 1.4, 5.9, 5.4)
text(s, 7.1, 1.55, 5.4, 0.5, "Next steps", size=20, color=ORANGE, font=HEAD, bold=True)
bullets(s, 7.1, 2.15, 5.5, 4.6, [
    "Fine-tune both policies on the full quadrotor",
    "PX4 SITL in Gazebo, then Pixhawk + RV1103 hardware",
    "Hover-relay fallback when no landing spot exists",
    "Real aerial images to close the sim-to-real gap",
], size=15)

# ---------------------------------------------------------------- 13 thanks
s = new(dark=True)
text(s, 0.8, 2.2, 11.7, 1.2, "Thank you", size=54, color=WHITE, font=HEAD, bold=True)
text(s, 0.8, 3.5, 11.7, 1.4, "Drones that land as relays, learned flight that handles the unknown, and vision that finds where to land — "
     "together they carry the network ≈ 62 m toward a disconnected camp.", size=18, color=RGBColor(0xC9, 0xD3, 0xE3), italic=True)
text(s, 0.8, 5.4, 11.7, 0.5, [[("Questions?   ", {"bold": True, "color": ORANGE, "size": 20}),
                               ("github.com/cosmosapien200/communication-drone-relay-sim", {"size": 14})]], color=WHITE)

prs.save(OUT)
print("saved", OUT, n, "slides")
