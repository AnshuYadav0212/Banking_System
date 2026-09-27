/*
    SecureBank Identity Database
    Run this script in SQL Server Management Studio, or with: sqlcmd -S localhost -E -C -I -i SecureBankIdentityDb.sql

    Safe to run repeatedly: a new database gets the final schema, and an older
    one is upgraded in place (see "Upgrade" near the end).

    Normalisation: every fact is stored once, and every fixed set of values has
    its own table that other tables point to by GUID key.

      Lookup tables (rows are the only allowed values)
        * Roles         - Customer, Employee, Admin.
        * Statuses      - Active, Inactive (used by customers, accounts, logins).
        * OtpPurposes   - PasswordReset, UsernameRecovery.

      Bank data (mirror of core banking)
        * BankCustomers - who the customer is: name, date of birth, national ID, phone.
        * BankAccounts  - the accounts a customer owns, each with its available balance.

      Online banking
        * Users         - the login only: username, email, password, role, status.
                          It points at its customer through BankCustomerId; name,
                          date of birth and phone are read from BankCustomers.
        * OtpChallenges - short-lived tokens (password reset, throttling log).

    Primary keys: every table uses a GUID (UNIQUEIDENTIFIER) as its primary key,
    never a serial number and never a business number. Business numbers (CIF,
    account number, username, email) are ordinary columns with their own UNIQUE
    constraint, so they can be validated, displayed and changed without touching
    any key or foreign key, and keys cannot be guessed by counting.

    The lookup GUIDs below are fixed on purpose (the application refers to them
    by value - see Models/LookupIds.cs, so they must not be changed) and are
    each a random v4 GUID, not a guessable sequential pattern: knowing one
    lookup key gives no way to guess another table's key, or another row in
    the same table. A database seeded with the older sequential pattern
    (10000000-...-0001, 20000000-...-0001, and so on) is rekeyed to these
    values in place - see "Rekey the lookup tables" near the end.
*/

IF DB_ID(N'SecureBankIdentityDb') IS NULL
BEGIN
    CREATE DATABASE [SecureBankIdentityDb];
END;
GO

SET QUOTED_IDENTIFIER ON;
GO

USE [SecureBankIdentityDb];
GO

/* ==================================================================
   Lookup tables
================================================================== */
IF OBJECT_ID(N'dbo.Roles', N'U') IS NULL
    CREATE TABLE dbo.Roles
    (
        RoleId UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_Roles PRIMARY KEY,

        Name NVARCHAR(50) NOT NULL
            CONSTRAINT UQ_Roles_Name UNIQUE
    );
GO

IF OBJECT_ID(N'dbo.Statuses', N'U') IS NULL
    CREATE TABLE dbo.Statuses
    (
        StatusId UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_Statuses PRIMARY KEY,

        Name NVARCHAR(30) NOT NULL
            CONSTRAINT UQ_Statuses_Name UNIQUE
    );
GO

IF OBJECT_ID(N'dbo.OtpPurposes', N'U') IS NULL
    CREATE TABLE dbo.OtpPurposes
    (
        PurposeId UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_OtpPurposes PRIMARY KEY,

        Name NVARCHAR(30) NOT NULL
            CONSTRAINT UQ_OtpPurposes_Name UNIQUE
    );
GO

INSERT dbo.Roles (RoleId, Name)
SELECT v.Id, v.Name
FROM (VALUES
        ('2AD39D82-4527-421F-BC48-0024F41392C3', N'Customer'),
        ('8AFC0CD1-50D7-4CDE-9555-C336E07C632E', N'Employee'),
        ('6B7A8B89-B9A0-4B50-B800-3806C6FDAB09', N'Admin')) AS v (Id, Name)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Roles r WHERE r.RoleId = v.Id);

INSERT dbo.Statuses (StatusId, Name)
SELECT v.Id, v.Name
FROM (VALUES
        ('29394BF4-EB03-4808-B2B4-5452F352D506', N'Active'),
        ('036BA01A-4FD0-4AB4-8A43-21B08A9DC67F', N'Inactive')) AS v (Id, Name)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Statuses s WHERE s.StatusId = v.Id);

