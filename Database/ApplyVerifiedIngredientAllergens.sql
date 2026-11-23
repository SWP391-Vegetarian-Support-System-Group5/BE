-- Targeted repair for 202611230001_AddVerifiedIngredientAllergens (MySQL 8).
-- Stop BE; back up and select the intended LOCAL/TEST database first.
-- Never run with mysql --force: execution must stop on the first SQL error.
-- This file is NOT invoked by API startup.
SELECT DATABASE() AS target_database, @@hostname AS database_host;

SET @vm_sql = IF(
  EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema=DATABASE()
    AND table_name='Ingredients' AND column_name='IsAllergenProfileVerified'),
  'SELECT 1',
  'ALTER TABLE `Ingredients` ADD COLUMN `IsAllergenProfileVerified` tinyint(1) NOT NULL DEFAULT 0');
PREPARE vm_statement FROM @vm_sql;
EXECUTE vm_statement;
DEALLOCATE PREPARE vm_statement;

SET @vm_sql = IF(
  EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema=DATABASE()
    AND table_name='Ingredients' AND column_name='AllergenEvidence'),
  'SELECT 1',
  'ALTER TABLE `Ingredients` ADD COLUMN `AllergenEvidence` varchar(1000) NULL');
PREPARE vm_statement FROM @vm_sql;
EXECUTE vm_statement;
DEALLOCATE PREPARE vm_statement;

CREATE TABLE IF NOT EXISTS `IngredientAllergens` (
  `IngredientId` int NOT NULL,
  `AllergenId` int NOT NULL,
  CONSTRAINT `PK_IngredientAllergens` PRIMARY KEY (`IngredientId`, `AllergenId`),
  KEY `IX_IngredientAllergens_AllergenId` (`AllergenId`),
  CONSTRAINT `FK_IngredientAllergens_Ingredients`
    FOREIGN KEY (`IngredientId`) REFERENCES `Ingredients` (`IngredientId`) ON DELETE CASCADE,
  CONSTRAINT `FK_IngredientAllergens_Allergens`
    FOREIGN KEY (`AllergenId`) REFERENCES `Allergens` (`AllergenId`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Preserve old single-source links AND any reviewed multi-source links/evidence.
-- Do not use INSERT IGNORE: invalid foreign keys must surface as an error.
INSERT INTO `IngredientAllergens` (`IngredientId`, `AllergenId`)
SELECT i.`IngredientId`, i.`AllergenId`
FROM `Ingredients` i
LEFT JOIN `IngredientAllergens` a
  ON a.`IngredientId`=i.`IngredientId` AND a.`AllergenId`=i.`AllergenId`
WHERE i.`AllergenId` IS NOT NULL AND a.`IngredientId` IS NULL;

-- Record ONLY this completed migration, so a subsequent EF update cannot
-- attempt to add the same columns again. Do not baseline other migrations.
CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
  `MigrationId` varchar(150) NOT NULL,
  `ProductVersion` varchar(32) NOT NULL,
  CONSTRAINT `PK___EFMigrationsHistory` PRIMARY KEY (`MigrationId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
SELECT '202611230001_AddVerifiedIngredientAllergens', '8.0.13'
WHERE NOT EXISTS (
  SELECT 1 FROM `__EFMigrationsHistory`
  WHERE `MigrationId`='202611230001_AddVerifiedIngredientAllergens'
);

SELECT `IngredientId`, `AllergenId`, `IsAllergenProfileVerified`, `AllergenEvidence`
FROM `Ingredients` ORDER BY `IngredientId`;
SELECT `IngredientId`, `AllergenId` FROM `IngredientAllergens`
ORDER BY `IngredientId`, `AllergenId`;
