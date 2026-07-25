namespace PicklinkBackend.Tests;

public class MoneySchemaContractTests
{
    [Fact]
    public void MoneyUsesDecimalAcrossModelsAndDatabaseMigration()
    {
        var modelFiles = new[]
        {
            SourcePath("Models", "Booking.cs"),
            SourcePath("Models", "BookingSlot.cs"),
            SourcePath("Models", "Court.cs"),
            SourcePath("Models", "InventoryItem.cs"),
            SourcePath("Models", "Payment.cs")
        };
        var context = File.ReadAllText(SourcePath("Data", "ApplicationDbContext.cs"));
        var migration = File.ReadAllText(SourcePath("Migrations", "20260716180839_ConvertMoneyToDecimal.cs"));

        Assert.All(modelFiles, path => Assert.DoesNotContain("public double Amount", File.ReadAllText(path)));
        Assert.Contains("public decimal TotalAmount", File.ReadAllText(modelFiles[0]));
        Assert.Contains("public decimal CourtAmount", File.ReadAllText(modelFiles[1]));
        Assert.Contains("public decimal HourlyPrice", File.ReadAllText(modelFiles[2]));
        Assert.Contains("public decimal PricePerUnit", File.ReadAllText(modelFiles[3]));
        Assert.Contains("public decimal Amount", File.ReadAllText(modelFiles[4]));
        Assert.True(Count(context, "decimal(18,2)") >= 8);
        Assert.Equal(8, Count(migration, "migrationBuilder.AlterColumn<T>"));
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
            count++;
        return count;
    }

    private static string SourcePath(params string[] relativeSegments)
    {
        var cleanSegments = relativeSegments.FirstOrDefault() == "PicklinkBackend" ? relativeSegments[1..] : relativeSegments;
        var fileName = cleanSegments.Last();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var projectDir = Path.Combine(directory.FullName, "PicklinkBackend");
            if (Directory.Exists(projectDir))
            {
                var candidate = Path.Combine([projectDir, .. cleanSegments]);
                if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;

                var foundFile = Directory.GetFiles(projectDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (foundFile is not null) return foundFile;

                var foundDir = Directory.GetDirectories(projectDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (foundDir is not null) return foundDir;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}