INSERT dbo.OtpPurposes (PurposeId, Name)
SELECT v.Id, v.Name
FROM (VALUES
        ('BD3B00CA-D8E3-4B4A-9B98-90E5B7F58B50', N'PasswordReset'),
        ('2E8909F7-34EF-4E74-8479-7A4A2FEE9D93', N'UsernameRecovery')) AS v (Id, Name)
WHERE NOT EXISTS (SELECT 1 FROM dbo.OtpPurposes p WHERE p.PurposeId = v.Id);
GO

/* ==================================================================
   Bank data
================================================================== */

/* ------------------------------------------------------------------
   Existing bank customers (mirror of the core-banking records).
   Online-banking registration verifies a person against these rows.
   In production this data is loaded/synchronised from the core system.
   PhoneNumber should be held in one consistent format (+countrycode digits).
   The customer's email is not bank data here: it is chosen at registration
   and belongs to the login (Users.Email).
   StatusId defaults to Active, so a plain INSERT creates an active customer.
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

        StatusId UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT DF_BankCustomers_Status DEFAULT ('29394BF4-EB03-4808-B2B4-5452F352D506')
            CONSTRAINT FK_BankCustomers_Status REFERENCES dbo.Statuses (StatusId),

        FailedVerificationCount INT NOT NULL
            CONSTRAINT DF_BankCustomers_Failed DEFAULT (0),

        LockedUntil DATETIME2(3) NULL,

        CONSTRAINT UQ_BankCustomers_Cif UNIQUE (CifNumber)
    );
END;
GO

/* ------------------------------------------------------------------
   Accounts. The key is AccountId (a GUID); AccountNumber is the number the
   customer sees: digits only, 6-20 long, unique. AvailableBalance is per
   account (two decimals), as reported by the bank; it defaults to 0.
------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.BankAccounts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BankAccounts
    (
        AccountId UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_BankAccounts PRIMARY KEY
            CONSTRAINT DF_BankAccounts_AccountId DEFAULT (NEWID()),

        AccountNumber NVARCHAR(20) NOT NULL
            CONSTRAINT UQ_BankAccounts_AccountNumber UNIQUE
            CONSTRAINT CK_BankAccounts_AccountNumber
                CHECK (LEN(AccountNumber) BETWEEN 6 AND 20 AND AccountNumber NOT LIKE '%[^0-9]%'),

        CustomerId UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT FK_BankAccounts_Customer
            REFERENCES dbo.BankCustomers (CustomerId),

        AvailableBalance DECIMAL(18, 2) NOT NULL
            CONSTRAINT DF_BankAccounts_Balance DEFAULT (0),

        StatusId UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT DF_BankAccounts_Status DEFAULT ('29394BF4-EB03-4808-B2B4-5452F352D506')
            CONSTRAINT FK_BankAccounts_Status REFERENCES dbo.Statuses (StatusId)
    );

    CREATE INDEX IX_BankAccounts_CustomerId ON dbo.BankAccounts (CustomerId);
END;
GO

/* ==================================================================
   Online banking
================================================================== */

/* ------------------------------------------------------------------
   Online-banking logins. Name, date of birth and phone are NOT stored here:
   they belong to the bank customer and are read through BankCustomerId.
   BankCustomerId is NULL only for logins created before registration was
   tied to bank records.
------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users
    (
        UserId UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_Users PRIMARY KEY,

        BankCustomerId UNIQUEIDENTIFIER NULL
            CONSTRAINT FK_Users_BankCustomer
            REFERENCES dbo.BankCustomers (CustomerId),

        Username NVARCHAR(30) NULL,

        Email NVARCHAR(256) NOT NULL,

        PasswordHash NVARCHAR(500) NOT NULL,

        RoleId UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT FK_Users_Role REFERENCES dbo.Roles (RoleId),

        StatusId UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT DF_Users_Status DEFAULT ('29394BF4-EB03-4808-B2B4-5452F352D506')
            CONSTRAINT FK_Users_Status REFERENCES dbo.Statuses (StatusId),

        CreatedAt DATETIME2(3) NOT NULL
            CONSTRAINT DF_Users_CreatedAt DEFAULT (SYSUTCDATETIME()),

        UpdatedAt DATETIME2(3) NULL,

        CONSTRAINT UQ_Users_Email UNIQUE (Email)
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

/* An older version keyed LoginHistory by a serial number; rebuild it with a GUID key.
   Nothing writes to it yet. If it ever holds rows they must be migrated by hand. */
