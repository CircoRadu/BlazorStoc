-- version: 99
-- description: Anunturi: cine a publicat anuntul
-- note: announcements.published_by keeps the user who published (or republished) the announcement; the window of the users shows "Publicat de: <user> <date and hour>" from it and from published_utc. Announcements published before have it empty and the application falls back to the user who last changed them.
-- column: announcements.published_by
-- statement
ALTER TABLE `announcements` ADD COLUMN IF NOT EXISTS `published_by` VARCHAR(120) NOT NULL DEFAULT ''
