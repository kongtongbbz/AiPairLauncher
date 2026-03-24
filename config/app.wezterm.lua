local wezterm = require("wezterm")
local act = wezterm.action

local config = wezterm.config_builder()

config.default_prog = { "powershell.exe", "-NoLogo" }
config.window_close_confirmation = "NeverPrompt"
config.use_fancy_tab_bar = false
config.tab_bar_at_bottom = true
config.initial_cols = 220
config.initial_rows = 48
config.disable_default_key_bindings = false

config.keys = {
  {
    key = "Enter",
    mods = "ALT|SHIFT",
    action = act.DisableDefaultAssignment,
  },
}

return config
