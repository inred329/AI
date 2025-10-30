using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AI
{
    public partial class StockChartWindow : Window
    {
        private readonly IReadOnlyList<StockDayChartPoint> _chartPoints;

        public StockChartWindow(string stockNumber, string? stockName, StockDayDataset dataset)
        {
            InitializeComponent();

            _chartPoints = dataset.ChartPoints;

            var displayTitle = string.IsNullOrWhiteSpace(stockName)
                ? stockNumber
                : $"{stockNumber} {stockName}";

            Title = $"{displayTitle} 日成交資訊";
            TitleTextBlock.Text = Title;
            SubtitleTextBlock.Text = dataset.Title ?? string.Empty;
            DateTextBlock.Text = !string.IsNullOrWhiteSpace(dataset.Date)
                ? $"資料日期：{dataset.Date}"
                : string.Empty;

            var statusParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(dataset.Stat))
            {
                statusParts.Add(dataset.Stat!.Trim());
            }

            if (dataset.Notes is { Count: > 0 } notes)
            {
                statusParts.AddRange(notes
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .Select(text => text.Trim()));
            }

            StatTextBlock.Text = statusParts.Count > 0
                ? string.Join(" | ", statusParts)
                : string.Empty;

            RecordsDataGrid.ItemsSource = dataset.Table.DefaultView;

            ChartCanvas.SizeChanged += (_, _) => RenderChart();
            Loaded += (_, _) => RenderChart();
        }

        private void RenderChart()
        {
            if (_chartPoints.Count == 0)
            {
                ChartEmptyTextBlock.Visibility = Visibility.Visible;
                ChartCanvas.Children.Clear();
                RangeTextBlock.Text = string.Empty;
                return;
            }

            var validPoints = _chartPoints
                .Where(point => !double.IsNaN(point.Close) && !double.IsInfinity(point.Close))
                .ToList();

            if (validPoints.Count < 2)
            {
                ChartEmptyTextBlock.Visibility = Visibility.Visible;
                ChartCanvas.Children.Clear();
                RangeTextBlock.Text = string.Empty;
                return;
            }

            ChartEmptyTextBlock.Visibility = Visibility.Collapsed;

            var width = ChartCanvas.ActualWidth;
            var height = ChartCanvas.ActualHeight;

            if (width <= 0 || height <= 0)
            {
                return;
            }

            ChartCanvas.Children.Clear();

            var minClose = validPoints.Min(point => point.Close);
            var maxClose = validPoints.Max(point => point.Close);

            if (Math.Abs(maxClose - minClose) < double.Epsilon)
            {
                maxClose += 1;
                minClose -= 1;
            }

            var polyline = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromRgb(65, 90, 119)),
                StrokeThickness = 2,
                Points = new PointCollection()
            };

            for (var index = 0; index < validPoints.Count; index++)
            {
                var point = validPoints[index];
                var x = index == validPoints.Count - 1
                    ? width
                    : index * (width / (validPoints.Count - 1));

                var normalized = (point.Close - minClose) / (maxClose - minClose);
                var y = height - (normalized * height);

                polyline.Points.Add(new Point(x, y));
            }

            ChartCanvas.Children.Add(polyline);

            DrawAxis(width, height);
            DrawExtremes(validPoints, width, height, minClose, maxClose);

            var firstDate = validPoints.First().Date.ToString("MM/dd");
            var lastDate = validPoints.Last().Date.ToString("MM/dd");
            RangeTextBlock.Text = $"收盤價範圍：{minClose:0.##} ~ {maxClose:0.##} | 期間：{firstDate} - {lastDate}";
        }

        private void DrawAxis(double width, double height)
        {
            var horizontalAxis = new Line
            {
                X1 = 0,
                Y1 = height,
                X2 = width,
                Y2 = height,
                Stroke = new SolidColorBrush(Color.FromRgb(208, 217, 226)),
                StrokeThickness = 1
            };
            ChartCanvas.Children.Add(horizontalAxis);

            var verticalAxis = new Line
            {
                X1 = 0,
                Y1 = 0,
                X2 = 0,
                Y2 = height,
                Stroke = new SolidColorBrush(Color.FromRgb(208, 217, 226)),
                StrokeThickness = 1
            };
            ChartCanvas.Children.Add(verticalAxis);
        }

        private void DrawExtremes(IReadOnlyList<StockDayChartPoint> points, double width, double height, double minClose, double maxClose)
        {
            var minPoint = points.Aggregate((currentMin, next) => next.Close < currentMin.Close ? next : currentMin);
            var maxPoint = points.Aggregate((currentMax, next) => next.Close > currentMax.Close ? next : currentMax);

            AddMarker(minPoint, points, width, height, minClose, maxClose, Colors.Firebrick, "低");
            AddMarker(maxPoint, points, width, height, minClose, maxClose, Colors.ForestGreen, "高");
        }

        private void AddMarker(StockDayChartPoint point, IReadOnlyList<StockDayChartPoint> points, double width, double height, double minClose, double maxClose, Color color, string label)
        {
            var position = points.IndexOf(point);
            if (position < 0)
            {
                return;
            }

            var x = position == points.Count - 1
                ? width
                : position * (width / (points.Count - 1));

            var normalized = (point.Close - minClose) / (maxClose - minClose);
            var y = height - (normalized * height);

            var ellipse = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(color),
                Stroke = Brushes.White,
                StrokeThickness = 1
            };

            Canvas.SetLeft(ellipse, x - ellipse.Width / 2);
            Canvas.SetTop(ellipse, y - ellipse.Height / 2);
            ChartCanvas.Children.Add(ellipse);

            var text = new TextBlock
            {
                Text = $"{label}:{point.Close:0.##}",
                Foreground = new SolidColorBrush(color),
                FontWeight = FontWeights.SemiBold,
                Background = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                Padding = new Thickness(4, 2, 4, 2)
            };

            Canvas.SetLeft(text, Math.Min(Math.Max(0, x + 6), Math.Max(0, width - 80)));
            Canvas.SetTop(text, Math.Max(0, y - 24));
            ChartCanvas.Children.Add(text);
        }
    }
}
