-- version: 54
-- description: Produse: arbore de subcategorii (o subcategorie poate avea subcategorii copil, pe oricate niveluri)
-- note: parent_id is NULL for the subcategories directly under a category (tier 2) and points to the parent subcategory for the deeper ones. A child inherits the parameters, parameter families and tags of every ancestor (computed when read, never copied) and can add its own. Categories (tier 1) have no parameters and no tags. Products are attached to leaves only. Names stay unique across the whole tree.
-- column: subcategories.parent_id
-- statement
ALTER TABLE `subcategories` ADD COLUMN IF NOT EXISTS `parent_id` BIGINT NULL
-- statement
ALTER TABLE `subcategories` ADD INDEX IF NOT EXISTS `ix_subcategories_parent` (`parent_id`)
-- statement
ALTER TABLE `subcategories` ADD CONSTRAINT `fk_subcategories_parent` FOREIGN KEY IF NOT EXISTS (`parent_id`) REFERENCES `subcategories` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
