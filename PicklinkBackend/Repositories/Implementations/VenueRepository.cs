using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Repositories.Implementations;

public class VenueRepository : IVenueRepository
{
    private readonly ApplicationDbContext _dbContext;

    public VenueRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IQueryable<Venue> Venues => _dbContext.Venues;
    public IQueryable<Staff> Staff => _dbContext.Staff;
    public IQueryable<VenueAuditLog> VenueAuditLogs => _dbContext.VenueAuditLogs;
    public IQueryable<BookingOperation> BookingOperations => _dbContext.BookingOperations;
    public IQueryable<VenueListingPayment> VenueListingPayments => _dbContext.VenueListingPayments;
    public IQueryable<Court> Courts => _dbContext.Courts;
    public IQueryable<VenueOwner> VenueOwners => _dbContext.VenueOwners;
    public IQueryable<OwnerBankAccount> OwnerBankAccounts => _dbContext.OwnerBankAccounts;

    public Task<Venue?> GetByIdAsync(int venueId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Venues.SingleOrDefaultAsync(v => v.VenueId == venueId, cancellationToken);
    }

    public Task<Venue?> GetApprovedVenueWithCourtsAsync(int venueId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Venues
            .Include(v => v.Courts)
            .SingleOrDefaultAsync(v => v.VenueId == venueId && v.ApprovalStatus == "Approved", cancellationToken);
    }

    public Task<List<Court>> GetCourtsByIdsAsync(List<int> courtIds, CancellationToken cancellationToken = default)
    {
        return _dbContext.Courts
            .Where(c => courtIds.Contains(c.CourtId))
            .ToListAsync(cancellationToken);
    }

    public Task<OwnerBankAccount?> GetOwnerBankAccountAsync(int ownerId, CancellationToken cancellationToken = default)
    {
        return _dbContext.OwnerBankAccounts
            .SingleOrDefaultAsync(b => b.OwnerId == ownerId, cancellationToken);
    }

    public async Task AddVenueAsync(Venue venue, CancellationToken cancellationToken = default)
    {
        await _dbContext.Venues.AddAsync(venue, cancellationToken);
    }

    public async Task AddStaffAsync(Staff staff, CancellationToken cancellationToken = default)
    {
        await _dbContext.Staff.AddAsync(staff, cancellationToken);
    }

    public async Task AddStaffRangeAsync(IEnumerable<Staff> staffList, CancellationToken cancellationToken = default)
    {
        await _dbContext.Staff.AddRangeAsync(staffList, cancellationToken);
    }

    public async Task AddAuditLogAsync(VenueAuditLog log, CancellationToken cancellationToken = default)
    {
        await _dbContext.VenueAuditLogs.AddAsync(log, cancellationToken);
    }

    public async Task AddVenueOwnerAsync(VenueOwner venueOwner, CancellationToken cancellationToken = default)
    {
        await _dbContext.VenueOwners.AddAsync(venueOwner, cancellationToken);
    }

    public async Task AddCourtAsync(Court court, CancellationToken cancellationToken = default)
    {
        await _dbContext.Courts.AddAsync(court, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    public IQueryable<Venue> GetApprovedVenuesQueryable()
    {
        return _dbContext.Venues.AsNoTracking()
            .Where(venue => venue.ApprovalStatus == "Approved");
    }

    public Task<Venue?> GetApprovedVenueForAvailabilityAsync(int venueId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Venues.AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.Courts)
            .Include(item => item.BookingRules)
            .SingleOrDefaultAsync(venue => venue.VenueId == venueId && venue.ApprovalStatus == "Approved", cancellationToken);
    }

    public Task<bool> IsApprovedVenueAsync(int venueId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Venues.AsNoTracking()
            .AnyAsync(item => item.VenueId == venueId && item.ApprovalStatus == "Approved", cancellationToken);
    }

    public Task<List<ProvinceResponse>> ListProvincesAsync(CancellationToken cancellationToken = default) =>
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

    public Task<List<WardResponse>> ListWardsAsync(string provinceCode, CancellationToken cancellationToken = default) =>
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
