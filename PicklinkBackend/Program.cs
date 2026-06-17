
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PicklinkBackend.Data;
using PicklinkBackend.Services;

namespace PicklinkBackend
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var jwtKey = builder.Configuration["Jwt:Key"]
                ?? throw new InvalidOperationException("Jwt:Key is not configured.");
            const string frontendCorsPolicy = "FrontendPolicy";

            // Add services to the container.

            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

            builder.Services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
            builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
            builder.Services.AddSingleton<IGoogleAuthService, GoogleAuthService>();
            builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
            builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
            builder.Services.AddCors(options =>
            {
                options.AddPolicy(frontendCorsPolicy, policy =>
                {
                    policy
                        .SetIsOriginAllowed(origin =>
                        {
                            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                            {
                                return false;
                            }

                            return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                                || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                                || origin.Equals("https://picklink.vercel.app", StringComparison.OrdinalIgnoreCase);
                        })
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                });
            });

            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = builder.Configuration["Jwt:Issuer"],
                        ValidAudience = builder.Configuration["Jwt:Audience"],
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                        ClockSkew = TimeSpan.Zero
                    };
                });

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "Picklink Backend API",
                    Version = "v1"
                });

                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Description = "Enter JWT Bearer token only.",
                    Type = SecuritySchemeType.Http,
                    Scheme = "Bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header
                });

                options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        Array.Empty<string>()
                    }
                });
            });

            Directory.CreateDirectory(Path.Combine(
                builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"),
                "uploads",
                "avatars"));

            var app = builder.Build();

            EnsurePasswordResetSchema(app);
            EnsureUserProfileSchema(app);
            EnsurePlayerProfileSchema(app);
            EnsureCommunitySchema(app);

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseCors(frontendCorsPolicy);
            app.UseStaticFiles();
            app.UseAuthentication();
            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }

        private static void EnsurePasswordResetSchema(WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            dbContext.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[PASSWORD_RESET_TOKEN]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [PASSWORD_RESET_TOKEN] (
                        [resetTokenId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_PASSWORD_RESET_TOKEN] PRIMARY KEY,
                        [userId] int NOT NULL,
                        [tokenHash] nvarchar(128) NOT NULL,
                        [createdAt] datetime NOT NULL CONSTRAINT [DF_PASSWORD_RESET_TOKEN_createdAt] DEFAULT (getutcdate()),
                        [expiresAt] datetime NOT NULL,
                        [usedAt] datetime NULL,
                        CONSTRAINT [FK_PASSWORD_RESET_TOKEN_USER] FOREIGN KEY ([userId]) REFERENCES [USER]([userId])
                    );
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_PASSWORD_RESET_TOKEN_userId'
                        AND object_id = OBJECT_ID(N'[PASSWORD_RESET_TOKEN]')
                )
                BEGIN
                    CREATE INDEX [IX_PASSWORD_RESET_TOKEN_userId] ON [PASSWORD_RESET_TOKEN] ([userId]);
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_PASSWORD_RESET_TOKEN_tokenHash'
                        AND object_id = OBJECT_ID(N'[PASSWORD_RESET_TOKEN]')
                )
                BEGIN
                    CREATE INDEX [IX_PASSWORD_RESET_TOKEN_tokenHash] ON [PASSWORD_RESET_TOKEN] ([tokenHash]);
                END
                """);
        }

        private static void EnsurePlayerProfileSchema(WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            dbContext.Database.ExecuteSqlRaw("""
                IF COL_LENGTH(N'PLAYER', N'playFrequency') IS NULL
                BEGIN
                    ALTER TABLE [PLAYER] ADD [playFrequency] nvarchar(50) NULL;
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF COL_LENGTH(N'PLAYER', N'preferredTimeSlot') IS NULL
                BEGIN
                    ALTER TABLE [PLAYER] ADD [preferredTimeSlot] nvarchar(50) NULL;
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF COL_LENGTH(N'PLAYER', N'bio') IS NULL
                BEGIN
                    ALTER TABLE [PLAYER] ADD [bio] nvarchar(500) NULL;
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF COL_LENGTH(N'PLAYER', N'birthDate') IS NULL
                BEGIN
                    ALTER TABLE [PLAYER] ADD [birthDate] date NULL;
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF COL_LENGTH(N'PLAYER', N'gender') IS NULL
                BEGIN
                    ALTER TABLE [PLAYER] ADD [gender] nvarchar(30) NULL;
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF COL_LENGTH(N'PLAYER', N'heightCm') IS NULL
                BEGIN
                    ALTER TABLE [PLAYER] ADD [heightCm] float NULL;
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF COL_LENGTH(N'PLAYER', N'weightKg') IS NULL
                BEGIN
                    ALTER TABLE [PLAYER] ADD [weightKg] float NULL;
                END
                """);
        }

        private static void EnsureCommunitySchema(WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            dbContext.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[SOCIAL_GROUP]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [SOCIAL_GROUP] (
                        [groupId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SOCIAL_GROUP] PRIMARY KEY,
                        [ownerId] int NOT NULL,
                        [groupName] nvarchar(200) NOT NULL,
                        [description] nvarchar(max) NULL,
                        [groupType] nvarchar(50) NOT NULL CONSTRAINT [DF_SOCIAL_GROUP_groupType] DEFAULT (N'Public'),
                        [coverImageUrl] nvarchar(500) NULL,
                        [createdAt] datetime NOT NULL CONSTRAINT [DF_SOCIAL_GROUP_createdAt] DEFAULT (getdate()),
                        CONSTRAINT [FK_SOCIAL_GROUP_OWNER] FOREIGN KEY ([ownerId]) REFERENCES [PLAYER]([playerId])
                    );
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[GROUP_MEMBER]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [GROUP_MEMBER] (
                        [groupId] int NOT NULL,
                        [userId] int NOT NULL,
                        [role] nvarchar(50) NOT NULL CONSTRAINT [DF_GROUP_MEMBER_role] DEFAULT (N'Member'),
                        [status] nvarchar(50) NOT NULL CONSTRAINT [DF_GROUP_MEMBER_status] DEFAULT (N'Accepted'),
                        [joinedAt] datetime NOT NULL CONSTRAINT [DF_GROUP_MEMBER_joinedAt] DEFAULT (getdate()),
                        CONSTRAINT [PK_GROUP_MEMBER] PRIMARY KEY ([groupId], [userId]),
                        CONSTRAINT [FK_GROUP_MEMBER_GROUP] FOREIGN KEY ([groupId]) REFERENCES [SOCIAL_GROUP]([groupId]),
                        CONSTRAINT [FK_GROUP_MEMBER_USER] FOREIGN KEY ([userId]) REFERENCES [USER]([userId])
                    );
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[POST]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [POST] (
                        [postId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_POST] PRIMARY KEY,
                        [authorId] int NOT NULL,
                        [groupId] int NULL,
                        [content] nvarchar(max) NULL,
                        [postType] nvarchar(50) NOT NULL CONSTRAINT [DF_POST_postType] DEFAULT (N'Post'),
                        [visibility] nvarchar(50) NOT NULL CONSTRAINT [DF_POST_visibility] DEFAULT (N'Public'),
                        [expiresAt] datetime NULL,
                        [createdAt] datetime NOT NULL CONSTRAINT [DF_POST_createdAt] DEFAULT (getdate()),
                        [updatedAt] datetime NOT NULL CONSTRAINT [DF_POST_updatedAt] DEFAULT (getdate()),
                        CONSTRAINT [FK_POST_AUTHOR] FOREIGN KEY ([authorId]) REFERENCES [USER]([userId]),
                        CONSTRAINT [FK_POST_SOCIAL_GROUP] FOREIGN KEY ([groupId]) REFERENCES [SOCIAL_GROUP]([groupId])
                    );
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[POST_COMMENT]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [POST_COMMENT] (
                        [commentId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_POST_COMMENT] PRIMARY KEY,
                        [postId] int NOT NULL,
                        [userId] int NOT NULL,
                        [parentCommentId] int NULL,
                        [content] nvarchar(max) NOT NULL,
                        [createdAt] datetime NOT NULL CONSTRAINT [DF_POST_COMMENT_createdAt] DEFAULT (getdate()),
                        [updatedAt] datetime NOT NULL CONSTRAINT [DF_POST_COMMENT_updatedAt] DEFAULT (getdate()),
                        CONSTRAINT [FK_POST_COMMENT_POST] FOREIGN KEY ([postId]) REFERENCES [POST]([postId]),
                        CONSTRAINT [FK_POST_COMMENT_USER] FOREIGN KEY ([userId]) REFERENCES [USER]([userId]),
                        CONSTRAINT [FK_POST_COMMENT_PARENT] FOREIGN KEY ([parentCommentId]) REFERENCES [POST_COMMENT]([commentId])
                    );
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[POST_LIKE]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [POST_LIKE] (
                        [likeId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_POST_LIKE] PRIMARY KEY,
                        [postId] int NOT NULL,
                        [userId] int NOT NULL,
                        [reactionType] nvarchar(50) NOT NULL CONSTRAINT [DF_POST_LIKE_reactionType] DEFAULT (N'Like'),
                        [createdAt] datetime NOT NULL CONSTRAINT [DF_POST_LIKE_createdAt] DEFAULT (getdate()),
                        CONSTRAINT [FK_POST_LIKE_POST] FOREIGN KEY ([postId]) REFERENCES [POST]([postId]),
                        CONSTRAINT [FK_POST_LIKE_USER] FOREIGN KEY ([userId]) REFERENCES [USER]([userId])
                    );
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[POST_MEDIA]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [POST_MEDIA] (
                        [mediaId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_POST_MEDIA] PRIMARY KEY,
                        [postId] int NOT NULL,
                        [mediaUrl] nvarchar(500) NOT NULL,
                        [mediaType] nvarchar(50) NOT NULL CONSTRAINT [DF_POST_MEDIA_mediaType] DEFAULT (N'Image'),
                        [displayOrder] int NOT NULL CONSTRAINT [DF_POST_MEDIA_displayOrder] DEFAULT (0),
                        CONSTRAINT [FK_POST_MEDIA_POST] FOREIGN KEY ([postId]) REFERENCES [POST]([postId])
                    );
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[CONVERSATION]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [CONVERSATION] (
                        [conversationId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CONVERSATION] PRIMARY KEY,
                        [groupId] int NULL,
                        [matchId] int NULL,
                        [conversationType] nvarchar(50) NOT NULL CONSTRAINT [DF_CONVERSATION_conversationType] DEFAULT (N'Direct'),
                        [conversationName] nvarchar(200) NULL,
                        [lastMessageAt] datetime NULL,
                        [createdAt] datetime NOT NULL CONSTRAINT [DF_CONVERSATION_createdAt] DEFAULT (getdate()),
                        CONSTRAINT [FK_CONVERSATION_SOCIAL_GROUP] FOREIGN KEY ([groupId]) REFERENCES [SOCIAL_GROUP]([groupId])
                    );
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[CONVERSATION_PARTICIPANT]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [CONVERSATION_PARTICIPANT] (
                        [conversationId] int NOT NULL,
                        [userId] int NOT NULL,
                        [joinedAt] datetime NOT NULL CONSTRAINT [DF_CONV_PARTICIPANT_joinedAt] DEFAULT (getdate()),
                        [lastReadAt] datetime NULL,
                        CONSTRAINT [PK_CONVERSATION_PARTICIPANT] PRIMARY KEY ([conversationId], [userId]),
                        CONSTRAINT [FK_CONV_PARTICIPANT_CONVERSATION] FOREIGN KEY ([conversationId]) REFERENCES [CONVERSATION]([conversationId]),
                        CONSTRAINT [FK_CONV_PARTICIPANT_USER] FOREIGN KEY ([userId]) REFERENCES [USER]([userId])
                    );
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[MESSAGE]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [MESSAGE] (
                        [messageId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_MESSAGE] PRIMARY KEY,
                        [conversationId] int NOT NULL,
                        [senderId] int NOT NULL,
                        [content] nvarchar(max) NULL,
                        [messageType] nvarchar(50) NOT NULL CONSTRAINT [DF_MESSAGE_messageType] DEFAULT (N'Text'),
                        [mediaUrl] nvarchar(500) NULL,
                        [replyToMessageId] int NULL,
                        [sentAt] datetime NOT NULL CONSTRAINT [DF_MESSAGE_sentAt] DEFAULT (getdate()),
                        [isDeleted] bit NOT NULL CONSTRAINT [DF_MESSAGE_isDeleted] DEFAULT (0),
                        CONSTRAINT [FK_MESSAGE_CONVERSATION] FOREIGN KEY ([conversationId]) REFERENCES [CONVERSATION]([conversationId]),
                        CONSTRAINT [FK_MESSAGE_SENDER] FOREIGN KEY ([senderId]) REFERENCES [USER]([userId]),
                        CONSTRAINT [FK_MESSAGE_REPLY] FOREIGN KEY ([replyToMessageId]) REFERENCES [MESSAGE]([messageId])
                    );
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[NOTIFICATION_LOG]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [NOTIFICATION_LOG] (
                        [notifId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_NOTIFICATION_LOG] PRIMARY KEY,
                        [userId] int NOT NULL,
                        [message] nvarchar(max) NOT NULL,
                        [isRead] bit NOT NULL CONSTRAINT [DF_NOTIFICATION_LOG_isRead] DEFAULT (0),
                        CONSTRAINT [FK_NOTIFICATION_LOG_USER] FOREIGN KEY ([userId]) REFERENCES [USER]([userId])
                    );
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF COL_LENGTH(N'POST', N'groupId') IS NULL
                BEGIN
                    ALTER TABLE [POST] ADD [groupId] int NULL;
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF COL_LENGTH(N'CONVERSATION', N'groupId') IS NULL
                BEGIN
                    ALTER TABLE [CONVERSATION] ADD [groupId] int NULL;
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF COL_LENGTH(N'CONVERSATION', N'matchId') IS NULL
                BEGIN
                    ALTER TABLE [CONVERSATION] ADD [matchId] int NULL;
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF NOT EXISTS (
                    SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_POST_SOCIAL_GROUP'
                )
                BEGIN
                    ALTER TABLE [POST]
                    ADD CONSTRAINT [FK_POST_SOCIAL_GROUP]
                    FOREIGN KEY ([groupId]) REFERENCES [SOCIAL_GROUP]([groupId]);
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF NOT EXISTS (
                    SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CONVERSATION_SOCIAL_GROUP'
                )
                BEGIN
                    ALTER TABLE [CONVERSATION]
                    ADD CONSTRAINT [FK_CONVERSATION_SOCIAL_GROUP]
                    FOREIGN KEY ([groupId]) REFERENCES [SOCIAL_GROUP]([groupId]);
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[MATCH]', N'U') IS NOT NULL
                    AND COL_LENGTH(N'CONVERSATION', N'matchId') IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CONVERSATION_MATCH'
                    )
                BEGIN
                    ALTER TABLE [CONVERSATION]
                    ADD CONSTRAINT [FK_CONVERSATION_MATCH]
                    FOREIGN KEY ([matchId]) REFERENCES [MATCH]([matchId]);
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_GROUP_MEMBER_userId'
                        AND object_id = OBJECT_ID(N'[GROUP_MEMBER]')
                )
                BEGIN
                    CREATE INDEX [IX_GROUP_MEMBER_userId] ON [GROUP_MEMBER] ([userId], [status]);
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_SOCIAL_GROUP_ownerId'
                        AND object_id = OBJECT_ID(N'[SOCIAL_GROUP]')
                )
                BEGIN
                    CREATE INDEX [IX_SOCIAL_GROUP_ownerId] ON [SOCIAL_GROUP] ([ownerId]);
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_POST_groupId'
                        AND object_id = OBJECT_ID(N'[POST]')
                )
                BEGIN
                    CREATE INDEX [IX_POST_groupId] ON [POST] ([groupId]) WHERE [groupId] IS NOT NULL;
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_CONVERSATION_groupId'
                        AND object_id = OBJECT_ID(N'[CONVERSATION]')
                )
                BEGIN
                    CREATE INDEX [IX_CONVERSATION_groupId] ON [CONVERSATION] ([groupId]) WHERE [groupId] IS NOT NULL;
                END
                """);
        }

        private static void EnsureUserProfileSchema(WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            dbContext.Database.ExecuteSqlRaw("""
                IF COL_LENGTH(N'USER', N'commune') IS NULL
                BEGIN
                    ALTER TABLE [USER] ADD [commune] nvarchar(150) NULL;
                END
                """);
        }
    }
}
