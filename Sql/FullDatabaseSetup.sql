-- =====================================================================
-- task_list - Tam veritabani kurulum scripti
-- appsettings.json -> ConnectionStrings:DefaultConnection ile eslesir:
--   Server=OZKAN\SQLEXPRESS; Database=DestekMailTakipDb
--
-- Bu script SQL Server Management Studio / Azure Data Studio / sqlcmd
-- ile OZKAN\SQLEXPRESS sunucusuna baglanip calistirilir. Baştan sona
-- tekrar tekrar calistirilabilir (idempotent).
-- =====================================================================

IF DB_ID(N'DestekMailTakipDb') IS NULL
BEGIN
    CREATE DATABASE [DestekMailTakipDb];
END;
GO

USE [DestekMailTakipDb];
GO

SET QUOTED_IDENTIFIER ON;
GO

-- ---------------------------------------------------------------------
-- 1) ASP.NET Core Identity tablolari
--    (dotnet ef migrations script --idempotent ile uretilmistir,
--     Data/Migrations/20260721062901_InitialIdentity.cs migration'ina karsilik gelir)
-- ---------------------------------------------------------------------

IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721062901_InitialIdentity'
)
BEGIN
    CREATE TABLE [AspNetRoles] (
        [Id] nvarchar(450) NOT NULL,
        [Name] nvarchar(256) NULL,
        [NormalizedName] nvarchar(256) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721062901_InitialIdentity'
)
BEGIN
    CREATE TABLE [AspNetUsers] (
        [Id] nvarchar(450) NOT NULL,
        [DisplayName] nvarchar(max) NOT NULL,
        [UserName] nvarchar(256) NULL,
        [NormalizedUserName] nvarchar(256) NULL,
        [Email] nvarchar(256) NULL,
        [NormalizedEmail] nvarchar(256) NULL,
        [EmailConfirmed] bit NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [SecurityStamp] nvarchar(max) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(max) NULL,
        [PhoneNumberConfirmed] bit NOT NULL,
        [TwoFactorEnabled] bit NOT NULL,
        [LockoutEnd] datetimeoffset NULL,
        [LockoutEnabled] bit NOT NULL,
        [AccessFailedCount] int NOT NULL,
        CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721062901_InitialIdentity'
)
BEGIN
    CREATE TABLE [AspNetRoleClaims] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721062901_InitialIdentity'
)
BEGIN
    CREATE TABLE [AspNetUserClaims] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721062901_InitialIdentity'
)
BEGIN
    CREATE TABLE [AspNetUserLogins] (
        [LoginProvider] nvarchar(450) NOT NULL,
        [ProviderKey] nvarchar(450) NOT NULL,
        [ProviderDisplayName] nvarchar(max) NULL,
        [UserId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721062901_InitialIdentity'
)
BEGIN
    CREATE TABLE [AspNetUserRoles] (
        [UserId] nvarchar(450) NOT NULL,
        [RoleId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721062901_InitialIdentity'
)
BEGIN
    CREATE TABLE [AspNetUserTokens] (
        [UserId] nvarchar(450) NOT NULL,
        [LoginProvider] nvarchar(450) NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721062901_InitialIdentity'
)
BEGIN
    CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721062901_InitialIdentity'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721062901_InitialIdentity'
)
BEGIN
    CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721062901_InitialIdentity'
)
BEGIN
    CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721062901_InitialIdentity'
)
BEGIN
    CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721062901_InitialIdentity'
)
BEGIN
    CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721062901_InitialIdentity'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721062901_InitialIdentity'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260721062901_InitialIdentity', N'8.0.11');
END;
GO

-- Rozet (avatar) rengi: her kullaniciya tekil bir renk indeksi. -1 = henuz
-- atanmadi; uygulama acilista IUserAvatarColorService.BackfillAsync ile bos
-- olan en kucuk indeksleri dagitir. Renk 8'li paletten okunur (AvatarPalette),
-- yani ilk 8 mühendis farkli renk alir, sonrasinda palet basa doner.
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811063000_AddUserAvatarColorIndex'
)
BEGIN
    IF COL_LENGTH('dbo.AspNetUsers', 'AvatarColorIndex') IS NULL
    BEGIN
        ALTER TABLE [AspNetUsers] ADD [AvatarColorIndex] int NOT NULL DEFAULT -1;
    END;

    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260811063000_AddUserAvatarColorIndex', N'8.0.11');
END;
GO

COMMIT;
GO

-- ---------------------------------------------------------------------
-- 2) Mail entegrasyonu tablolari (ADO.NET ile MailRepository uzerinden yonetilir)
-- ---------------------------------------------------------------------

