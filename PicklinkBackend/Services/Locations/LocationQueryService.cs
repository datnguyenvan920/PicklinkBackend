using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services.Locations;

public sealed class LocationQueryService
{
    private readonly ApplicationDbContext _dbContext;

    public LocationQueryService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<List<ProvinceResponse>> ListProvincesAsync(CancellationToken cancellationToken) =>
        _dbContext.Provinces
            .AsNoTracking()
            .OrderBy(province => province.Code)
            .Select(province => new ProvinceResponse
            {
                Code = province.Code,
                Name = province.Name,
                FullName = province.FullName
            })
            .ToListAsync(cancellationToken);

    public Task<List<WardResponse>> ListWardsAsync(string provinceCode, CancellationToken cancellationToken) =>
        _dbContext.Wards
            .AsNoTracking()
            .Where(ward => ward.ProvinceCode == provinceCode)
            .OrderBy(ward => ward.Code)
            .Select(ward => new WardResponse
            {
                Code = ward.Code,
                ProvinceCode = ward.ProvinceCode,
                Name = ward.Name,
                FullName = ward.FullName
            })
            .ToListAsync(cancellationToken);
}