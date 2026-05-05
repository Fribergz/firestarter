using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Firestarter.Core.Data;

public class FirestarterDbContextFactory : IDesignTimeDbContextFactory<FirestarterDbContext>
{
    public FirestarterDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FirestarterDbContext>()
            .UseSqlite("Data Source=firestarter.design.db")
            .Options;
        return new FirestarterDbContext(options);
    }
}
