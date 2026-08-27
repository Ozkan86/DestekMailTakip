namespace task_list.Models;

public static class AvatarHelper
{
    public static string Initials(string? name, string? address)
    {
        var source = !string.IsNullOrWhiteSpace(name) ? name : address;
        if (string.IsNullOrWhiteSpace(source))
        {
            return "?";
        }

        var parts = source.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
        }

        return source.Substring(0, Math.Min(2, source.Length)).ToUpperInvariant();
    }
}
