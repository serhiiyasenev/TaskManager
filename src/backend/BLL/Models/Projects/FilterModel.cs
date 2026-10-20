namespace BLL.Models.Projects;

public record FilterModel(
    string Name = null,
    string Description = null,
    string AuthorFirstName = null,
    string AuthorLastName = null,
    string TeamName = null);
