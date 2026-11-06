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
