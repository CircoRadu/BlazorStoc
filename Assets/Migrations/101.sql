-- version: 101
-- description: Dashboard personalizat: latimea fiecarei zone
-- note: user_dashboard_zones.width_px is the width the user chose for one zone, in pixels (0 = the zone follows the global width of the zones, which is saved with the display settings of the dashboard, key zw; no global width = automatic). The table is the user's own and keeps its data.
-- column: user_dashboard_zones.width_px
-- statement
ALTER TABLE `user_dashboard_zones` ADD COLUMN IF NOT EXISTS `width_px` INT NOT NULL DEFAULT 0
