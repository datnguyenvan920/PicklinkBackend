using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace PicklinkBackend.Startup;

internal static class ApplicationPipeline
{
    internal static WebApplication UsePicklinkPipeline(this WebApplication app)
    {
        app.UseExceptionHandler();

        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Picklink Backend API v1");
            c.RoutePrefix = "swagger";
        });

        if (app.Configuration.GetValue("HttpsRedirection:Enabled", !app.Environment.IsDevelopment()))
        {
            app.UseHttpsRedirection();
        }

        app.UseRouting();
        app.UseCors(ServiceRegistration.FrontendCorsPolicy);
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
        app.UseRateLimiter();
        app.UseAuthorization();
        app.MapControllers();
        app.MapGet("/", () => Results.Ok(new
        {
            name = "Picklink Backend API",
            status = "Online",
            timestamp = DateTime.UtcNow,
            documentation = "/swagger"
        }));
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("live")
        });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready")
        });

        return app;
    }
}
