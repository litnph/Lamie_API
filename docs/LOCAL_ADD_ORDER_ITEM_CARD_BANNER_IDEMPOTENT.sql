BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260810090000_AddOrderItemCardAndBanner'
)
BEGIN
    ALTER TABLE [sales_order_items] ADD [banner_message] nvarchar(1000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260810090000_AddOrderItemCardAndBanner'
)
BEGIN
    ALTER TABLE [sales_order_items] ADD [card_message] nvarchar(1000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260810090000_AddOrderItemCardAndBanner'
)
BEGIN
    ALTER TABLE [sales_order_items] ADD [has_banner] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260810090000_AddOrderItemCardAndBanner'
)
BEGIN
    ALTER TABLE [sales_order_items] ADD [has_card] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260810090000_AddOrderItemCardAndBanner'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([migration_id], [product_version])
    VALUES (N'20260810090000_AddOrderItemCardAndBanner', N'8.0.23');
END;
GO

COMMIT;
GO

