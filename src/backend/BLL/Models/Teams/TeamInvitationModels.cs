using System.ComponentModel.DataAnnotations;

namespace BLL.Models.Teams;

public sealed record CreateTeamInvitationRequest([property: Required, EmailAddress, MaxLength(256)] string Email);
public sealed record AcceptTeamInvitationRequest([property: Required, MinLength(32), MaxLength(128)] string Token);
public sealed record CreatedTeamInvitation(Guid Id, string Email, string Token, DateTime ExpiresAtUtc);
public sealed record TeamInvitationDetail(Guid Id, string Email, DateTime CreatedAtUtc, DateTime ExpiresAtUtc,
    DateTime? AcceptedAtUtc, DateTime? RevokedAtUtc);
public sealed record TeamInvitationPreview(string TeamName, bool Expired);
public sealed record AcceptedTeamInvitation(int TeamId);
