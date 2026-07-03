using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PicklinkBackend.Controllers;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services;
using Xunit;

namespace PicklinkBackend.Tests;

public class MatchPlayerRecommendationTests
{
    [Fact]
    public async Task Recommendations_UseLatestPlayerSearchLocationAndRadius()
    {
        await using var dbContext = CreateDbContext();
        var current = AddPlayer(dbContext, 1, 101, "Host", 3, "Hưng Yên", "Nghĩa Trụ");
        var nearby = AddPlayer(dbContext, 2, 102, "Nearby", 3, "Hưng Yên", "Nghĩa Trụ");
        var outside = AddPlayer(dbContext, 3, 103, "Outside", 3, "Hưng Yên", "Nghĩa Trụ");
        AddSearchLocation(nearby, 20.952, 105.971);
        AddSearchLocation(outside, 21.052, 105.971);
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, current.UserId);
        var result = await controller.GetPlayerRecommendations(
            radiusKm: 5,
            latitude: 20.9517,
            longitude: 105.9707,
            province: "Hưng Yên",
            ward: "Nghĩa Trụ",
            minSkillLevel: 2,
            maxSkillLevel: 4);

        var recommendations = AssertOk(result);
        var player = Assert.Single(recommendations);
        Assert.Equal(nearby.PlayerId, player.PlayerId);
        Assert.NotNull(player.DistanceKm);
        Assert.InRange(player.DistanceKm!.Value, 0, 1);
    }

    [Fact]
    public async Task Recommendations_FallBackToNormalizedProfileAreaWithoutCoordinates()
    {
        await using var dbContext = CreateDbContext();
        var current = AddPlayer(dbContext, 1, 201, "Host", 3, "Hưng Yên", "Nghĩa Trụ");
        var sameWard = AddPlayer(dbContext, 2, 202, "Same ward", 3, "Tỉnh Hưng Yên", "Xã Nghĩa Trụ");
        AddPlayer(dbContext, 3, 203, "Other ward", 3, "Tỉnh Hưng Yên", "Xã Long Hưng");
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, current.UserId);
        var result = await controller.GetPlayerRecommendations(
            radiusKm: 5,
            province: "Hung Yen",
            ward: "Nghia Tru",
            minSkillLevel: 2,
            maxSkillLevel: 4);

        var recommendations = AssertOk(result);
        var player = Assert.Single(recommendations);
        Assert.Equal(sameWard.PlayerId, player.PlayerId);
        Assert.Null(player.DistanceKm);
        Assert.Equal("Cùng xã/phường trong hồ sơ", player.MatchReason);
    }

    private static List<MatchPlayerRecommendationResponse> AssertOk(
        ActionResult<List<MatchPlayerRecommendationResponse>> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<List<MatchPlayerRecommendationResponse>>(ok.Value);
    }

    private static MatchController CreateController(ApplicationDbContext dbContext, int userId)
    {
        var controller = new MatchController(
            dbContext,
            new ConfigurationBuilder().Build(),
            new ScheduleRealtimeNotifier(),
            new MatchRealtimeNotifier(),
            new PlayerScheduleConflictService(dbContext));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                    "Test"))
            }
        };
        return controller;
    }

    private static Player AddPlayer(
        ApplicationDbContext dbContext,
        int playerId,
        int userId,
        string username,
        double skillLevel,
        string city,
        string commune)
    {
        var user = new User
        {
            UserId = userId,
            Username = username,
            Email = $"{username.Replace(" ", string.Empty).ToLowerInvariant()}@example.com",
            PasswordHash = "test",
            UserType = "Player",
            City = city,
            Commune = commune
        };
        var player = new Player
        {
            PlayerId = playerId,
            UserId = userId,
            User = user,
            SkillLevel = skillLevel,
            Prestige = 100
        };
        dbContext.Players.Add(player);
        return player;
    }

    private static void AddSearchLocation(Player player, double latitude, double longitude)
    {
        player.HostedMatches.Add(new Match
        {
            HostPlayerId = player.PlayerId,
            HostPlayer = player,
            MatchType = "2vs2",
            MatchSkillLevel = 3,
            MinSkillLevel = 2,
            MaxSkillLevel = 4,
            RequiredPlayerCount = 4,
            Status = "Recruiting",
            SearchLatitude = latitude,
            SearchLongitude = longitude,
            SearchRadiusKm = 5,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
