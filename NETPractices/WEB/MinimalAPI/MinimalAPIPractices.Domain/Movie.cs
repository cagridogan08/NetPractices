
namespace MinimalAPIPractices.Domain;

public class Movie : BaseEntity
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Genre { get; set; } = "";
    public int ReleaseYear { get; set; }
    public string Director { get; set; } = "";

    public virtual ICollection<Rating> Ratings { get; set; } = new List<Rating>();

}