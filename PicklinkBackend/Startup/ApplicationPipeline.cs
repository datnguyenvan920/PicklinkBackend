namespace PicklinkBackend.Startup;

internal static class ApplicationPipeline
{
    internal static WebApplication UsePicklinkPipeline(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        if (app.Configuration.GetValue("HttpsRedirection:Enabled", !app.Environment.IsDevelopment()))
        {
            app.UseHttpsRedirection();
        }

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
        app.UseAuthorization();
        app.MapControllers();

        return app;
    }
}
