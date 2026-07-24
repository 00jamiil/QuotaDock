from pages.base_page import BasePage


class WidgetPage(BasePage):
    @property
    def root(self):
        return self.by_id("WidgetRoot")

    @property
    def refresh_button(self):
        return self.by_id("RefreshButton")

    @property
    def pin_button(self):
        return self.by_id("PinButton")

    @property
    def settings_button(self):
        return self.by_id("SettingsButton")

    @property
    def tab_strip(self):
        return self.by_id("TabStrip")

    @property
    def details_button(self):
        return self.by_id("DetailsButton")

    @property
    def metric_list(self):
        return self.by_id("MetricList")

    @property
    def status_label(self):
        return self.by_id("StatusLabel")
