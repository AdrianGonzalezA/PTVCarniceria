using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Central.Infrastructure;

public sealed class CentralAccessDbContext(DbContextOptions<CentralAccessDbContext> options) : DbContext(options)
{
    public DbSet<CentralCompany> Companies => Set<CentralCompany>();
    public DbSet<CentralAdministrator> Administrators => Set<CentralAdministrator>();
    public DbSet<CentralSession> Sessions => Set<CentralSession>();
    public DbSet<CentralAuditEvent> AuditEvents => Set<CentralAuditEvent>();
    public DbSet<CentralInstallation> Installations => Set<CentralInstallation>();
    public DbSet<CentralBranchCredential> BranchCredentials => Set<CentralBranchCredential>();
    public DbSet<CentralManufacturer> Manufacturers => Set<CentralManufacturer>();
    public DbSet<CentralManufacturerSession> ManufacturerSessions => Set<CentralManufacturerSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("central_access");

        modelBuilder.Entity<CentralCompany>(entity =>
        {
            entity.ToTable("companies");
            entity.HasKey(company => company.Id);
            entity.Property(company => company.Code).HasMaxLength(40).IsRequired();
            entity.Property(company => company.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(company => company.Code).IsUnique();
        });

        modelBuilder.Entity<CentralAdministrator>(entity =>
        {
            entity.ToTable("administrators");
            entity.HasKey(administrator => administrator.Id);
            entity.HasAlternateKey(administrator => new { administrator.CentralCompanyId, administrator.Id });
            entity.Property(administrator => administrator.Username).HasMaxLength(100).IsRequired();
            entity.Property(administrator => administrator.UsernameNormalized).HasMaxLength(100).IsRequired();
            entity.Property(administrator => administrator.PasswordHash).HasMaxLength(256).IsRequired();
            entity.HasIndex(administrator => new { administrator.CentralCompanyId,
                administrator.UsernameNormalized }).IsUnique();
            entity.HasOne<CentralCompany>().WithMany()
                .HasForeignKey(administrator => administrator.CentralCompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CentralSession>(entity =>
        {
            entity.ToTable("sessions");
            entity.HasKey(session => session.Id);
            entity.Property(session => session.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(session => session.TokenHash).IsUnique();
            entity.HasIndex(session => new { session.CentralCompanyId, session.AdministratorId,
                session.ExpiresAtUtc });
            entity.HasOne<CentralAdministrator>().WithMany()
                .HasForeignKey(session => new { session.CentralCompanyId, session.AdministratorId })
                .HasPrincipalKey(administrator => new { administrator.CentralCompanyId, administrator.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CentralAuditEvent>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(audit => audit.Id);
            entity.Property(audit => audit.Action).HasConversion<int>();
            entity.Property(audit => audit.Result).HasConversion<int>();
            entity.HasIndex(audit => new { audit.CentralCompanyId, audit.OccurredAtUtc });
        });

        modelBuilder.Entity<CentralInstallation>(entity =>
        {
            entity.ToTable("installations");
            entity.HasKey(installation => installation.InstallationId);
            entity.Property(installation => installation.Label).HasMaxLength(200).IsRequired();
            entity.Property(installation => installation.DestinationUrl).HasMaxLength(2048).IsRequired();
            entity.Property(installation => installation.SecretReference).HasMaxLength(200).IsRequired();
            entity.HasIndex(installation => new { installation.CentralCompanyId,
                installation.IsActive, installation.Label });
            entity.HasOne<CentralCompany>().WithMany()
                .HasForeignKey(installation => installation.CentralCompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CentralBranchCredential>(entity =>
        {
            entity.ToTable("branch_credentials");
            entity.HasKey(credential => credential.InstallationId);
            entity.Property(credential => credential.Nonce).HasMaxLength(12).IsRequired();
            entity.Property(credential => credential.Ciphertext).HasMaxLength(32).IsRequired();
            entity.Property(credential => credential.Tag).HasMaxLength(16).IsRequired();
            entity.HasOne<CentralInstallation>().WithOne()
                .HasForeignKey<CentralBranchCredential>(credential => credential.InstallationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CentralManufacturer>(entity =>
        {
            entity.ToTable("manufacturers");
            entity.HasKey(manufacturer => manufacturer.Id);
            entity.Property(manufacturer => manufacturer.Username).HasMaxLength(100).IsRequired();
            entity.Property(manufacturer => manufacturer.UsernameNormalized).HasMaxLength(100).IsRequired();
            entity.Property(manufacturer => manufacturer.PasswordHash).HasMaxLength(256).IsRequired();
            entity.HasIndex(manufacturer => manufacturer.UsernameNormalized).IsUnique();
        });

        modelBuilder.Entity<CentralManufacturerSession>(entity =>
        {
            entity.ToTable("manufacturer_sessions");
            entity.HasKey(session => session.Id);
            entity.Property(session => session.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(session => session.TokenHash).IsUnique();
            entity.HasIndex(session => new { session.ManufacturerId, session.ExpiresAtUtc });
            entity.HasOne<CentralManufacturer>().WithMany()
                .HasForeignKey(session => session.ManufacturerId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
