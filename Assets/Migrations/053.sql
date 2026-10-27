-- version: 53
-- description: Produse: etichete pe subcategorie (On/Off pe produs, neobligatorii, folosite la filtrarea catalogului)
-- note: a tag is defined for a subcategory (for example "PoE" for the IP cameras) and each product of the subcategory has it On or Off. Tags are never required, are not part of the product code and never block the stock. Deleting a tag or a product removes the links; deleting an empty subcategory removes its tags.
-- column: subcategory_tags.id
-- column: subcategory_tags.subcategory_id
-- column: subcategory_tags.name
-- column: subcategory_tags.normalized_name
-- column: subcategory_tags.position
-- column: subcategory_tags.version
-- column: product_tags.product_id
-- column: product_tags.tag_id
-- statement
CREATE TABLE IF NOT EXISTS `subcategory_tags` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `subcategory_id` BIGINT NOT NULL,
  `name` VARCHAR(100) NOT NULL,
  `normalized_name` VARCHAR(512) NOT NULL,
  `position` INT NOT NULL DEFAULT 0,
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_subcategory_tags_name` (`subcategory_id`, `normalized_name`),
  CONSTRAINT `fk_subcategory_tags_subcategory` FOREIGN KEY (`subcategory_id`) REFERENCES `subcategories` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `product_tags` (
  `product_id` BIGINT NOT NULL,
  `tag_id` BIGINT NOT NULL,
  PRIMARY KEY (`product_id`, `tag_id`),
  KEY `ix_product_tags_tag` (`tag_id`),
  CONSTRAINT `fk_product_tags_product` FOREIGN KEY (`product_id`) REFERENCES `products` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `fk_product_tags_tag` FOREIGN KEY (`tag_id`) REFERENCES `subcategory_tags` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
