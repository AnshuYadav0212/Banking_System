

IF DB_ID(N'SecureBankIdentityDb') IS NULL
BEGIN
    CREATE DATABASE [SecureBankIdentityDb];
END;
GO

SET QUOTED_IDENTIFIER ON;
GO

USE [SecureBankIdentityDb];
GO

IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users
    (
        UserId UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_Users PRIMARY KEY,

        Username NVARCHAR(30) NULL,

        FirstName NVARCHAR(100) NULL,

        LastName NVARCHAR(100) NULL,

        Email NVARCHAR(256) NOT NULL,

        PhoneNumber NVARCHAR(20) NULL,

        DateOfBirth DATE NULL,

        Gender NVARCHAR(20) NULL,

        Address NVARCHAR(250) NULL,

        City NVARCHAR(100) NULL,

        State NVARCHAR(100) NULL,

        PostalCode NVARCHAR(20) NULL,

        PasswordHash NVARCHAR(500) NOT NULL,

        Role NVARCHAR(50) NOT NULL,

        IsActive BIT NOT NULL
            CONSTRAINT DF_Users_IsActive DEFAULT (1),

        CreatedAt DATETIME2(3) NOT NULL
            CONSTRAINT DF_Users_CreatedAt DEFAULT (SYSUTCDATETIME()),

        UpdatedAt DATETIME2(3) NULL,

        CONSTRAINT UQ_Users_Email UNIQUE (Email),

        CONSTRAINT CK_Users_Role
            CHECK (Role IN ('Customer', 'Employee', 'Admin'))
    );
END;
GO

IF OBJECT_ID(N'dbo.RefreshTokens', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RefreshTokens
    (
        RefreshTokenId UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_RefreshTokens PRIMARY KEY,

        UserId UNIQUEIDENTIFIER NOT NULL,

        TokenHash NVARCHAR(500) NOT NULL,

        ExpiresAt DATETIME2(3) NOT NULL,

        RevokedAt DATETIME2(3) NULL,

        CreatedAt DATETIME2(3) NOT NULL
            CONSTRAINT DF_RefreshTokens_CreatedAt DEFAULT (SYSUTCDATETIME())
    );
END;
GO

IF OBJECT_ID(N'dbo.LoginHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LoginHistory
    (
        LoginHistoryId BIGINT IDENTITY(1, 1) NOT NULL
            CONSTRAINT PK_LoginHistory PRIMARY KEY,

        UserId UNIQUEIDENTIFIER NULL,

        Success BIT NOT NULL,

        IpAddress NVARCHAR(45) NULL,

        UserAgent NVARCHAR(512) NULL,

        LoginTime DATETIME2(3) NOT NULL
            CONSTRAINT DF_LoginHistory_LoginTime DEFAULT (SYSUTCDATETIME())
    );
END;
GO

/*
    Upgrade for databases created before the profile fields existed.
    Safe to run repeatedly. Columns are nullable so existing users keep working
    (they can still sign in with their email).
*/
IF COL_LENGTH(N'dbo.Users', N'Username') IS NULL ALTER TABLE dbo.Users ADD Username NVARCHAR(30) NULL;
IF COL_LENGTH(N'dbo.Users', N'FirstName') IS NULL ALTER TABLE dbo.Users ADD FirstName NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.Users', N'LastName') IS NULL ALTER TABLE dbo.Users ADD LastName NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.Users', N'PhoneNumber') IS NULL ALTER TABLE dbo.Users ADD PhoneNumber NVARCHAR(20) NULL;
IF COL_LENGTH(N'dbo.Users', N'DateOfBirth') IS NULL ALTER TABLE dbo.Users ADD DateOfBirth DATE NULL;
IF COL_LENGTH(N'dbo.Users', N'Gender') IS NULL ALTER TABLE dbo.Users ADD Gender NVARCHAR(20) NULL;
IF COL_LENGTH(N'dbo.Users', N'Address') IS NULL ALTER TABLE dbo.Users ADD Address NVARCHAR(250) NULL;
IF COL_LENGTH(N'dbo.Users', N'City') IS NULL ALTER TABLE dbo.Users ADD City NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.Users', N'State') IS NULL ALTER TABLE dbo.Users ADD State NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.Users', N'PostalCode') IS NULL ALTER TABLE dbo.Users ADD PostalCode NVARCHAR(20) NULL;
GO

/*
    Usernames are stored lower-cased and must be unique. The filtered index lets
    older rows without a username coexist, and gives the availability check an
    index seek instead of a table scan.
*/
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Users_Username' AND object_id = OBJECT_ID(N'dbo.Users'))
BEGIN
    CREATE UNIQUE INDEX UX_Users_Username
        ON dbo.Users (Username)
        WHERE Username IS NOT NULL;
END;
GO

