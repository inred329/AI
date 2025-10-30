using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AI
{
    public partial class MainWindow : Window
    {
        private static readonly Uri VariationEndpoint = new("https://www.twse.com.tw/rwd/zh/variation/TWT84U?response=json");
        private static readonly string StockDayEndpoint = "https://www.twse.com.tw/rwd/zh/afterTrading/STOCK_DAY";
        private readonly HttpClient _httpClient = new();

        public MainWindow()
        {
            InitializeComponent();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-TW,zh;q=0.9");
            _httpClient.DefaultRequestHeaders.Referrer = new Uri("https://www.twse.com.tw/");
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _httpClient.Dispose();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadVariationAsync();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadVariationAsync();
        }

        private async void VariationDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid dataGrid)
            {
                return;
            }

            if (e.OriginalSource is DependencyObject dependencyObject)
            {
                var row = FindVisualParent<DataGridRow>(dependencyObject);
                if (row == null)
                {
                    return;
                }
            }

            if (dataGrid.SelectedItem is not DataRowView rowView)
            {
                return;
            }

            var stockNumber = TryGetCellValue(rowView.Row, "證券代號");
            if (string.IsNullOrWhiteSpace(stockNumber))
            {
                MessageBox.Show(this, "無法取得證券代號。", "提醒", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var stockName = TryGetCellValue(rowView.Row, "證券名稱");

            await ShowStockDayWindowAsync(stockNumber.Trim(), stockName);
        }

        private async Task LoadVariationAsync()
        {
            try
            {
                StatusTextBlock.Text = "載入中…";
                VariationDataGrid.ItemsSource = null;

                using var response = await _httpClient.GetAsync(VariationEndpoint);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync();
                var variation = await JsonSerializer.DeserializeAsync<VariationResponse>(
                    stream,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                if (variation == null || variation.Fields == null || variation.Data == null)
                {
                    StatusTextBlock.Text = "找不到資料";
                    return;
                }

                var titleParts = new[] { variation.Title, variation.Subtitle }
                    .Where(part => !string.IsNullOrWhiteSpace(part))
                    .Select(part => part!.Trim())
                    .ToArray();
                TitleTextBlock.Text = titleParts.Length > 0
                    ? string.Join(" - ", titleParts)
                    : "台灣證券交易所資料";

                var timestamp = string.Join(
                    " ",
                    new[] { variation.Date, variation.Time }
                        .Where(part => !string.IsNullOrWhiteSpace(part))
                        .Select(part => part!.Trim()));
                TimestampTextBlock.Text = timestamp;

                var table = new DataTable();
                var duplicateFieldCounts = variation.Fields
                    .GroupBy(field => field)
                    .Where(group => group.Count() > 1)
                    .ToDictionary(group => group.Key, group => group.Count());
                var fieldInstanceTracker = new Dictionary<string, int>();
                foreach (var field in variation.Fields)
                {
                    var columnName = field;
                    if (duplicateFieldCounts.TryGetValue(field, out _))
                    {
                        var occurrenceIndex = fieldInstanceTracker.TryGetValue(field, out var currentIndex)
                            ? currentIndex + 1
                            : 1;
                        fieldInstanceTracker[field] = occurrenceIndex;
                        columnName = $"{field}{occurrenceIndex}";
                    }

                    table.Columns.Add(columnName);
                }

                foreach (var row in variation.Data)
                {
                    var dataRow = table.NewRow();
                    for (int i = 0; i < table.Columns.Count && i < row.Count; i++)
                    {
                        dataRow[i] = row[i];
                    }

                    table.Rows.Add(dataRow);
                }

                VariationDataGrid.ItemsSource = table.DefaultView;

                var statusParts = new List<string>
                {
                    $"資料筆數：{table.Rows.Count} 筆"
                };

                if (!string.IsNullOrWhiteSpace(variation.Stat))
                {
                    statusParts.Add(variation.Stat!);
                }

                StatusTextBlock.Text = string.Join(" | ", statusParts);
            }
            catch (Exception ex)
            {
                VariationDataGrid.ItemsSource = null;
                TitleTextBlock.Text = "台灣證券交易所資料";
                TimestampTextBlock.Text = string.Empty;
                StatusTextBlock.Text = $"載入失敗：{ex.Message}";
            }
        }

        private async Task ShowStockDayWindowAsync(string stockNumber, string? stockName)
        {
            try
            {
                StatusTextBlock.Text = $"正在載入 {stockNumber} 的日成交資料…";

                var firstDayOfMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                var requestUri = new Uri($"{StockDayEndpoint}?date={firstDayOfMonth:yyyyMMdd}&stockNo={Uri.EscapeDataString(stockNumber)}&response=json");

                using var response = await _httpClient.GetAsync(requestUri);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync();
                var stockDayResponse = await JsonSerializer.DeserializeAsync<StockDayResponse>(
                    stream,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                if (stockDayResponse == null)
                {
                    throw new InvalidOperationException("伺服器未傳回資料。");
                }

                if (!string.Equals(stockDayResponse.Stat, "OK", StringComparison.OrdinalIgnoreCase))
                {
                    var message = string.IsNullOrWhiteSpace(stockDayResponse.Stat)
                        ? "證交所回傳未成功的狀態。"
                        : stockDayResponse.Stat!;
                    throw new InvalidOperationException(message);
                }

                if (stockDayResponse.Fields == null || stockDayResponse.Data == null)
                {
                    throw new InvalidOperationException("日成交資料缺少必要欄位。");
                }

                var dataset = BuildStockDayDataset(stockDayResponse);

                var chartWindow = new StockChartWindow(stockNumber, stockName, dataset)
                {
                    Owner = this
                };
                chartWindow.Show();

                StatusTextBlock.Text = $"已開啟 {stockNumber} 的日成交視窗。";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"載入 {stockNumber} 資料時發生錯誤：{ex.Message}", "載入失敗", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = $"載入 {stockNumber} 資料失敗";
            }
        }

        private static StockDayDataset BuildStockDayDataset(StockDayResponse response)
        {
            var table = new DataTable();

            var duplicateFieldCounts = response.Fields
                .GroupBy(field => field)
                .Where(group => group.Count() > 1)
                .ToDictionary(group => group.Key, group => group.Count());

            var fieldInstanceTracker = new Dictionary<string, int>();
            foreach (var field in response.Fields)
            {
                var columnName = field;
                if (duplicateFieldCounts.TryGetValue(field, out _))
                {
                    var occurrenceIndex = fieldInstanceTracker.TryGetValue(field, out var currentIndex)
                        ? currentIndex + 1
                        : 1;
                    fieldInstanceTracker[field] = occurrenceIndex;
                    columnName = $"{field}{occurrenceIndex}";
                }

                table.Columns.Add(columnName);
            }

            var dateIndex = response.Fields.FindIndex(field => field.Contains("日期"));
            var closeIndex = response.Fields.FindIndex(field => field.Contains("收盤"));
            var highIndex = response.Fields.FindIndex(field => field.Contains("最高"));
            var lowIndex = response.Fields.FindIndex(field => field.Contains("最低"));

            var chartPoints = new List<StockDayChartPoint>();

            foreach (var row in response.Data)
            {
                var dataRow = table.NewRow();
                for (var i = 0; i < table.Columns.Count && i < row.Count; i++)
                {
                    dataRow[i] = row[i];
                }
                table.Rows.Add(dataRow);

                if (TryCreateChartPoint(row, dateIndex, closeIndex, highIndex, lowIndex, out var point))
                {
                    chartPoints.Add(point);
                }
            }

            chartPoints.Sort((left, right) => left.Date.CompareTo(right.Date));

            return new StockDayDataset(response.Title, response.Stat, response.Date, response.Notes, table, chartPoints.AsReadOnly());
        }

        private static bool TryCreateChartPoint(IReadOnlyList<string> row, int dateIndex, int closeIndex, int highIndex, int lowIndex, out StockDayChartPoint point)
        {
            point = default;

            if (dateIndex < 0 || closeIndex < 0)
            {
                return false;
            }

            if (dateIndex >= row.Count || closeIndex >= row.Count)
            {
                return false;
            }

            if (!TryParseTaiwanDate(row[dateIndex], out var date))
            {
                return false;
            }

            var close = ParseNullableDouble(row[closeIndex]);
            if (!close.HasValue)
            {
                return false;
            }

            double? high = null;
            if (highIndex >= 0 && highIndex < row.Count)
            {
                high = ParseNullableDouble(row[highIndex]);
            }

            double? low = null;
            if (lowIndex >= 0 && lowIndex < row.Count)
            {
                low = ParseNullableDouble(row[lowIndex]);
            }

            point = new StockDayChartPoint(date, close.Value, high, low);
            return true;
        }

        private static bool TryParseTaiwanDate(string? text, out DateTime date)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                var trimmed = text.Trim();
                if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                {
                    return true;
                }

                if (DateTime.TryParseExact(trimmed, new[] { "yyyy/MM/dd", "yyyy/M/d" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                {
                    return true;
                }
            }

            date = default;
            return false;
        }

        private static double? ParseNullableDouble(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var sanitized = new string(text.Where(ch => char.IsDigit(ch) || ch == '.' || ch == '-' || ch == '+').ToArray());
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return null;
            }

            return double.TryParse(sanitized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }

        private static string? TryGetCellValue(DataRow row, string columnName)
        {
            if (row.Table.Columns.Contains(columnName))
            {
                return row[columnName]?.ToString();
            }

            foreach (DataColumn column in row.Table.Columns)
            {
                if (column.ColumnName.StartsWith(columnName, StringComparison.Ordinal))
                {
                    var suffix = column.ColumnName[columnName.Length..];
                    if (suffix.All(char.IsDigit))
                    {
                        return row[column]?.ToString();
                    }
                }
            }

            return null;
        }

        private static T? FindVisualParent<T>(DependencyObject child)
            where T : DependencyObject
        {
            var current = child;
            while (current != null)
            {
                if (current is T target)
                {
                    return target;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }

    internal sealed class VariationResponse
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("subtitle")]
        public string? Subtitle { get; set; }

        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("time")]
        public string? Time { get; set; }

        [JsonPropertyName("stat")]
        public string? Stat { get; set; }

        [JsonPropertyName("fields")]
        public string[]? Fields { get; set; }

        [JsonPropertyName("data")]
        public List<List<string>>? Data { get; set; }
    }

    internal sealed class StockDayResponse
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("stat")]
        public string? Stat { get; set; }

        [JsonPropertyName("fields")]
        public List<string>? Fields { get; set; }

        [JsonPropertyName("data")]
        public List<List<string>>? Data { get; set; }

        [JsonPropertyName("notes")]
        public List<string>? Notes { get; set; }
    }

    internal sealed record StockDayChartPoint(DateTime Date, double Close, double? High, double? Low);

    internal sealed class StockDayDataset
    {
        public StockDayDataset(string? title, string? stat, string? date, IReadOnlyList<string>? notes, DataTable table, IReadOnlyList<StockDayChartPoint> chartPoints)
        {
            Title = title;
            Stat = stat;
            Date = date;
            Notes = notes;
            Table = table;
            ChartPoints = chartPoints;
        }

        public string? Title { get; }

        public string? Stat { get; }

        public string? Date { get; }

        public IReadOnlyList<string>? Notes { get; }

        public DataTable Table { get; }

        public IReadOnlyList<StockDayChartPoint> ChartPoints { get; }
    }
}
