-- ═══════════════════════════════════════════════════════════════════════════
-- SQL Server / Azure SQL demo warehouse — attached in CI via a service
-- container (DATAHUB_SQLSERVER_CONN). DataHub only ever runs READ queries
-- here; this file ships the schema + deterministic seed the pipeline expects.
-- Run as: sqlcmd -S <host> -i sqlserver_warehouse.sql
-- ═══════════════════════════════════════════════════════════════════════════

IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE [name] = N'finance')
BEGIN
    CREATE DATABASE finance;
END;
GO

USE finance;
GO

IF OBJECT_ID(N'dbo.hr_employees', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.hr_employees (
        employee_id INT IDENTITY(1,1) PRIMARY KEY,
        name        NVARCHAR(120) NOT NULL,
        department  NVARCHAR(64)  NOT NULL,
        salary      DECIMAL(10,2) NOT NULL,
        hired_utc   DATETIME2     NOT NULL,
        note        NVARCHAR(200) NULL
    );
END;
GO

-- Re-seed deterministically (idempotent): the CI suite asserts exact totals.
TRUNCATE TABLE dbo.hr_employees;

INSERT INTO dbo.hr_employees (name, department, salary, hired_utc, note) VALUES
    ('Amir Hassan',  N'facade',   7200.00, '2026-01-06T09:00:00', 'senior'),
    ('Lina Maher',   N'facade',   6800.00, '2026-01-13T09:00:00', NULL),
    ('Omar Rashad',  N'platform', 8100.00, '2026-02-03T09:00:00', NULL),
    ('Sara Waled',   N'platform', 7500.00, '2026-02-10T09:00:00', 'remote'),
    ('Hana Fathy',   N'data',     9200.00, '2026-03-02T09:00:00', NULL),
    ('Yara Adly',    N'data',     8600.00, '2026-03-09T09:00:00', NULL),
    ('Karim Sherif', N'data',     9900.00, '2026-04-01T09:00:00', 'lead'),
    ('Mona Selim',   N'ops',      5400.00, '2026-04-06T09:00:00', NULL),
    ('Nabil Hassan', N'ops',      5100.00, '2026-05-04T09:00:00', NULL),
    ('Dina Kamel',   N'facade',   6200.00, '2026-05-11T09:00:00', NULL);

-- The integration suite runs the dialect wrapper:
--   SELECT * FROM (SELECT department AS dept, SUM(salary) AS payroll
--                  FROM hr_employees GROUP BY department ORDER BY payroll DESC) AS sub
--   ORDER BY 1 OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY
-- Expected rows: data 27700.00, facade 20200.00, ops 10500.00, platform 15600.00
GO
