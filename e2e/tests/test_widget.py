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
    page.wait_visible(page.tab_strip)
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
def test_details_exposes_connection_surface(app_window):
    page = WidgetPage(app_window)
    page.click(page.details_button)

    details_win32 = Desktop(backend="win32").window(
        process=app_window.process_id(), title="QuotaDock — Usage details"
    )
    details_win32.wait("exists visible enabled ready", timeout=10)
    details = Desktop(backend="uia").window(handle=details_win32.handle)
    assert details.child_window(auto_id="DetailsRoot").exists(timeout=10)

    # The details window hosts auto-detect, a refresh-all action, and the
    # per-provider tab surface used to connect Codex, Claude, Grok, and Kimi.
    assert details.child_window(auto_id="AutoDetectButton").exists(timeout=10)
    assert details.child_window(auto_id="DetailsRefreshButton").exists(timeout=10)
    assert details.child_window(auto_id="ProviderTabs").exists(timeout=10)
    assert details.child_window(auto_id="StartupToggle").exists(timeout=10)
