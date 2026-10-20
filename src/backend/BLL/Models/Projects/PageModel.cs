namespace BLL.Models.Projects;

/// <summary>
/// Model of page size and number
/// </summary>
/// <param name="PageSize">Page size. Number of elements in one page</param>
/// <param name="PageNumber">Sequence number of page. Starts from 1 (not 0)</param>
/// <example>PageSize = 3 and PageNumber = 2 and List of elements = [1, 2, 3, 4, 5, 6, 7, 8]. Result will be [4, 5, 6]</example>
public record PageModel(int PageSize, int PageNumber);
