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
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidates = new[]
            {
                Path.Combine(new[] { directory.FullName, "PicklinkBackend" }.Concat(relativeSegments).ToArray()),
                Path.Combine(new[] { directory.FullName, "PicklinkBackend", "PicklinkBackend" }.Concat(relativeSegments).ToArray())
            };
            var candidate = candidates.FirstOrDefault(File.Exists);
            if (candidate is not null) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Could not locate schema source file.");
    }
}