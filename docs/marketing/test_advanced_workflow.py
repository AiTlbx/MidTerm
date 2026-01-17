#!/usr/bin/env python3
"""
Advanced Workflow Test: Person at desk -> Person standing -> Dancing transition video

Models used:
- Image: gemini-3-pro-image-preview (Nano Banana Pro)
- Video: veo-3.1-generate-001

Steps:
1. Generate base image (person sitting at desk)
2. Use image as reference to generate variation (person standing)
3. Use both images as first/last frame for Veo video (dancing transition)
"""

import os
import sys
import time
import base64
from pathlib import Path

def setup_client():
    """Initialize the Google GenAI client with service account credentials."""
    from google import genai
    from google.oauth2.service_account import Credentials

    project_id = os.environ.get("VERTEX_AI_PROJECT_ID")
    service_account_path = os.environ.get("VERTEX_AI_SERVICE_ACCOUNT_JSON")

    if not project_id or not service_account_path:
        print("ERROR: Missing environment variables")
        sys.exit(1)

    scopes = ["https://www.googleapis.com/auth/cloud-platform"]
    credentials = Credentials.from_service_account_file(
        service_account_path,
        scopes=scopes
    )

    # IMPORTANT: gemini-3-pro-image-preview requires location="global"
    client = genai.Client(
        vertexai=True,
        project=project_id,
        location="global",
        credentials=credentials,
    )

    return client


def setup_video_client():
    """Separate client for video (needs us-central1, not global)."""
    from google import genai
    from google.oauth2.service_account import Credentials

    project_id = os.environ.get("VERTEX_AI_PROJECT_ID")
    service_account_path = os.environ.get("VERTEX_AI_SERVICE_ACCOUNT_JSON")

    scopes = ["https://www.googleapis.com/auth/cloud-platform"]
    credentials = Credentials.from_service_account_file(
        service_account_path,
        scopes=scopes
    )

    client = genai.Client(
        vertexai=True,
        project=project_id,
        location="us-central1",
        credentials=credentials,
    )

    return client


def step1_generate_base_image(client, output_dir: Path):
    """Generate base image using gemini-3-pro-image-preview."""
    from google.genai import types

    print("=" * 60)
    print("STEP 1: Generate base image (person sitting at desk)")
    print("Model: gemini-3-pro-image-preview")
    print("=" * 60)

    prompt = "A young professional person with short dark hair sitting at a modern desk with a laptop, looking at the screen, office environment with warm lighting, photorealistic high quality image"

    print(f"Prompt: {prompt}")
    print("Generating...")

    response = client.models.generate_content(
        model="gemini-3-pro-image-preview",
        contents=prompt,
        config=types.GenerateContentConfig(
            response_modalities=["IMAGE"],
            image_config=types.ImageConfig(
                aspect_ratio="16:9",
            ),
        ),
    )

    # Extract image from response
    output_path = output_dir / "step1_sitting.png"

    for part in response.candidates[0].content.parts:
        if part.inline_data and part.inline_data.mime_type.startswith("image/"):
            image_bytes = part.inline_data.data
            with open(output_path, "wb") as f:
                f.write(image_bytes)
            print(f"Saved: {output_path}")
            print(f"Size: {len(image_bytes)} bytes")
            return output_path

    print("ERROR: No image in response")
    sys.exit(1)


