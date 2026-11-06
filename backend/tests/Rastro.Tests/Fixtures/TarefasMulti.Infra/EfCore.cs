// Um pedaço mínimo do EF Core, com os mesmos nomes e namespaces, para a fixture compilar sem o pacote.
using System.Threading;

namespace Microsoft.EntityFrameworkCore
{
    public abstract class DbContext
    {
        public DbSet<T> Set<T>() where T : class => new();
        public Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T> Entry<T>(T entity) where T : class => new();
        public void Add<T>(T entity) where T : class { }
        public void Remove<T>(T entity) where T : class { }
        public DatabaseFacade Database { get; } = new();
        public int SaveChanges() => 0;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    public class DbSet<T> : List<T> where T : class
    {
        public Task<T?> FindAsync(params object[] keys) => Task.FromResult<T?>(null);
        public new void Remove(T entity) { }
        public void RemoveRange(IEnumerable<T> entities) { }
        public void Update(T entity) { }
    }

    public class DatabaseFacade { }

    public class SetPropertyCalls<T>
    {
        public SetPropertyCalls<T> SetProperty<P>(Func<T, P> selector, P value) => this;
    }

    public static class RelationalDatabaseFacadeExtensions
    {
        public static Task<int> ExecuteSqlInterpolatedAsync(this DatabaseFacade database, FormattableString sql, CancellationToken ct = default) => Task.FromResult(0);
        public static Task<int> ExecuteSqlRawAsync(this DatabaseFacade database, string sql, params object[] parameters) => Task.FromResult(0);
    }

    public class DbFunctions { }

    public static class EF
    {
        public static DbFunctions Functions { get; } = new();
    }

    public static class DbFunctionsExtensions
    {
        public static bool Like(this DbFunctions functions, string matchExpression, string pattern) => true;
        public static bool ILike(this DbFunctions functions, string matchExpression, string pattern) => true;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IEnumerable<T> AsNoTracking<T>(this IEnumerable<T> source) => source;
        public static Task<int> ExecuteUpdateAsync<T>(this IEnumerable<T> source, Func<SetPropertyCalls<T>, SetPropertyCalls<T>> setters, CancellationToken ct = default) => Task.FromResult(0);
        public static Task<int> ExecuteDeleteAsync<T>(this IEnumerable<T> source, CancellationToken ct = default) => Task.FromResult(0);
        public static Task<List<T>> ToListAsync<T>(this IEnumerable<T> source, CancellationToken ct = default) => Task.FromResult(source.ToList());
        public static Task<T?> FirstOrDefaultAsync<T>(this IEnumerable<T> source, Func<T, bool> predicate, CancellationToken ct = default) => Task.FromResult(source.FirstOrDefault(predicate));
    }
}

namespace Microsoft.EntityFrameworkCore.ChangeTracking
{
    public class PropertyValues
    {
        public void SetValues(object values) { }
    }

    public class EntityEntry<T> where T : class
    {
        public PropertyValues CurrentValues { get; } = new();
    }
}
