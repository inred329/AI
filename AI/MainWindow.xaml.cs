using System;
using System.Data;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;

namespace AI
{
    public partial class MainWindow : Window
    {
        private static readonly Uri VariationEndpoint = new("https://www.twse.com.tw/rwd/zh/variation/TWT84U?response=json");
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
}