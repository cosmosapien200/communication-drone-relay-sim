# Vision results — unet_v4

Test set: 1650 frames from held-out map seeds; model params 482788, best val mIoU 0.914.

| Class | IoU | Precision | Recall |
|---|---|---|---|
| unknown | 0.962 | 0.967 | 0.995 |
| landable | 0.825 | 0.888 | 0.921 |
| not landable | 0.934 | 0.972 | 0.960 |
| obstacle | 0.952 | 0.978 | 0.973 |
| **mean** | **0.919** | | |

Pixel accuracy 0.9571.

## Landing-spot locator (nadir, Unity in-the-loop, held-out maps)

- Frames: 864 (patch in view: 752, out of view: 112)
- False-negative rate (patch visible, not found): **0.012**
- False-positive rate (patch out of view, something reported): **0.446**
- Centre localisation error: bias 0.008, sigma 0.293 (robust 0.253) normalised image units; median 0.440 m, 90th pct 1.153 m
- Relative area error sigma: 0.280

These rates parameterise `VisionNoiseModel` (unity/Assets/Resources/vision_noise.json).
