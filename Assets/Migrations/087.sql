-- version: 87
-- description: Raportare erori: versiunea aplicatiei in care a fost rezolvat un raport (pentru changelog)
-- note: resolved_version is filled with the version of the application when the state of a report becomes Rezolvat (empty for the others and for the reports resolved before this migration); tools\changelog-draft.ps1 groups the resolved reports of a version under Bugs.
-- column: bug_reports.resolved_version
-- statement
ALTER TABLE `bug_reports` ADD COLUMN IF NOT EXISTS `resolved_version` VARCHAR(40) NOT NULL DEFAULT ''
