-- version: 89
-- description: Invatare OCR la preluarea inventarului: sesiuni de preluare si esantioane (cifre citite gresit si corectate de utilizator)
-- note: ocr_learning_sessions = one confirmed inventory take-over (who, when, which file, how many lines were sent and how many of them the user had corrected); ocr_learning_samples = one corrected value of that take-over:
-- note: kind 'digit' = same number of digits before and after, one sample per digit that differs (read_digit -> real_digit at digit_index); kind 'segment' = a different number of digits or nothing read (no digit labels);
-- note: the pictures are base64 PNG text (the cell of the scanned form and the 28x28 digit), so backup and restore treat them like any other text; status new / learned / ignored is the decision of the user in the learning list
-- column: ocr_learning_sessions.id
-- column: ocr_learning_sessions.created_utc
-- column: ocr_learning_sessions.created_by
-- column: ocr_learning_sessions.file_name
-- column: ocr_learning_sessions.lines_sent
-- column: ocr_learning_sessions.lines_corrected
-- column: ocr_learning_samples.id
-- column: ocr_learning_samples.session_id
-- column: ocr_learning_samples.kind
-- column: ocr_learning_samples.status
-- column: ocr_learning_samples.cell_png
-- column: ocr_learning_samples.digit_png
-- statement
CREATE TABLE IF NOT EXISTS `ocr_learning_sessions` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `created_utc` DATETIME NOT NULL,
  `created_by` VARCHAR(100) NOT NULL DEFAULT '',
  `file_name` VARCHAR(260) NOT NULL DEFAULT '',
  `lines_sent` INT NOT NULL DEFAULT 0,
  `lines_corrected` INT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  KEY `ix_ocr_learning_sessions_created` (`created_utc`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
-- statement
CREATE TABLE IF NOT EXISTS `ocr_learning_samples` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `session_id` BIGINT NOT NULL,
  `page` INT NOT NULL DEFAULT 0,
  `raw_code` VARCHAR(300) NOT NULL DEFAULT '',
  `original_value` INT NULL,
  `final_value` INT NOT NULL,
  `kind` VARCHAR(10) NOT NULL,
  `digit_index` INT NULL,
  `read_digit` TINYINT NULL,
  `real_digit` TINYINT NULL,
  `top3` VARCHAR(60) NOT NULL DEFAULT '',
  `confidence` DECIMAL(5,4) NULL,
  `box_x` INT NOT NULL DEFAULT 0,
  `box_y` INT NOT NULL DEFAULT 0,
  `box_w` INT NOT NULL DEFAULT 0,
  `box_h` INT NOT NULL DEFAULT 0,
  `cell_w` INT NOT NULL DEFAULT 0,
  `cell_h` INT NOT NULL DEFAULT 0,
  `cell_png` MEDIUMTEXT NULL,
  `digit_png` TEXT NULL,
  `status` VARCHAR(10) NOT NULL DEFAULT 'new',
  `decided_by` VARCHAR(100) NOT NULL DEFAULT '',
  `decided_utc` DATETIME NULL,
  PRIMARY KEY (`id`),
  KEY `ix_ocr_learning_samples_session` (`session_id`),
  KEY `ix_ocr_learning_samples_status` (`status`, `kind`, `read_digit`, `real_digit`),
  CONSTRAINT `fk_ocr_learning_samples_session` FOREIGN KEY (`session_id`) REFERENCES `ocr_learning_sessions` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
