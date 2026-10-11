using BLL.Common;
using BLL.Models.Teams;
using DAL.Entities;

namespace BLL.Interfaces;

public interface ITeamManagementService
{
    Task<Result<Team>> CreateTeamAsync(string name, CancellationToken ct = default);
    Task<Result<Team>> UpdateTeamAsync(int teamId, string name, CancellationToken ct = default);
    Task<Result> DeleteTeamAsync(int teamId, CancellationToken ct = default);
    Task<Result<CreatedTeamInvitation>> CreateInvitationAsync(int teamId, string email, CancellationToken ct = default);
    Task<Result<List<TeamInvitationDetail>>> GetInvitationsAsync(int teamId, CancellationToken ct = default);
    Task<Result> RevokeInvitationAsync(int teamId, Guid invitationId, CancellationToken ct = default);
    Task<Result<TeamInvitationPreview>> PreviewInvitationAsync(string token, CancellationToken ct = default);
    Task<Result<AcceptedTeamInvitation>> AcceptInvitationAsync(string token, CancellationToken ct = default);
    Task<Result> RemoveMemberAsync(int teamId, int userId, CancellationToken ct = default);
    Task<Result> LeaveTeamAsync(CancellationToken ct = default);
}
