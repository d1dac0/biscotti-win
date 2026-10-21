using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Biscotti.Win;

/// <summary>Text-only Markdown subset. Never loads a URI, HTML, image, or XAML.</summary>
public sealed class SafeMarkdownView : FlowDocumentScrollViewer
{
    private static readonly Regex InlineSyntax = new(
        @"!?\[([^\]\r\n]*)\]\(([^)\r\n]*)\)|`([^`\r\n]+)`|\*\*([^*\r\n]+)\*\*|\*([^*\r\n]+)\*",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Heading = new(@"^(#{1,6}) (.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex ListItem = new(@"^(?:[-*+] |\d+\. )(.*)$", RegexOptions.CultureInvariant);

    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown), typeof(string), typeof(SafeMarkdownView),
        new PropertyMetadata("", (d, _) => ((SafeMarkdownView)d).Render()));

    public string? Markdown { get => (string?)GetValue(MarkdownProperty); set => SetValue(MarkdownProperty, value); }
    public event EventHandler<TimestampNavigationEventArgs>? TimestampRequested;

    public SafeMarkdownView()
    {
        IsToolBarVisible = false;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Background = Brushes.Transparent;
        Render();
    }

    private void Render()
    {
        var document = new FlowDocument { PagePadding = new Thickness(8), FontSize = 15 };
        document.SetResourceReference(ForegroundProperty, "InkBrush");
        bool code = false;
        foreach (var line in (Markdown ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal)) { code = !code; continue; }
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
            var text = line;
            if (code)
            {
                paragraph.FontFamily = new FontFamily("Consolas");
                paragraph.Inlines.Add(new Run(text));
            }
            else
            {
                var heading = Heading.Match(line);
                var list = ListItem.Match(line);
                if (heading.Success)
                {
                    paragraph.FontSize = 26 - heading.Groups[1].Length * 2;
                    paragraph.FontWeight = FontWeights.SemiBold;
                    text = heading.Groups[2].Value;
                }
                else if (list.Success) text = "• " + list.Groups[1].Value;
                else if (line.StartsWith("> ", StringComparison.Ordinal))
                {
                    text = line[2..];
                    paragraph.Margin = new Thickness(16, 0, 0, 8);
                    paragraph.FontStyle = FontStyles.Italic;
                }
                AddInlines(paragraph, text);
            }
            document.Blocks.Add(paragraph);
        }
        Document = document;
    }

    private void AddInlines(Paragraph paragraph, string text)
    {
        // Fully enumerate before changing the paragraph so a timeout can safely
        // fall back to the exact source text without dropping or duplicating it.
        Match[] matches;
        try { matches = InlineSyntax.Matches(text).Cast<Match>().ToArray(); }
        catch (RegexMatchTimeoutException) { paragraph.Inlines.Add(new Run(text)); return; }
        int offset = 0;
        foreach (var match in matches)
        {
            paragraph.Inlines.Add(new Run(text[offset..match.Index]));
            if (match.Groups[2].Success)
            {
                if (!match.Value.StartsWith('!') && TryGetTimestamp(match.Groups[2].Value, out var seconds))
                {
                    var link = new Hyperlink(new Run(match.Groups[1].Value))
                    {
                        Focusable = true,
                        ToolTip = $"Go to transcript at {TimeSpan.FromSeconds(seconds):hh\\:mm\\:ss}",
                    };
                    // No NavigateUri: activation only raises our local event.
                    link.Click += (_, _) => TimestampRequested?.Invoke(this, new(seconds));
                    paragraph.Inlines.Add(link);
                }
                else paragraph.Inlines.Add(new Run(match.Value));
            }
            else if (match.Groups[3].Success)
                paragraph.Inlines.Add(new Run(match.Groups[3].Value) { FontFamily = new FontFamily("Consolas") });
            else if (match.Groups[4].Success) paragraph.Inlines.Add(new Bold(new Run(match.Groups[4].Value)));
            else paragraph.Inlines.Add(new Italic(new Run(match.Groups[5].Value)));
            offset = match.Index + match.Length;
        }
        paragraph.Inlines.Add(new Run(text[offset..]));
    }

    internal static bool TryGetTimestamp(string target, out double seconds)
    {
        const string prefix = "biscotti://time/";
        seconds = 0;
        return target.StartsWith(prefix, StringComparison.Ordinal) &&
            double.TryParse(target[prefix.Length..], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out seconds) &&
            double.IsFinite(seconds) && seconds >= 0 && seconds <= TimeSpan.MaxValue.TotalSeconds / 2;
    }
}

public sealed class TimestampNavigationEventArgs(double seconds) : EventArgs
{
    public double Seconds { get; } = seconds;
}
