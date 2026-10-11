using BLL.Common;
using BLL.Models.AI;

namespace BLL.Services.AI;

public static class TaskExtractionOutputValidator
{
    public static Result<IReadOnlyList<AiValidatedTaskCandidate>> Validate(
        TaskExtractionProviderInput input, TaskExtractionProviderResult result, int maxItems = 25)
        => throw new NotImplementedException();
}
