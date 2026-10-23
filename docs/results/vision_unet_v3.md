# Vision results — unet_v3

Test set: 1650 frames from held-out map seeds; model params 482788, best val mIoU 0.910.

| Class | IoU | Precision | Recall |
|---|---|---|---|
| unknown | 0.966 | 0.972 | 0.994 |
| landable | 0.816 | 0.895 | 0.903 |
| not landable | 0.931 | 0.965 | 0.963 |
| obstacle | 0.947 | 0.977 | 0.968 |
| **mean** | **0.915** | | |

Pixel accuracy 0.9547.

## Landing-spot locator (nadir, Unity in-the-loop, held-out maps)

- Frames: 864 (patch in view: 752, out of view: 112)
- False-negative rate (patch visible, not found): **0.028**
- False-positive rate (patch out of view, something reported): **0.402**
- Centre localisation error: bias 0.016, sigma 0.303 (robust 0.260) normalised image units; median 0.448 m, 90th pct 1.234 m
- Relative area error sigma: 0.301

These rates parameterise `VisionNoiseModel` (unity/Assets/Resources/vision_noise.json).
