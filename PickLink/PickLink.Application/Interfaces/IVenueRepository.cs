using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Application.Interfaces;

using PickLink.Domain.Entities;

public interface IVenueRepository
{
    Task<Venue?> GetByIdAsync(int venueId);
    Task<IEnumerable<Venue>> GetNearbyAsync(decimal lat, decimal lng, double radiusKm); // BR-18
    Task AddAsync(Venue venue);
    Task UpdateAsync(Venue venue);
    Task SaveChangesAsync();
}
