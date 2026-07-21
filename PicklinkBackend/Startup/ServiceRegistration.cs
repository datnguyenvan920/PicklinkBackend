using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PicklinkBackend.Data;
using PicklinkBackend.Services.Admin;
using PicklinkBackend.Services.Auth;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Community;
using PicklinkBackend.Services.Infrastructure;
using PicklinkBackend.Services.ListingFees;
using PicklinkBackend.Services.Locations;
using PicklinkBackend.Services.Matches;
using PicklinkBackend.Services.Notifications;
using PicklinkBackend.Services.Owner;
using PicklinkBackend.Services.Payments;
using PicklinkBackend.Services.Players;
using PicklinkBackend.Services.Schedules;
using PicklinkBackend.Services.Staff;
using PicklinkBackend.Services.Ticketing;
using PicklinkBackend.Services.Venues;

namespace PicklinkBackend.Startup;

internal static class ServiceRegistration
{
    internal const string FrontendCorsPolicy = "FrontendPolicy";

    internal static IServiceCollection AddPicklinkServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtKey = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured.");

        services.AddDbContextPool<ApplicationDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sql => sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));

        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        });
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<PlayerScheduleConflictService>();
        services.AddScoped<PlayerBookingReviewService>();
        services.AddScoped<PlayerProfileService>();
        services.AddScoped<OwnerStaffService>();
        services.AddScoped<OwnerOperationQueryService>();
        services.AddScoped<StaffOperationService>();
        services.AddScoped<TicketingService>();
        services.AddSingleton<IGoogleAuthService, GoogleAuthService>();
        services.Configure<EmailOptions>(configuration.GetSection("Email"));
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<CloudinarySignatureService>();
        services.AddScoped<LocalUploadService>();
        services.AddScoped<AdminBookingQueryService>();
        services.AddScoped<AuthService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<PaymentServiceDependencies>();
        services.AddScoped<OwnerVenueService>();
        services.AddScoped<OwnerVenueServiceDependencies>();
        services.AddScoped<PlayerBookingService>();
        services.AddScoped<PlayerBookingServiceDependencies>();
        services.AddScoped<MatchService>();
        services.AddScoped<MatchServiceDependencies>();
        services.AddScoped<MatchmakingService>();
        services.AddScoped<CommunityService>();
        services.AddScoped<CommunityServiceDependencies>();
        services.AddScoped<CommunityDiscoveryService>();
        services.AddScoped<CommunityDirectConversationService>();
        services.AddScoped<AdminDashboardService>();
        services.AddScoped<AdminListingFeeSettingService>();
        services.AddScoped<AdminListingFeePaymentService>();
        services.AddScoped<AdminVenueQueryService>();
        services.AddScoped<AdminVenueApprovalService>();
        services.AddScoped<VenueNearbyQueryService>();
        services.AddScoped<AdminReviewQueryService>();
        services.AddScoped<AdminReviewModerationService>();
        services.AddScoped<AdminReportQueryService>();
        services.AddScoped<AdminReportReviewService>();
        services.AddScoped<AdminSettingService>();
        services.AddScoped<AdminUserQueryService>();
        services.AddScoped<AdminUserLockService>();
        services.AddScoped<NotificationQueryService>();
        services.AddScoped<LocationQueryService>();
        services.AddSingleton<GeocodingService>();
        services.AddScoped<NotificationCommandService>();
        services.AddScoped<CommunityReportSubmissionService>();
        services.AddSingleton<ScheduleRealtimeNotifier>();
        services.AddSingleton<PaymentRealtimeNotifier>();
        services.AddSingleton<MatchRealtimeNotifier>();
        services.AddSingleton<VenueRealtimeNotifier>();
        services.AddSingleton<NotificationRealtimeNotifier>();
        services.AddScoped<NotificationService>();
        services.AddMemoryCache();
        services.AddHttpClient();
        services.AddHostedService<BookingHoldExpirationService>();
        services.AddHostedService<ListingFeeReminderService>();
        services.AddCors(options =>
        {
            options.AddPolicy(FrontendCorsPolicy, policy =>
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
        services.AddPicklinkRateLimits();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = configuration["Jwt:Issuer"],
                    ValidAudience = configuration["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                    ClockSkew = TimeSpan.Zero
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var userIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                        if (!int.TryParse(userIdValue, out var userId))
                        {
                            context.Fail("Token does not contain a valid user identifier.");
                            return;
                        }

                        var dbContext = context.HttpContext.RequestServices
                            .GetRequiredService<ApplicationDbContext>();
                        var account = await dbContext.Users
                            .AsNoTracking()
                            .Where(user => user.UserId == userId)
                            .Select(user => new { user.IsLocked })
                            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

                        if (account is null || account.IsLocked)
                        {
                            context.Fail("Account is unavailable.");
                        }
                    }
                };
            });

        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
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

        return services;
    }
}
