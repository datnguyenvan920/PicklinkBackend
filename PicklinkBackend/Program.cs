
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

            builder.Services.AddDbContextPool<ApplicationDbContext>(options =>
                options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

            builder.Services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
            builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
            builder.Services.AddScoped<PlayerScheduleConflictService>();
            builder.Services.AddSingleton<IGoogleAuthService, GoogleAuthService>();
            builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
            builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
            builder.Services.AddSingleton<ScheduleRealtimeNotifier>();
            builder.Services.AddSingleton<PaymentRealtimeNotifier>();
            builder.Services.AddSingleton<MatchRealtimeNotifier>();
            builder.Services.AddSingleton<VenueRealtimeNotifier>();
            builder.Services.AddSingleton<NotificationRealtimeNotifier>();
            builder.Services.AddScoped<NotificationService>();
            builder.Services.AddHostedService<MatchExpirationService>();
            builder.Services.AddHostedService<BookingHoldExpirationService>();
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
            Directory.CreateDirectory(Path.Combine(
                builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"),
                "uploads",
                "venues"));
            Directory.CreateDirectory(Path.Combine(
                builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"),
                "uploads",
                "payment-receipts"));

            var app = builder.Build();

            if (app.Configuration.GetValue("Startup:RunSchemaChecks", false))
            {
                EnsurePasswordResetSchema(app);
                EnsureAdminUserSchema(app);
                EnsureUserProfileSchema(app);
                EnsurePlayerProfileSchema(app);
                EnsureCommunitySchema(app);
                EnsureOwnerVenueSchema(app);
                EnsureListingFeeSchema(app);
                EnsurePaymentSchema(app);
                EnsureStaffOperationSchema(app);
                EnsurePlayerPhase7Schema(app);
                EnsurePlayerPhase8Schema(app);
            }

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseCors(frontendCorsPolicy);
            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = context =>
                {
                    if (context.File.PhysicalPath?.Contains(
                            $"{Path.DirectorySeparatorChar}uploads{Path.DirectorySeparatorChar}",
                            StringComparison.OrdinalIgnoreCase) == true)
                    {
                        context.Context.Response.Headers.CacheControl = "public,max-age=604800,immutable";
                    }
                }
            });
            app.UseAuthentication();
            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }

        private static void EnsureAdminUserSchema(WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            dbContext.Database.ExecuteSqlRaw("""
                IF COL_LENGTH(N'USER', N'isLocked') IS NULL
                    ALTER TABLE [USER] ADD [isLocked] bit NOT NULL CONSTRAINT [DF_USER_isLocked] DEFAULT (0);
                """);
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
                        [isPinned] bit NOT NULL CONSTRAINT [DF_MESSAGE_isPinned] DEFAULT (0),
                        CONSTRAINT [FK_MESSAGE_CONVERSATION] FOREIGN KEY ([conversationId]) REFERENCES [CONVERSATION]([conversationId]),
                        CONSTRAINT [FK_MESSAGE_SENDER] FOREIGN KEY ([senderId]) REFERENCES [USER]([userId]),
                        CONSTRAINT [FK_MESSAGE_REPLY] FOREIGN KEY ([replyToMessageId]) REFERENCES [MESSAGE]([messageId])
                    );
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF COL_LENGTH(N'MESSAGE', N'isPinned') IS NULL
                    ALTER TABLE [MESSAGE] ADD [isPinned] bit NOT NULL CONSTRAINT [DF_MESSAGE_isPinned] DEFAULT (0);
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[NOTIFICATION_LOG]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [NOTIFICATION_LOG] (
                        [notifId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_NOTIFICATION_LOG] PRIMARY KEY,
                        [userId] int NOT NULL,
                        [notificationType] nvarchar(30) NOT NULL CONSTRAINT [DF_NOTIFICATION_LOG_notificationType] DEFAULT (N'system'),
                        [title] nvarchar(200) NOT NULL CONSTRAINT [DF_NOTIFICATION_LOG_title] DEFAULT (N'Thông báo'),
                        [message] nvarchar(max) NOT NULL,
                        [tone] nvarchar(20) NOT NULL CONSTRAINT [DF_NOTIFICATION_LOG_tone] DEFAULT (N'default'),
                        [linkTo] nvarchar(500) NULL,
                        [linkLabel] nvarchar(100) NULL,
                        [createdAt] datetime2 NOT NULL CONSTRAINT [DF_NOTIFICATION_LOG_createdAt] DEFAULT (getutcdate()),
                        [isRead] bit NOT NULL CONSTRAINT [DF_NOTIFICATION_LOG_isRead] DEFAULT (0),
                        CONSTRAINT [FK_NOTIFICATION_LOG_USER] FOREIGN KEY ([userId]) REFERENCES [USER]([userId])
                    );
                END
                IF COL_LENGTH(N'NOTIFICATION_LOG', N'notificationType') IS NULL
                    ALTER TABLE [NOTIFICATION_LOG] ADD [notificationType] nvarchar(30) NOT NULL CONSTRAINT [DF_NOTIFICATION_LOG_notificationType] DEFAULT (N'system');
                IF COL_LENGTH(N'NOTIFICATION_LOG', N'title') IS NULL
                    ALTER TABLE [NOTIFICATION_LOG] ADD [title] nvarchar(200) NOT NULL CONSTRAINT [DF_NOTIFICATION_LOG_title] DEFAULT (N'Thông báo');
                IF COL_LENGTH(N'NOTIFICATION_LOG', N'tone') IS NULL
                    ALTER TABLE [NOTIFICATION_LOG] ADD [tone] nvarchar(20) NOT NULL CONSTRAINT [DF_NOTIFICATION_LOG_tone] DEFAULT (N'default');
                IF COL_LENGTH(N'NOTIFICATION_LOG', N'linkTo') IS NULL
                    ALTER TABLE [NOTIFICATION_LOG] ADD [linkTo] nvarchar(500) NULL;
                IF COL_LENGTH(N'NOTIFICATION_LOG', N'linkLabel') IS NULL
                    ALTER TABLE [NOTIFICATION_LOG] ADD [linkLabel] nvarchar(100) NULL;
                IF COL_LENGTH(N'NOTIFICATION_LOG', N'createdAt') IS NULL
                    ALTER TABLE [NOTIFICATION_LOG] ADD [createdAt] datetime2 NOT NULL CONSTRAINT [DF_NOTIFICATION_LOG_createdAt] DEFAULT (getutcdate());
                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE [name] = N'IX_NOTIFICATION_LOG_user_unread_created'
                      AND [object_id] = OBJECT_ID(N'[NOTIFICATION_LOG]')
                )
                    CREATE INDEX [IX_NOTIFICATION_LOG_user_unread_created]
                    ON [NOTIFICATION_LOG] ([userId], [isRead], [createdAt] DESC);
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

        private static void EnsureOwnerVenueSchema(WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            dbContext.Database.ExecuteSqlRaw("""
                IF COL_LENGTH(N'VENUE', N'isOpen') IS NULL
                    ALTER TABLE [VENUE] ADD [isOpen] bit NOT NULL CONSTRAINT [DF_VENUE_isOpen] DEFAULT (1);
                IF COL_LENGTH(N'VENUE', N'approvalStatus') IS NULL
                    ALTER TABLE [VENUE] ADD [approvalStatus] nvarchar(30) NOT NULL CONSTRAINT [DF_VENUE_approvalStatus] DEFAULT (N'Draft');
                IF COL_LENGTH(N'VENUE', N'rejectionReason') IS NULL
                    ALTER TABLE [VENUE] ADD [rejectionReason] nvarchar(500) NULL;
                IF COL_LENGTH(N'COURT', N'courtType') IS NULL
                    ALTER TABLE [COURT] ADD [courtType] nvarchar(100) NOT NULL CONSTRAINT [DF_COURT_courtType] DEFAULT (N'Standard');
                IF COL_LENGTH(N'COURT', N'hourlyPrice') IS NULL
                    ALTER TABLE [COURT] ADD [hourlyPrice] float NOT NULL CONSTRAINT [DF_COURT_hourlyPrice] DEFAULT (0);
                IF COL_LENGTH(N'BOOKING', N'ownerEntryType') IS NULL
                    ALTER TABLE [BOOKING] ADD [ownerEntryType] nvarchar(30) NULL;
                IF COL_LENGTH(N'BOOKING', N'title') IS NULL
                    ALTER TABLE [BOOKING] ADD [title] nvarchar(200) NULL;
                IF COL_LENGTH(N'BOOKING', N'bookingCode') IS NULL
                    ALTER TABLE [BOOKING] ADD [bookingCode] nvarchar(30) NULL;
                IF COL_LENGTH(N'BOOKING', N'createdAt') IS NULL
                    ALTER TABLE [BOOKING] ADD [createdAt] datetime NOT NULL CONSTRAINT [DF_BOOKING_createdAt] DEFAULT (getutcdate());
                IF COL_LENGTH(N'BOOKING', N'holdExpiresAt') IS NULL
                    ALTER TABLE [BOOKING] ADD [holdExpiresAt] datetime NULL;
                IF COL_LENGTH(N'BOOKING', N'hourlyPriceSnapshot') IS NULL
                    ALTER TABLE [BOOKING] ADD [hourlyPriceSnapshot] float NOT NULL CONSTRAINT [DF_BOOKING_hourlyPriceSnapshot] DEFAULT (0);
                IF COL_LENGTH(N'BOOKING', N'courtAmount') IS NULL
                    ALTER TABLE [BOOKING] ADD [courtAmount] float NOT NULL CONSTRAINT [DF_BOOKING_courtAmount] DEFAULT (0);
                IF COL_LENGTH(N'BOOKING', N'totalAmount') IS NULL
                    ALTER TABLE [BOOKING] ADD [totalAmount] float NOT NULL CONSTRAINT [DF_BOOKING_totalAmount] DEFAULT (0);
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[VENUE_IMAGE]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [VENUE_IMAGE] (
                        [venueImageId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_VENUE_IMAGE] PRIMARY KEY,
                        [venueId] int NOT NULL,
                        [imageUrl] nvarchar(1000) NOT NULL,
                        [caption] nvarchar(200) NULL,
                        [isPrimary] bit NOT NULL CONSTRAINT [DF_VENUE_IMAGE_isPrimary] DEFAULT (0),
                        [sortOrder] int NOT NULL CONSTRAINT [DF_VENUE_IMAGE_sortOrder] DEFAULT (0),
                        [createdAt] datetime NOT NULL CONSTRAINT [DF_VENUE_IMAGE_createdAt] DEFAULT (getutcdate()),
                        CONSTRAINT [FK_VENUE_IMAGE_VENUE] FOREIGN KEY ([venueId]) REFERENCES [VENUE]([venueId]) ON DELETE CASCADE
                    );
                    CREATE INDEX [IX_VENUE_IMAGE_venueId] ON [VENUE_IMAGE] ([venueId]);
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[BOOKING_STATUS_HISTORY]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [BOOKING_STATUS_HISTORY] (
                        [bookingStatusHistoryId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_BOOKING_STATUS_HISTORY] PRIMARY KEY,
                        [bookingId] int NOT NULL,
                        [fromStatus] nvarchar(50) NULL,
                        [toStatus] nvarchar(50) NOT NULL,
                        [reason] nvarchar(500) NULL,
                        [actorUserId] int NULL,
                        [changedAt] datetime NOT NULL CONSTRAINT [DF_BOOKING_STATUS_HISTORY_changedAt] DEFAULT (getutcdate()),
                        CONSTRAINT [FK_BOOKING_STATUS_HISTORY_BOOKING] FOREIGN KEY ([bookingId]) REFERENCES [BOOKING]([bookingId]) ON DELETE CASCADE
                    );
                    CREATE INDEX [IX_BOOKING_STATUS_HISTORY_bookingId] ON [BOOKING_STATUS_HISTORY] ([bookingId]);
                END

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BOOKING_court_time' AND object_id = OBJECT_ID(N'[BOOKING]'))
                    CREATE INDEX [IX_BOOKING_court_time] ON [BOOKING] ([courtId], [startTime], [endTime], [status]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_BOOKING_bookingCode' AND object_id = OBJECT_ID(N'[BOOKING]'))
                    CREATE UNIQUE INDEX [UQ_BOOKING_bookingCode] ON [BOOKING] ([bookingCode]) WHERE [bookingCode] IS NOT NULL;
                """);
        }

        private static void EnsureListingFeeSchema(WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            dbContext.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[LISTING_FEE_SETTING]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [LISTING_FEE_SETTING] (
                        [listingFeeSettingId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_LISTING_FEE_SETTING] PRIMARY KEY,
                        [pricePerCourtPerMonth] decimal(18,2) NOT NULL,
                        [updatedAt] datetime NOT NULL CONSTRAINT [DF_LISTING_FEE_SETTING_updatedAt] DEFAULT (getutcdate()),
                        [updatedByUserId] int NULL,
                        CONSTRAINT [FK_LISTING_FEE_SETTING_USER] FOREIGN KEY ([updatedByUserId]) REFERENCES [USER]([userId])
                    );
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[VENUE_LISTING_PAYMENT]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [VENUE_LISTING_PAYMENT] (
                        [venueListingPaymentId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_VENUE_LISTING_PAYMENT] PRIMARY KEY,
                        [venueId] int NOT NULL,
                        [months] int NOT NULL,
                        [activeCourtCount] int NOT NULL,
                        [pricePerCourtPerMonth] decimal(18,2) NOT NULL,
                        [amount] decimal(18,2) NOT NULL,
                        [status] nvarchar(30) NOT NULL,
                        [receiptImageUrl] nvarchar(1000) NULL,
                        [rejectionReason] nvarchar(500) NULL,
                        [submittedAt] datetime NOT NULL CONSTRAINT [DF_VENUE_LISTING_PAYMENT_submittedAt] DEFAULT (getutcdate()),
                        [reviewedAt] datetime NULL,
                        [reviewedByUserId] int NULL,
                        [paidFrom] datetime NULL,
                        [paidUntil] datetime NULL,
                        CONSTRAINT [FK_VENUE_LISTING_PAYMENT_VENUE] FOREIGN KEY ([venueId]) REFERENCES [VENUE]([venueId]) ON DELETE CASCADE,
                        CONSTRAINT [FK_VENUE_LISTING_PAYMENT_REVIEWER] FOREIGN KEY ([reviewedByUserId]) REFERENCES [USER]([userId])
                    );
                    CREATE INDEX [IX_VENUE_LISTING_PAYMENT_venueId] ON [VENUE_LISTING_PAYMENT] ([venueId]);
                    CREATE INDEX [IX_VENUE_LISTING_PAYMENT_status] ON [VENUE_LISTING_PAYMENT] ([status]);
                END
                """);
        }

        private static void EnsurePaymentSchema(WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            dbContext.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[OWNER_BANK_ACCOUNT]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [OWNER_BANK_ACCOUNT] (
                        [ownerBankAccountId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_OWNER_BANK_ACCOUNT] PRIMARY KEY,
                        [ownerId] int NOT NULL,
                        [bankCode] nvarchar(30) NOT NULL,
                        [bankName] nvarchar(150) NOT NULL,
                        [accountNumber] nvarchar(50) NOT NULL,
                        [accountHolderName] nvarchar(200) NOT NULL,
                        [isActive] bit NOT NULL CONSTRAINT [DF_OWNER_BANK_ACCOUNT_isActive] DEFAULT (1),
                        [createdAt] datetime NOT NULL CONSTRAINT [DF_OWNER_BANK_ACCOUNT_createdAt] DEFAULT (getutcdate()),
                        [updatedAt] datetime NOT NULL CONSTRAINT [DF_OWNER_BANK_ACCOUNT_updatedAt] DEFAULT (getutcdate()),
                        CONSTRAINT [FK_OWNER_BANK_ACCOUNT_OWNER] FOREIGN KEY ([ownerId]) REFERENCES [VENUE_OWNER]([ownerId]) ON DELETE CASCADE
                    );
                    CREATE UNIQUE INDEX [UQ_OWNER_BANK_ACCOUNT_ownerId] ON [OWNER_BANK_ACCOUNT] ([ownerId]);
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF COL_LENGTH(N'PAYMENT', N'transferCode') IS NULL ALTER TABLE [PAYMENT] ADD [transferCode] nvarchar(40) NULL;
                IF COL_LENGTH(N'PAYMENT', N'transferContent') IS NULL ALTER TABLE [PAYMENT] ADD [transferContent] nvarchar(140) NULL;
                IF COL_LENGTH(N'PAYMENT', N'bankCode') IS NULL ALTER TABLE [PAYMENT] ADD [bankCode] nvarchar(30) NULL;
                IF COL_LENGTH(N'PAYMENT', N'bankName') IS NULL ALTER TABLE [PAYMENT] ADD [bankName] nvarchar(150) NULL;
                IF COL_LENGTH(N'PAYMENT', N'bankAccountNumber') IS NULL ALTER TABLE [PAYMENT] ADD [bankAccountNumber] nvarchar(50) NULL;
                IF COL_LENGTH(N'PAYMENT', N'bankAccountName') IS NULL ALTER TABLE [PAYMENT] ADD [bankAccountName] nvarchar(200) NULL;
                IF COL_LENGTH(N'PAYMENT', N'qrImageUrl') IS NULL ALTER TABLE [PAYMENT] ADD [qrImageUrl] nvarchar(2000) NULL;
                IF COL_LENGTH(N'PAYMENT', N'receiptImageUrl') IS NULL ALTER TABLE [PAYMENT] ADD [receiptImageUrl] nvarchar(1000) NULL;
                IF COL_LENGTH(N'PAYMENT', N'submittedAt') IS NULL ALTER TABLE [PAYMENT] ADD [submittedAt] datetime NULL;
                IF COL_LENGTH(N'PAYMENT', N'verifiedAt') IS NULL ALTER TABLE [PAYMENT] ADD [verifiedAt] datetime NULL;
                IF COL_LENGTH(N'PAYMENT', N'verifiedByUserId') IS NULL ALTER TABLE [PAYMENT] ADD [verifiedByUserId] int NULL;
                IF COL_LENGTH(N'PAYMENT', N'rejectionReason') IS NULL ALTER TABLE [PAYMENT] ADD [rejectionReason] nvarchar(500) NULL;
                """);

            // CREATE INDEX must be compiled after transferCode has been added. Dynamic SQL
            // prevents SQL Server from resolving the new column while compiling the ALTER batch.
            dbContext.Database.ExecuteSqlRaw("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_PAYMENT_transferCode' AND object_id = OBJECT_ID(N'[PAYMENT]'))
                    EXEC(N'CREATE UNIQUE INDEX [UQ_PAYMENT_transferCode] ON [PAYMENT] ([transferCode]) WHERE [transferCode] IS NOT NULL;');
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[PAYMENT_STATUS_HISTORY]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [PAYMENT_STATUS_HISTORY] (
                        [paymentStatusHistoryId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_PAYMENT_STATUS_HISTORY] PRIMARY KEY,
                        [paymentId] int NOT NULL,
                        [fromStatus] nvarchar(50) NULL,
                        [toStatus] nvarchar(50) NOT NULL,
                        [action] nvarchar(100) NOT NULL,
                        [reason] nvarchar(500) NULL,
                        [actorUserId] int NULL,
                        [createdAt] datetime NOT NULL CONSTRAINT [DF_PAYMENT_STATUS_HISTORY_createdAt] DEFAULT (getutcdate()),
                        CONSTRAINT [FK_PAYMENT_STATUS_HISTORY_PAYMENT] FOREIGN KEY ([paymentId]) REFERENCES [PAYMENT]([paymentId]) ON DELETE CASCADE
                    );
                    CREATE INDEX [IX_PAYMENT_STATUS_HISTORY_paymentId] ON [PAYMENT_STATUS_HISTORY] ([paymentId]);
                END
                """);
        }

        private static void EnsureStaffOperationSchema(WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            dbContext.Database.ExecuteSqlRaw("""
                IF COL_LENGTH(N'STAFF', N'permissions') IS NULL
                    ALTER TABLE [STAFF] ADD [permissions] nvarchar(500) NOT NULL CONSTRAINT [DF_STAFF_permissions] DEFAULT (N'ViewBookings,VerifyBooking,ConfirmPayment,CheckIn,MarkNoShow');
                IF COL_LENGTH(N'STAFF', N'isActive') IS NULL
                    ALTER TABLE [STAFF] ADD [isActive] bit NOT NULL CONSTRAINT [DF_STAFF_isActive] DEFAULT (1);
                IF COL_LENGTH(N'STAFF', N'assignedAt') IS NULL
                    ALTER TABLE [STAFF] ADD [assignedAt] datetime NOT NULL CONSTRAINT [DF_STAFF_assignedAt] DEFAULT (getutcdate());
                IF COL_LENGTH(N'STAFF', N'assignedByUserId') IS NULL
                    ALTER TABLE [STAFF] ADD [assignedByUserId] int NULL;
                IF COL_LENGTH(N'STAFF', N'revokedAt') IS NULL
                    ALTER TABLE [STAFF] ADD [revokedAt] datetime NULL;
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_STAFF_userId_venueId' AND object_id = OBJECT_ID(N'[STAFF]'))
                    CREATE UNIQUE INDEX [UQ_STAFF_userId_venueId] ON [STAFF] ([userId], [venueId]);
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[BOOKING_OPERATION]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [BOOKING_OPERATION] (
                        [bookingOperationId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_BOOKING_OPERATION] PRIMARY KEY,
                        [bookingId] int NOT NULL,
                        [checkInStatus] nvarchar(30) NOT NULL CONSTRAINT [DF_BOOKING_OPERATION_checkInStatus] DEFAULT (N'Ready'),
                        [codeVerifiedAt] datetime NULL,
                        [codeVerifiedByUserId] int NULL,
                        [paymentConfirmedAt] datetime NULL,
                        [paymentConfirmedByUserId] int NULL,
                        [checkedInAt] datetime NULL,
                        [checkedInByUserId] int NULL,
                        [noShowAt] datetime NULL,
                        [noShowByUserId] int NULL,
                        [updatedAt] datetime NOT NULL CONSTRAINT [DF_BOOKING_OPERATION_updatedAt] DEFAULT (getutcdate()),
                        CONSTRAINT [FK_BOOKING_OPERATION_BOOKING] FOREIGN KEY ([bookingId]) REFERENCES [BOOKING]([bookingId]) ON DELETE CASCADE
                    );
                    CREATE UNIQUE INDEX [UQ_BOOKING_OPERATION_bookingId] ON [BOOKING_OPERATION] ([bookingId]);
                END
                """);
        }

        private static void EnsurePlayerPhase7Schema(WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            dbContext.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[FAVORITE_VENUE]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [FAVORITE_VENUE] (
                        [playerId] int NOT NULL,
                        [venueId] int NOT NULL,
                        [createdAt] datetime NOT NULL CONSTRAINT [DF_FAVORITE_VENUE_createdAt] DEFAULT (getutcdate()),
                        CONSTRAINT [PK_FAVORITE_VENUE] PRIMARY KEY ([playerId], [venueId]),
                        CONSTRAINT [FK_FAVORITE_VENUE_PLAYER] FOREIGN KEY ([playerId]) REFERENCES [PLAYER]([playerId]) ON DELETE CASCADE,
                        CONSTRAINT [FK_FAVORITE_VENUE_VENUE] FOREIGN KEY ([venueId]) REFERENCES [VENUE]([venueId]) ON DELETE CASCADE
                    );
                    CREATE INDEX [IX_FAVORITE_VENUE_venueId] ON [FAVORITE_VENUE] ([venueId]);
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF COL_LENGTH(N'RATING_HISTORY', N'bookingId') IS NULL ALTER TABLE [RATING_HISTORY] ADD [bookingId] int NULL;
                IF COL_LENGTH(N'RATING_HISTORY', N'comment') IS NULL ALTER TABLE [RATING_HISTORY] ADD [comment] nvarchar(1000) NULL;
                IF COL_LENGTH(N'RATING_HISTORY', N'tags') IS NULL ALTER TABLE [RATING_HISTORY] ADD [tags] nvarchar(500) NULL;
                IF COL_LENGTH(N'RATING_HISTORY', N'isAnonymous') IS NULL ALTER TABLE [RATING_HISTORY] ADD [isAnonymous] bit NOT NULL CONSTRAINT [DF_RATING_HISTORY_isAnonymous] DEFAULT (0);
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_RATING_HISTORY_BOOKING')
                    ALTER TABLE [RATING_HISTORY] ADD CONSTRAINT [FK_RATING_HISTORY_BOOKING]
                    FOREIGN KEY ([bookingId]) REFERENCES [BOOKING]([bookingId]) ON DELETE CASCADE;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_RATING_HISTORY_booking_user' AND object_id = OBJECT_ID(N'[RATING_HISTORY]'))
                    CREATE UNIQUE INDEX [UQ_RATING_HISTORY_booking_user] ON [RATING_HISTORY] ([bookingId], [userId]) WHERE [bookingId] IS NOT NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_RATING_HISTORY_score')
                    ALTER TABLE [RATING_HISTORY] WITH NOCHECK ADD CONSTRAINT [CK_RATING_HISTORY_score] CHECK ([score] >= 1 AND [score] <= 5);
                """);
        }

        private static void EnsurePlayerPhase8Schema(WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            dbContext.Database.ExecuteSqlRaw("""
                IF COL_LENGTH(N'MATCH', N'hostPlayerId') IS NULL ALTER TABLE [MATCH] ADD [hostPlayerId] int NULL;
                IF COL_LENGTH(N'MATCH', N'requiredPlayerCount') IS NULL
                    ALTER TABLE [MATCH] ADD [requiredPlayerCount] int NOT NULL CONSTRAINT [DF_MATCH_requiredPlayerCount] DEFAULT (2);
                IF COL_LENGTH(N'MATCH', N'note') IS NULL ALTER TABLE [MATCH] ADD [note] nvarchar(1000) NULL;
                IF COL_LENGTH(N'MATCH', N'title') IS NULL ALTER TABLE [MATCH] ADD [title] nvarchar(200) NULL;
                IF COL_LENGTH(N'MATCH', N'province') IS NULL ALTER TABLE [MATCH] ADD [province] nvarchar(100) NULL;
                IF COL_LENGTH(N'MATCH', N'ward') IS NULL ALTER TABLE [MATCH] ADD [ward] nvarchar(150) NULL;
                IF COL_LENGTH(N'MATCH', N'searchRadiusKm') IS NULL
                    ALTER TABLE [MATCH] ADD [searchRadiusKm] float NOT NULL CONSTRAINT [DF_MATCH_searchRadiusKm] DEFAULT (5);
                IF COL_LENGTH(N'MATCH', N'searchLatitude') IS NULL ALTER TABLE [MATCH] ADD [searchLatitude] float NULL;
                IF COL_LENGTH(N'MATCH', N'searchLongitude') IS NULL ALTER TABLE [MATCH] ADD [searchLongitude] float NULL;
                IF COL_LENGTH(N'MATCH', N'availableDateFrom') IS NULL ALTER TABLE [MATCH] ADD [availableDateFrom] date NULL;
                IF COL_LENGTH(N'MATCH', N'availableDateTo') IS NULL ALTER TABLE [MATCH] ADD [availableDateTo] date NULL;
                IF COL_LENGTH(N'MATCH', N'minSkillLevel') IS NULL
                    ALTER TABLE [MATCH] ADD [minSkillLevel] int NOT NULL CONSTRAINT [DF_MATCH_minSkillLevel] DEFAULT (1);
                IF COL_LENGTH(N'MATCH', N'maxSkillLevel') IS NULL
                    ALTER TABLE [MATCH] ADD [maxSkillLevel] int NOT NULL CONSTRAINT [DF_MATCH_maxSkillLevel] DEFAULT (5);
                IF COL_LENGTH(N'MATCH', N'createdAt') IS NULL
                    ALTER TABLE [MATCH] ADD [createdAt] datetime NOT NULL CONSTRAINT [DF_MATCH_createdAt] DEFAULT (getutcdate());
                IF COL_LENGTH(N'MATCH', N'cancelledAt') IS NULL ALTER TABLE [MATCH] ADD [cancelledAt] datetime NULL;

                IF COL_LENGTH(N'MATCH_PARTICIPANT', N'status') IS NULL
                    ALTER TABLE [MATCH_PARTICIPANT] ADD [status] nvarchar(30) NOT NULL CONSTRAINT [DF_MATCH_PARTICIPANT_status] DEFAULT (N'Accepted');
                IF COL_LENGTH(N'MATCH_PARTICIPANT', N'isHost') IS NULL
                    ALTER TABLE [MATCH_PARTICIPANT] ADD [isHost] bit NOT NULL CONSTRAINT [DF_MATCH_PARTICIPANT_isHost] DEFAULT (0);
                IF COL_LENGTH(N'MATCH_PARTICIPANT', N'requestedAt') IS NULL
                    ALTER TABLE [MATCH_PARTICIPANT] ADD [requestedAt] datetime NOT NULL CONSTRAINT [DF_MATCH_PARTICIPANT_requestedAt] DEFAULT (getutcdate());
                IF COL_LENGTH(N'MATCH_PARTICIPANT', N'respondedAt') IS NULL
                    ALTER TABLE [MATCH_PARTICIPANT] ADD [respondedAt] datetime NULL;
                """);

            dbContext.Database.ExecuteSqlRaw("""
                UPDATE [MATCH]
                SET [requiredPlayerCount] = CASE
                    WHEN LOWER(REPLACE([matchType], N' ', N'')) IN (N'2vs2', N'2v2') THEN 4
                    ELSE 2
                END
                WHERE [availableDateFrom] IS NULL
                AND [requiredPlayerCount] <> CASE
                    WHEN LOWER(REPLACE([matchType], N' ', N'')) IN (N'2vs2', N'2v2') THEN 4
                    ELSE 2
                END;
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_MATCH_HOST_PLAYER')
                    ALTER TABLE [MATCH] ADD CONSTRAINT [FK_MATCH_HOST_PLAYER]
                    FOREIGN KEY ([hostPlayerId]) REFERENCES [PLAYER]([playerId]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_MATCH_PARTICIPANT_match_player' AND object_id = OBJECT_ID(N'[MATCH_PARTICIPANT]'))
                BEGIN
                    ;WITH [DuplicateParticipants] AS (
                        SELECT [participantId],
                            ROW_NUMBER() OVER (
                                PARTITION BY [matchId], [playerId]
                                ORDER BY [isHost] DESC, [respondedAt] DESC, [participantId]
                            ) AS [rowNumber]
                        FROM [MATCH_PARTICIPANT]
                    )
                    DELETE FROM [MATCH_PARTICIPANT]
                    WHERE [participantId] IN (
                        SELECT [participantId] FROM [DuplicateParticipants] WHERE [rowNumber] > 1
                    );
                    CREATE UNIQUE INDEX [UQ_MATCH_PARTICIPANT_match_player]
                        ON [MATCH_PARTICIPANT] ([matchId], [playerId]);
                END
                IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_MATCH_requiredPlayerCount')
                    ALTER TABLE [MATCH] DROP CONSTRAINT [CK_MATCH_requiredPlayerCount];
                ALTER TABLE [MATCH] WITH NOCHECK ADD CONSTRAINT [CK_MATCH_requiredPlayerCount]
                    CHECK ([requiredPlayerCount] BETWEEN 2 AND 4);
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[MATCH_PLAYER_REVIEW]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [MATCH_PLAYER_REVIEW] (
                        [matchPlayerReviewId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_MATCH_PLAYER_REVIEW] PRIMARY KEY,
                        [matchId] int NOT NULL,
                        [reviewerPlayerId] int NOT NULL,
                        [revieweePlayerId] int NOT NULL,
                        [score] int NOT NULL,
                        [comment] nvarchar(1000) NULL,
                        [createdAt] datetime NOT NULL CONSTRAINT [DF_MATCH_PLAYER_REVIEW_createdAt] DEFAULT (getutcdate()),
                        CONSTRAINT [FK_MATCH_PLAYER_REVIEW_MATCH] FOREIGN KEY ([matchId]) REFERENCES [MATCH]([matchId]) ON DELETE CASCADE,
                        CONSTRAINT [FK_MATCH_PLAYER_REVIEW_REVIEWER] FOREIGN KEY ([reviewerPlayerId]) REFERENCES [PLAYER]([playerId]),
                        CONSTRAINT [FK_MATCH_PLAYER_REVIEW_REVIEWEE] FOREIGN KEY ([revieweePlayerId]) REFERENCES [PLAYER]([playerId]),
                        CONSTRAINT [CK_MATCH_PLAYER_REVIEW_score] CHECK ([score] >= 1 AND [score] <= 5),
                        CONSTRAINT [CK_MATCH_PLAYER_REVIEW_distinct_players] CHECK ([reviewerPlayerId] <> [revieweePlayerId])
                    );
                    CREATE UNIQUE INDEX [UQ_MATCH_PLAYER_REVIEW]
                        ON [MATCH_PLAYER_REVIEW] ([matchId], [reviewerPlayerId], [revieweePlayerId]);
                    CREATE INDEX [IX_MATCH_PLAYER_REVIEW_revieweePlayerId]
                        ON [MATCH_PLAYER_REVIEW] ([revieweePlayerId]);
                END
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[MATCH_AVAILABILITY_SLOT]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [MATCH_AVAILABILITY_SLOT] (
                        [matchAvailabilitySlotId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_MATCH_AVAILABILITY_SLOT] PRIMARY KEY,
                        [matchId] int NOT NULL,
                        [timeStart] time NOT NULL,
                        [timeEnd] time NOT NULL,
                        CONSTRAINT [FK_MATCH_AVAILABILITY_SLOT_MATCH] FOREIGN KEY ([matchId]) REFERENCES [MATCH]([matchId]) ON DELETE CASCADE,
                        CONSTRAINT [CK_MATCH_AVAILABILITY_SLOT_time] CHECK ([timeEnd] > [timeStart])
                    );
                    CREATE INDEX [IX_MATCH_AVAILABILITY_SLOT_matchId]
                        ON [MATCH_AVAILABILITY_SLOT] ([matchId]);
                    CREATE UNIQUE INDEX [UQ_MATCH_AVAILABILITY_SLOT]
                        ON [MATCH_AVAILABILITY_SLOT] ([matchId], [timeStart], [timeEnd]);
                END
                IF COL_LENGTH(N'MATCH_AVAILABILITY_SLOT', N'availableDate') IS NOT NULL
                BEGIN
                    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_MATCH_AVAILABILITY_SLOT' AND object_id = OBJECT_ID(N'[MATCH_AVAILABILITY_SLOT]'))
                        DROP INDEX [UQ_MATCH_AVAILABILITY_SLOT] ON [MATCH_AVAILABILITY_SLOT];
                    ALTER TABLE [MATCH_AVAILABILITY_SLOT] DROP COLUMN [availableDate];
                END
                ;WITH [DuplicateSlots] AS (
                    SELECT [matchAvailabilitySlotId],
                        ROW_NUMBER() OVER (
                            PARTITION BY [matchId], [timeStart], [timeEnd]
                            ORDER BY [matchAvailabilitySlotId]
                        ) AS [rowNumber]
                    FROM [MATCH_AVAILABILITY_SLOT]
                )
                DELETE FROM [MATCH_AVAILABILITY_SLOT]
                WHERE [matchAvailabilitySlotId] IN (
                    SELECT [matchAvailabilitySlotId] FROM [DuplicateSlots] WHERE [rowNumber] > 1
                );
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_MATCH_AVAILABILITY_SLOT' AND object_id = OBJECT_ID(N'[MATCH_AVAILABILITY_SLOT]'))
                    CREATE UNIQUE INDEX [UQ_MATCH_AVAILABILITY_SLOT]
                        ON [MATCH_AVAILABILITY_SLOT] ([matchId], [timeStart], [timeEnd]);
                """);

            dbContext.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[MATCH_SLOT_VOTE]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [MATCH_SLOT_VOTE] (
                        [matchSlotVoteId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_MATCH_SLOT_VOTE] PRIMARY KEY,
                        [matchId] int NOT NULL,
                        [playerId] int NOT NULL,
                        [courtId] int NOT NULL,
                        [startTime] datetime NOT NULL,
                        [endTime] datetime NOT NULL,
                        [createdAt] datetime NOT NULL CONSTRAINT [DF_MATCH_SLOT_VOTE_createdAt] DEFAULT (getutcdate()),
                        CONSTRAINT [FK_MATCH_SLOT_VOTE_MATCH] FOREIGN KEY ([matchId]) REFERENCES [MATCH]([matchId]) ON DELETE CASCADE,
                        CONSTRAINT [FK_MATCH_SLOT_VOTE_PLAYER] FOREIGN KEY ([playerId]) REFERENCES [PLAYER]([playerId]) ON DELETE CASCADE,
                        CONSTRAINT [FK_MATCH_SLOT_VOTE_COURT] FOREIGN KEY ([courtId]) REFERENCES [COURT]([courtId]) ON DELETE NO ACTION,
                        CONSTRAINT [CK_MATCH_SLOT_VOTE_time] CHECK ([endTime] > [startTime])
                    );
                    CREATE INDEX [IX_MATCH_SLOT_VOTE_matchId]
                        ON [MATCH_SLOT_VOTE] ([matchId]);
                    CREATE INDEX [IX_MATCH_SLOT_VOTE_court_time]
                        ON [MATCH_SLOT_VOTE] ([courtId], [startTime], [endTime]);
                    CREATE UNIQUE INDEX [UQ_MATCH_SLOT_VOTE_player_slot]
                        ON [MATCH_SLOT_VOTE] ([matchId], [playerId], [courtId], [startTime], [endTime]);
                END
                """);
        }
    }
}