IF OBJECT_ID(N'dbo.LoginHistory', N'U') IS NOT NULL
   AND COLUMNPROPERTY(OBJECT_ID(N'dbo.LoginHistory'), N'LoginHistoryId', 'IsIdentity') = 1
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.LoginHistory)
        RAISERROR (N'dbo.LoginHistory has rows and a serial key: migrate them to a GUID key first.', 16, 1);
    ELSE
        DROP TABLE dbo.LoginHistory;
END;
GO

IF OBJECT_ID(N'dbo.LoginHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LoginHistory
    (
        LoginHistoryId UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_LoginHistory PRIMARY KEY
            CONSTRAINT DF_LoginHistory_Id DEFAULT (NEWID()),

        UserId UNIQUEIDENTIFIER NULL,

        Success BIT NOT NULL,

        IpAddress NVARCHAR(45) NULL,

        UserAgent NVARCHAR(512) NULL,

        LoginTime DATETIME2(3) NOT NULL
            CONSTRAINT DF_LoginHistory_LoginTime DEFAULT (SYSUTCDATETIME())
    );
END;
GO

/* ------------------------------------------------------------------
   Short-lived one-time tokens. Only a keyed hash of the token is stored.
     PasswordReset    - UserId identifies the login; nothing else is copied.
     UsernameRecovery - a throttling log keyed by the email or phone asked
                        about (there may be no matching login).
------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.OtpChallenges', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OtpChallenges
    (
        ChallengeId UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_OtpChallenges PRIMARY KEY,

        PurposeId UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT FK_OtpChallenges_Purpose REFERENCES dbo.OtpPurposes (PurposeId),

        UserId UNIQUEIDENTIFIER NULL,

        Email NVARCHAR(256) NULL,

        PhoneNumber NVARCHAR(20) NULL,

        OtpHash NVARCHAR(200) NOT NULL,

        Attempts INT NOT NULL
            CONSTRAINT DF_OtpChallenges_Attempts DEFAULT (0),

        CreatedAt DATETIME2(3) NOT NULL,

        ExpiresAt DATETIME2(3) NOT NULL,

        ConsumedAt DATETIME2(3) NULL
    );
END;
GO

/* ==================================================================
   Upgrade an older database to the schema above. Idempotent.
   (Statements that mention old columns are dynamic SQL so this script also
   compiles on a new database, where those columns never existed.)
================================================================== */

/* ---- Users: columns older versions did not have. */
IF COL_LENGTH(N'dbo.Users', N'Username') IS NULL ALTER TABLE dbo.Users ADD Username NVARCHAR(30) NULL;
IF COL_LENGTH(N'dbo.Users', N'BankCustomerId') IS NULL ALTER TABLE dbo.Users ADD BankCustomerId UNIQUEIDENTIFIER NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Users_BankCustomer')
    ALTER TABLE dbo.Users ADD CONSTRAINT FK_Users_BankCustomer
        FOREIGN KEY (BankCustomerId) REFERENCES dbo.BankCustomers (CustomerId);
GO

/* ---- Users: derived / duplicated / unused columns removed.
     PhoneLast10                                   - computed copy of PhoneNumber.
     PhoneNumber, FirstName, LastName, DateOfBirth - duplicates of BankCustomers.
     Gender, Address, City, State, PostalCode      - no longer collected. */
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Users_PhoneLast10' AND object_id = OBJECT_ID(N'dbo.Users'))
    DROP INDEX UX_Users_PhoneLast10 ON dbo.Users;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Users_PhoneLast10' AND object_id = OBJECT_ID(N'dbo.Users'))
    DROP INDEX IX_Users_PhoneLast10 ON dbo.Users;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Users_PhoneNumber' AND object_id = OBJECT_ID(N'dbo.Users'))
    DROP INDEX UX_Users_PhoneNumber ON dbo.Users;
GO

IF COL_LENGTH(N'dbo.Users', N'PhoneLast10') IS NOT NULL ALTER TABLE dbo.Users DROP COLUMN PhoneLast10;
IF COL_LENGTH(N'dbo.Users', N'PhoneNumber') IS NOT NULL ALTER TABLE dbo.Users DROP COLUMN PhoneNumber;
IF COL_LENGTH(N'dbo.Users', N'FirstName') IS NOT NULL ALTER TABLE dbo.Users DROP COLUMN FirstName;
IF COL_LENGTH(N'dbo.Users', N'LastName') IS NOT NULL ALTER TABLE dbo.Users DROP COLUMN LastName;
IF COL_LENGTH(N'dbo.Users', N'DateOfBirth') IS NOT NULL ALTER TABLE dbo.Users DROP COLUMN DateOfBirth;
IF COL_LENGTH(N'dbo.Users', N'Gender') IS NOT NULL ALTER TABLE dbo.Users DROP COLUMN Gender;
IF COL_LENGTH(N'dbo.Users', N'Address') IS NOT NULL ALTER TABLE dbo.Users DROP COLUMN Address;
IF COL_LENGTH(N'dbo.Users', N'City') IS NOT NULL ALTER TABLE dbo.Users DROP COLUMN City;
IF COL_LENGTH(N'dbo.Users', N'State') IS NOT NULL ALTER TABLE dbo.Users DROP COLUMN State;
IF COL_LENGTH(N'dbo.Users', N'PostalCode') IS NOT NULL ALTER TABLE dbo.Users DROP COLUMN PostalCode;
GO

/* ---- BankAccounts: a GUID primary key; the account number becomes a plain unique column. */
IF COL_LENGTH(N'dbo.BankAccounts', N'AccountId') IS NULL
    ALTER TABLE dbo.BankAccounts ADD AccountId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT DF_BankAccounts_AccountId DEFAULT (NEWID());
GO

IF EXISTS (SELECT 1
           FROM sys.key_constraints kc
           JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
           JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
           WHERE kc.parent_object_id = OBJECT_ID(N'dbo.BankAccounts') AND kc.type = 'PK' AND c.name = N'AccountNumber')
    ALTER TABLE dbo.BankAccounts DROP CONSTRAINT PK_BankAccounts;
GO

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'PK_BankAccounts' AND parent_object_id = OBJECT_ID(N'dbo.BankAccounts'))
    ALTER TABLE dbo.BankAccounts ADD CONSTRAINT PK_BankAccounts PRIMARY KEY (AccountId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_BankAccounts_AccountNumber' AND parent_object_id = OBJECT_ID(N'dbo.BankAccounts'))
    ALTER TABLE dbo.BankAccounts ADD CONSTRAINT UQ_BankAccounts_AccountNumber UNIQUE (AccountNumber);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_BankAccounts_AccountNumber')
    ALTER TABLE dbo.BankAccounts ADD CONSTRAINT CK_BankAccounts_AccountNumber
        CHECK (LEN(AccountNumber) BETWEEN 6 AND 20 AND AccountNumber NOT LIKE '%[^0-9]%');
GO

/* ---- Available balance belongs to the account, not the customer. */
IF COL_LENGTH(N'dbo.BankAccounts', N'AvailableBalance') IS NULL
    ALTER TABLE dbo.BankAccounts ADD AvailableBalance DECIMAL(18, 2) NOT NULL
        CONSTRAINT DF_BankAccounts_Balance DEFAULT (0);
GO

/* A balance that was held on the customer moves to the customer's first account
   (lowest account number), so nothing is lost; adjust it afterwards if needed. */
IF COL_LENGTH(N'dbo.BankCustomers', N'AvailableBalance') IS NOT NULL
    EXEC (N'UPDATE a SET AvailableBalance = c.AvailableBalance
            FROM dbo.BankAccounts a
            JOIN dbo.BankCustomers c ON c.CustomerId = a.CustomerId
            WHERE c.AvailableBalance <> 0
              AND a.AvailableBalance = 0
              AND a.AccountNumber = (SELECT MIN(a2.AccountNumber) FROM dbo.BankAccounts a2 WHERE a2.CustomerId = c.CustomerId)');
GO

IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_BankCustomers_Balance')
    ALTER TABLE dbo.BankCustomers DROP CONSTRAINT DF_BankCustomers_Balance;
GO

IF COL_LENGTH(N'dbo.BankCustomers', N'AvailableBalance') IS NOT NULL ALTER TABLE dbo.BankCustomers DROP COLUMN AvailableBalance;
GO

/* ---- BankCustomers: the email is chosen at registration and lives on the login. */
IF COL_LENGTH(N'dbo.BankCustomers', N'Email') IS NOT NULL ALTER TABLE dbo.BankCustomers DROP COLUMN Email;
GO

/* ---- OtpChallenges: leftovers of the old confirmation-email registration
        (a pending username, phone and password hash) and resend counters. */
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OtpChallenges_Throttle' AND object_id = OBJECT_ID(N'dbo.OtpChallenges'))
    DROP INDEX IX_OtpChallenges_Throttle ON dbo.OtpChallenges;
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_OtpChallenges_Resend')
    ALTER TABLE dbo.OtpChallenges DROP CONSTRAINT DF_OtpChallenges_Resend;
GO

IF COL_LENGTH(N'dbo.OtpChallenges', N'BankCustomerId') IS NOT NULL ALTER TABLE dbo.OtpChallenges DROP COLUMN BankCustomerId;
IF COL_LENGTH(N'dbo.OtpChallenges', N'Username') IS NOT NULL ALTER TABLE dbo.OtpChallenges DROP COLUMN Username;
IF COL_LENGTH(N'dbo.OtpChallenges', N'PasswordHash') IS NOT NULL ALTER TABLE dbo.OtpChallenges DROP COLUMN PasswordHash;
IF COL_LENGTH(N'dbo.OtpChallenges', N'ResendCount') IS NOT NULL ALTER TABLE dbo.OtpChallenges DROP COLUMN ResendCount;
IF COL_LENGTH(N'dbo.OtpChallenges', N'LastSentAt') IS NOT NULL ALTER TABLE dbo.OtpChallenges DROP COLUMN LastSentAt;
GO

/* ---- Lookups, step 1: add the new key columns (nullable until filled). */
IF COL_LENGTH(N'dbo.Users', N'RoleId') IS NULL ALTER TABLE dbo.Users ADD RoleId UNIQUEIDENTIFIER NULL;
IF COL_LENGTH(N'dbo.Users', N'StatusId') IS NULL ALTER TABLE dbo.Users ADD StatusId UNIQUEIDENTIFIER NULL;
IF COL_LENGTH(N'dbo.BankCustomers', N'StatusId') IS NULL ALTER TABLE dbo.BankCustomers ADD StatusId UNIQUEIDENTIFIER NULL;
IF COL_LENGTH(N'dbo.BankAccounts', N'StatusId') IS NULL ALTER TABLE dbo.BankAccounts ADD StatusId UNIQUEIDENTIFIER NULL;
IF COL_LENGTH(N'dbo.OtpChallenges', N'PurposeId') IS NULL ALTER TABLE dbo.OtpChallenges ADD PurposeId UNIQUEIDENTIFIER NULL;
GO

/* ---- Lookups, step 2: fill them from the old text / bit columns. */
IF COL_LENGTH(N'dbo.Users', N'Role') IS NOT NULL
    EXEC (N'UPDATE u SET RoleId = r.RoleId FROM dbo.Users u JOIN dbo.Roles r ON r.Name = u.Role WHERE u.RoleId IS NULL');
IF COL_LENGTH(N'dbo.Users', N'IsActive') IS NOT NULL
    EXEC (N'UPDATE dbo.Users SET StatusId = CASE WHEN IsActive = 1 THEN ''29394BF4-EB03-4808-B2B4-5452F352D506'' ELSE ''036BA01A-4FD0-4AB4-8A43-21B08A9DC67F'' END WHERE StatusId IS NULL');
IF COL_LENGTH(N'dbo.BankCustomers', N'IsActive') IS NOT NULL
    EXEC (N'UPDATE dbo.BankCustomers SET StatusId = CASE WHEN IsActive = 1 THEN ''29394BF4-EB03-4808-B2B4-5452F352D506'' ELSE ''036BA01A-4FD0-4AB4-8A43-21B08A9DC67F'' END WHERE StatusId IS NULL');
IF COL_LENGTH(N'dbo.BankAccounts', N'IsActive') IS NOT NULL
    EXEC (N'UPDATE dbo.BankAccounts SET StatusId = CASE WHEN IsActive = 1 THEN ''29394BF4-EB03-4808-B2B4-5452F352D506'' ELSE ''036BA01A-4FD0-4AB4-8A43-21B08A9DC67F'' END WHERE StatusId IS NULL');
IF COL_LENGTH(N'dbo.OtpChallenges', N'Purpose') IS NOT NULL
    EXEC (N'UPDATE c SET PurposeId = p.PurposeId FROM dbo.OtpChallenges c JOIN dbo.OtpPurposes p ON p.Name = c.Purpose WHERE c.PurposeId IS NULL');

/* Challenges are short-lived; any row that maps to no known purpose is stale. */
DELETE FROM dbo.OtpChallenges WHERE PurposeId IS NULL;
GO

/* ---- Lookups, step 3: make the keys mandatory and add defaults / foreign keys. */
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = N'RoleId' AND is_nullable = 1)
    ALTER TABLE dbo.Users ALTER COLUMN RoleId UNIQUEIDENTIFIER NOT NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = N'StatusId' AND is_nullable = 1)
    ALTER TABLE dbo.Users ALTER COLUMN StatusId UNIQUEIDENTIFIER NOT NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.BankCustomers') AND name = N'StatusId' AND is_nullable = 1)
    ALTER TABLE dbo.BankCustomers ALTER COLUMN StatusId UNIQUEIDENTIFIER NOT NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.BankAccounts') AND name = N'StatusId' AND is_nullable = 1)
    ALTER TABLE dbo.BankAccounts ALTER COLUMN StatusId UNIQUEIDENTIFIER NOT NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.OtpChallenges') AND name = N'PurposeId' AND is_nullable = 1)
    ALTER TABLE dbo.OtpChallenges ALTER COLUMN PurposeId UNIQUEIDENTIFIER NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_Users_Status')
    ALTER TABLE dbo.Users ADD CONSTRAINT DF_Users_Status DEFAULT ('29394BF4-EB03-4808-B2B4-5452F352D506') FOR StatusId;
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_BankCustomers_Status')
    ALTER TABLE dbo.BankCustomers ADD CONSTRAINT DF_BankCustomers_Status DEFAULT ('29394BF4-EB03-4808-B2B4-5452F352D506') FOR StatusId;
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_BankAccounts_Status')
    ALTER TABLE dbo.BankAccounts ADD CONSTRAINT DF_BankAccounts_Status DEFAULT ('29394BF4-EB03-4808-B2B4-5452F352D506') FOR StatusId;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Users_Role')
    ALTER TABLE dbo.Users ADD CONSTRAINT FK_Users_Role FOREIGN KEY (RoleId) REFERENCES dbo.Roles (RoleId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Users_Status')
    ALTER TABLE dbo.Users ADD CONSTRAINT FK_Users_Status FOREIGN KEY (StatusId) REFERENCES dbo.Statuses (StatusId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BankCustomers_Status')
    ALTER TABLE dbo.BankCustomers ADD CONSTRAINT FK_BankCustomers_Status FOREIGN KEY (StatusId) REFERENCES dbo.Statuses (StatusId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BankAccounts_Status')
    ALTER TABLE dbo.BankAccounts ADD CONSTRAINT FK_BankAccounts_Status FOREIGN KEY (StatusId) REFERENCES dbo.Statuses (StatusId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_OtpChallenges_Purpose')
    ALTER TABLE dbo.OtpChallenges ADD CONSTRAINT FK_OtpChallenges_Purpose FOREIGN KEY (PurposeId) REFERENCES dbo.OtpPurposes (PurposeId);
GO

/* ---- Lookups, step 4: drop the old text / bit columns and their constraints. */
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Users_Role')
    ALTER TABLE dbo.Users DROP CONSTRAINT CK_Users_Role;
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_Users_IsActive')
    ALTER TABLE dbo.Users DROP CONSTRAINT DF_Users_IsActive;
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_BankCustomers_IsActive')
    ALTER TABLE dbo.BankCustomers DROP CONSTRAINT DF_BankCustomers_IsActive;
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_BankAccounts_IsActive')
    ALTER TABLE dbo.BankAccounts DROP CONSTRAINT DF_BankAccounts_IsActive;
GO

IF COL_LENGTH(N'dbo.Users', N'Role') IS NOT NULL ALTER TABLE dbo.Users DROP COLUMN Role;
IF COL_LENGTH(N'dbo.Users', N'IsActive') IS NOT NULL ALTER TABLE dbo.Users DROP COLUMN IsActive;
IF COL_LENGTH(N'dbo.BankCustomers', N'IsActive') IS NOT NULL ALTER TABLE dbo.BankCustomers DROP COLUMN IsActive;
IF COL_LENGTH(N'dbo.BankAccounts', N'IsActive') IS NOT NULL ALTER TABLE dbo.BankAccounts DROP COLUMN IsActive;
IF COL_LENGTH(N'dbo.OtpChallenges', N'Purpose') IS NOT NULL ALTER TABLE dbo.OtpChallenges DROP COLUMN Purpose;
GO

/* ==================================================================
   Rekey the lookup tables to non-predictable GUIDs.

   The three lookup tables above used to be seeded with a guessable
   sequential pattern (10000000-...-0001, ...-0002, ...; 20000000-...; and
   so on): anyone who learned one lookup key could guess the rest. This
   swaps every lookup row to a fresh random GUID and repoints every foreign
   key that used the old value - matched by name, so it works regardless of
   which old value a given database happens to still have.

   The Name column is unique, so each row is renamed out of the way first,
   the new row is inserted, every reference is repointed, and only then is
   the old row removed - never two rows sharing a name, and no window where
   a foreign key points at a row that does not exist.
================================================================== */
IF EXISTS (SELECT 1 FROM dbo.Roles WHERE RoleId IN
    ('10000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000003'))
BEGIN
    UPDATE dbo.Roles SET Name = Name + N'__rekey' WHERE RoleId IN
        ('10000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000003');

    INSERT dbo.Roles (RoleId, Name)
    SELECT v.Id, v.Name
    FROM (VALUES
            ('2AD39D82-4527-421F-BC48-0024F41392C3', N'Customer'),
            ('8AFC0CD1-50D7-4CDE-9555-C336E07C632E', N'Employee'),
            ('6B7A8B89-B9A0-4B50-B800-3806C6FDAB09', N'Admin')) AS v (Id, Name)
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Roles r WHERE r.RoleId = v.Id);

    UPDATE dbo.Users SET RoleId = '2AD39D82-4527-421F-BC48-0024F41392C3' WHERE RoleId = '10000000-0000-0000-0000-000000000001';
    UPDATE dbo.Users SET RoleId = '8AFC0CD1-50D7-4CDE-9555-C336E07C632E' WHERE RoleId = '10000000-0000-0000-0000-000000000002';
    UPDATE dbo.Users SET RoleId = '6B7A8B89-B9A0-4B50-B800-3806C6FDAB09' WHERE RoleId = '10000000-0000-0000-0000-000000000003';

    DELETE FROM dbo.Roles WHERE RoleId IN
        ('10000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000003');
END;
GO

IF EXISTS (SELECT 1 FROM dbo.Statuses WHERE StatusId IN
    ('20000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000002'))
BEGIN
    UPDATE dbo.Statuses SET Name = Name + N'__rekey' WHERE StatusId IN
        ('20000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000002');

    INSERT dbo.Statuses (StatusId, Name)
    SELECT v.Id, v.Name
    FROM (VALUES
            ('29394BF4-EB03-4808-B2B4-5452F352D506', N'Active'),
            ('036BA01A-4FD0-4AB4-8A43-21B08A9DC67F', N'Inactive')) AS v (Id, Name)
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Statuses s WHERE s.StatusId = v.Id);

    UPDATE dbo.Users SET StatusId = '29394BF4-EB03-4808-B2B4-5452F352D506' WHERE StatusId = '20000000-0000-0000-0000-000000000001';
    UPDATE dbo.Users SET StatusId = '036BA01A-4FD0-4AB4-8A43-21B08A9DC67F' WHERE StatusId = '20000000-0000-0000-0000-000000000002';
    UPDATE dbo.BankCustomers SET StatusId = '29394BF4-EB03-4808-B2B4-5452F352D506' WHERE StatusId = '20000000-0000-0000-0000-000000000001';
    UPDATE dbo.BankCustomers SET StatusId = '036BA01A-4FD0-4AB4-8A43-21B08A9DC67F' WHERE StatusId = '20000000-0000-0000-0000-000000000002';
    UPDATE dbo.BankAccounts SET StatusId = '29394BF4-EB03-4808-B2B4-5452F352D506' WHERE StatusId = '20000000-0000-0000-0000-000000000001';
    UPDATE dbo.BankAccounts SET StatusId = '036BA01A-4FD0-4AB4-8A43-21B08A9DC67F' WHERE StatusId = '20000000-0000-0000-0000-000000000002';

    DELETE FROM dbo.Statuses WHERE StatusId IN
        ('20000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000002');
END;
GO

IF EXISTS (SELECT 1 FROM dbo.OtpPurposes WHERE PurposeId IN
    ('30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000002'))
BEGIN
    UPDATE dbo.OtpPurposes SET Name = Name + N'__rekey' WHERE PurposeId IN
        ('30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000002');

    INSERT dbo.OtpPurposes (PurposeId, Name)
    SELECT v.Id, v.Name
    FROM (VALUES
            ('BD3B00CA-D8E3-4B4A-9B98-90E5B7F58B50', N'PasswordReset'),
            ('2E8909F7-34EF-4E74-8479-7A4A2FEE9D93', N'UsernameRecovery')) AS v (Id, Name)
    WHERE NOT EXISTS (SELECT 1 FROM dbo.OtpPurposes p WHERE p.PurposeId = v.Id);

    UPDATE dbo.OtpChallenges SET PurposeId = 'BD3B00CA-D8E3-4B4A-9B98-90E5B7F58B50' WHERE PurposeId = '30000000-0000-0000-0000-000000000001';
    UPDATE dbo.OtpChallenges SET PurposeId = '2E8909F7-34EF-4E74-8479-7A4A2FEE9D93' WHERE PurposeId = '30000000-0000-0000-0000-000000000002';

    DELETE FROM dbo.OtpPurposes WHERE PurposeId IN
        ('30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000002');
END;
GO

/* ==================================================================
   Indexes. Each unique index doubles as the fast lookup for its column.
   They are filtered so older logins with no username / customer can coexist.
================================================================== */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Users_Username' AND object_id = OBJECT_ID(N'dbo.Users'))
    CREATE UNIQUE INDEX UX_Users_Username ON dbo.Users (Username) WHERE Username IS NOT NULL;
GO

/* One online-banking login per bank customer. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Users_BankCustomerId' AND object_id = OBJECT_ID(N'dbo.Users'))
    CREATE UNIQUE INDEX UX_Users_BankCustomerId ON dbo.Users (BankCustomerId) WHERE BankCustomerId IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OtpChallenges_Throttle' AND object_id = OBJECT_ID(N'dbo.OtpChallenges'))
    CREATE INDEX IX_OtpChallenges_Throttle
        ON dbo.OtpChallenges (PurposeId, CreatedAt)
        INCLUDE (Email, PhoneNumber, UserId);
GO
