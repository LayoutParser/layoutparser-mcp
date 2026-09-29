-- =============================================================================
-- MCP Hub — esquema [mcp] (Fase 1). Idempotente: pode rodar várias vezes.
--
-- ATENÇÃO: aplicar SOMENTE no IdentityDatabase (container layoutparser-identity-sql).
-- NUNCA em 172.31.249.51 / Database:* (somente leitura — ver .claude/rules/security.md).
-- O serviço NÃO cria tabelas sozinho (sem DDL lazy): este script é o único caminho.
-- Contract e Status ficam para a Fase 2.
--
-- Cadastro de um chat (o token NUNCA vai para o banco, só o hash):
--   dotnet LayoutParserMcp.dll hash-token   (lê o token do stdin; usa MCP_HUB_TOKEN_PEPPER)
--   INSERT INTO mcp.Chat (Name, Scope, TokenHash) VALUES (N'decrypt', N'repo:decrypt', 0x<hash>);
-- Revogar: UPDATE mcp.Chat SET RevokedAt = SYSUTCDATETIME() WHERE Name = N'decrypt';
-- =============================================================================
IF SCHEMA_ID(N'mcp') IS NULL EXEC(N'CREATE SCHEMA mcp');
GO

IF OBJECT_ID(N'mcp.Chat', N'U') IS NULL
CREATE TABLE mcp.Chat (
    Id         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_mcp_Chat PRIMARY KEY,
    Name       NVARCHAR(64)  NOT NULL,
    Scope      NVARCHAR(128) NULL,
    TokenHash  VARBINARY(32) NOT NULL,
    CreatedAt  DATETIME2(3)  NOT NULL CONSTRAINT DF_mcp_Chat_CreatedAt DEFAULT SYSUTCDATETIME(),
    RevokedAt  DATETIME2(3)  NULL,
    LastSeenAt DATETIME2(3)  NULL,
    CONSTRAINT UQ_mcp_Chat_Name UNIQUE (Name),
    CONSTRAINT UQ_mcp_Chat_TokenHash UNIQUE (TokenHash)
);
GO

IF OBJECT_ID(N'mcp.Message', N'U') IS NULL
CREATE TABLE mcp.Message (
    Id         UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_mcp_Message PRIMARY KEY,
    ThreadId   UNIQUEIDENTIFIER NOT NULL,
    FromChatId INT NOT NULL CONSTRAINT FK_mcp_Message_From REFERENCES mcp.Chat(Id),
    ToChatId   INT NULL CONSTRAINT FK_mcp_Message_To REFERENCES mcp.Chat(Id),
    Channel    NVARCHAR(40) NULL,
    Kind       NVARCHAR(16) NOT NULL,
    Subject    NVARCHAR(200) NOT NULL,
    Body       NVARCHAR(MAX) NOT NULL,
    RefsJson   NVARCHAR(MAX) NULL,
    CreatedAt  DATETIME2(3) NOT NULL CONSTRAINT DF_mcp_Message_CreatedAt DEFAULT SYSUTCDATETIME(),
    -- Destino é chat OU canal, nunca os dois nem nenhum.
    CONSTRAINT CK_mcp_Message_Dest CHECK ((ToChatId IS NULL AND Channel IS NOT NULL) OR (ToChatId IS NOT NULL AND Channel IS NULL)),
    CONSTRAINT CK_mcp_Message_Kind CHECK (Kind IN (N'info', N'question', N'request', N'handoff'))
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_mcp_Message_To' AND object_id = OBJECT_ID(N'mcp.Message'))
    CREATE INDEX IX_mcp_Message_To ON mcp.Message (ToChatId, CreatedAt) INCLUDE (ThreadId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_mcp_Message_Channel' AND object_id = OBJECT_ID(N'mcp.Message'))
    CREATE INDEX IX_mcp_Message_Channel ON mcp.Message (Channel, CreatedAt);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_mcp_Message_Thread' AND object_id = OBJECT_ID(N'mcp.Message'))
    CREATE INDEX IX_mcp_Message_Thread ON mcp.Message (ThreadId, CreatedAt);
GO

IF OBJECT_ID(N'mcp.MessageAck', N'U') IS NULL
CREATE TABLE mcp.MessageAck (
    MessageId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_mcp_Ack_Message REFERENCES mcp.Message(Id),
    ChatId    INT NOT NULL CONSTRAINT FK_mcp_Ack_Chat REFERENCES mcp.Chat(Id),
    AckedAt   DATETIME2(3) NOT NULL CONSTRAINT DF_mcp_Ack_AckedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_mcp_MessageAck PRIMARY KEY (MessageId, ChatId)
);
GO

IF OBJECT_ID(N'mcp.Audit', N'U') IS NULL
CREATE TABLE mcp.Audit (
    Id      BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_mcp_Audit PRIMARY KEY,
    ChatId  INT NULL,
    Tool    NVARCHAR(64) NOT NULL,
    At      DATETIME2(3) NOT NULL CONSTRAINT DF_mcp_Audit_At DEFAULT SYSUTCDATETIME(),
    Outcome NVARCHAR(200) NOT NULL   -- ex.: ok, rejeitado:segredo (nunca conteúdo de mensagem)
);
GO
