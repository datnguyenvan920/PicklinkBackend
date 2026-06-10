using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Application.Interfaces;

using PickLink.Domain.Entities;

public interface IPlayerRepository
{
    Task<Player?> GetByIdAsync(int playerId);
    Task<Player?> GetByUserIdAsync(int userId);
    Task AddAsync(Player player);
    Task UpdateAsync(Player player);
    Task SaveChangesAsync();
}
