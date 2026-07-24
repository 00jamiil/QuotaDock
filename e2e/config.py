import os
from pathlib import Path

APP_PATH = os.environ.get("APP_PATH", "")
APP_TITLE = os.environ.get("APP_TITLE", "QuotaDock")
LAUNCH_TIMEOUT = int(os.environ.get("LAUNCH_TIMEOUT", "20"))
ACTION_TIMEOUT = int(os.environ.get("ACTION_TIMEOUT", "10"))
ARTIFACT_DIR = Path(__file__).parent / "artifacts"

