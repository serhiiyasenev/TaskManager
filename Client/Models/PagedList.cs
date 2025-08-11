namespace Client.Models;

public record PagedList<T>(
    List<T> Items,
    int TotalCount);