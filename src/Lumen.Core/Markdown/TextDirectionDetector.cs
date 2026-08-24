namespace Lumen.Core.Markdown;

/// <summary>
/// Decides whether a block of text should be laid out right-to-left.
/// </summary>
/// <remarks>
/// Gemini transcribes a page in whatever script the page itself uses, and Lumen never asks the
/// user which script to expect. A results panel that always renders left-to-right puts Urdu,
/// Arabic, Hebrew, and similar scripts through the wrong paragraph direction: wrapping runs the
/// wrong way and the block starts from the wrong margin, even though the individual glyphs still
/// shape correctly. Digits, punctuation, and whitespace are directionally neutral and are
/// excluded from the count, so a mostly-Urdu paragraph that happens to contain a page number or
/// a Latin acronym is still read correctly.
/// </remarks>
public static class TextDirectionDetector
{
    public static bool IsPredominantlyRightToLeft(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var rtl = 0;
        var ltr = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            if (IsRightToLeftScript(rune.Value))
            {
                rtl++;
            }
            else if (System.Text.Rune.IsLetter(rune))
            {
                ltr++;
            }
        }

        return rtl > ltr;
    }

    /// <summary>
    /// Covers the Unicode blocks used by the RTL scripts Lumen is realistically asked to
    /// transcribe: Arabic (and its Urdu/Persian/Sindhi extensions), Hebrew, and their
    /// presentation-form blocks. Not exhaustive of every RTL script in Unicode (Syriac, Thaana,
    /// N'Ko, etc. are not covered), but covers what actually shows up in scanned documents.
    /// </summary>
    private static bool IsRightToLeftScript(int codepoint) => codepoint switch
    {
        >= 0x0590 and <= 0x05FF => true, // Hebrew
        >= 0x0600 and <= 0x06FF => true, // Arabic (includes Urdu, Persian, Sindhi letters)
        >= 0x0750 and <= 0x077F => true, // Arabic Supplement
        >= 0x08A0 and <= 0x08FF => true, // Arabic Extended-A
        >= 0xFB1D and <= 0xFB4F => true, // Hebrew presentation forms
        >= 0xFB50 and <= 0xFDFF => true, // Arabic presentation forms A
        >= 0xFE70 and <= 0xFEFF => true, // Arabic presentation forms B
        _ => false
    };
}
