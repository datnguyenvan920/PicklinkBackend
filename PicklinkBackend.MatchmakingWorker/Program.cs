using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.MatchmakingWorker;
using PicklinkBackend.Services.Shared;

var builder = Host.CreateApplicationBuilder(args);

// Register transitive ApplicationDbContext from the shared project
builder.Services.AddDbContextPool<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register Firebase service for real-time queue subscription
builder.Services.AddSingleton<IFirebaseService, FirebaseService>();

// Register HTTP Client for notifying the main API Server about match changes
builder.Services.AddHttpClient();

// Register MatchmakingWorker background service
builder.Services.AddHostedService<MatchmakingWorker>();

var host = builder.Build();
host.Run();
