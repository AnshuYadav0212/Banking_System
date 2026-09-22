/*
    SEED an employee login for development and demos ONLY.
    Employees are not bank customers, so they never go through customer
    registration; this script inserts the login directly. Safe to re-run.

    Login:
        Username: employee1
        Email:    employee1@securebank.local
        Password: Employee@123

    The password hash below was produced with ASP.NET Core Identity's
    PasswordHasher<T> (the same one AuthService verifies with), so this
    account logs in exactly like one created by the application.
*/
USE [SecureBankIdentityDb];
GO

SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Email = N'employee1@securebank.local')
BEGIN
    INSERT INTO dbo.Users (UserId, BankCustomerId, Username, Email, PasswordHash, RoleId, StatusId, CreatedAt)
    VALUES
    (
        NEWID(),
        NULL,
        N'employee1',
        N'employee1@securebank.local',
        N'AQAAAAIAAYagAAAAEMeUOQAQGu//BfIEdoYWVXF9IL2HKwmYCYoBqppGQhpm5oo9/fNWycD/U8a+YLVUPw==',
        '10000000-0000-0000-0000-000000000002', -- Employee
        '20000000-0000-0000-0000-000000000001', -- Active
        SYSUTCDATETIME()
    );
END;
GO
