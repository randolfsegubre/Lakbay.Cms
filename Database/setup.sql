USE [master]
GO

-- Cms's own database, per ADR-0001/ADR-0005.
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'umbracoDb')
    BEGIN
        CREATE DATABASE [umbracoDb]
    END;
GO

-- Booking's own database, per ADR-0003 (deliberately never shared tables
-- with umbracoDb — this is the same SQL Server *instance* for local-dev
-- convenience only, two fully separate databases on it).
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'lakbayBookingDb')
    BEGIN
        CREATE DATABASE [lakbayBookingDb]
    END;
GO
