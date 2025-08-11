namespace BLL.Models;

public record PagedList<T>(List<T> Items, int TotalCount);
