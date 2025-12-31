# MiddleManager Marketing Assets

AI-generated images and videos for social media marketing.

## Setup

1. Install Python dependencies:
   ```bash
   pip install -r requirements.txt
   ```

2. Set environment variables:
   ```powershell
   $env:VERTEX_AI_PROJECT_ID = "your-project-id"
   $env:VERTEX_AI_SERVICE_ACCOUNT_JSON = "C:\path\to\service-account.json"
   ```

## Scripts

### generate_image.py - Imagen 3

Generate images from text prompts.

```bash
python generate_image.py "A terminal with glowing text" output.png
python generate_image.py  # Uses default prompt
```

**Model:** `imagen-3.0-generate-002`

### generate_video.py - Veo 3

Generate videos from text prompts.

```bash
python generate_video.py "A terminal with scrolling code" output.mp4
python generate_video.py  # Uses default prompt
```

**Model:** `veo-3.0-generate-preview`

**Note:** Video generation takes 2-5 minutes.

### test_hello_world.ps1

Runs both scripts with test prompts.

```powershell
.\test_hello_world.ps1
```

## API Reference

- [Imagen API](https://docs.cloud.google.com/vertex-ai/generative-ai/docs/image/generate-images)
- [Veo API](https://docs.cloud.google.com/vertex-ai/generative-ai/docs/video/generate-videos-from-text)
