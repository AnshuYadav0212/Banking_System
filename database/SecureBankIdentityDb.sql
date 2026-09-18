

IF DB_ID(N'SecureBankIdentityDb') IS NULL
BEGIN
    CREATE DATABASE [SecureBankIdentityDb];
END;
GO

USE [SecureBankIdentityDb];
GO

IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users
    (
        UserId UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_Users PRIMARY KEY,

        Email NVARCHAR(256) NOT NULL,

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
