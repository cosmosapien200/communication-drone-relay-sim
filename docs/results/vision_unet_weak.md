# Vision results — unet_weak

Test set: 1650 frames from held-out map seeds; model params 482788, best val mIoU 0.434.

| Class | IoU | Precision | Recall |
|---|---|---|---|
| unknown | 0.568 | 0.734 | 0.715 |
| landable | 0.190 | 0.361 | 0.287 |
| not landable | 0.639 | 0.761 | 0.799 |
| obstacle | 0.506 | 0.658 | 0.686 |
| **mean** | **0.476** | | |

Pixel accuracy 0.6859.

## Landing-spot locator (nadir, Unity in-the-loop, held-out maps)

- Frames: 864 (patch in view: 752, out of view: 112)
- False-negative rate (patch visible, not found): **1.000**
- False-positive rate (patch out of view, something reported): **0.000**
- Centre localisation error: bias 1.414, sigma 0.000 (robust 0.000) normalised image units; median nan m, 90th pct nan m
- Relative area error sigma: 0.000

These rates parameterise `VisionNoiseModel` (unity/Assets/Resources/vision_noise.json).
