using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.MatchmakingWorker;

var builder = Host.CreateApplicationBuilder(args);

// Register transitive ApplicationDbContext from the shared project
builder.Services.AddDbContextPool<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register HTTP Client for notifying the main API Server about match changes
builder.Services.AddHttpClient();

// Register MatchmakingWorker background service
builder.Services.AddHostedService<MatchmakingWorker>();

var host = builder.Build();
host.Run();
