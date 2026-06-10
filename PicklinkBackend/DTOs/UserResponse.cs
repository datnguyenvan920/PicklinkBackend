using PicklinkBackend.Models;

namespace PicklinkBackend.DTOs;

public class UserResponse
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string UserType { get; set; } = string.Empty;
    public string? ProfileImageUrl { get; set; }
    public string? City { get; set; }

    public static UserResponse FromUser(User user)
    {
        return new UserResponse
        {
            UserId = user.UserId,
            Username = user.Username,
            Email = user.Email,
            UserType = user.UserType,
            ProfileImageUrl = user.ProfileImageUrl,
            City = user.City
        };
    }
}
