using System.Globalization;

namespace Lumen.Core.Selection;

/// <summary>
/// The outcome of parsing a page-range expression. <see cref="Error"/> is non-null only when the
/// input was invalid, in which case <see cref="Pages"/> is empty.
/// </summary>
public readonly record struct PageRangeResult(IReadOnlySet<int> Pages, string? Error)
{
    public bool IsValid => Error is null;
}

/// <summary>
/// Parses page-range expressions such as <c>1-5, 12, 20-24</c> into a set of 1-based page numbers.
/// </summary>
/// <remarks>
/// This parser never throws. Invalid input is reported through <see cref="PageRangeResult.Error"/>
/// so the UI can render the problem inline next to the textbox rather than surfacing an exception.
/// </remarks>
public static class PageRangeParser
{
    private static readonly IReadOnlySet<int> Empty = new HashSet<int>();

    /// <summary>
    /// Parses <paramref name="input"/> against a document of <paramref name="pageCount"/> pages.
    /// </summary>
    /// <param name="input">An expression such as <c>1-5, 12, 20-24</c>.</param>
    /// <param name="pageCount">Total pages in the document. Page numbers are 1-based.</param>
    public static PageRangeResult Parse(string? input, int pageCount)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new PageRangeResult(Empty, "Enter a page range, for example 1-5, 12, 20-24.");
        }

        if (pageCount <= 0)
        {
            return new PageRangeResult(Empty, "No document is open.");
        }

        var tokens = input.Split(',');
        var pages = new SortedSet<int>();

        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i].Trim();

            if (token.Length == 0)
            {
                // A trailing comma is a natural thing to type mid-edit, so tolerate it.
                // An empty token anywhere else is a genuine mistake.
                if (i == tokens.Length - 1)
                {
                    continue;
                }

                return new PageRangeResult(Empty, "Remove the empty entry between the commas.");
            }

            var error = ParseToken(token, pageCount, pages);
            if (error is not null)
            {
                return new PageRangeResult(Empty, error);
            }
        }

        return pages.Count == 0
            ? new PageRangeResult(Empty, "Enter a page range, for example 1-5, 12, 20-24.")
            : new PageRangeResult(pages, null);
    }

    private static string? ParseToken(string token, int pageCount, SortedSet<int> pages)
    {
        var dash = token.IndexOf('-');

        if (dash < 0)
        {
            if (!TryParsePage(token, out var single))
            {
                return $"\"{token}\" is not a page number.";
            }

            if (single < 1 || single > pageCount)
            {
                return $"Page {single} is outside this document, which has {pageCount} pages.";
            }

            pages.Add(single);
            return null;
        }

        // Reject "1-2-3", "1--3", "-5" and "1-" before attempting to read the numbers,
        // so the message names the token the user actually typed.
        if (token.IndexOf('-', dash + 1) >= 0)
        {
            return $"\"{token}\" is not a valid range.";
        }

        var startText = token[..dash].Trim();
        var endText = token[(dash + 1)..].Trim();

        if (!TryParsePage(startText, out var start) || !TryParsePage(endText, out var end))
        {
            return $"\"{token}\" is not a valid range.";
        }

        if (start < 1 || end < 1)
        {
            return "Page numbers start at 1.";
        }

        if (start > end)
        {
            return $"\"{token}\" runs backwards. Write it as {end}-{start}.";
        }

        if (end > pageCount)
        {
            return $"Page {end} is outside this document, which has {pageCount} pages.";
        }

        for (var page = start; page <= end; page++)
        {
            pages.Add(page);
        }

        return null;
    }

    /// <summary>
    /// Accepts only plain digits. This rejects "+3", "-3", "1e3", thousands separators, and
    /// values that overflow <see cref="int"/>, all of which would otherwise slip through
    /// a permissive <c>int.TryParse</c>.
    /// </summary>
    private static bool TryParsePage(string text, out int value)
    {
        value = 0;

        if (text.Length == 0)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
