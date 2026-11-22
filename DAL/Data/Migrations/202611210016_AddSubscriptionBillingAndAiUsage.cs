using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Data.Migrations;

[DbContext(typeof(VegetarianDbContext))]
[Migration("202611210016_AddSubscriptionBillingAndAiUsage")]
public partial class AddSubscriptionBillingAndAiUsage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS `SubscriptionPlans` (
              `SubscriptionPlanId` int NOT NULL AUTO_INCREMENT,
              `Code` varchar(40) NOT NULL,
              `Name` varchar(120) NOT NULL,
              `PriceVnd` decimal(12,0) NOT NULL,
              `ChatbotLimit` int NOT NULL,
              `AiPostLimit` int NOT NULL,
              `RefillIntervalHours` int NOT NULL,
              `DurationDays` int NOT NULL,
              `IsActive` tinyint(1) NOT NULL,
              `CreatedAt` datetime(6) NOT NULL,
              `UpdatedAt` datetime(6) NOT NULL,
              PRIMARY KEY (`SubscriptionPlanId`), UNIQUE KEY `UX_SubscriptionPlans_Code` (`Code`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS `UserSubscriptions` (
              `UserSubscriptionId` bigint NOT NULL AUTO_INCREMENT,
              `UserId` int NOT NULL, `SubscriptionPlanId` int NOT NULL,
              `Status` varchar(20) NOT NULL, `StartedAt` datetime(6) NOT NULL, `ExpiresAt` datetime(6) NOT NULL,
              PRIMARY KEY (`UserSubscriptionId`), KEY `IX_UserSubscriptions_User_Status_Expiry` (`UserId`,`Status`,`ExpiresAt`),
              CONSTRAINT `FK_UserSubscriptions_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`UserId`),
              CONSTRAINT `FK_UserSubscriptions_SubscriptionPlans_Plan` FOREIGN KEY (`SubscriptionPlanId`) REFERENCES `SubscriptionPlans` (`SubscriptionPlanId`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS `PaymentTransactions` (
              `PaymentTransactionId` bigint NOT NULL AUTO_INCREMENT,
              `UserId` int NOT NULL, `SubscriptionPlanId` int NOT NULL, `TransactionCode` varchar(100) NOT NULL,
              `AmountVnd` decimal(12,0) NOT NULL, `Status` varchar(20) NOT NULL,
              `ProviderTransactionId` varchar(120) NULL, `ProviderPayload` longtext NULL,
              `CreatedAt` datetime(6) NOT NULL, `ExpiresAt` datetime(6) NOT NULL, `PaidAt` datetime(6) NULL,
              PRIMARY KEY (`PaymentTransactionId`), UNIQUE KEY `UX_PaymentTransactions_Code` (`TransactionCode`),
              UNIQUE KEY `UX_PaymentTransactions_ProviderTransactionId` (`ProviderTransactionId`),
              CONSTRAINT `FK_PaymentTransactions_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`UserId`),
              CONSTRAINT `FK_PaymentTransactions_SubscriptionPlans_Plan` FOREIGN KEY (`SubscriptionPlanId`) REFERENCES `SubscriptionPlans` (`SubscriptionPlanId`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS `AiUsageCounters` (
              `AiUsageCounterId` bigint NOT NULL AUTO_INCREMENT, `UserId` int NOT NULL,
              `Feature` varchar(30) NOT NULL, `PeriodStartedAt` datetime(6) NOT NULL,
              `NextRefillAt` datetime(6) NOT NULL, `UsedCount` int NOT NULL,
              PRIMARY KEY (`AiUsageCounterId`), UNIQUE KEY `UX_AiUsageCounters_User_Feature` (`UserId`,`Feature`),
              CONSTRAINT `FK_AiUsageCounters_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`UserId`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS `AiUsageCounters`");
        migrationBuilder.Sql("DROP TABLE IF EXISTS `PaymentTransactions`");
        migrationBuilder.Sql("DROP TABLE IF EXISTS `UserSubscriptions`");
        migrationBuilder.Sql("DROP TABLE IF EXISTS `SubscriptionPlans`");
    }
}
