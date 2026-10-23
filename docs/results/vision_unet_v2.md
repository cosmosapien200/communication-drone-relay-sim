# Vision results — unet_v2

Test set: 1650 frames from held-out map seeds; model params 482788, best val mIoU 0.906.

| Class | IoU | Precision | Recall |
|---|---|---|---|
| unknown | 0.955 | 0.960 | 0.994 |
| landable | 0.810 | 0.880 | 0.910 |
| not landable | 0.927 | 0.966 | 0.958 |
| obstacle | 0.944 | 0.981 | 0.961 |
| **mean** | **0.909** | | |

Pixel accuracy 0.9521.

## Landing-spot locator (nadir, Unity in-the-loop, held-out maps)

- Frames: 864 (patch in view: 752, out of view: 112)
- False-negative rate (patch visible, not found): **0.016**
- False-positive rate (patch out of view, something reported): **0.402**
- Centre localisation error: bias 0.004, sigma 0.292 (robust 0.247) normalised image units; median 0.444 m, 90th pct 1.116 m
- Relative area error sigma: 0.268

These rates parameterise `VisionNoiseModel` (unity/Assets/Resources/vision_noise.json).
