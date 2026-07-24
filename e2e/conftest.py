import os
import subprocess
from pathlib import Path

import pytest
from pywinauto import Desktop

from config import APP_PATH, APP_TITLE, ARTIFACT_DIR, LAUNCH_TIMEOUT


@pytest.fixture(scope="function")
def app_window(request, tmp_path):
    if not APP_PATH:
        pytest.fail("APP_PATH must point to the built QuotaDock executable")

    executable = Path(APP_PATH)
    if not executable.is_file():
        pytest.fail(f"APP_PATH does not exist: {executable}")

    sandbox_env = os.environ.copy()
    sandbox_env["APPDATA"] = str(tmp_path / "AppData" / "Roaming")
    sandbox_env["LOCALAPPDATA"] = str(tmp_path / "AppData" / "Local")
    sandbox_env["TEMP"] = sandbox_env["TMP"] = str(tmp_path / "Temp")
    for directory in (
        sandbox_env["APPDATA"],
        sandbox_env["LOCALAPPDATA"],
        sandbox_env["TEMP"],
    ):
        Path(directory).mkdir(parents=True, exist_ok=True)

    process = subprocess.Popen([str(executable), "--e2e"], env=sandbox_env)
    win32_window = Desktop(backend="win32").window(
        process=process.pid, title=APP_TITLE
    )
    win32_window.wait("exists visible enabled ready", timeout=LAUNCH_TIMEOUT)
    window = Desktop(backend="uia").window(handle=win32_window.handle)
    yield window

    if getattr(getattr(request.node, "rep_call", None), "failed", False):
        ARTIFACT_DIR.mkdir(parents=True, exist_ok=True)
        try:
            window.capture_as_image().save(
                ARTIFACT_DIR / f"FAIL_{request.node.name}.png"
            )
        except Exception:
            pass

    if process.poll() is None:
        process.terminate()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()


@pytest.hookimpl(tryfirst=True, hookwrapper=True)
def pytest_runtest_makereport(item, call):
    outcome = yield
    setattr(item, f"rep_{outcome.get_result().when}", outcome.get_result())
