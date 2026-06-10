using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Application.Interfaces;

using PickLink.Domain.Entities;

public interface IBookingRepository
{
    Task<Booking?> GetByIdAsync(int bookingId);
    Task<int> CountActiveByPlayerIdAsync(int playerId); // BR-10: max 3
    Task AddAsync(Booking booking);
    Task UpdateAsync(Booking booking);
    Task SaveChangesAsync();
}
