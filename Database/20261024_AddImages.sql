-- Additive MySQL schema update for post images, profile avatars and personal meal images.
-- Select the intended database first. Back it up; MySQL DDL is not transactional.
-- Startup applies the same additions when Database__RunStartupTasks=true.
-- This is not an initializer for an empty database. No data is deleted.
DROP PROCEDURE IF EXISTS AddManagedImageColumn_20261024;
DELIMITER $$
CREATE PROCEDURE AddManagedImageColumn_20261024(IN tbl varchar(64), IN col varchar(64), IN definition varchar(100))
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=tbl) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Base table missing. Select the existing application database.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=tbl AND COLUMN_NAME=col) THEN
        SET @image_ddl=CONCAT('ALTER TABLE `',tbl,'` ADD COLUMN `',col,'` ',definition);
        PREPARE image_stmt FROM @image_ddl;
        EXECUTE image_stmt;
        DEALLOCATE PREPARE image_stmt;
    END IF;
END$$
DELIMITER ;
CALL AddManagedImageColumn_20261024('UserProfiles','AvatarUrl','varchar(1000) NULL');
CALL AddManagedImageColumn_20261024('UserProfiles','AvatarPublicId','varchar(255) NULL');
CALL AddManagedImageColumn_20261024('Posts','ImageUrl','varchar(1000) NULL');
CALL AddManagedImageColumn_20261024('Posts','ImagePublicId','varchar(255) NULL');
CALL AddManagedImageColumn_20261024('MealPlanMeals','ImageUrl','varchar(1000) NULL');
CALL AddManagedImageColumn_20261024('MealPlanMeals','ImagePublicId','varchar(255) NULL');
CALL AddManagedImageColumn_20261024('MealPlanMeals','ImageIsGenerated','tinyint(1) NOT NULL DEFAULT 0');
DROP PROCEDURE AddManagedImageColumn_20261024;
