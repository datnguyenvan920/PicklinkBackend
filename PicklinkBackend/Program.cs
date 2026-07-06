using PicklinkBackend.Startup;

namespace PicklinkBackend
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddPicklinkServices(builder.Configuration);
            builder.EnsureUploadDirectories();

            var app = builder.Build();

            if (app.Configuration.GetValue("Startup:RunSchemaChecks", false))
            {
                app.RunSchemaChecks();
            }

            app.UsePicklinkPipeline();
            app.Run();
        }
    }
}
