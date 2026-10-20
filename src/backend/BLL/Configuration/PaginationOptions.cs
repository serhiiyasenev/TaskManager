namespace BLL.Configuration;

public class PaginationOptions
{
    public const string SectionName = "Pagination";
    
    public int DefaultPageSize { get; set; } = 20;
    public int MaxPageSize { get; set; } = 100;
}
