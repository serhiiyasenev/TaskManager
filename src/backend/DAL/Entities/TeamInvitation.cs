namespace DAL.Entities;

public sealed class TeamInvitation
{
    public Guid Id { get; set; }
    public int TeamId { get; set; }
    public int IssuedByUserId { get; set; }
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public int? AcceptedByUserId { get; set; }
    public Guid Version { get; set; }
}
