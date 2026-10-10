-- Least-privileged login for the API: data read/write only, no DDL. Idempotent; also repairs the
-- user-to-login mapping after a restore onto a new host (orphaned user with a different SID).
SET NOCOUNT ON;
IF SUSER_ID(N'$(APP_DB_LOGIN)') IS NULL
    EXEC(N'CREATE LOGIN [$(APP_DB_LOGIN)] WITH PASSWORD = N''$(APP_DB_PASSWORD)'', CHECK_POLICY = ON, DEFAULT_DATABASE = [$(DB_NAME)];');
ELSE
    EXEC(N'ALTER LOGIN [$(APP_DB_LOGIN)] WITH PASSWORD = N''$(APP_DB_PASSWORD)'';');

IF USER_ID(N'$(APP_DB_LOGIN)') IS NULL
    EXEC(N'CREATE USER [$(APP_DB_LOGIN)] FOR LOGIN [$(APP_DB_LOGIN)];');
ELSE
    EXEC(N'ALTER USER [$(APP_DB_LOGIN)] WITH LOGIN = [$(APP_DB_LOGIN)];');

ALTER ROLE db_datareader ADD MEMBER [$(APP_DB_LOGIN)];
ALTER ROLE db_datawriter ADD MEMBER [$(APP_DB_LOGIN)];
