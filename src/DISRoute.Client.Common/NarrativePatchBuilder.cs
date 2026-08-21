namespace DISRoute.Client.Common;

public static class NarrativePatchBuilder
{
    public static NarrativePatchDelta Build(string original, string edited)
    {
        original ??= string.Empty;
        edited ??= string.Empty;

        var prefix = 0;
        var sharedLength = Math.Min(original.Length, edited.Length);
        while (prefix < sharedLength && original[prefix] == edited[prefix]) prefix++;

        var suffix = 0;
        var originalRemaining = original.Length - prefix;
        var editedRemaining = edited.Length - prefix;
        while (suffix < originalRemaining && suffix < editedRemaining
               && original[original.Length - suffix - 1] == edited[edited.Length - suffix - 1])
        {
            suffix++;
        }

        var deleteCount = original.Length - prefix - suffix;
        var insertLength = edited.Length - prefix - suffix;
        var insertText = insertLength == 0 ? string.Empty : edited.Substring(prefix, insertLength);
        return new NarrativePatchDelta(prefix, deleteCount, insertText);
    }
}

public sealed record NarrativePatchDelta(int Start, int DeleteCount, string InsertText)
{
    public bool HasChanges => DeleteCount != 0 || InsertText.Length != 0;

    public string Apply(string source)
    {
        source ??= string.Empty;
        if (Start < 0 || DeleteCount < 0 || Start + DeleteCount > source.Length)
            throw new ArgumentOutOfRangeException(nameof(source), "Narrative patch does not fit the supplied source text.");
        return source.Remove(Start, DeleteCount).Insert(Start, InsertText);
    }
}
