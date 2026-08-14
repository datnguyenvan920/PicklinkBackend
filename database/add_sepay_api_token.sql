BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814111401_AddSePayApiTokenToOwnerBankAccount'
)
BEGIN
    IF COL_LENGTH(N'OWNER_BANK_ACCOUNT', N'sePayApiToken') IS NULL
        ALTER TABLE [OWNER_BANK_ACCOUNT] ADD [sePayApiToken] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814111401_AddSePayApiTokenToOwnerBankAccount'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260814111401_AddSePayApiTokenToOwnerBankAccount', N'8.0.28');
END;
GO

COMMIT;
GO

