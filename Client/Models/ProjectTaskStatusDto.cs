namespace Client.Models;

public record ProjectTaskStatusDto(
    int ProjectId,
    string ProjectName,
    int ToDo,
    int InProgress,
    int Done,
    int Canceled,
    int Total);