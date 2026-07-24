from config import ACTION_TIMEOUT, ARTIFACT_DIR


class BasePage:
    def __init__(self, window):
        self.window = window

    def by_id(self, automation_id, **kwargs):
        return self.window.child_window(auto_id=automation_id, **kwargs)

    def wait_visible(self, control, timeout=ACTION_TIMEOUT):
        control.wait("visible", timeout=timeout)
        return control

    def click(self, control):
        self.wait_visible(control)
        control.click_input()

    def text(self, control):
        wrapped = control.wrapper_object()
        for accessor in ("window_text", "get_value"):
            try:
                value = getattr(wrapped, accessor)()
                if value:
                    return value
            except Exception:
                continue
        return ""

    def screenshot(self, name):
        ARTIFACT_DIR.mkdir(parents=True, exist_ok=True)
        path = ARTIFACT_DIR / f"{name}.png"
        self.window.capture_as_image().save(path)
        return path

