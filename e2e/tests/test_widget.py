import pytest
from pywinauto import Desktop

from pages.widget_page import WidgetPage


@pytest.mark.smoke
def test_widget_exposes_native_usage_controls(app_window):
    page = WidgetPage(app_window)

    page.wait_visible(page.root)
    page.wait_visible(page.refresh_button)
    page.wait_visible(page.pin_button)
    page.wait_visible(page.settings_button)
    page.wait_visible(page.metric_list)
    assert len(page.metric_list.children()) > 0


@pytest.mark.smoke
def test_manual_refresh_updates_visible_status(app_window):
    page = WidgetPage(app_window)

    page.click(page.refresh_button)
    page.wait_visible(page.status_label)
    assert page.text(page.status_label) in {
        "Refreshing…",
        "Up to date",
        "Some providers need attention",
        "1 account up to date",
    }


@pytest.mark.smoke
def test_details_exposes_all_provider_connection_actions(app_window):
    page = WidgetPage(app_window)
    page.click(page.details_button)

    details_win32 = Desktop(backend="win32").window(
        process=app_window.process_id(), title="QuotaDock — Usage details"
    )
    details_win32.wait("exists visible enabled ready", timeout=10)
    details = Desktop(backend="uia").window(handle=details_win32.handle)
    assert details.child_window(auto_id="DetailsRoot").exists(timeout=10)

    # Codex is the primary, always-visible action.
    assert details.child_window(auto_id="ConnectCodexButton").exists(timeout=10)
    assert details.child_window(auto_id="StartupToggle").exists(timeout=10)

    # The remaining providers live under a collapsed "Optional providers" group.
    optional_toggle = details.child_window(auto_id="OptionalProvidersToggle")
    assert optional_toggle.exists(timeout=10)
    # Use the UIA toggle pattern rather than a physical click: the control can be
    # scrolled out of view in the flyout, which makes coordinate clicks flaky.
    optional_toggle.wrapper_object().toggle()

    for automation_id in (
        "ConnectCompatibleButton",
        "ConnectOpenAiButton",
        "ConnectClaudeButton",
        "ImportClaudeButton",
        "ConnectAnthropicButton",
        "ConnectAlibabaButton",
    ):
        assert details.child_window(auto_id=automation_id).exists(timeout=10)
