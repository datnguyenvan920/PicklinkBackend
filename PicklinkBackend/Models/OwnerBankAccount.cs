namespace PicklinkBackend.Models;

public class OwnerBankAccount
{
    public int OwnerBankAccountId { get; set; }
    public int OwnerId { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountNo { get => AccountNumber; set => AccountNumber = value; }
    public string AccountHolderName { get; set; } = string.Empty;

    /// <summary>
    /// The owner's SePay Secret API token, encrypted at rest by IEncryptionService.
    /// Never expose this value outside the backend: responses carry a mask instead.
    /// </summary>
    public string? SePayApiToken { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual VenueOwner Owner { get; set; } = null!;
}
