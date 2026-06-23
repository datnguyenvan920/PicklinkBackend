namespace PicklinkBackend.Models;

public class OwnerBankAccount
{
    public int OwnerBankAccountId { get; set; }
    public int OwnerId { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountHolderName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual VenueOwner Owner { get; set; } = null!;
}
