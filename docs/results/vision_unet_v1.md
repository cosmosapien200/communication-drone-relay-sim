# Vision results — unet_v1

Test set: 1650 frames from held-out map seeds; model params 482788, best val mIoU 0.879.

| Class | IoU | Precision | Recall |
|---|---|---|---|
| unknown | 0.936 | 0.940 | 0.996 |
| landable | 0.728 | 0.836 | 0.850 |
| not landable | 0.961 | 0.983 | 0.978 |
| obstacle | 0.937 | 0.967 | 0.967 |
| **mean** | **0.891** | | |

Pixel accuracy 0.9700.

## Landing-spot locator (nadir, Unity in-the-loop, held-out maps)

- Frames: 874 (patch in view: 742, out of view: 132)
- False-negative rate (patch visible, not found): **0.073**
- False-positive rate (patch out of view, something reported): **0.417**
- Centre localisation error: bias 0.022, sigma 0.259 (robust 0.158) normalised image units; median 0.331 m, 90th pct 0.837 m
- Relative area error sigma: 0.303

These rates parameterise `VisionNoiseModel` (unity/Assets/Resources/vision_noise.json).
