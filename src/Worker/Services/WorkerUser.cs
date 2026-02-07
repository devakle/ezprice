using EZPrice.Application.Common.Interfaces;

namespace EZPrice.Worker.Services;

// Background worker has no authenticated user.
public sealed class WorkerUser : IUser
{
    public string? Id => null;
    public List<string>? Roles => null;
}
