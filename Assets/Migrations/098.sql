-- version: 98
-- description: Modulul Anunturi: anunturi cu text formatat si poze, audienta, stare vazut/citit si preferinta utilizatorului
-- note: announcements keeps the announcements (state draft/activ/arhivat, mandatory = shown at every sign-in until "Am citit", audience all or selected, valid_until as yyyy-MM-dd or empty, publication_no grows at every republishing). announcement_users lists the selected users (by user name). announcement_seen is the state seen/read of a user for an announcement (deleted at a republishing). announcement_images keeps the pictures pasted or added in the editor (announcement_id is empty until the announcement is saved). user_announcement_settings keeps the preference "Nu mai afisa anunturile la pornire" of each user. The body is cleaned on the server before it is saved.
-- column: announcements.id
-- column: announcements.title
-- column: announcements.body_html
-- column: announcements.mandatory
-- column: announcements.audience
-- column: announcements.valid_until
-- column: announcements.state
-- column: announcements.publication_no
-- column: announcements.created_by
-- column: announcements.created_utc
-- column: announcements.updated_by
-- column: announcements.updated_utc
-- column: announcements.published_utc
-- column: announcement_users.announcement_id
-- column: announcement_users.user_name
-- column: announcement_seen.announcement_id
-- column: announcement_seen.user_name
-- column: announcement_seen.seen_utc
-- column: announcement_seen.acknowledged
-- column: announcement_images.id
-- column: announcement_images.announcement_id
-- column: announcement_images.file_name
-- column: announcement_images.content_type
-- column: announcement_images.size_bytes
-- column: announcement_images.content
-- column: announcement_images.created_by
-- column: announcement_images.created_utc
-- column: user_announcement_settings.user_name
-- column: user_announcement_settings.hide_normal
-- statement
CREATE TABLE IF NOT EXISTS `announcements` (
  `id` INT NOT NULL AUTO_INCREMENT,
  `title` VARCHAR(200) NOT NULL,
  `body_html` MEDIUMTEXT NOT NULL,
  `mandatory` TINYINT(1) NOT NULL DEFAULT 0,
  `audience` VARCHAR(12) NOT NULL DEFAULT 'all',
  `valid_until` VARCHAR(10) NOT NULL DEFAULT '',
  `state` VARCHAR(12) NOT NULL DEFAULT 'draft',
  `publication_no` INT NOT NULL DEFAULT 0,
  `created_by` VARCHAR(120) NOT NULL DEFAULT '',
  `created_utc` VARCHAR(40) NOT NULL DEFAULT '',
  `updated_by` VARCHAR(120) NOT NULL DEFAULT '',
  `updated_utc` VARCHAR(40) NOT NULL DEFAULT '',
  `published_utc` VARCHAR(40) NOT NULL DEFAULT '',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `announcement_users` (
  `announcement_id` INT NOT NULL,
  `user_name` VARCHAR(120) NOT NULL,
  PRIMARY KEY (`announcement_id`, `user_name`),
  CONSTRAINT `fk_announcement_users_announcement` FOREIGN KEY (`announcement_id`) REFERENCES `announcements` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `announcement_seen` (
  `announcement_id` INT NOT NULL,
  `user_name` VARCHAR(120) NOT NULL,
  `seen_utc` VARCHAR(40) NOT NULL DEFAULT '',
  `acknowledged` TINYINT(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`announcement_id`, `user_name`),
  CONSTRAINT `fk_announcement_seen_announcement` FOREIGN KEY (`announcement_id`) REFERENCES `announcements` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `announcement_images` (
  `id` INT NOT NULL AUTO_INCREMENT,
  `announcement_id` INT NULL,
  `file_name` VARCHAR(120) NOT NULL DEFAULT '',
  `content_type` VARCHAR(40) NOT NULL DEFAULT '',
  `size_bytes` INT NOT NULL DEFAULT 0,
  `content` MEDIUMBLOB NOT NULL,
  `created_by` VARCHAR(120) NOT NULL DEFAULT '',
  `created_utc` VARCHAR(40) NOT NULL DEFAULT '',
  PRIMARY KEY (`id`),
  KEY `ix_announcement_images_announcement` (`announcement_id`),
  CONSTRAINT `fk_announcement_images_announcement` FOREIGN KEY (`announcement_id`) REFERENCES `announcements` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `user_announcement_settings` (
  `user_name` VARCHAR(120) NOT NULL,
  `hide_normal` TINYINT(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`user_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
