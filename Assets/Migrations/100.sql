-- version: 100
-- description: Dashboard personalizat: zonele pot sta si pe rand nou, aliniate centrat, la stanga sau la dreapta
-- note: user_dashboard_zones.placement says where a zone stands: beside (next to the zone before it, as before the column existed), center, left or right (on a new row of the page, aligned that way, as wide as its tiles need). The first zone is always beside. The table is the user's own and keeps its data.
-- column: user_dashboard_zones.placement
-- statement
ALTER TABLE `user_dashboard_zones` ADD COLUMN IF NOT EXISTS `placement` VARCHAR(8) NOT NULL DEFAULT 'beside'
