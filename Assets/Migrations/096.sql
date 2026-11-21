-- version: 96
-- description: Dashboard personalizat: etichete si spatii intre randurile de tile-uri
-- note: user_dashboard_rows holds, per user, the rows of the main page that carry no tiles: a label (text and an optional colour, #rrggbb, empty = the colour of the theme) or a space of fixed height. row_index is the place of the row among all the rows of the page (the tiles of user_dashboard_entries use the same index in grid_row). The table is new: it starts empty and is removed together with the user.
-- column: user_dashboard_rows.user_id
-- column: user_dashboard_rows.row_index
-- column: user_dashboard_rows.kind
-- column: user_dashboard_rows.text
-- column: user_dashboard_rows.color
-- statement
CREATE TABLE IF NOT EXISTS `user_dashboard_rows` (
  `user_id` BIGINT NOT NULL,
  `row_index` INT NOT NULL,
  `kind` VARCHAR(10) NOT NULL,
  `text` VARCHAR(80) NOT NULL DEFAULT '',
  `color` VARCHAR(7) NOT NULL DEFAULT '',
  `updated_utc` VARCHAR(40) NOT NULL DEFAULT '',
  PRIMARY KEY (`user_id`, `row_index`),
  CONSTRAINT `fk_user_dashboard_rows_user` FOREIGN KEY (`user_id`) REFERENCES `web_users` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
