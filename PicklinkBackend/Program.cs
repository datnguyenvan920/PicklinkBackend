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

            try
            {
                var app = builder.Build();

                if (app.Configuration.GetValue("Startup:RunSchemaChecks", false))
                {
                    app.RunSchemaChecks();
                }

                app.UsePicklinkPipeline();
                app.Run();
            }
            catch (AggregateException ex)
            {
                foreach (var inner in ex.InnerExceptions)
                {
                    Console.WriteLine($"[DI Error]: {inner.Message}");
                }
                throw;
            }
        }
    }
}
