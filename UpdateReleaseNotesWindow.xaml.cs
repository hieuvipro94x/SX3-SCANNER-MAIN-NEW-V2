using Markdig;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using SX3_SCANER.Helper;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace SX3_SCANER
{
    public partial class UpdateReleaseNotesWindow : Window
    {
        private static readonly Brush HeadingBrush = CreateBrush("#0F172A");
        private static readonly Brush BodyBrush = CreateBrush("#1E293B");
        private static readonly Brush MutedBrush = CreateBrush("#64748B");
        private static readonly Brush LinkBrush = CreateBrush("#2563EB");
        private static readonly Brush CodeBrush = CreateBrush("#BE123C");
        private static readonly Brush CodeBackgroundBrush = CreateBrush("#F1F5F9");
        private static readonly Brush QuoteBackgroundBrush = CreateBrush("#F8FAFC");
        private static readonly Brush QuoteBorderBrush = CreateBrush("#60A5FA");
        private static readonly Brush DefaultBorderBrush = CreateBrush("#CBD5E1");
        private static readonly Brush TableHeaderBrush = CreateBrush("#F1F5F9");
        private static readonly Brush TableCellBrush = CreateBrush("#FFFFFF");
        private static readonly Brush DarkCodeBackgroundBrush = CreateBrush("#0F172A");
        private static readonly Brush DarkCodeForegroundBrush = CreateBrush("#E2E8F0");

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

            if (update == null)
            {
                txtCurrentVersion.Text = FormatVersion(currentVersion);
                txtNewVersion.Text = "Không xác định";
                txtFileName.Text = "Không xác định";
                txtFileSize.Text = "Không xác định";
                txtReleaseSource.Text = SafeText(UpdateService.ReleasesPageUrl, "Không xác định");
                txtReleaseSource.ToolTip = txtReleaseSource.Text;
                RequiredBadge.Visibility = Visibility.Collapsed;
                SetReleaseNotesMarkdown("Không đọc được thông tin bản cập nhật.");
                return;
            }

            txtCurrentVersion.Text = FormatVersion(currentVersion);
            txtNewVersion.Text = FormatVersion(update.Version);
            txtFileName.Text = SafeText(update.FileName, "Không xác định");
            txtFileSize.Text = FormatFileSize(update.FileSize);
            txtReleaseSource.Text = SafeText(UpdateService.ReleasesPageUrl, "Không xác định");

            txtFileName.ToolTip = txtFileName.Text;
            txtReleaseSource.ToolTip = txtReleaseSource.Text;
            RequiredBadge.Visibility = IsUpdateRequired(update)
                ? Visibility.Visible
                : Visibility.Collapsed;

            SetReleaseNotesMarkdown(update.ReleaseNotes);
        }

        private void SetReleaseNotesMarkdown(string markdown)
        {
            releaseNotesDocument.Blocks.Clear();

            if (string.IsNullOrWhiteSpace(markdown))
            {
                releaseNotesDocument.Blocks.Add(CreateEmptyParagraph("Không có nội dung thay đổi."));
                return;
            }

            try
            {
                MarkdownDocument document = Markdown.Parse(markdown, MarkdownPipeline);
                AddMarkdownBlocks(releaseNotesDocument.Blocks, document);

                if (releaseNotesDocument.Blocks.Count == 0)
                {
                    releaseNotesDocument.Blocks.Add(CreateEmptyParagraph("Không có nội dung thay đổi."));
                }
            }
            catch (Exception ex)
            {
                releaseNotesDocument.Blocks.Clear();
                releaseNotesDocument.Blocks.Add(CreateEmptyParagraph(
                    "Không thể hiển thị nội dung Markdown. Hiển thị dạng văn bản thường."));
                releaseNotesDocument.Blocks.Add(new Paragraph(new Run(markdown))
                {
                    Foreground = BodyBrush,
                    Margin = new Thickness(0, 10, 0, 0),
                    LineHeight = 28,
                    TextAlignment = TextAlignment.Left
                });
                releaseNotesDocument.Blocks.Add(new Paragraph(new Run("Chi tiết lỗi: " + ex.Message))
                {
                    Foreground = MutedBrush,
                    FontSize = 12.5,
                    Margin = new Thickness(0, 10, 0, 0),
                    FontStyle = FontStyles.Italic
                });
            }
        }

        private static Paragraph CreateEmptyParagraph(string text)
        {
            return new Paragraph(new Run(text))
            {
                Foreground = MutedBrush,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 0, 0, 12),
                LineHeight = 28
            };
        }

        private void AddMarkdownBlocks(
            BlockCollection target,
            ContainerBlock container)
        {
            if (target == null || container == null)
            {
                return;
            }

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
                        heading.Level <= 2 ? 11 : 8),
                    FontWeight = FontWeights.Black,
                    Foreground = HeadingBrush,
                    FontSize = GetHeadingSize(heading.Level),
                    LineHeight = GetHeadingSize(heading.Level) + 8,
                    TextAlignment = TextAlignment.Left
                };
                AddInlineContent(paragraph.Inlines, heading.Inline);
                return paragraph;
            }

            if (block is ParagraphBlock paragraphBlock)
            {
                var paragraph = new Paragraph
                {
                    Margin = new Thickness(0, 0, 0, 12),
                    LineHeight = 29,
                    Foreground = BodyBrush,
                    TextAlignment = TextAlignment.Left
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
                    BorderThickness = new Thickness(4, 0, 0, 0),
                    Foreground = BodyBrush
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
                    Background = DarkCodeBackgroundBrush,
                    Foreground = DarkCodeForegroundBrush,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 13.5,
                    LineHeight = 21,
                    TextAlignment = TextAlignment.Left
                };
            }

            if (block is MdTable tableBlock)
            {
                return RenderTable(tableBlock);
            }

            if (block is ThematicBreakBlock)
            {
                return new Paragraph
                {
                    Margin = new Thickness(0, 12, 0, 16),
                    Padding = new Thickness(0),
                    BorderBrush = DefaultBorderBrush,
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
                var section = new Section
                {
                    Margin = new Thickness(0, 0, 0, 8)
                };
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
                Margin = new Thickness(22, 0, 0, 14),
                Padding = new Thickness(8, 0, 0, 0),
                Foreground = BodyBrush
            };

            foreach (ListItemBlock itemBlock in listBlock)
            {
                var item = new ListItem
                {
                    Margin = new Thickness(0, 4, 0, 4)
                };

                foreach (Markdig.Syntax.Block child in itemBlock)
                {
                    System.Windows.Documents.Block rendered = RenderBlock(child);
                    if (rendered != null)
                    {
                        item.Blocks.Add(rendered);
                    }
                }

                if (item.Blocks.Count == 0)
                {
                    item.Blocks.Add(new Paragraph(new Run(string.Empty)));
                }

                list.ListItems.Add(item);
            }

            return list;
        }

        private System.Windows.Documents.Table RenderTable(MdTable tableBlock)
        {
            var table = new System.Windows.Documents.Table
            {
                CellSpacing = 0,
                Margin = new Thickness(0, 6, 0, 16)
            };
            table.RowGroups.Add(new TableRowGroup());

            int rowIndex = 0;
            foreach (var rowBlock in tableBlock)
            {
                MdTableRow markdigRow = rowBlock as MdTableRow;
                if (markdigRow == null)
                {
                    continue;
                }

                var row = new System.Windows.Documents.TableRow();
                bool isHeaderRow = rowIndex == 0;

                foreach (var cellBlock in markdigRow)
                {
                    MdTableCell markdigCell = cellBlock as MdTableCell;
                    if (markdigCell == null)
                    {
                        continue;
                    }

                    var cell = new System.Windows.Documents.TableCell
                    {
                        Padding = new Thickness(10, 8, 10, 8),
                        BorderBrush = DefaultBorderBrush,
                        BorderThickness = new Thickness(1, 1, 1, 1),
                        Background = isHeaderRow ? TableHeaderBrush : TableCellBrush
                    };

                    AddMarkdownBlocks(cell.Blocks, markdigCell);
                    if (cell.Blocks.Count == 0)
                    {
                        cell.Blocks.Add(new Paragraph(new Run(string.Empty)));
                    }

                    if (isHeaderRow)
                    {
                        cell.FontWeight = FontWeights.Bold;
                        cell.Foreground = HeadingBrush;
                    }

                    row.Cells.Add(cell);
                }

                if (row.Cells.Count > 0)
                {
                    table.RowGroups[0].Rows.Add(row);
                    rowIndex++;
                }
            }

            if (table.RowGroups[0].Rows.Count == 0)
            {
                table.RowGroups[0].Rows.Add(new System.Windows.Documents.TableRow());
                table.RowGroups[0].Rows[0].Cells.Add(
                    new System.Windows.Documents.TableCell(
                        new Paragraph(new Run("Không thể hiển thị bảng."))));
            }

            return table;
        }

        private void AddInlineContent(
            InlineCollection target,
            ContainerInline container)
        {
            if (target == null || container == null)
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
                        FontSize = 13.2,
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

                if (inline is HtmlInline htmlInline)
                {
                    string htmlText = htmlInline.Tag;
                    if (!string.IsNullOrWhiteSpace(htmlText))
                    {
                        target.Add(new Run(htmlText)
                        {
                            Foreground = MutedBrush,
                            FontSize = 13
                        });
                    }
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
            if (link == null)
            {
                return;
            }

            if (link.IsImage)
            {
                string imageText = GetInlineText(link);
                if (string.IsNullOrWhiteSpace(imageText))
                {
                    imageText = link.Url;
                }

                if (!string.IsNullOrWhiteSpace(imageText))
                {
                    target.Add(new Run("[Ảnh: " + imageText + "]")
                    {
                        Foreground = MutedBrush,
                        FontStyle = FontStyles.Italic
                    });
                }
                return;
            }

            var hyperlink = new Hyperlink
            {
                Foreground = LinkBrush,
                TextDecorations = TextDecorations.Underline,
                ToolTip = link.Url ?? string.Empty
            };
            AddInlineContent(hyperlink.Inlines, link);

            if (hyperlink.Inlines.Count == 0 && !string.IsNullOrWhiteSpace(link.Url))
            {
                hyperlink.Inlines.Add(new Run(link.Url));
            }

            Uri uri;
            if (Uri.TryCreate(link.Url, UriKind.Absolute, out uri) &&
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

        private static string GetInlineText(ContainerInline container)
        {
            if (container == null)
            {
                return string.Empty;
            }

            var result = string.Empty;
            foreach (Markdig.Syntax.Inlines.Inline inline in container)
            {
                if (inline is LiteralInline literal)
                {
                    result += literal.Content.ToString();
                }
                else if (inline is ContainerInline nested)
                {
                    result += GetInlineText(nested);
                }
            }

            return result.Trim();
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            var hyperlink = sender as Hyperlink;
            string url = hyperlink == null ? null : hyperlink.Tag as string;
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
                ProfessionalMessageBox.Show(
                    "Không thể mở liên kết.\n\n" + ex.Message,
                    "SX3 Scanner",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private static bool IsUpdateRequired(UpdateInfo update)
        {
            if (update == null)
            {
                return false;
            }

            string[] propertyNames =
            {
                "IsRequired",
                "Required",
                "Mandatory",
                "IsMandatory",
                "ForceUpdate",
                "IsForceUpdate"
            };

            Type updateType = update.GetType();
            foreach (string propertyName in propertyNames)
            {
                var property = updateType.GetProperty(propertyName);
                if (property == null || property.PropertyType != typeof(bool))
                {
                    continue;
                }

                object value = property.GetValue(update, null);
                if (value is bool && (bool)value)
                {
                    return true;
                }
            }

            return false;
        }

        private static string FormatVersion(string version)
        {
            version = SafeText(version, "Không xác định").Trim();
            if (version.StartsWith("V", StringComparison.OrdinalIgnoreCase))
            {
                return version;
            }

            return "V" + version;
        }

        private static string SafeText(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static double GetHeadingSize(int level)
        {
            switch (level)
            {
                case 1:
                    return 28;
                case 2:
                    return 23;
                case 3:
                    return 20;
                default:
                    return 18;
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

            return size.ToString(unitIndex == 0 ? "0" : "0.##", CultureInfo.CurrentCulture) +
                " " + units[unitIndex];
        }

        private void Update_Click(object sender, RoutedEventArgs e)
        {
            CloseWithResult(true);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            CloseWithResult(false);
        }

        private void CloseWithResult(bool accepted)
        {
            Accepted = accepted;

            try
            {
                DialogResult = accepted;
            }
            catch (InvalidOperationException)
            {
                Close();
            }
        }
    }
}
