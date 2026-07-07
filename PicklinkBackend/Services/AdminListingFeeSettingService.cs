using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services;

public sealed class AdminListingFeeSettingService
{
    private readonly ApplicationDbContext _dbContext;

    public AdminListingFeeSettingService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ListingFeeSettingsResponse> GetAsync(CancellationToken cancellationToken)
    {
        return Map(await LatestSetting(cancellationToken));
    }

    public async Task<ListingFeeSettingUpdateResult> UpdateAsync(
        ListingFeeSettingsRequest request,
        int? currentUserId,
        CancellationToken cancellationToken)
    {
        if (request.PricePerCourtPerMonth <= 0 || request.PricePerCourtPerMonth > 100_000_000)
        {
            return ListingFeeSettingUpdateResult.BadRequest("Don gia phai lon hon 0 va khong vuot qua 100.000.000d.");
        }

        var setting = new ListingFeeSetting
        {
            PricePerCourtPerMonth = request.PricePerCourtPerMonth,
            UpdatedAt = DateTime.UtcNow,
            UpdatedByUserId = currentUserId
        };
        _dbContext.ListingFeeSettings.Add(setting);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ListingFeeSettingUpdateResult.Success(Map(setting));
    }

    private async Task<ListingFeeSetting?> LatestSetting(CancellationToken cancellationToken) =>
        await _dbContext.ListingFeeSettings.AsNoTracking()
            .OrderByDescending(setting => setting.UpdatedAt)
            .ThenByDescending(setting => setting.ListingFeeSettingId)
            .FirstOrDefaultAsync(cancellationToken);

    private static ListingFeeSettingsResponse Map(ListingFeeSetting? setting) => new()
    {
        ListingFeeSettingId = setting?.ListingFeeSettingId ?? 0,
        PricePerCourtPerMonth = setting?.PricePerCourtPerMonth ?? 0,
        UpdatedAt = setting?.UpdatedAt
    };
}

public sealed record ListingFeeSettingUpdateResult(
    ListingFeeSettingUpdateResultStatus Status,
    ListingFeeSettingsResponse? Setting,
    string? ErrorMessage)
{
    public static ListingFeeSettingUpdateResult Success(ListingFeeSettingsResponse setting) =>
        new(ListingFeeSettingUpdateResultStatus.Success, setting, ErrorMessage: null);

    public static ListingFeeSettingUpdateResult BadRequest(string errorMessage) =>
        new(ListingFeeSettingUpdateResultStatus.BadRequest, Setting: null, errorMessage);
}

public enum ListingFeeSettingUpdateResultStatus
{
    Success,
    BadRequest
}