def step2_generate_variation(client, base_image_path: Path, output_dir: Path):
    """Generate variation using the base image as reference."""
    from google.genai import types

    print()
    print("=" * 60)
    print("STEP 2: Generate variation (same person, now standing)")
    print("Model: gemini-3-pro-image-preview with reference image")
    print("=" * 60)

    # Load base image
    with open(base_image_path, "rb") as f:
        base_image_bytes = f.read()

    print(f"Base image loaded: {len(base_image_bytes)} bytes")

    # Create content with image reference
    prompt_text = "Generate a new image of the exact same person from the reference image, but now they are standing next to the desk with arms raised in a celebratory pose. Keep the same office environment, same lighting, same person appearance. Photorealistic high quality."

    print(f"Prompt: {prompt_text}")
    print("Generating with image reference...")

    response = client.models.generate_content(
        model="gemini-3-pro-image-preview",
        contents=[
            types.Part.from_bytes(data=base_image_bytes, mime_type="image/png"),
            prompt_text,
        ],
        config=types.GenerateContentConfig(
            response_modalities=["IMAGE"],
            image_config=types.ImageConfig(
                aspect_ratio="16:9",
            ),
        ),
    )

    # Extract image from response
    output_path = output_dir / "step2_standing.png"

    for part in response.candidates[0].content.parts:
        if part.inline_data and part.inline_data.mime_type.startswith("image/"):
            image_bytes = part.inline_data.data
            with open(output_path, "wb") as f:
                f.write(image_bytes)
            print(f"Saved: {output_path}")
            print(f"Size: {len(image_bytes)} bytes")
            return output_path

    print("ERROR: No image in response")
    sys.exit(1)


def step3_generate_video(client, first_frame: Path, last_frame: Path, output_dir: Path):
    """Generate transition video with first and last frame."""
    from google.genai import types
    from google.genai.types import GenerateVideosConfig

    print()
    print("=" * 60)
    print("STEP 3: Generate transition video (dancing animation)")
    print("Model: veo-3.1-generate-001")
    print("=" * 60)

    first_image = types.Image.from_file(location=str(first_frame))
    last_image = types.Image.from_file(location=str(last_frame))

    prompt = "The person smoothly transitions from sitting to standing while doing a celebratory dance move, continuous fluid motion, same office environment"

    print(f"First frame: {first_frame}")
    print(f"Last frame: {last_frame}")
    print(f"Prompt: {prompt}")
    print("Generating video (2-3 minutes)...")

    try:
        operation = client.models.generate_videos(
            model="veo-3.1-generate-001",
            prompt=prompt,
            image=first_image,
            config=GenerateVideosConfig(
                aspect_ratio="16:9",
                duration_seconds=4,
                generate_audio=False,
                resolution="720p",
                last_frame=last_image,
            ),
        )

        poll_count = 0
        while not operation.done:
            poll_count += 1
            print(f"  Waiting... (poll #{poll_count})")
            time.sleep(15)
            operation = client.operations.get(operation)

        print()

        if operation.response:
            result = operation.result
            if result.generated_videos:
                video = result.generated_videos[0].video

                if hasattr(video, 'video_bytes') and video.video_bytes:
                    video_bytes = video.video_bytes
                    if isinstance(video_bytes, str):
                        video_bytes = base64.b64decode(video_bytes)
                    output_path = output_dir / "step3_dance_transition.mp4"
                    with open(output_path, 'wb') as f:
                        f.write(video_bytes)
                    print(f"Saved: {output_path}")
                    print(f"Size: {len(video_bytes)} bytes")
                    return output_path
                elif hasattr(video, 'uri') and video.uri:
                    print(f"Video saved to GCS: {video.uri}")
                    return video.uri

        print("ERROR: No video generated")
        return None

    except Exception as e:
        print(f"ERROR: {e}")
        import traceback
        traceback.print_exc()
        return None


def main():
    print("=" * 60)
    print("ADVANCED WORKFLOW TEST")
    print("Person sitting -> Person standing -> Dancing transition")
    print("=" * 60)
    print()

    # Setup clients
    image_client = setup_client()  # global location for gemini-3-pro-image-preview
    video_client = setup_video_client()  # us-central1 for veo

    output_dir = Path("output/advanced_test")
    output_dir.mkdir(parents=True, exist_ok=True)

    print(f"Output directory: {output_dir}")
    print()

    # Step 1: Generate base image
    base_image = step1_generate_base_image(image_client, output_dir)

    # Step 2: Generate variation with reference
    variation_image = step2_generate_variation(image_client, base_image, output_dir)

    # Step 3: Generate video
    video = step3_generate_video(video_client, base_image, variation_image, output_dir)

    print()
    print("=" * 60)
    print("WORKFLOW COMPLETE")
    print("=" * 60)
    print(f"Base image: {base_image}")
    print(f"Variation: {variation_image}")
    print(f"Video: {video}")


if __name__ == "__main__":
    main()
