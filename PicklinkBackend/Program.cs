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
                    // One-time backfill for check-in codes created before the 6-char format;
                    // Next() has only ever produced 6-char codes since, so this converges to a
                    // no-op full-table scan on every cold start. Gated behind the same flag as
                    // RunSchemaChecks — flip it on for one deploy if a sweep is ever needed again.
                    app.NormalizeLegacyCheckInCodes();
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