/* ------------------------------------------------------------------
   Existing bank customers (mirror of the core-banking records).
   Online-banking registration verifies a person against these rows.
   In production this data is loaded/synchronised from the core system.
------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.BankCustomers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BankCustomers
    (
        CustomerId UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_BankCustomers PRIMARY KEY,

        CifNumber NVARCHAR(20) NOT NULL,

        FirstName NVARCHAR(100) NOT NULL,

        LastName NVARCHAR(100) NOT NULL,

        DateOfBirth DATE NOT NULL,

        NationalId NVARCHAR(30) NULL,

        PhoneNumber NVARCHAR(20) NOT NULL,

        Email NVARCHAR(256) NULL,

        IsActive BIT NOT NULL
            CONSTRAINT DF_BankCustomers_IsActive DEFAULT (1),

        FailedVerificationCount INT NOT NULL
            CONSTRAINT DF_BankCustomers_Failed DEFAULT (0),

        LockedUntil DATETIME2(3) NULL,

        CONSTRAINT UQ_BankCustomers_Cif UNIQUE (CifNumber)
    );
END;
GO

IF OBJECT_ID(N'dbo.BankAccounts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BankAccounts
    (
        AccountNumber NVARCHAR(20) NOT NULL
            CONSTRAINT PK_BankAccounts PRIMARY KEY,

        CustomerId UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT FK_BankAccounts_Customer
            REFERENCES dbo.BankCustomers (CustomerId),

        IsActive BIT NOT NULL
            CONSTRAINT DF_BankAccounts_IsActive DEFAULT (1)
    );

    CREATE INDEX IX_BankAccounts_CustomerId ON dbo.BankAccounts (CustomerId);
END;
GO

/* ------------------------------------------------------------------
   One row per one-time-passcode challenge: online-banking registration,
   password reset, and the throttling log for username recovery.
   Only a keyed hash of the code is stored, never the code itself.
------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.OtpChallenges', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OtpChallenges
    (
        ChallengeId UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_OtpChallenges PRIMARY KEY,

        Purpose NVARCHAR(30) NOT NULL,

        BankCustomerId UNIQUEIDENTIFIER NULL,

        UserId UNIQUEIDENTIFIER NULL,

        Username NVARCHAR(30) NULL,

        Email NVARCHAR(256) NULL,

        PhoneNumber NVARCHAR(20) NULL,

        PasswordHash NVARCHAR(500) NULL,

        OtpHash NVARCHAR(200) NOT NULL,

        Attempts INT NOT NULL
            CONSTRAINT DF_OtpChallenges_Attempts DEFAULT (0),

        ResendCount INT NOT NULL
            CONSTRAINT DF_OtpChallenges_Resend DEFAULT (0),

        CreatedAt DATETIME2(3) NOT NULL,

        LastSentAt DATETIME2(3) NOT NULL,

        ExpiresAt DATETIME2(3) NOT NULL,

        ConsumedAt DATETIME2(3) NULL
    );

    CREATE INDEX IX_OtpChallenges_Throttle
        ON dbo.OtpChallenges (Purpose, CreatedAt)
        INCLUDE (Email, PhoneNumber, BankCustomerId, UserId);
END;
GO

/* Link an online-banking user to the bank customer they were verified against. */
IF COL_LENGTH(N'dbo.Users', N'BankCustomerId') IS NULL
    ALTER TABLE dbo.Users ADD BankCustomerId UNIQUEIDENTIFIER NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Users_BankCustomer')
    ALTER TABLE dbo.Users ADD CONSTRAINT FK_Users_BankCustomer
        FOREIGN KEY (BankCustomerId) REFERENCES dbo.BankCustomers (CustomerId);
GO

/* One online-banking login per bank customer. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Users_BankCustomerId' AND object_id = OBJECT_ID(N'dbo.Users'))
    CREATE UNIQUE INDEX UX_Users_BankCustomerId
        ON dbo.Users (BankCustomerId)
        WHERE BankCustomerId IS NOT NULL;
GO

/* Last 10 digits of the phone number, indexed, for "forgot username" lookups. */
IF COL_LENGTH(N'dbo.Users', N'PhoneLast10') IS NULL
    ALTER TABLE dbo.Users ADD PhoneLast10 AS RIGHT(PhoneNumber, 10) PERSISTED;
GO

/* Replaces an earlier non-unique index with a unique one: one login per phone number. */
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Users_PhoneLast10' AND object_id = OBJECT_ID(N'dbo.Users'))
    DROP INDEX IX_Users_PhoneLast10 ON dbo.Users;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Users_PhoneLast10' AND object_id = OBJECT_ID(N'dbo.Users'))
    CREATE UNIQUE INDEX UX_Users_PhoneLast10
        ON dbo.Users (PhoneLast10)
        WHERE PhoneNumber IS NOT NULL;
GO
