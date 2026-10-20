namespace BLL.Services.AI;

public static class TaskExtractionSchema
{
    public const string Json = """
    {"type":"object","additionalProperties":false,"required":["items"],"properties":{"items":{"type":"array","items":{
      "type":"object","additionalProperties":false,
      "required":["title","description","proposedAssigneeId","originalAssigneeText","proposedDueAtUtc","originalDueText","sourceQuote","sourceStart","sourceLength","isSuggestion","interpretations","clarificationReasons"],
      "properties":{
        "title":{"type":"string"},"description":{"type":"string"},
        "proposedAssigneeId":{"type":["integer","null"]},"originalAssigneeText":{"type":["string","null"]},
        "proposedDueAtUtc":{"type":["string","null"]},"originalDueText":{"type":["string","null"]},
        "sourceQuote":{"type":"string"},"sourceStart":{"type":"integer"},"sourceLength":{"type":"integer"},
        "isSuggestion":{"type":"boolean"},"interpretations":{"type":"array","items":{"type":"string"}},
        "clarificationReasons":{"type":"array","items":{"type":"string"}}
      }}}}}
    """;
}