IF OBJECT_ID('dbo.Mails', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Mails
    (
        Id              INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ImapUid         NVARCHAR(64)      NOT NULL,
        MessageId       NVARCHAR(998)     NULL,
        FromAddress     NVARCHAR(320)     NOT NULL,
        FromName        NVARCHAR(256)     NULL,
        Subject         NVARCHAR(998)     NULL,
        BodyText        NVARCHAR(MAX)     NULL,
        BodyHtml        NVARCHAR(MAX)     NULL,
        ReceivedAt      DATETIMEOFFSET    NOT NULL,
        IsRead          BIT               NOT NULL DEFAULT 0,
        IsFlagged       BIT               NOT NULL DEFAULT 0,
        FlaggedByUserId NVARCHAR(450)     NULL,
        FlaggedAt       DATETIMEOFFSET    NULL,
        CONSTRAINT UQ_Mails_ImapUid UNIQUE (ImapUid)
    );
END;
GO

IF OBJECT_ID('dbo.MailAttachments', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MailAttachments
    (
        Id            INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        MailMessageId INT               NOT NULL,
        FileName      NVARCHAR(260)     NOT NULL,
        FilePath      NVARCHAR(400)     NOT NULL,
        ContentType   NVARCHAR(150)     NULL,
        IsImage       BIT               NOT NULL DEFAULT 0,
        CONSTRAINT FK_MailAttachments_Mails FOREIGN KEY (MailMessageId)
            REFERENCES dbo.Mails (Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_MailAttachments_MailMessageId ON dbo.MailAttachments (MailMessageId);
END;
GO

IF OBJECT_ID('dbo.MailReplies', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MailReplies
    (
        Id            INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        MailMessageId INT               NOT NULL,
        Body          NVARCHAR(MAX)     NOT NULL,
        SentByUserId  NVARCHAR(450)     NOT NULL,
        SentAt        DATETIMEOFFSET    NOT NULL,
        CONSTRAINT FK_MailReplies_Mails FOREIGN KEY (MailMessageId)
            REFERENCES dbo.Mails (Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_MailReplies_MailMessageId ON dbo.MailReplies (MailMessageId);
END;
GO

IF OBJECT_ID('dbo.MailReplyAttachments', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MailReplyAttachments
    (
        Id           INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        MailReplyId  INT               NOT NULL,
        FileName     NVARCHAR(260)     NOT NULL,
        FilePath     NVARCHAR(400)     NOT NULL,
        ContentType  NVARCHAR(150)     NULL,
        IsImage      BIT               NOT NULL DEFAULT 0,
        CONSTRAINT FK_MailReplyAttachments_MailReplies FOREIGN KEY (MailReplyId)
            REFERENCES dbo.MailReplies (Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_MailReplyAttachments_MailReplyId ON dbo.MailReplyAttachments (MailReplyId);
END;
GO

-- ---------------------------------------------------------------------
-- 3) Atama / durum / taslak alanlari (Help Scout tarzi klasorler icin)
-- ---------------------------------------------------------------------

IF COL_LENGTH('dbo.Mails', 'AssignedToUserId') IS NULL
BEGIN
    ALTER TABLE dbo.Mails ADD
        AssignedToUserId     NVARCHAR(450)  NULL,
        AssignedAt           DATETIMEOFFSET NULL,
        IsClosed             BIT            NOT NULL CONSTRAINT DF_Mails_IsClosed DEFAULT 0,
        ClosedAt             DATETIMEOFFSET NULL,
        IsSpam               BIT            NOT NULL CONSTRAINT DF_Mails_IsSpam DEFAULT 0,
        DraftBody            NVARCHAR(MAX)  NULL,
        DraftUpdatedByUserId NVARCHAR(450)  NULL,
        DraftUpdatedAt       DATETIMEOFFSET NULL;

    CREATE INDEX IX_Mails_AssignedToUserId ON dbo.Mails (AssignedToUserId);
END;
GO

-- ---------------------------------------------------------------------
-- 4) Bagimsiz taslak sablonlari (Taslaklar bolumunde olusturulup
--    herhangi bir yanita eklenebilen serbest metin sablonlari)
-- ---------------------------------------------------------------------

IF OBJECT_ID('dbo.MailDraftTemplates', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MailDraftTemplates
    (
        Id              INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Title           NVARCHAR(200)     NOT NULL,
        Body            NVARCHAR(MAX)     NOT NULL,
        CreatedByUserId NVARCHAR(450)     NOT NULL,
        CreatedAt       DATETIMEOFFSET    NOT NULL,
        UpdatedByUserId NVARCHAR(450)     NULL,
        UpdatedAt       DATETIMEOFFSET    NULL,
        IsPrivate       BIT               NOT NULL CONSTRAINT DF_MailDraftTemplates_IsPrivate DEFAULT 0
    );
END;
GO

IF COL_LENGTH('dbo.MailDraftTemplates', 'IsPrivate') IS NULL
BEGIN
    ALTER TABLE dbo.MailDraftTemplates ADD
        IsPrivate BIT NOT NULL CONSTRAINT DF_MailDraftTemplates_IsPrivate DEFAULT 0;
END;
GO

-- ---------------------------------------------------------------------
-- 5) Kim kapatti (Kapatilmis klasorunde "sadece benim" filtresi icin)
-- ---------------------------------------------------------------------

IF COL_LENGTH('dbo.Mails', 'ClosedByUserId') IS NULL
BEGIN
    ALTER TABLE dbo.Mails ADD
        ClosedByUserId NVARCHAR(450) NULL;
END;
GO

-- ---------------------------------------------------------------------
-- 6) Coklu atama: bir mail birden fazla calisana atanabilir.
--    Eski tekil Mails.AssignedToUserId kolonu geriye donuk uyumluluk
--    icin dokunulmadan birakildi, ancak Unassigned/Assigned/Mine klasor
--    mantigi artik bu tabloya gore calisiyor.
-- ---------------------------------------------------------------------

IF OBJECT_ID('dbo.MailAssignments', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MailAssignments
    (
        MailId               INT            NOT NULL,
        UserId               NVARCHAR(450)  NOT NULL,
        AssignedAt           DATETIMEOFFSET NOT NULL,
        AssignedByUserId     NVARCHAR(450)  NULL,
        AssignedByDisplayName NVARCHAR(256) NULL,
        CONSTRAINT PK_MailAssignments PRIMARY KEY (MailId, UserId),
        CONSTRAINT FK_MailAssignments_Mails FOREIGN KEY (MailId)
            REFERENCES dbo.Mails (Id) ON DELETE CASCADE,
        CONSTRAINT FK_MailAssignments_AspNetUsers FOREIGN KEY (UserId)
            REFERENCES dbo.AspNetUsers (Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_MailAssignments_UserId ON dbo.MailAssignments (UserId);
END;
GO

-- ---------------------------------------------------------------------
-- 7) Yonetici -> calisan mesajlari (genel yayin veya hedefli)
-- ---------------------------------------------------------------------

IF OBJECT_ID('dbo.Messages', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Messages
    (
        Id                    INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        SenderUserId          NVARCHAR(450)  NULL,
        SenderDisplayName     NVARCHAR(256)  NULL,
        Body                  NVARCHAR(MAX)  NOT NULL,
        CreatedAt             DATETIMEOFFSET NOT NULL,
        RecipientUserId       NVARCHAR(450)  NULL,
        RecipientDisplayName  NVARCHAR(256)  NULL
    );

    CREATE INDEX IX_Messages_RecipientUserId ON dbo.Messages (RecipientUserId);
END;
GO

IF OBJECT_ID('dbo.MessageReads', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MessageReads
    (
        MessageId INT           NOT NULL,
        UserId    NVARCHAR(450) NOT NULL,
        ReadAt    DATETIMEOFFSET NOT NULL,
        CONSTRAINT PK_MessageReads PRIMARY KEY (MessageId, UserId),
        CONSTRAINT FK_MessageReads_Messages FOREIGN KEY (MessageId)
            REFERENCES dbo.Messages (Id) ON DELETE CASCADE
    );
END;
GO

-- ---------------------------------------------------------------------
-- 8) Onayli kapatma akisi: atanan tum calisanlar "Kapat"a basana kadar
--    mail kapanmaz; hepsi bastiginda yoneticiye onay bildirimi gider,
--    yonetici onaylayana kadar mail acik kalir.
-- ---------------------------------------------------------------------

IF OBJECT_ID('dbo.MailCloseVotes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MailCloseVotes
    (
        MailId  INT           NOT NULL,
        UserId  NVARCHAR(450) NOT NULL,
        VotedAt DATETIMEOFFSET NOT NULL,
        CONSTRAINT PK_MailCloseVotes PRIMARY KEY (MailId, UserId),
        CONSTRAINT FK_MailCloseVotes_Mails FOREIGN KEY (MailId)
            REFERENCES dbo.Mails (Id) ON DELETE CASCADE
    );
END;
GO

IF OBJECT_ID('dbo.MailCloseRequests', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MailCloseRequests
    (
        MailId      INT NOT NULL PRIMARY KEY,
        RequestedAt DATETIMEOFFSET NOT NULL,
        CONSTRAINT FK_MailCloseRequests_Mails FOREIGN KEY (MailId)
            REFERENCES dbo.Mails (Id) ON DELETE CASCADE
    );
END;
GO

-- ---------------------------------------------------------------------
-- 9) "Uzerime Al" -> yonetici onay akisi (tek kullanicilik istek,
--    kapatma onayindaki gibi ama oybirligi gerektirmiyor).
-- ---------------------------------------------------------------------

IF OBJECT_ID('dbo.MailAssignmentRequests', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MailAssignmentRequests
    (
        MailId      INT           NOT NULL,
        UserId      NVARCHAR(450) NOT NULL,
        RequestedAt DATETIMEOFFSET NOT NULL,
        CONSTRAINT PK_MailAssignmentRequests PRIMARY KEY (MailId, UserId),
        CONSTRAINT FK_MailAssignmentRequests_Mails FOREIGN KEY (MailId)
            REFERENCES dbo.Mails (Id) ON DELETE CASCADE
    );
END;
GO

-- ---------------------------------------------------------------------
-- 11) Gorev adi: yonetici bir maili task'a cevirirken (atarken) bir isim
--     belirleyebilir; calisan "uzerime al" isterken bir isim onerebilir.
-- ---------------------------------------------------------------------

IF COL_LENGTH('dbo.Mails', 'TaskName') IS NULL
BEGIN
    ALTER TABLE dbo.Mails ADD
        TaskName NVARCHAR(200) NULL;
END;
GO

IF COL_LENGTH('dbo.MailAssignmentRequests', 'SuggestedTaskName') IS NULL
BEGIN
    ALTER TABLE dbo.MailAssignmentRequests ADD
        SuggestedTaskName NVARCHAR(200) NULL;
END;
GO

-- ---------------------------------------------------------------------
-- 12) "Birakma" onay akisi: bir calisan artik kendisine atanmis bir gorevi
--     dogrudan birakamaz, yoneticiye istek gonderir (kapatma/atanma
--     onay akislariyla ayni desen).
-- ---------------------------------------------------------------------

IF OBJECT_ID('dbo.MailUnassignmentRequests', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MailUnassignmentRequests
    (
        MailId      INT           NOT NULL,
        UserId      NVARCHAR(450) NOT NULL,
        RequestedAt DATETIMEOFFSET NOT NULL,
        CONSTRAINT PK_MailUnassignmentRequests PRIMARY KEY (MailId, UserId),
        CONSTRAINT FK_MailUnassignmentRequests_Mails FOREIGN KEY (MailId)
            REFERENCES dbo.Mails (Id) ON DELETE CASCADE
    );
END;
GO

-- ---------------------------------------------------------------------
-- 10) Mail-thread takibi: MailReplies artik hem giden (calisan) hem
--     gelen (musteri, IMAP ile eslesen) mesajlari tutabiliyor.
-- ---------------------------------------------------------------------

IF COLUMNPROPERTY(OBJECT_ID('dbo.MailReplies'), 'SentByUserId', 'AllowsNull') = 0
BEGIN
    ALTER TABLE dbo.MailReplies ALTER COLUMN SentByUserId NVARCHAR(450) NULL;
END;
GO

IF COL_LENGTH('dbo.MailReplies', 'IsInbound') IS NULL
BEGIN
    ALTER TABLE dbo.MailReplies ADD
        IsInbound   BIT            NOT NULL CONSTRAINT DF_MailReplies_IsInbound DEFAULT 0,
        FromAddress NVARCHAR(320)  NULL,
        FromName    NVARCHAR(256)  NULL,
        ImapUid     NVARCHAR(64)   NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_MailReplies_ImapUid' AND object_id = OBJECT_ID('dbo.MailReplies'))
BEGIN
    CREATE UNIQUE INDEX UQ_MailReplies_ImapUid ON dbo.MailReplies (ImapUid) WHERE ImapUid IS NOT NULL;
END;
GO

-- ---------------------------------------------------------------------
-- 13) Mail govdesi uzerine kullaniciya ozel cizim/isaretleme (kalem,
--     isaretleyici). Sadece cizimi yapan kullaniciya gorunur, mail+kullanici
--     basina tek satir (upsert).
-- ---------------------------------------------------------------------

IF OBJECT_ID('dbo.MailAnnotations', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MailAnnotations
    (
        MailId      INT             NOT NULL,
        UserId      NVARCHAR(450)   NOT NULL,
        StrokesJson NVARCHAR(MAX)   NOT NULL,
        UpdatedAt   DATETIMEOFFSET  NOT NULL,
        CONSTRAINT PK_MailAnnotations PRIMARY KEY (MailId, UserId),
        CONSTRAINT FK_MailAnnotations_Mails FOREIGN KEY (MailId)
            REFERENCES dbo.Mails (Id) ON DELETE CASCADE
    );
END;
GO

-- ---------------------------------------------------------------------
-- 14) Kullaniciya ozel klasorler ve arsiv. Bir mail'i bir klasore/arsive
--     eklemek etiket gibi calisir; mail Unassigned/Mine/Assigned gibi
--     diger klasorlerden kaybolmaz, sadece o kullanicinin gorunumune
--     ek bir etiket eklenir.
-- ---------------------------------------------------------------------

IF OBJECT_ID('dbo.MailUserFolders', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MailUserFolders
    (
        Id        INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        UserId    NVARCHAR(450)     NOT NULL,
        Name      NVARCHAR(200)     NOT NULL,
        CreatedAt DATETIMEOFFSET    NOT NULL,
        CONSTRAINT UQ_MailUserFolders_UserId_Name UNIQUE (UserId, Name)
    );
END;
GO

IF OBJECT_ID('dbo.MailFolderItems', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MailFolderItems
    (
        FolderId INT            NOT NULL,
        MailId   INT            NOT NULL,
        AddedAt  DATETIMEOFFSET NOT NULL,
        CONSTRAINT PK_MailFolderItems PRIMARY KEY (FolderId, MailId),
        CONSTRAINT FK_MailFolderItems_Folder FOREIGN KEY (FolderId)
            REFERENCES dbo.MailUserFolders (Id) ON DELETE CASCADE,
        CONSTRAINT FK_MailFolderItems_Mail FOREIGN KEY (MailId)
            REFERENCES dbo.Mails (Id) ON DELETE CASCADE
    );
END;
GO

IF OBJECT_ID('dbo.MailArchives', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MailArchives
    (
        MailId     INT            NOT NULL,
        UserId     NVARCHAR(450)  NOT NULL,
        ArchivedAt DATETIMEOFFSET NOT NULL,
        CONSTRAINT PK_MailArchives PRIMARY KEY (MailId, UserId),
        CONSTRAINT FK_MailArchives_Mails FOREIGN KEY (MailId)
            REFERENCES dbo.Mails (Id) ON DELETE CASCADE
    );
END;
GO

-- ---------------------------------------------------------------------
-- 15) Musteri panolari (Trello benzeri, 3 sabit kolon: Yapilacaklar/Test/
--     Tamamlanan). Pano sahibi ve yetkilendirilmis musteri e-postalari
--     panoyu goruntuleyip Yapilacaklar'a madde ekleyebilir; muhendisler
--     (Employee/Admin) maddeleri ustlerine alip test'e tasir.
-- ---------------------------------------------------------------------

IF OBJECT_ID('dbo.Boards', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Boards
    (
        Id                   INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Title                NVARCHAR(300)      NOT NULL,
        CreatedByUserId      NVARCHAR(450)      NOT NULL,
        CreatedByDisplayName NVARCHAR(256)      NOT NULL,
        CreatedAt            DATETIMEOFFSET     NOT NULL
    );
END;
GO

IF OBJECT_ID('dbo.BoardAuthorizedEmails', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BoardAuthorizedEmails
    (
        Id      INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        BoardId INT               NOT NULL,
        Email   NVARCHAR(256)     NOT NULL,
        AddedAt DATETIMEOFFSET    NOT NULL,
        CONSTRAINT FK_BoardAuthorizedEmails_Boards FOREIGN KEY (BoardId)
            REFERENCES dbo.Boards (Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_BoardAuthorizedEmails_Email ON dbo.BoardAuthorizedEmails (Email);
END;
GO

IF OBJECT_ID('dbo.BoardCards', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BoardCards
    (
        Id                   INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        BoardId              INT             NOT NULL,
        ListKey              NVARCHAR(20)    NOT NULL,
        Title                NVARCHAR(300)   NOT NULL,
        Description          NVARCHAR(MAX)   NULL,
        CreatedByUserId      NVARCHAR(450)   NOT NULL,
        CreatedByDisplayName NVARCHAR(256)   NOT NULL,
        CreatedAt            DATETIMEOFFSET  NOT NULL,
        AssignedUserId       NVARCHAR(450)   NULL,
        AssignedAt           DATETIMEOFFSET  NULL,
        MovedToTestAt        DATETIMEOFFSET  NULL,
        CompletedAt          DATETIMEOFFSET  NULL,
        LastRejectionNote    NVARCHAR(MAX)   NULL,
        LastRejectedAt       DATETIMEOFFSET  NULL,
        RejectedCount        INT             NOT NULL DEFAULT 0,
        CONSTRAINT FK_BoardCards_Boards FOREIGN KEY (BoardId)
            REFERENCES dbo.Boards (Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_BoardCards_BoardId_ListKey ON dbo.BoardCards (BoardId, ListKey);
    CREATE INDEX IX_BoardCards_AssignedUserId ON dbo.BoardCards (AssignedUserId);
END;
GO

-- ---------------------------------------------------------------------
-- 16) Pano kolonlarina (Yapilacaklar/Test/Tamamlanan) ozel arkaplan
--     rengi (Trello listesi rengi benzeri). Kolon sayisi sabit (3) oldugu
--     icin ayri bir tablo yerine Boards uzerinde 3 nullable kolon.
-- ---------------------------------------------------------------------

IF COL_LENGTH('dbo.Boards', 'TodoColor') IS NULL
BEGIN
    ALTER TABLE dbo.Boards ADD
        TodoColor NVARCHAR(20) NULL,
        TestColor NVARCHAR(20) NULL,
        DoneColor NVARCHAR(20) NULL;
END;
GO

-- ---------------------------------------------------------------------
-- 17) Pano etiketleri (Trello label'lari benzeri). Her pano kendi etiket
--     setine sahiptir; kartlar bu etiketlerden birden fazlasini tasiyabilir.
-- ---------------------------------------------------------------------

IF OBJECT_ID('dbo.BoardLabels', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BoardLabels
    (
        Id        INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        BoardId   INT             NOT NULL,
        Name      NVARCHAR(100)   NOT NULL DEFAULT '',
        Color     NVARCHAR(20)    NOT NULL,
        CreatedAt DATETIMEOFFSET  NOT NULL,
        CONSTRAINT FK_BoardLabels_Boards FOREIGN KEY (BoardId)
            REFERENCES dbo.Boards (Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_BoardLabels_BoardId ON dbo.BoardLabels (BoardId);
END;
GO

-- Panonun varsayilan (sari/mor/mavi) etiketleri bir kez seed edilir; bu
-- bayrak, kullanici onlari sildikten sonra tekrar tekrar geri gelmesini
-- (yeniden seed edilmesini) onlemek icin kullanilir.
IF COL_LENGTH('dbo.Boards', 'DefaultLabelsSeeded') IS NULL
BEGIN
    ALTER TABLE dbo.Boards ADD
        DefaultLabelsSeeded BIT NOT NULL CONSTRAINT DF_Boards_DefaultLabelsSeeded DEFAULT 0;
END;
GO

IF OBJECT_ID('dbo.BoardCardLabels', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BoardCardLabels
    (
        CardId  INT NOT NULL,
        LabelId INT NOT NULL,
        AddedAt DATETIMEOFFSET NOT NULL,
        CONSTRAINT PK_BoardCardLabels PRIMARY KEY (CardId, LabelId),
        CONSTRAINT FK_BoardCardLabels_Cards FOREIGN KEY (CardId)
            REFERENCES dbo.BoardCards (Id) ON DELETE CASCADE,
        -- NO ACTION (Boards -> BoardCards -> BoardCardLabels ve Boards -> BoardLabels -> BoardCardLabels
        -- ayni tabloya iki cakisan CASCADE yolu olusturur, SQL Server buna izin vermez).
        -- Etiket silinirken bu satirlar BoardRepository.DeleteLabelAsync icinde elle temizlenir.
        CONSTRAINT FK_BoardCardLabels_Labels FOREIGN KEY (LabelId)
            REFERENCES dbo.BoardLabels (Id)
    );
END;
GO

-- ---------------------------------------------------------------------
-- 18) Kart kapagi (Trello cover benzeri: duz renk veya resim) ve kolon
--     icinde elle siralanabilme (Tasi/Kopyala pencereleri) icin SortOrder.
-- ---------------------------------------------------------------------

IF COL_LENGTH('dbo.BoardCards', 'CoverColor') IS NULL
BEGIN
    ALTER TABLE dbo.BoardCards ADD
        CoverColor     NVARCHAR(20)  NULL,
        CoverImagePath NVARCHAR(400) NULL;
END;
GO

IF COL_LENGTH('dbo.BoardCards', 'SortOrder') IS NULL
BEGIN
    ALTER TABLE dbo.BoardCards ADD
        SortOrder INT NOT NULL CONSTRAINT DF_BoardCards_SortOrder DEFAULT 0;
END;
GO

-- Var olan kartlara, kendi (BoardId, ListKey) grubu icinde olusturulma sirasina
-- gore 0'dan baslayan bir SortOrder ata (geriye donuk, tek seferlik doldurma;
-- herhangi bir kart zaten sifirdan farkli bir SortOrder'a sahipse -yani daha
-- once doldurulmus veya elle siralanmissa- tekrar calismaz).
IF NOT EXISTS (SELECT 1 FROM dbo.BoardCards WHERE SortOrder <> 0)
BEGIN
    ;WITH Ordered AS
    (
        SELECT Id,
               ROW_NUMBER() OVER (PARTITION BY BoardId, ListKey ORDER BY CreatedAt ASC, Id ASC) - 1 AS Seq
        FROM dbo.BoardCards
    )
    UPDATE bc
    SET bc.SortOrder = o.Seq
    FROM dbo.BoardCards bc
    INNER JOIN Ordered o ON o.Id = bc.Id;
END;
GO

-- ---------------------------------------------------------------------
-- 19) Kart arsivi: kullaniciya ozel arsiv kutusu. Arsivlenen kart panoda
--     gorunmez, sadece onu arsivleyen kullanicinin Arsiv Kutusu sayfasinda
--     listelenir; Geri Yukle ile kaldigi listeye geri doner.
-- ---------------------------------------------------------------------

IF COL_LENGTH('dbo.BoardCards', 'IsArchived') IS NULL
BEGIN
    ALTER TABLE dbo.BoardCards ADD
        IsArchived       BIT             NOT NULL CONSTRAINT DF_BoardCards_IsArchived DEFAULT 0,
        ArchivedAt       DATETIMEOFFSET  NULL,
        ArchivedByUserId NVARCHAR(450)   NULL;

    CREATE INDEX IX_BoardCards_ArchivedByUserId ON dbo.BoardCards (ArchivedByUserId);
END;
GO

-- ---------------------------------------------------------------------
-- 20) Kart detay penceresi: eklentiler (baglanti veya dosya) ve yorumlar.
--     BoardCards.Description zaten var (NVARCHAR(MAX)); artik zengin metin
--     kutusundan gelen (sunucuda sanitize edilmis) HTML tutuyor.
-- ---------------------------------------------------------------------

IF OBJECT_ID('dbo.BoardCardAttachments', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BoardCardAttachments
    (
        Id                   INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CardId               INT             NOT NULL,
        AttachmentType       NVARCHAR(10)    NOT NULL, -- 'link' | 'file'
        Url                  NVARCHAR(1000)  NOT NULL,
        FileName             NVARCHAR(300)   NULL,
        CreatedByUserId      NVARCHAR(450)   NOT NULL,
        CreatedByDisplayName NVARCHAR(256)   NOT NULL,
        CreatedAt            DATETIMEOFFSET  NOT NULL,
        CONSTRAINT FK_BoardCardAttachments_Cards FOREIGN KEY (CardId)
            REFERENCES dbo.BoardCards (Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_BoardCardAttachments_CardId ON dbo.BoardCardAttachments (CardId);
END;
GO

IF OBJECT_ID('dbo.BoardCardComments', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BoardCardComments
    (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CardId      INT             NOT NULL,
        UserId      NVARCHAR(450)   NOT NULL,
        DisplayName NVARCHAR(256)   NOT NULL,
        Role        NVARCHAR(50)    NOT NULL,
        Body        NVARCHAR(MAX)   NOT NULL,
        CreatedAt   DATETIMEOFFSET  NOT NULL,
        CONSTRAINT FK_BoardCardComments_Cards FOREIGN KEY (CardId)
            REFERENCES dbo.BoardCards (Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_BoardCardComments_CardId ON dbo.BoardCardComments (CardId);
END;
GO

-- ---------------------------------------------------------------------
-- 21) Yorum tepkileri (WhatsApp benzeri): kullanici basina yorum basina TEK
--     satir - ayni emoji'ye tekrar basmak kaldirir, farkli emoji'ye basmak
--     degistirir (UNIQUE (CommentId, UserId) bunu garanti eder).
-- ---------------------------------------------------------------------

IF OBJECT_ID('dbo.BoardCardCommentReactions', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BoardCardCommentReactions
    (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CommentId   INT             NOT NULL,
        UserId      NVARCHAR(450)   NOT NULL,
        DisplayName NVARCHAR(256)   NOT NULL,
        Emoji       NVARCHAR(20)    NOT NULL,
        CreatedAt   DATETIMEOFFSET  NOT NULL,
        CONSTRAINT FK_BoardCardCommentReactions_Comments FOREIGN KEY (CommentId)
            REFERENCES dbo.BoardCardComments (Id) ON DELETE CASCADE,
        CONSTRAINT UQ_BoardCardCommentReactions_Comment_User UNIQUE (CommentId, UserId)
    );

    CREATE INDEX IX_BoardCardCommentReactions_CommentId ON dbo.BoardCardCommentReactions (CommentId);
END;
GO

-- ---------------------------------------------------------------------
-- 22) Tekli bayrak sistemi (Aktif/Pending/Closed/Spam - eskiden bagimsiz
--     IsFlagged/IsClosed/IsSpam bayraklarinin yerini alir; bir mail'in
--     her zaman TAM OLARAK bir bayragi vardir, bayragi kim koyarsa sadece
--     o kullanici degistirebilir) + kategori belirteci + serbest etiketler.
--     Eski IsFlagged/IsClosed/IsSpam/ClosedBy* kolonlari ve onay/red akisi
--     tablolari (MailCloseVotes/MailCloseRequests/MailAssignmentRequests/
--     MailUnassignmentRequests) geriye donuk bozmamak icin silinmedi, ama
--     artik uygulama kodu tarafindan okunup yazilmiyor.
-- ---------------------------------------------------------------------

IF COL_LENGTH('dbo.Mails', 'FlagType') IS NULL
BEGIN
    ALTER TABLE dbo.Mails ADD
        FlagType         NVARCHAR(20)   NOT NULL CONSTRAINT DF_Mails_FlagType DEFAULT 'active',
        FlagSetByUserId  NVARCHAR(450)  NULL,
        FlagSetAt        DATETIMEOFFSET NULL,
        CategoryColorKey NVARCHAR(20)   NULL;
END;
GO

-- Var olan verilerden geriye donuk taniyorum: kapatilmis/spam olarak
-- isaretli mailler yeni sistemde de ayni durumla baslasin. Iki kez
-- calistirilmasi zararsizdir (deger degismez).
UPDATE dbo.Mails SET FlagType = 'closed', FlagSetByUserId = ClosedByUserId, FlagSetAt = ClosedAt WHERE IsClosed = 1;
UPDATE dbo.Mails SET FlagType = 'spam' WHERE IsSpam = 1 AND FlagType = 'active';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Mails_FlagType' AND object_id = OBJECT_ID('dbo.Mails'))
BEGIN
    CREATE INDEX IX_Mails_FlagType ON dbo.Mails (FlagType);
END;
GO

IF OBJECT_ID('dbo.MailTags', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MailTags
    (
        Id              INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        MailId          INT            NOT NULL,
        Text            NVARCHAR(60)   NOT NULL,
        ColorKey        NVARCHAR(20)   NOT NULL,
        CreatedByUserId NVARCHAR(450)  NULL,
        CreatedAt       DATETIMEOFFSET NOT NULL,
        CONSTRAINT FK_MailTags_Mails FOREIGN KEY (MailId)
            REFERENCES dbo.Mails (Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_MailTags_MailId ON dbo.MailTags (MailId);
END;
GO

-- ---------------------------------------------------------------------
-- 23) Pano sablonlari: Klasik (Yapilacaklar/Test/Tamamlanan) artik
--     birden fazla secenekten biri. Sablonlarin liste/gecis kurallari
--     kod tarafinda (Models/BoardTemplateModels.cs) tanimli sabit
--     referans veridir; burada yalnizca panonun hangi sablonu sectigi
--     ve (yalnizca "Software Development" sablonu icin) aktif sprint
--     turu/kartin ait oldugu tur/onay durumu saklanir.
-- ---------------------------------------------------------------------

IF COL_LENGTH('dbo.Boards', 'TemplateKey') IS NULL
BEGIN
    ALTER TABLE dbo.Boards ADD
        TemplateKey NVARCHAR(60) NOT NULL CONSTRAINT DF_Boards_TemplateKey DEFAULT 'klasik';
END;
GO

IF COL_LENGTH('dbo.Boards', 'CurrentSprintRound') IS NULL
BEGIN
    ALTER TABLE dbo.Boards ADD
        CurrentSprintRound INT NOT NULL CONSTRAINT DF_Boards_CurrentSprintRound DEFAULT 1;
END;
GO

IF COL_LENGTH('dbo.BoardCards', 'SprintRound') IS NULL
BEGIN
    ALTER TABLE dbo.BoardCards ADD
        SprintRound INT NOT NULL CONSTRAINT DF_BoardCards_SprintRound DEFAULT 1;
END;
GO

IF COL_LENGTH('dbo.BoardCards', 'ApprovalStatus') IS NULL
BEGIN
    ALTER TABLE dbo.BoardCards ADD
        ApprovalStatus NVARCHAR(20) NULL;
END;
GO

-- Klasik disi sablonlarda liste sayisi/anahtari degisken oldugu icin (Boards
-- uzerindeki sabit TodoColor/TestColor/DoneColor kolonlari gibi degil) her
-- (BoardId, ListKey) çifti icin ayri bir satirda kolon arkaplan rengi tutulur.
-- Sadece renk secilmis (varsayilan disi) listeler icin satir bulunur.
IF OBJECT_ID('dbo.BoardListColors', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BoardListColors
    (
        BoardId INT           NOT NULL,
        ListKey NVARCHAR(60)  NOT NULL,
        Color   NVARCHAR(20)  NOT NULL,
        CONSTRAINT PK_BoardListColors PRIMARY KEY (BoardId, ListKey),
        CONSTRAINT FK_BoardListColors_Boards FOREIGN KEY (BoardId)
            REFERENCES dbo.Boards (Id) ON DELETE CASCADE
    );
END;
GO

-- BoardCards.ListKey ilk basta NVARCHAR(20) olarak tanimlanmisti (Klasik'in
-- "todo"/"test"/"done" gibi kisa anahtarlari icin yeterliydi). Klasik disi
-- sablonlarin liste anahtarlari daha uzun oldugu icin (orn.
-- "requirement-discussion" = 23 karakter) genisletiliyor; index'i olan bir
-- kolon oldugu icin ALTER COLUMN dogrudan calisir (veri kaybi olmaz, sadece buyur).
IF EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'BoardCards' AND COLUMN_NAME = 'ListKey' AND CHARACTER_MAXIMUM_LENGTH < 60
)
BEGIN
    ALTER TABLE dbo.BoardCards ALTER COLUMN ListKey NVARCHAR(60) NOT NULL;
END;
GO

-- ---------------------------------------------------------------------
-- 18) Sablon onizleme panolari: musteri "Pano Sablonlarim" bolumunden bir
--     sablonu onizlediginde gercek bir Boards satiri olusturulur (boylece
--     mevcut pano detay sayfasi/surukle-birak/kart ozellikleri aynen
--     calisir) ama IsPreview=1 ile isaretlenir; boyle panolar normal pano
--     listelerinde/istatistiklerde/bildirimlerde hic gorunmez ve
--     "Onizlemeyi Durdur" ile (ya da ayni sablon icin yeniden baslatildiginda)
--     tamamen silinir.
-- ---------------------------------------------------------------------

IF COL_LENGTH('dbo.Boards', 'IsPreview') IS NULL
BEGIN
    ALTER TABLE dbo.Boards ADD
        IsPreview BIT NOT NULL CONSTRAINT DF_Boards_IsPreview DEFAULT 0;
END;
GO

-- Bir musteri, ayni sablon icin ayni anda birden fazla aktif onizleme
-- panosuna sahip olamaz (StartPreview eskisini silip yenisini olusturur;
-- bu index sadece guvenlik agi).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Boards_PreviewOwner_Template')
BEGIN
    CREATE UNIQUE INDEX UX_Boards_PreviewOwner_Template
        ON dbo.Boards (CreatedByUserId, TemplateKey)
        WHERE IsPreview = 1;
END;
GO

-- ---------------------------------------------------------------------
-- 19) Bir mühendisin "üstüne aldigi" karti "birakabilmesi" (ReleaseFromMe)
--     icin: kartin ilk kez uzerine alindigi andaki ListKey'i ayrica saklanir.
--     Birakma islemi sadece kart hala o listedeyken (ListKey = AssignedListKey)
--     mumkundur; kart sonraki bir listeye tasindiktan sonra bu ikisi
--     birbirinden farklilasir ve birakma secenegi kalici olarak devre disi kalir.
-- ---------------------------------------------------------------------

IF COL_LENGTH('dbo.BoardCards', 'AssignedListKey') IS NULL
BEGIN
    ALTER TABLE dbo.BoardCards ADD
        AssignedListKey NVARCHAR(60) NULL;
END;
GO

-- ---------------------------------------------------------------------
-- 20) Bir mail (uygulama icinde) silindiginde, IMAP senkronizasyonu
--     (ImapMailService.SyncAsync) sadece dbo.Mails/dbo.MailReplies'te
--     halihazirda VAR OLAN UID'leri "zaten var" sayip atliyordu; silinen bir
--     mailin UID'i artik tabloda olmadigi icin bir sonraki senkronizasyonda
--     gercek posta kutusundan aynen tekrar cekilip "yeniden geliyormus" gibi
--     goruluyordu. Silinen UID'ler burada kalici olarak (mezar tasi/tombstone
--     olarak) tutulur ve GetExistingImapUidsAsync bunlari da "zaten var"
--     sayarak sync'in bir daha getirmesini engeller.
-- ---------------------------------------------------------------------

IF OBJECT_ID('dbo.DeletedMailImapUids', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DeletedMailImapUids
    (
        ImapUid   NVARCHAR(64)   NOT NULL PRIMARY KEY,
        DeletedAt DATETIMEOFFSET NOT NULL
    );
END;
GO

-- ---------------------------------------------------------------------
-- 21) Mühendis "İstatistiklerim" sayfası (profil menüsündeki rozet):
--     - EngineerStatEvents: "gördükten 4 saat sonra sayacı etkilemeyen"
--       decay'li sayaçlar (kapatılmış görevler, yeni atananlar, kart
--       onay/red, yeni etiket/yorum) için ortak olay günlüğü. Bir satır
--       SeenAt IS NULL veya SeenAt üzerinden 4 saatten az geçmişse sayaca
--       dahil edilir (bkz. StatsRepository).
--     - MailReplySeen: "görevlerime ait mesajlar" sayacı için canlı
--       (decay'siz) "bu maili en son ne zaman gördüm" işaretçisi.
--     - EngineerNotes: İstatistiklerim sayfasının alt kısmındaki kullanıcıya
--       özel not alanı.
-- ---------------------------------------------------------------------

IF OBJECT_ID('dbo.EngineerStatEvents', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EngineerStatEvents
    (
        Id         INT IDENTITY PRIMARY KEY,
        UserId     NVARCHAR(450)  NOT NULL,
        StatKey    NVARCHAR(40)   NOT NULL,
        EntityType NVARCHAR(20)   NOT NULL,
        EntityId   INT            NOT NULL,
        BoardId    INT            NULL,
        ExtraId    INT            NULL,
        EventAt    DATETIMEOFFSET NOT NULL,
        SeenAt     DATETIMEOFFSET NULL,
        CONSTRAINT FK_EngineerStatEvents_User FOREIGN KEY (UserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_EngineerStatEvents_User_Stat_Seen ON dbo.EngineerStatEvents (UserId, StatKey, SeenAt);
END;
GO

IF OBJECT_ID('dbo.MailReplySeen', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MailReplySeen
    (
        MailId     INT            NOT NULL,
        UserId     NVARCHAR(450)  NOT NULL,
        LastSeenAt DATETIMEOFFSET NOT NULL,
        CONSTRAINT PK_MailReplySeen PRIMARY KEY (MailId, UserId),
        CONSTRAINT FK_MailReplySeen_Mail FOREIGN KEY (MailId) REFERENCES dbo.Mails(Id) ON DELETE CASCADE,
        CONSTRAINT FK_MailReplySeen_User FOREIGN KEY (UserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE
    );
END;
GO

IF OBJECT_ID('dbo.EngineerNotes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EngineerNotes
    (
        Id        INT IDENTITY PRIMARY KEY,
        UserId    NVARCHAR(450)  NOT NULL,
        Body      NVARCHAR(2000) NOT NULL,
        CreatedAt DATETIMEOFFSET NOT NULL,
        SortOrder INT            NOT NULL CONSTRAINT DF_EngineerNotes_SortOrder DEFAULT 0,
        CONSTRAINT FK_EngineerNotes_User FOREIGN KEY (UserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_EngineerNotes_User ON dbo.EngineerNotes (UserId, SortOrder ASC);
END;
GO

-- Var olan kurulumlarda (tablo SortOrder eklenmeden once olusturulduysa) sutunu
-- sonradan ekle ve mevcut notlari, o ana kadarki gorunum sirasina (CreatedAt DESC,
-- yani en yeni en ustte) esdeger bir SortOrder ile geriye donuk doldur.
IF COL_LENGTH('dbo.EngineerNotes', 'SortOrder') IS NULL
BEGIN
    ALTER TABLE dbo.EngineerNotes ADD
        SortOrder INT NOT NULL CONSTRAINT DF_EngineerNotes_SortOrder DEFAULT 0;
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.EngineerNotes WHERE SortOrder <> 0)
BEGIN
    ;WITH Ordered AS
    (
        SELECT Id,
               ROW_NUMBER() OVER (PARTITION BY UserId ORDER BY CreatedAt DESC, Id DESC) - 1 AS Seq
        FROM dbo.EngineerNotes
    )
    UPDATE n
    SET n.SortOrder = o.Seq
    FROM dbo.EngineerNotes n
    INNER JOIN Ordered o ON o.Id = n.Id;
END;
GO

-- ---------------------------------------------------------------------
-- 22) Etiketler artik pano genelinde paylasilan kayitlar degil, tamamen
--     karta ozgudur (bir etiket her zaman tek bir karta aittir; baska bir
--     kartin etiket sayfasinda hic gorunmez, duzenlemesi/silinmesi sadece
--     kendi kartini etkiler). dbo.BoardCardLabels join tablosu artik
--     kullanilmiyor (yeni kod dogrudan BoardLabels.CardId'yi okur/yazar);
--     geriye donuk uyumluluk icin tabloya dokunulmadi, sadece bos birakildi.
-- ---------------------------------------------------------------------

IF COL_LENGTH('dbo.BoardLabels', 'CardId') IS NULL
BEGIN
    ALTER TABLE dbo.BoardLabels ADD CardId INT NULL;
END;
GO

-- ON DELETE CASCADE burada mumkun degil (Boards -> BoardLabels dogrudan VE
-- Boards -> BoardCards -> BoardLabels dolayli olmak uzere birden fazla cascade
-- yoluna yol acar, SQL Server bunu reddeder). Bunun yerine NO ACTION kullanilir;
-- bir kart silinirken (DeleteCardAsync) etiketleri ayni islemde elle silinir.
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_BoardLabels_Card')
BEGIN
    ALTER TABLE dbo.BoardLabels WITH CHECK ADD CONSTRAINT FK_BoardLabels_Card
        FOREIGN KEY (CardId) REFERENCES dbo.BoardCards(Id) ON DELETE NO ACTION;
END;
GO

-- Var olan (eski, pano genelinde paylasilan) etiketleri karta-ozgu modele
-- tasir: bir etiket birden fazla karta atanmissa, ilk kart etiketin kendisini
-- devralir; digerleri icin ayni ad/renkle bagimsiz kopyalar olusturulur.
-- Hicbir karta atanmamis (hic kullanilmamis) etiketler temizlenir.
IF EXISTS (SELECT 1 FROM dbo.BoardLabels WHERE CardId IS NULL)
BEGIN
    DECLARE @MigrationNow DATETIMEOFFSET = SYSDATETIMEOFFSET();

    ;WITH Ranked AS (
        SELECT LabelId, CardId, ROW_NUMBER() OVER (PARTITION BY LabelId ORDER BY CardId) AS rn
        FROM dbo.BoardCardLabels
    )
    UPDATE bl SET bl.CardId = r.CardId
    FROM dbo.BoardLabels bl
    INNER JOIN Ranked r ON r.LabelId = bl.Id AND r.rn = 1
    WHERE bl.CardId IS NULL;

    ;WITH Ranked2 AS (
        SELECT LabelId, CardId, ROW_NUMBER() OVER (PARTITION BY LabelId ORDER BY CardId) AS rn
        FROM dbo.BoardCardLabels
    )
    INSERT INTO dbo.BoardLabels (BoardId, CardId, Name, Color, CreatedAt)
    SELECT bl.BoardId, r2.CardId, bl.Name, bl.Color, @MigrationNow
    FROM dbo.BoardLabels bl
    INNER JOIN Ranked2 r2 ON r2.LabelId = bl.Id AND r2.rn > 1;

    DELETE FROM dbo.BoardLabels WHERE CardId IS NULL;
END;
GO

-- ---------------------------------------------------------------------
-- 25) Etiket secimi: etiketin var olmasi artik "kartta gorunuyor" demek
--     degildir. Etiketler ekrani her kart icin varsayilan uc rengi (isimsiz)
--     listeler ve her satirin solundaki kutucuk o etiketin kartta gorunup
--     gorunmedigini belirler (IsSelected). Boylece varsayilan renkler karta
--     kendiliginden eklenmeden secilebilir halde durur.
-- ---------------------------------------------------------------------

IF COL_LENGTH('dbo.BoardLabels', 'IsSelected') IS NULL
BEGIN
    ALTER TABLE dbo.BoardLabels ADD
        IsSelected BIT NOT NULL CONSTRAINT DF_BoardLabels_IsSelected DEFAULT 0;
END;
GO

-- Geriye donuk doldurma: bu surumden once bir etiketin VAR OLMASI kartta
-- gorunmesi demekti. Kullanicinin kendi olusturdugu (adi olan) etiketler secili
-- hale getirilir; adi bos olanlar ise eski "kart acilinca uc etiketi kendiliginden
-- ekle" hatasinin biraktigi kayitlardir ve varsayilan palet satirlari olarak
-- secilmemis birakilir. (Ayri bir batch: ALTER ile ayni batch'te yeni sutuna
-- basvurulamaz.) Tek seferliktir: hic secili etiket yoksa calisir.
IF NOT EXISTS (SELECT 1 FROM dbo.BoardLabels WHERE IsSelected = 1)
BEGIN
    UPDATE dbo.BoardLabels SET IsSelected = 1 WHERE LTRIM(RTRIM(Name)) <> '';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BoardLabels_Card' AND object_id = OBJECT_ID('dbo.BoardLabels'))
BEGIN
    CREATE INDEX IX_BoardLabels_Card ON dbo.BoardLabels (CardId, IsSelected);
END;
GO
