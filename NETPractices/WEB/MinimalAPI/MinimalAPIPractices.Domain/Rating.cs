
namespace MinimalAPIPractices.Domain;

public class Rating : BaseEntity
{
    public string? RatingNote { get; set; }

    public virtual User User { get; set; }

    public virtual Movie Movie { get; set; }

    public int RatingValue { get; set; }
}