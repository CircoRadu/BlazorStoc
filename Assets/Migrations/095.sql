-- version: 95
-- description: Dashboard personalizat: pozitia fiecarui tile in caroiaj (rand si coloana)
-- note: user_dashboard_entries keeps, besides the order, the place of every tile in the grid of the main page (grid_row and grid_col, both from 0), chosen by the user by dragging the tiles. Rows made before this migration keep both columns at 0; the application then places those tiles from their order, four per row (a migration account only changes the structure).
-- column: user_dashboard_entries.grid_row
-- column: user_dashboard_entries.grid_col
-- statement
ALTER TABLE `user_dashboard_entries` ADD COLUMN IF NOT EXISTS `grid_row` INT NOT NULL DEFAULT 0
-- statement
ALTER TABLE `user_dashboard_entries` ADD COLUMN IF NOT EXISTS `grid_col` INT NOT NULL DEFAULT 0
