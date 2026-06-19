using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace SX3_SCANER.Helper
{
    internal static class ProfessionalMessageBox
    {
        public static MessageBoxResult Show(string messageBoxText)
        {
            return Show(messageBoxText, "SX3 SCANNER", MessageBoxButton.OK, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption)
        {
            return Show(messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(
            string messageBoxText,
            string caption,
            MessageBoxButton button)
        {
            return Show(messageBoxText, caption, button, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon)
        {
            Window owner = Application.Current == null ? null : Application.Current.MainWindow;
            ProfessionalMessageWindow dialog = new ProfessionalMessageWindow(
                messageBoxText,
                caption,
                button,
                icon)
            {
                WindowStartupLocation = owner != null && owner.IsVisible
                    ? WindowStartupLocation.CenterOwner
                    : WindowStartupLocation.CenterScreen
            };

            if (owner != null && owner.IsVisible && !ReferenceEquals(owner, dialog))
            {
                dialog.Owner = owner;
            }

            bool? result = dialog.ShowDialog();
            if (result == true)
                return dialog.Result;

            if (button == MessageBoxButton.OK)
                return MessageBoxResult.OK;

            return MessageBoxResult.Cancel;
        }

        private sealed class ProfessionalMessageWindow : Window
        {
            private readonly MessageBoxButton _button;
            private readonly MessageBoxImage _icon;

            public ProfessionalMessageWindow(
                string message,
                string caption,
                MessageBoxButton button,
                MessageBoxImage icon)
            {
                _button = button;
                _icon = icon;
                Result = MessageBoxResult.Cancel;

                Title = string.IsNullOrWhiteSpace(caption) ? "SX3 SCANNER" : caption;
                Width = 620;
                SizeToContent = SizeToContent.Height;
                WindowStyle = WindowStyle.None;
                AllowsTransparency = true;
                Background = Brushes.Transparent;
                ResizeMode = ResizeMode.NoResize;
                ShowInTaskbar = false;
                FontFamily = new FontFamily("Segoe UI");
                Content = BuildContent(message ?? string.Empty, Title);
            }

            public MessageBoxResult Result { get; private set; }

            private UIElement BuildContent(string message, string caption)
            {
                DialogPalette palette = DialogPalette.FromIcon(_icon);

                Border shell = new Border
                {
                    Margin = new Thickness(18),
                    Background = Brushes.White,
                    BorderBrush = palette.Border,
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(22),
                    Effect = new DropShadowEffect
                    {
                        Color = Color.FromRgb(51, 65, 85),
                        BlurRadius = 28,
                        ShadowDepth = 0,
                        Opacity = 0.24
                    }
                };

                Grid grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                Border header = new Border
                {
                    Background = palette.SoftBackground,
                    BorderBrush = palette.SoftBorder,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    CornerRadius = new CornerRadius(22, 22, 0, 0),
                    Padding = new Thickness(22, 18, 22, 18)
                };

                Grid headerGrid = new Grid();
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Border iconCircle = new Border
                {
                    Width = 50,
                    Height = 50,
                    CornerRadius = new CornerRadius(25),
                    Background = palette.Accent,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                };

                iconCircle.Child = new TextBlock
                {
                    Text = palette.IconText,
                    Foreground = Brushes.White,
                    FontSize = 30,
                    FontWeight = FontWeights.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                };

                StackPanel headerText = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center
                };
                headerText.Children.Add(new TextBlock
                {
                    Text = caption,
                    FontSize = 22,
                    FontWeight = FontWeights.Black,
                    Foreground = palette.Title,
                    TextWrapping = TextWrapping.Wrap
                });
                headerText.Children.Add(new TextBlock
                {
                    Text = GetSubTitle(_icon),
                    Margin = new Thickness(0, 4, 0, 0),
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = palette.Title,
                    Opacity = 0.82,
                    TextWrapping = TextWrapping.Wrap
                });

                Grid.SetColumn(iconCircle, 0);
                Grid.SetColumn(headerText, 1);
                headerGrid.Children.Add(iconCircle);
                headerGrid.Children.Add(headerText);
                header.Child = headerGrid;
                Grid.SetRow(header, 0);
                grid.Children.Add(header);

                Border messageBox = new Border
                {
                    Margin = new Thickness(22, 20, 22, 18),
                    Padding = new Thickness(16, 14, 16, 14),
                    Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(14),
                    Child = new TextBlock
                    {
                        Text = message,
                        FontSize = 17,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = 25
                    }
                };
                Grid.SetRow(messageBox, 1);
                grid.Children.Add(messageBox);

                Border footer = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    CornerRadius = new CornerRadius(0, 0, 22, 22),
                    Padding = new Thickness(22, 16, 22, 16)
                };
                footer.Child = CreateButtons(palette);
                Grid.SetRow(footer, 2);
                grid.Children.Add(footer);

                shell.Child = grid;
                return shell;
            }

            private UIElement CreateButtons(DialogPalette palette)
            {
                Grid buttons = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                if (_button == MessageBoxButton.YesNo)
                {
                    buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                    buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    Button noButton = CreateButton("KHÔNG", Brushes.White, new SolidColorBrush(Color.FromRgb(15, 23, 42)), new SolidColorBrush(Color.FromRgb(203, 213, 225)));
                    noButton.Click += delegate { Result = MessageBoxResult.No; DialogResult = true; Close(); };
                    Grid.SetColumn(noButton, 0);
                    buttons.Children.Add(noButton);

                    Button yesButton = CreateButton("ĐỒNG Ý", palette.Accent, Brushes.White, palette.Accent);
                    yesButton.Click += delegate { Result = MessageBoxResult.Yes; DialogResult = true; Close(); };
                    Grid.SetColumn(yesButton, 2);
                    buttons.Children.Add(yesButton);
                    return buttons;
                }

                if (_button == MessageBoxButton.OKCancel)
                {
                    buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                    buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    Button cancelButton = CreateButton("HỦY", Brushes.White, new SolidColorBrush(Color.FromRgb(15, 23, 42)), new SolidColorBrush(Color.FromRgb(203, 213, 225)));
                    cancelButton.Click += delegate { Result = MessageBoxResult.Cancel; DialogResult = true; Close(); };
                    Grid.SetColumn(cancelButton, 0);
                    buttons.Children.Add(cancelButton);

                    Button okButton = CreateButton("ĐỒNG Ý", palette.Accent, Brushes.White, palette.Accent);
                    okButton.Click += delegate { Result = MessageBoxResult.OK; DialogResult = true; Close(); };
                    Grid.SetColumn(okButton, 2);
                    buttons.Children.Add(okButton);
                    return buttons;
                }

                Button closeButton = CreateButton("ĐÃ HIỂU", palette.Accent, Brushes.White, palette.Accent);
                closeButton.Click += delegate { Result = MessageBoxResult.OK; DialogResult = true; Close(); };
                buttons.Children.Add(closeButton);
                return buttons;
            }

            private static Button CreateButton(string text, Brush background, Brush foreground, Brush borderBrush)
            {
                Button button = new Button
                {
                    Content = text,
                    Height = 44,
                    MinWidth = 130,
                    Padding = new Thickness(16, 0, 16, 0),
                    Background = background,
                    Foreground = foreground,
                    BorderBrush = borderBrush,
                    BorderThickness = new Thickness(1),
                    FontSize = 14,
                    FontWeight = FontWeights.Black,
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                ControlTemplate template = new ControlTemplate(typeof(Button));
                FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
                border.SetValue(Border.CornerRadiusProperty, new CornerRadius(13));
                border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
                border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
                border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });

                FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
                presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                presenter.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding("Content") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
                border.AppendChild(presenter);
                template.VisualTree = border;
                button.Template = template;

                return button;
            }

            private static string GetSubTitle(MessageBoxImage icon)
            {
                if (icon == MessageBoxImage.Error ||
                    icon == MessageBoxImage.Hand ||
                    icon == MessageBoxImage.Stop)
                {
                    return "Cần xử lý trước khi tiếp tục.";
                }

                if (icon == MessageBoxImage.Warning ||
                    icon == MessageBoxImage.Exclamation)
                {
                    return "Vui lòng kiểm tra kỹ thông tin.";
                }

                if (icon == MessageBoxImage.Question)
                {
                    return "Xác nhận lựa chọn để tiếp tục.";
                }

                if (icon == MessageBoxImage.Information ||
                    icon == MessageBoxImage.Asterisk)
                {
                    return "Thông báo từ hệ thống.";
                }

                return "Thông báo từ SX3 Scanner.";
            }
        }

        private sealed class DialogPalette
        {
            public Brush Accent { get; private set; }
            public Brush Border { get; private set; }
            public Brush SoftBackground { get; private set; }
            public Brush SoftBorder { get; private set; }
            public Brush Title { get; private set; }
            public string IconText { get; private set; }

            public static DialogPalette FromIcon(MessageBoxImage icon)
            {
                if (icon == MessageBoxImage.Error ||
                    icon == MessageBoxImage.Hand ||
                    icon == MessageBoxImage.Stop)
                {
                    return Create("!", 220, 38, 38, 254, 242, 242, 254, 202, 202, 153, 27, 27);
                }

                if (icon == MessageBoxImage.Warning ||
                    icon == MessageBoxImage.Exclamation)
                {
                    return Create("!", 245, 158, 11, 255, 251, 235, 253, 230, 138, 146, 64, 14);
                }

                if (icon == MessageBoxImage.Question)
                {
                    return Create("?", 37, 99, 235, 239, 246, 255, 191, 219, 254, 30, 64, 175);
                }

                return Create("i", 37, 99, 235, 239, 246, 255, 191, 219, 254, 30, 64, 175);
            }

            private static DialogPalette Create(
                string iconText,
                byte ar,
                byte ag,
                byte ab,
                byte br,
                byte bg,
                byte bb,
                byte sr,
                byte sg,
                byte sb,
                byte tr,
                byte tg,
                byte tb)
            {
                SolidColorBrush accent = new SolidColorBrush(Color.FromRgb(ar, ag, ab));
                SolidColorBrush softBg = new SolidColorBrush(Color.FromRgb(br, bg, bb));
                SolidColorBrush softBorder = new SolidColorBrush(Color.FromRgb(sr, sg, sb));
                SolidColorBrush title = new SolidColorBrush(Color.FromRgb(tr, tg, tb));

                return new DialogPalette
                {
                    IconText = iconText,
                    Accent = accent,
                    Border = accent,
                    SoftBackground = softBg,
                    SoftBorder = softBorder,
                    Title = title
                };
            }
        }
    }
}
