using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Repositories;

public interface IVenueRepository
{
    IQueryable<Venue> Venues { get; }
    IQueryable<Staff> Staff { get; }
    IQueryable<VenueAuditLog> VenueAuditLogs { get; }
    IQueryable<BookingOperation> BookingOperations { get; }
    IQueryable<VenueListingPayment> VenueListingPayments { get; }
    IQueryable<Court> Courts { get; }
    IQueryable<VenueOwner> VenueOwners { get; }
    IQueryable<OwnerBankAccount> OwnerBankAccounts { get; }
    IQueryable<RatingHistory> RatingHistories { get; }

    Task<Venue?> GetByIdAsync(int venueId, CancellationToken cancellationToken = default);
    Task<Venue?> GetApprovedVenueWithCourtsAsync(int venueId, CancellationToken cancellationToken = default);
    Task<List<Court>> GetCourtsByIdsAsync(List<int> courtIds, CancellationToken cancellationToken = default);
    Task<bool> CourtHasDependentsAsync(int courtId, CancellationToken cancellationToken = default);
    void RemoveCourt(Court court);
    void RemoveAmenities(IEnumerable<Amenity> amenities);
    Task<OwnerBankAccount?> GetOwnerBankAccountAsync(int ownerId, CancellationToken cancellationToken = default);
    Task AddVenueAsync(Venue venue, CancellationToken cancellationToken = default);
    Task AddStaffAsync(Staff staff, CancellationToken cancellationToken = default);
    Task AddStaffRangeAsync(IEnumerable<Staff> staffList, CancellationToken cancellationToken = default);
    Task AddAuditLogAsync(VenueAuditLog log, CancellationToken cancellationToken = default);
    Task AddVenueOwnerAsync(VenueOwner venueOwner, CancellationToken cancellationToken = default);
    Task AddCourtAsync(Court court, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
    IQueryable<Venue> GetApprovedVenuesQueryable();
    Task<Venue?> GetApprovedVenueForAvailabilityAsync(int venueId, CancellationToken cancellationToken = default);
    Task<bool> IsApprovedVenueAsync(int venueId, CancellationToken cancellationToken = default);
    Task<List<ProvinceResponse>> ListProvincesAsync(CancellationToken cancellationToken = default);
    Task<List<WardResponse>> ListWardsAsync(string provinceCode, CancellationToken cancellationToken = default);
}
