
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
