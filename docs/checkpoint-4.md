# Checkpoint 4 — Vision dataset and segmentation model

- Dataset: synthetic, auto-labelled by re-rendering with flat per-class unlit colours; 125 maps × 110 frames,
  split by map seed (train 11 000 / val 1 100 / test 1 650), 45° explore and nadir poses, domain randomisation
  (palette, dust, lighting direction/intensity, shadows, fog; blur/noise/colour jitter as GPU augmentation).
- Model: U-Net (0.48 M params), CE + Dice, class weights. v1 test mIoU 0.891, landable IoU 0.728.
  See `docs/results/vision_unet_v1.md` (and v2 after the world update).
- Verified inside Unity (Inference Engine): 95.8 % pixel accuracy on test PNGs; orientation checked.
