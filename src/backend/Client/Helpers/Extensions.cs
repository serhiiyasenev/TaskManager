using System.Text;

namespace Client.Helpers;

public static class Extensions
{
    public static string ToPrintString(this Dictionary<string, int> dictionary)
    {
        var sb = new StringBuilder();
        foreach (var kvp in dictionary)
        {
            sb.AppendLine($"{kvp.Key} - tasks count: {kvp.Value}");
        }
        return sb.ToString();
    }
}