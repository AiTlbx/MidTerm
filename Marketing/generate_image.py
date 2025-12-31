#!/usr/bin/env python3
"""
Hello World - Imagen Image Generation via Vertex AI
Uses Google's Imagen model to generate images from text prompts.

Environment variables required:
  VERTEX_AI_PROJECT_ID - Google Cloud project ID
  VERTEX_AI_SERVICE_ACCOUNT_JSON - Path to service account JSON file

Usage:
  python generate_image.py "A terminal window showing code" output.png
  python generate_image.py  # Uses default prompt
"""

import os
import sys
from pathlib import Path

def main():
    from google import genai
    from google.genai.types import GenerateImagesConfig
    from google.oauth2.service_account import Credentials

    # Get configuration from environment
    project_id = os.environ.get("VERTEX_AI_PROJECT_ID")
    service_account_path = os.environ.get("VERTEX_AI_SERVICE_ACCOUNT_JSON")

    if not project_id:
        print("ERROR: VERTEX_AI_PROJECT_ID environment variable not set")
        sys.exit(1)
    if not service_account_path:
        print("ERROR: VERTEX_AI_SERVICE_ACCOUNT_JSON environment variable not set")
        sys.exit(1)
    if not Path(service_account_path).exists():
        print(f"ERROR: Service account file not found: {service_account_path}")
        sys.exit(1)

    # Parse arguments
    prompt = sys.argv[1] if len(sys.argv) > 1 else "A modern terminal application with glowing text on a dark background, digital art style"
    output_file = sys.argv[2] if len(sys.argv) > 2 else "output_image.png"

    print(f"Project ID: {project_id}")
    print(f"Service Account: {service_account_path}")
    print(f"Prompt: {prompt}")
    print(f"Output: {output_file}")
    print()

    # Create credentials from service account file
    scopes = ["https://www.googleapis.com/auth/cloud-platform"]
    credentials = Credentials.from_service_account_file(
        service_account_path,
        scopes=scopes
    )

    # Initialize the client with Vertex AI
    client = genai.Client(
        vertexai=True,
        project=project_id,
        location="us-central1",
        credentials=credentials,
    )

    print("Generating image...")

    # Generate image using Imagen
    # Models: imagen-4.0-generate-001 (GA), imagen-3.0-generate-002
    # Size options: "1K" (default, smallest), "2K"
    # Aspect ratios: "1:1" (default), "3:4", "4:3", "16:9", "9:16"
    response = client.models.generate_images(
        model="imagen-3.0-generate-002",
        prompt=prompt,
        config=GenerateImagesConfig(
            number_of_images=1,
            aspect_ratio="1:1",  # smallest
        ),
    )

    # Save the generated image
    if response.generated_images:
        image_data = response.generated_images[0].image
        image_data.save(output_file)
        print(f"Image saved to: {output_file}")
        print(f"Image size: {len(image_data.image_bytes)} bytes")
    else:
        print("ERROR: No images generated")
        sys.exit(1)


if __name__ == "__main__":
    main()
