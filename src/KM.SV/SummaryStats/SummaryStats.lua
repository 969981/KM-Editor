-- SPDX-License-Identifier: GPL-3.0-only
-- Modes belong to the Summary panel, never to the stored Pokemon or retail enum.
local modes = setmetatable({}, { __mode = "k" })
local panel = CEF98AE7D385E7985.prototype
local controller = C3071A56DB3D315C8.prototype
local footer = CF1D9D619D324F233
local originalRefresh = panel.F837233999D634B7A
local originalDisplay = panel.FB23C5EA926246E08
local originalSwitch = controller.FB23C5EA926246E08
local originalFooter = controller.FE625730A0180E2D1
local originalRegister = footer.S64E3D663DD2D5843
local footerPanel
local captions = {
    [0] = "km_ui_summary_stats",
    "km_ui_summary_raw_ivs",
    "km_ui_summary_evs",
    "km_ui_summary_ev_graph"
}

local function clearValues(self)
    if not self[4] or not self[14] then return end
    for stat = 0, 5 do self[4]:f7798D9F4(self[14][stat], false) end
    self[4]:f7798D9F4("L_graph_chart_00/T_hp_param_01", false)
    self[4]:f7798D9F4("L_graph_chart_00/T_hp_slash_00", false)
    self[4]:f7798D9F4("L_graph_chart_00/N_graph_radarchart_00", false)
    for stat = 0, 5 do
        self[4]:f7798D9F4("L_graph_chart_00/L_icon_kira_0" .. stat, false)
    end
end

panel.F837233999D634B7A = function(self, pokemon, special)
    if pokemon == nil then
        modes[self] = nil
        self[6] = nil
        clearValues(self)
        return
    end
    return originalRefresh(self, pokemon, special)
end

panel.FB91212B98B6ABE3A = function(self)
    if not self[6] then clearValues(self); return end
    local mode = ((modes[self] or 0) + 1) % 4
    modes[self] = mode
    self[13] = mode == 3 and E4ED4DE496EDA8069.EffortPower or E4ED4DE496EDA8069.NormalPower
    -- Stock refresh restores ordinary stat values after either numeric mode.
    if mode == 0 then originalRefresh(self, self[6], self[23]) end
end

panel.FB23C5EA926246E08 = function(self)
    if not self[6] or not self[4] or not self[14] then clearValues(self); return end
    local mode = modes[self] or 0
    local numbers
    if mode == 1 or mode == 2 then
        local valid, values = pcall(function()
            local result = {}
            for stat = 0, 5 do
                local value
                if mode == 1 then value = self[6]:f80FCDC00(stat)
                else value = self[6]:f70AFFE74(stat) end
                local maximum = mode == 1 and 31 or 252
                if type(value) ~= "number" or value % 1 ~= 0 or value < 0 or value > maximum then
                    error("Invalid Summary value")
                end
                result[stat] = value
            end
            return result
        end)
        if not valid then clearValues(self); return end
        numbers = values
    end
    self[13] = mode == 3 and E4ED4DE496EDA8069.EffortPower or E4ED4DE496EDA8069.NormalPower
    self[4]:f7798D9F4("L_graph_chart_00/N_graph_radarchart_00", true)
    originalDisplay(self)
    if numbers then
        -- Neither the ordinary stat graph nor its HP fraction describes raw values.
        self[4]:f7798D9F4("L_graph_chart_00/N_graph_radarchart_00", false)
        self[4]:f7798D9F4("L_graph_chart_00/T_hp_param_01", false)
        self[4]:f7798D9F4("L_graph_chart_00/T_hp_slash_00", false)
        for stat = 0, 5 do
            c8C3BF576.f316077B2(self[1], self[14][stat], tostring(numbers[stat]))
            self[4]:f7798D9F4(self[14][stat], true)
        end
    end
end

-- Retain the stock glyph, input action, ordering, and other footer entries.
footer.S64E3D663DD2D5843 = function(id, glyph, text)
    if footerPanel and id == "radar" then
        text = c8C3BF576.fC8CEF9EF("status", captions[modes[footerPanel] or 0])
    end
    return originalRegister(id, glyph, text)
end

controller.FE625730A0180E2D1 = function(self)
    local previous = footerPanel
    footerPanel = self[3]
    local result = table.pack(pcall(originalFooter, self))
    footerPanel = previous
    if not result[1] then error(result[2]) end
    return table.unpack(result, 2, result.n)
end

controller.FB23C5EA926246E08 = function(self)
    originalSwitch(self)
    self:FE625730A0180E2D1()
end
