// Um pedaço mínimo do EF Core, com os mesmos nomes e namespaces, para a fixture compilar sem o pacote.
using System.Threading;

namespace Microsoft.EntityFrameworkCore
{
    public abstract class DbContext
    {
        public void Add<T>(T entity) where T : class { }
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    public class DbSet<T> : List<T> where T : class { }
}
