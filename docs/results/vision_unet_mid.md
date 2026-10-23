# Vision results — unet_mid

Test set: 1650 frames from held-out map seeds; model params 482788, best val mIoU 0.640.

| Class | IoU | Precision | Recall |
|---|---|---|---|
| unknown | 0.722 | 0.757 | 0.941 |
| landable | 0.422 | 0.619 | 0.569 |
| not landable | 0.749 | 0.852 | 0.860 |
| obstacle | 0.729 | 0.856 | 0.831 |
| **mean** | **0.656** | | |

Pixel accuracy 0.8082.

## Landing-spot locator (nadir, Unity in-the-loop, held-out maps)

- Frames: 864 (patch in view: 752, out of view: 112)
- False-negative rate (patch visible, not found): **0.194**
- False-positive rate (patch out of view, something reported): **0.161**
- Centre localisation error: bias 0.042, sigma 0.323 (robust 0.284) normalised image units; median 0.574 m, 90th pct 1.419 m
- Relative area error sigma: 0.345

These rates parameterise `VisionNoiseModel` (unity/Assets/Resources/vision_noise.json).
