using Microsoft.EntityFrameworkCore;
using MinimalAPIPractices.Domain;



namespace MinimalAPIPractices.Infastructure.Context;

public class ApplicationContext : DbContext
{
    public ApplicationContext(DbContextOptions<ApplicationContext> options) : base(options)
    {
    }
    public DbSet<Rating> Ratings { get; set; }
    public DbSet<Movie> Movies { get; set; }
    public DbSet<User> Users { get; set; }
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite("Data Source=movieratings.db");
        }
        base.OnConfiguring(optionsBuilder);
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure Movie entity
        modelBuilder.Entity<Movie>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired();
            entity.Property(e => e.ReleaseYear).IsRequired();

            // Configure one-to-many relationship with Rating
            entity.HasMany(m => m.Ratings)
                .WithOne(r => r.Movie)
                .HasForeignKey("MovieId")
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure User entity
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserName).IsRequired();
            entity.Property(e => e.Email).IsRequired();
            entity.Property(e => e.Password).IsRequired();
            // Configure one-to-many relationship with Rating
            entity.HasMany(u => u.Ratings)
                .WithOne(r => r.User)
                .HasForeignKey("UserId")
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Rating entity
        modelBuilder.Entity<Rating>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RatingValue).IsRequired();

            // These relationships are already defined above, but we can specify additional configuration here if needed
        });

        base.OnModelCreating(modelBuilder);
    }
}