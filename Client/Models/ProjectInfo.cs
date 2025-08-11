namespace Client.Models;

public class ProjectInfo(string name)
{
    public int Id { get; set; }
    public string Name { get; set; } = name;
}