# MidTerm Marketing Assets

AI-generated images and videos for social media marketing using Google Vertex AI.

## Models

| Purpose | Model | Location | Notes |
|---------|-------|----------|-------|
| Image generation | `gemini-3-pro-image-preview` | `global` | Nano Banana Pro - high quality, supports reference images |
| Video generation | `veo-3.1-generate-001` | `us-central1` | Supports first+last frame transitions |

**Important:** Different models require different locations. The image model MUST use `location="global"`.

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

### generate_image.py
Basic text-to-image generation.

```bash
python generate_image.py "A terminal with glowing text" output.png
```

### generate_video.py
Basic text-to-video generation.

```bash
python generate_video.py "A terminal with scrolling code" output.mp4
```

### test_advanced_workflow.py
**Full creative pipeline** - the main workflow for creating marketing content:

1. Generate base image (person sitting at desk)
2. Generate variation using reference image (same person, now standing)
3. Generate transition video with first+last frame (smooth animation)

```bash
python test_advanced_workflow.py
```

Output in `output/advanced_test/`:
- `step1_sitting.png` - Base image
- `step2_standing.png` - Variation with same person
- `step3_dance_transition.mp4` - Smooth transition video

## Proven Capabilities

### Image Generation (gemini-3-pro-image-preview)

**Text-to-image:**
```python
response = client.models.generate_content(
    model="gemini-3-pro-image-preview",
    contents=prompt,
    config=types.GenerateContentConfig(
        response_modalities=["IMAGE"],
        image_config=types.ImageConfig(
            aspect_ratio="16:9",  # 1:1, 3:4, 4:3, 9:16, 16:9, etc.
        ),
    ),
)
```

**Image-to-image (reference):**
```python
response = client.models.generate_content(
    model="gemini-3-pro-image-preview",
    contents=[
        types.Part.from_bytes(data=image_bytes, mime_type="image/png"),
        "Generate the same person but now standing...",
    ],
    config=types.GenerateContentConfig(
        response_modalities=["IMAGE"],
        image_config=types.ImageConfig(aspect_ratio="16:9"),
    ),
)
```

### Video Generation (veo-3.1-generate-001)

**First + Last Frame Transition:**
```python
operation = client.models.generate_videos(
    model="veo-3.1-generate-001",
    prompt="Smooth transition with dance move",
    image=first_image,  # First frame
    config=GenerateVideosConfig(
        aspect_ratio="16:9",
        duration_seconds=4,  # 4, 6, or 8 seconds
        generate_audio=False,
        resolution="720p",  # or 1080p
        last_frame=last_image,  # Last frame
    ),
)
```

**Note:** `veo-3.0-generate-preview` does NOT support first+last frame. Must use `veo-3.1-generate-001`.

## Key Learnings

1. **Location matters:** `gemini-3-pro-image-preview` requires `location="global"`, video models use `us-central1`

2. **Aspect ratio consistency:** Always specify the same aspect ratio for both images when doing A→B transitions

3. **Reference images work:** Pass the first image as `Part.from_bytes()` in the contents array to maintain person/scene consistency

4. **Extracting images from response:**
   ```python
   for part in response.candidates[0].content.parts:
       if part.inline_data and part.inline_data.mime_type.startswith("image/"):
           image_bytes = part.inline_data.data
   ```

5. **Video polling:** Video generation is async - poll `operation.done` every 15 seconds

6. **Cost optimization for testing:**
   - Images: Use smaller aspect ratios (1:1) during dev
   - Videos: Use `duration_seconds=4`, `resolution="720p"`, `generate_audio=False`

## API Reference

- [Gemini Image Generation](https://ai.google.dev/gemini-api/docs/image-generation)
- [Veo First+Last Frame](https://docs.cloud.google.com/vertex-ai/generative-ai/docs/video/generate-videos-from-first-and-last-frames)
- [Vertex AI Veo API](https://docs.cloud.google.com/vertex-ai/generative-ai/docs/model-reference/veo-video-generation)
