// Um pedaço mínimo do EF Core, com os mesmos nomes e namespaces, para a fixture compilar sem o pacote.
using System.Threading;

namespace Microsoft.EntityFrameworkCore
{
    public abstract class DbContext
    {
        public DbSet<T> Set<T>() where T : class => new();
        public DatabaseFacade Database { get; } = new();
        public void Add<T>(T entity) where T : class { }
        public void Remove<T>(T entity) where T : class { }
        public int SaveChanges() => 0;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    public class DatabaseFacade { }

    public class DbSet<T> : List<T> where T : class { }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IEnumerable<T> AsNoTracking<T>(this IEnumerable<T> source) => source;
        public static Task<List<T>> ToListAsync<T>(this IEnumerable<T> source, CancellationToken ct = default) => Task.FromResult(source.ToList());
        public static Task<T?> FirstOrDefaultAsync<T>(this IEnumerable<T> source, Func<T, bool> predicate, CancellationToken ct = default) => Task.FromResult(source.FirstOrDefault(predicate));
    }

    public static class RelationalQueryableExtensions
    {
        public static IEnumerable<T> FromSqlRaw<T>(this DbSet<T> source, string sql, params object[] parameters) where T : class => source;
    }

    public static class RelationalDatabaseFacadeExtensions
    {
        public static int ExecuteSqlRaw(this DatabaseFacade database, string sql, params object[] parameters) => 0;
    }
}
