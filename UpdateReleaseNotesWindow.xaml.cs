using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using SX3_SCANER.Helper;
using System;
using System.Diagnostics;
using System.Net;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace SX3_SCANER
{
    public partial class UpdateReleaseNotesWindow : Window
    {
        private static readonly Brush HeadingBrush = CreateBrush("#0F172A");
        private static readonly Brush MutedBrush = CreateBrush("#64748B");
        private static readonly Brush LinkBrush = CreateBrush("#2563EB");
        private static readonly Brush CodeBrush = CreateBrush("#BE123C");
        private static readonly Brush CodeBackgroundBrush = CreateBrush("#F1F5F9");
        private static readonly Brush QuoteBackgroundBrush = CreateBrush("#F8FAFC");
        private static readonly Brush QuoteBorderBrush = CreateBrush("#60A5FA");
        private static readonly MarkdownPipeline MarkdownPipeline =
            new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .Build();

        public bool Accepted { get; private set; }

        public UpdateReleaseNotesWindow(string currentVersion, UpdateInfo update)
        {
            InitializeComponent();

            MaxWidth = Math.Max(MinWidth, SystemParameters.WorkArea.Width - 32);
            MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 32);

            txtCurrentVersion.Text = "V" + currentVersion;
            txtNewVersion.Text = "V" + update.Version;
            txtFileName.Text = update.FileName;
            txtFileSize.Text = FormatFileSize(update.FileSize);
            txtReleaseSource.Text = UpdateService.ReleasesPageUrl;
            RequiredBadge.Visibility = Visibility.Collapsed;

            SetReleaseNotesMarkdown(update.ReleaseNotes);
        }

        private void SetReleaseNotesMarkdown(string markdown)
        {
            releaseNotesDocument.Blocks.Clear();

            if (string.IsNullOrWhiteSpace(markdown))
            {
                releaseNotesDocument.Blocks.Add(new Paragraph(
                    new Run("Không có nội dung thay đổi."))
                {
                    Foreground = MutedBrush,
                    FontStyle = FontStyles.Italic
                });
                return;
            }

            MarkdownDocument document = Markdown.Parse(markdown, MarkdownPipeline);
            AddMarkdownBlocks(releaseNotesDocument.Blocks, document);
        }

        private void AddMarkdownBlocks(
            BlockCollection target,
            ContainerBlock container)
        {
            foreach (Markdig.Syntax.Block block in container)
            {
                System.Windows.Documents.Block rendered = RenderBlock(block);
                if (rendered != null)
                {
                    target.Add(rendered);
                }
            }
        }

        private System.Windows.Documents.Block RenderBlock(
            Markdig.Syntax.Block block)
        {
            if (block is HeadingBlock heading)
            {
                var paragraph = new Paragraph
                {
                    Margin = new Thickness(
                        0,
                        heading.Level == 1 ? 0 : 18,
                        0,
                        heading.Level <= 2 ? 10 : 7),
                    FontWeight = FontWeights.Bold,
                    Foreground = HeadingBrush,
                    FontSize = GetHeadingSize(heading.Level),
                    LineHeight = GetHeadingSize(heading.Level) + 9
                };
                AddInlineContent(paragraph.Inlines, heading.Inline);
                return paragraph;
            }

            if (block is ParagraphBlock paragraphBlock)
            {
                var paragraph = new Paragraph
                {
                    Margin = new Thickness(0, 0, 0, 10),
                    LineHeight = 26
                };
                AddInlineContent(paragraph.Inlines, paragraphBlock.Inline);
                return paragraph;
            }

            if (block is ListBlock listBlock)
            {
                return RenderList(listBlock);
            }

            if (block is QuoteBlock quoteBlock)
            {
                var section = new Section
                {
                    Margin = new Thickness(0, 4, 0, 14),
                    Padding = new Thickness(16, 12, 14, 8),
                    Background = QuoteBackgroundBrush,
                    BorderBrush = QuoteBorderBrush,
                    BorderThickness = new Thickness(4, 0, 0, 0)
                };
                AddMarkdownBlocks(section.Blocks, quoteBlock);
                return section;
            }

            if (block is CodeBlock codeBlock)
            {
                string code = codeBlock.Lines.ToString() ?? string.Empty;
                return new Paragraph(new Run(code.TrimEnd()))
                {
                    Margin = new Thickness(0, 4, 0, 14),
                    Padding = new Thickness(16, 13, 16, 13),
                    Background = CreateBrush("#0F172A"),
                    Foreground = CreateBrush("#E2E8F0"),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 13.5,
                    LineHeight = 21
                };
            }

            if (block is ThematicBreakBlock)
            {
                return new Paragraph
                {
                    Margin = new Thickness(0, 12, 0, 16),
                    Padding = new Thickness(0),
                    BorderBrush = CreateBrush("#CBD5E1"),
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    FontSize = 1,
                    LineHeight = 1
                };
            }

            if (block is HtmlBlock)
            {
                return null;
            }

            if (block is ContainerBlock nestedContainer)
            {
                var section = new Section();
                AddMarkdownBlocks(section.Blocks, nestedContainer);
                return section;
            }

            return null;
        }

        private System.Windows.Documents.List RenderList(ListBlock listBlock)
        {
            var list = new System.Windows.Documents.List
            {
                MarkerStyle = listBlock.IsOrdered
                    ? TextMarkerStyle.Decimal
                    : TextMarkerStyle.Disc,
                Margin = new Thickness(20, 0, 0, 12),
                Padding = new Thickness(8, 0, 0, 0)
            };

            foreach (ListItemBlock itemBlock in listBlock)
            {
                var item = new ListItem
                {
                    Margin = new Thickness(0, 0, 0, 5)
                };

                foreach (Markdig.Syntax.Block child in itemBlock)
                {
                    System.Windows.Documents.Block rendered = RenderBlock(child);
                    if (rendered != null)
                    {
                        item.Blocks.Add(rendered);
                    }
                }

                list.ListItems.Add(item);
            }

            return list;
        }

        private void AddInlineContent(
            InlineCollection target,
            ContainerInline container)
        {
            if (container == null)
            {
                return;
            }

            foreach (Markdig.Syntax.Inlines.Inline inline in container)
            {
                if (inline is LiteralInline literal)
                {
                    target.Add(new Run(literal.Content.ToString()));
                    continue;
                }

                if (inline is EmphasisInline emphasis)
                {
                    var span = new Span();
                    if (emphasis.DelimiterChar == '~')
                    {
                        span.TextDecorations = TextDecorations.Strikethrough;
                    }
                    else if (emphasis.DelimiterCount >= 2)
                    {
                        span.FontWeight = FontWeights.Bold;
                    }
                    else
                    {
                        span.FontStyle = FontStyles.Italic;
                    }

                    AddInlineContent(span.Inlines, emphasis);
                    target.Add(span);
                    continue;
                }

                if (inline is LineBreakInline)
                {
                    target.Add(new LineBreak());
                    continue;
                }

                if (inline is CodeInline code)
                {
                    target.Add(new Run(code.Content)
                    {
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 13.5,
                        Background = CodeBackgroundBrush,
                        Foreground = CodeBrush
                    });
                    continue;
                }

                if (inline is LinkInline link)
                {
                    AddLink(target, link);
                    continue;
                }

                if (inline is ContainerInline nested)
                {
                    AddInlineContent(target, nested);
                }
            }
        }

        private void AddLink(InlineCollection target, LinkInline link)
        {
            var hyperlink = new Hyperlink
            {
                Foreground = LinkBrush,
                TextDecorations = TextDecorations.Underline,
                ToolTip = link.Url ?? string.Empty
            };
            AddInlineContent(hyperlink.Inlines, link);

            Uri uri;
            if (!link.IsImage &&
                Uri.TryCreate(link.Url, UriKind.Absolute, out uri) &&
                string.Equals(
                    uri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase) &&
                !IPAddress.TryParse(uri.DnsSafeHost, out _))
            {
                hyperlink.Tag = uri.AbsoluteUri;
                hyperlink.Click += Hyperlink_Click;
            }

            target.Add(hyperlink);
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            var hyperlink = sender as Hyperlink;
            string url = hyperlink?.Tag as string;
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Không thể mở liên kết.\n\n" + ex.Message,
                    "SX3 Scanner",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private static double GetHeadingSize(int level)
        {
            switch (level)
            {
                case 1:
                    return 27;
                case 2:
                    return 22;
                case 3:
                    return 18;
                default:
                    return 16;
            }
        }

        private static Brush CreateBrush(string color)
        {
            var brush = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes <= 0)
            {
                return "Không xác định";
            }

            string[] units = { "B", "KB", "MB", "GB" };
            double size = bytes;
            int unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return size.ToString(unitIndex == 0 ? "0" : "0.##") +
                " " + units[unitIndex];
        }

        private void Update_Click(object sender, RoutedEventArgs e)
        {
            Accepted = true;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Accepted = false;
            DialogResult = false;
        }
    }
}
