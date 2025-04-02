

namespace MinimalAPIPractices.Domain;

public class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}