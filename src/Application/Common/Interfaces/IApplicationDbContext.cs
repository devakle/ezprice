using EZPrice.Domain.Entities;

namespace EZPrice.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<TodoList> TodoLists { get; }

    DbSet<TodoItem> TodoItems { get; }

    DbSet<Offer> Offers { get; }

    DbSet<SearchQuery> SearchQueries { get; }

    DbSet<Source> Sources { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
