using System.Runtime.Serialization;

namespace Api.Models;

public class BaseEntity
{
    public Guid Id { get; set; }
}

[DataContract]
public class Author : BaseEntity
{
    public string FirtName { get; set; } = "";

    public string LastName { get; set; } = "";

    public string FullName => $"{FirtName} {LastName}";
}