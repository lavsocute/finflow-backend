using FinFlow.Infrastructure.Data;
using FinFlow.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pgvector.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Data;

public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        
        // Default to PostgreSQL for design-time - check which port
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5434;Database=finflow_db;Username=postgres;Password=postgres123",
            o => o.UseVector());

        // Create a null currentTenant for design-time (no tenant context needed for migrations).
        var currentTenant = new CurrentTenant();
        currentTenant.SetFromRequest(tenantId: null, membershipId: null, isSuperAdmin: false);

        return new ApplicationDbContext(optionsBuilder.Options, currentTenant);
    }
}
