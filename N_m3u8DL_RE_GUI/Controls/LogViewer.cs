using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using RichTextBox = System.Windows.Controls.RichTextBox;

namespace N_m3u8DL_RE_GUI.Controls;

/// <summary>
/// Read-only log viewer that colour-codes each line by the engine's record level
/// ("HH:MM:SS.mmm LEVEL : ..."), mirroring N_m3u8DL-RE's own console palette: INFO
/// green, WARN amber, ERROR red, DEBUG/EXTRA dim, GUI/progress lines default
/// foreground. Colours come from the app theme dictionaries via resource references,
/// so a theme switch also re-colours history.
/// </summary>
public class LogViewer : RichTextBox
{
    // "00:04:53.149 WARN : rest of the record" — the engine's record stamp.
    private static readonly System.Text.RegularExpressions.Regex RecordLevelPattern =
        new(@"^\d{2}:\d{2}:\d{2}\.\d{3}\s+(INFO|WARN|ERROR|EXTRA|DEBUG)\s*:",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    public LogViewer()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = true;
        Document = new FlowDocument(new Paragraph()) { PagePadding = new Thickness(4) };
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
    }

    /// <summary>Appends one line, coloured by its record level; ends with a newline.</summary>
    public void AppendLine(string message)
    {
        var paragraph = (Paragraph)Document.Blocks.LastBlock;

        var text = message;
        if (text.Length > 0)
        {
            var run = new Run(text);
            // A resource reference (not a fixed brush) keeps history re-coloured on
            // theme switches, and follows the active palette at write time.
            run.SetResourceReference(TextElement.ForegroundProperty, BrushKeyFor(message));
            paragraph.Inlines.Add(run);
        }
        paragraph.Inlines.Add(new LineBreak());

        if (Document.Blocks.Count > MaxBlocks)
        {
            // Trim history in blocks to bound memory on very long sessions.
            Document.Blocks.Remove(Document.Blocks.FirstBlock);
        }

        ScrollToEnd();
    }

    /// <summary>Clears all content.</summary>
    public void ClearLog()
    {
        Document.Blocks.Clear();
        Document.Blocks.Add(new Paragraph());
    }

    // The first record stamp in the message decides the colour: multi-record lines
    // (glued engine records) are level-consistent in practice, and the trailing
    // "(+N since last)" progress annotations have no stamp at all.
    private static string BrushKeyFor(string message)
    {
        var match = RecordLevelPattern.Match(message);
        if (!match.Success)
        {
            // No stamp → the line came from the GUI itself, or is a progress redraw.
            if (message.StartsWith("Vid ", StringComparison.Ordinal)
                || message.StartsWith("Aud ", StringComparison.Ordinal)
                || message.StartsWith("Sub ", StringComparison.Ordinal))
            {
                return "TextPrimaryBrush"; // progress bar rows, not log records
            }

            if (message.StartsWith("… ", StringComparison.Ordinal))
            {
                return "DimBrush"; // burst-suppression note
            }

            // GUI informational lines ("Starting download...", "Command: ...") follow
            // the engine's INFO colour so the stream stays visually consistent;
            // failure wording turns them red.
            return IsFailureLine(message) ? "ErrorBrush" : "SuccessBrush";
        }

        return match.Groups[1].Value switch
        {
            "INFO" => "SuccessBrush",
            "ERROR" => "ErrorBrush",
            "WARN" => "CfAmberBrush",
            "DEBUG" or "EXTRA" => "DimBrush",
            _ => "TextPrimaryBrush"
        };
    }

    private static bool IsFailureLine(string message)
    {
        return message.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("error", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }

    private const int MaxBlocks = 2000;
}
