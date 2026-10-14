-- Run after deploying the updated API and stopping all older API instances.
-- Back up the database first: MySQL DDL cannot be rolled back with a transaction.
DROP PROCEDURE IF EXISTS DropLegacyColumn;
DELIMITER $$
CREATE PROCEDURE DropLegacyColumn(IN tbl varchar(64), IN col varchar(64))
BEGIN
  IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name=tbl AND column_name=col) THEN
    SET @ddl=CONCAT('ALTER TABLE `',tbl,'` DROP COLUMN `',col,'`');
    PREPARE stmt FROM @ddl;
    EXECUTE stmt;
    DEALLOCATE PREPARE stmt;
  END IF;
END$$
DELIMITER ;
CALL DropLegacyColumn('MealPlanMeals','PlannedCalories');
CALL DropLegacyColumn('MealPlans','BMR');
CALL DropLegacyColumn('MealPlans','TDEE');
CALL DropLegacyColumn('MealPlans','HealthGoal');
CALL DropLegacyColumn('MealPlans','TargetCaloriesPerDay');
CALL DropLegacyColumn('VideoRecipeDrafts','Servings');
CALL DropLegacyColumn('VideoRecipeDrafts','CaloriesPerServing');
DROP PROCEDURE DropLegacyColumn;
DROP TABLE IF EXISTS PostTags;
DROP TABLE IF EXISTS Tags;
