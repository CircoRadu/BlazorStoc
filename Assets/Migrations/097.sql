-- version: 97
-- description: Dashboard personalizat: zone una langa alta, fiecare cu titlu si caroiaj propriu
-- note: user_dashboard_zones keeps, per user, the zones of the main page (zone_index = the order from left to right, a required title, the number of tiles per row of the zone). user_dashboard_entries and user_dashboard_rows get zone_index (0 for everything saved before: the first zone, which the application names Dashboard when the user has no zone row); grid_row and row_index then count the rows inside the zone. The primary key of user_dashboard_rows includes the zone. The tables are the user's own and keep their data.
-- column: user_dashboard_zones.user_id
-- column: user_dashboard_zones.zone_index
-- column: user_dashboard_zones.title
-- column: user_dashboard_zones.columns_count
-- column: user_dashboard_entries.zone_index
-- column: user_dashboard_rows.zone_index
-- statement
CREATE TABLE IF NOT EXISTS `user_dashboard_zones` (
  `user_id` BIGINT NOT NULL,
  `zone_index` INT NOT NULL,
  `title` VARCHAR(60) NOT NULL,
  `columns_count` INT NOT NULL DEFAULT 4,
  `updated_utc` VARCHAR(40) NOT NULL DEFAULT '',
  PRIMARY KEY (`user_id`, `zone_index`),
  CONSTRAINT `fk_user_dashboard_zones_user` FOREIGN KEY (`user_id`) REFERENCES `web_users` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
ALTER TABLE `user_dashboard_entries` ADD COLUMN IF NOT EXISTS `zone_index` INT NOT NULL DEFAULT 0
-- statement
ALTER TABLE `user_dashboard_rows` ADD COLUMN IF NOT EXISTS `zone_index` INT NOT NULL DEFAULT 0
-- statement
ALTER TABLE `user_dashboard_rows` DROP PRIMARY KEY, ADD PRIMARY KEY (`user_id`, `zone_index`, `row_index`)
