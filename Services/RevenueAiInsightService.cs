using CmsTools.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CmsTools.Services
{
    public interface IRevenueAiInsightService
    {
        Task<RevenueAiInsightResult?> BuildAsync(RevenueReportViewModel vm, CancellationToken ct = default);
        Task<string?> AskAsync(RevenueReportViewModel vm, string question, CancellationToken ct = default);
    }

    public sealed class RevenueAiInsightService : IRevenueAiInsightService
    {
        private readonly IHttpClientFactory _hf;
        private readonly IConfiguration _cfg;
        private readonly ILogger<RevenueAiInsightService> _logger;

        private static readonly Regex RxFence = new(@"```(?:json)?\s*([\s\S]*?)\s*```",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly JsonSerializerOptions JsonCaseInsensitive =
            new() { PropertyNameCaseInsensitive = true };

        public RevenueAiInsightService(IHttpClientFactory hf, IConfiguration cfg, ILogger<RevenueAiInsightService> logger)
        {
            _hf = hf; _cfg = cfg; _logger = logger;
        }

        private static long Vnd(decimal x)
     => (long)Math.Round(x, 0, MidpointRounding.AwayFromZero);

        public async Task<RevenueAiInsightResult?> BuildAsync(RevenueReportViewModel vm, CancellationToken ct = default)
        {
            var useLlm = _cfg.GetValue<bool?>("Reports:RevenueAi:UseLlm") ?? true;
            var model = _cfg["OpenAI:Model"];
            if (!useLlm || string.IsNullOrWhiteSpace(model)) return null;

            if (vm.Rows == null || vm.Rows.Count == 0)
            {
                return new RevenueAiInsightResult
                {
                    Summary = "Không có dữ liệu trong khoảng lọc.",
                    Highlights = new(),
                    Alerts = new()
                };
            }

            var statAlerts = BuildStatAlerts(vm);
            var maxRows = _cfg.GetValue<int?>("Reports:RevenueAi:MaxRows") ?? 120;

            var rows = vm.Rows
                .OrderBy(r => r.PeriodDate)
                .Take(maxRows)
                .Select(r => new
                {
                    date = (vm.GroupBy == "month") ? r.PeriodDate.ToString("yyyy-MM") : r.PeriodDate.ToString("yyyy-MM-dd"),
                    OrderCount = r.OrderCount,
                    SubTotal = Vnd(r.SubTotal),
                    DiscountTotal = Vnd(r.DiscountTotal),
                    ShippingTotal = Vnd(r.ShippingTotal),
                    VatTotal = Vnd(r.VatTotal),
                    PayTotal = Vnd(r.PayTotal)
                })
                .ToList();

            var providers = (vm.Providers ?? new())
                .OrderByDescending(p => p.TotalAmount)
                .Take(8)
                .Select(p => new
                {
                    p.ProviderLabel,
                    p.MethodLabel,
                    p.SuccessfulOrders,
                    TotalAmount = Vnd(p.TotalAmount)
                })
                .ToList();

            var data = new
            {
                from = vm.FromDate.ToString("yyyy-MM-dd"),
                to = vm.ToDate.ToString("yyyy-MM-dd"),
                groupBy = vm.GroupBy,

                overview = new
                {
                    vm.Overview.TotalOrders,
                    TotalSubTotal = Vnd(vm.Overview.TotalSubTotal),
                    TotalDiscountTotal = Vnd(vm.Overview.TotalDiscountTotal),
                    TotalShippingTotal = Vnd(vm.Overview.TotalShippingTotal),
                    TotalVatTotal = Vnd(vm.Overview.TotalVatTotal),
                    TotalPayTotal = Vnd(vm.Overview.TotalPayTotal),
                    AverageOrderValue = Vnd(vm.Overview.AverageOrderValue)
                },

                previous = new
                {
                    vm.PreviousOverview.TotalOrders,
                    TotalSubTotal = Vnd(vm.PreviousOverview.TotalSubTotal),
                    TotalDiscountTotal = Vnd(vm.PreviousOverview.TotalDiscountTotal),
                    TotalShippingTotal = Vnd(vm.PreviousOverview.TotalShippingTotal),
                    TotalVatTotal = Vnd(vm.PreviousOverview.TotalVatTotal),
                    TotalPayTotal = Vnd(vm.PreviousOverview.TotalPayTotal),
                    AverageOrderValue = Vnd(vm.PreviousOverview.AverageOrderValue)
                },

                RevenueChangePercent = (double)vm.RevenueChangePercent,
                OrdersChangePercent = (double)vm.OrdersChangePercent,

                rows,
                providers,
                statAlerts
            };

            var dataJson = JsonSerializer.Serialize(data);

            var sys = """
Bạn là trợ lý phân tích báo cáo doanh thu cho quản trị viên.
QUY TẮC BẮT BUỘC:
- Chỉ dùng số liệu trong JSON được cung cấp.
- TẤT CẢ số tiền trong JSON là đơn vị VND (SỐ NGUYÊN). Không được tự thêm/bớt 0, không đổi đơn vị.
- Không suy đoán/không bịa số.
- Nếu thiếu dữ kiện để kết luận, hãy nói "không đủ dữ liệu".
- Trả về DUY NHẤT JSON đúng schema:
{
  "summary": "string",
  "highlights": ["string", ...],
  "alerts": ["string", ...]
}
Không được thêm chữ ngoài JSON.
""";

            var user = new StringBuilder();
            user.AppendLine("Dữ liệu báo cáo (JSON):");
            user.AppendLine(dataJson);
            user.AppendLine();
            user.AppendLine("Yêu cầu:");
            user.AppendLine("- summary 2–4 câu, tiếng Việt, có số liệu đúng.");
            user.AppendLine("- highlights: 3–6 gạch đầu dòng, có ít nhất 2 gợi ý hành động (actionable).");
            user.AppendLine("- alerts: 0–6 gạch đầu dòng, ưu tiên cảnh báo bất thường/giảm giá/biến động, có thể dùng statAlerts.");

            var http = _hf.CreateClient("OpenAI");
            var chatPath = (_cfg["OpenAI:ChatPath"] ?? "chat/completions").TrimStart('/');

            try
            {
                var req = new
                {
                    model,
                    temperature = _cfg.GetValue<double?>("Reports:RevenueAi:Temperature") ?? 0.0,
                    max_tokens = _cfg.GetValue<int?>("Reports:RevenueAi:MaxTokens") ?? 450,
                    messages = new object[]
                    {
                new { role = "system", content = sys },
                new { role = "user", content = user.ToString() }
                    }
                };

                var resp = await http.PostAsJsonAsync(chatPath, req, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    _logger.LogWarning("RevenueAi BuildAsync HTTP {Status}. Path={Path}. Body={Body}",
                        resp.StatusCode, chatPath, body);

                    return new RevenueAiInsightResult
                    {
                        Summary = "",
                        Highlights = new(),
                        Alerts = statAlerts,
                        Raw = _cfg.GetValue<bool>("Reports:RevenueAi:KeepRaw") ? body : null
                    };
                }

                var root = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct).ConfigureAwait(false);
                var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";

                var extracted = ExtractJson(content);

                RevenueAiInsightResult parsed;
                try
                {
                    parsed = JsonSerializer.Deserialize<RevenueAiInsightResult>(extracted, JsonCaseInsensitive)
                             ?? new RevenueAiInsightResult();
                    // ✅ SỬA SỐ TIỀN AI BỊ THÊM 000 (dựa trên dữ liệu thật trong vm)
                    SanitizeInsightMoney(parsed, vm);

                }
                catch
                {
                    parsed = new RevenueAiInsightResult();
                }

                if (parsed.Alerts == null || parsed.Alerts.Count == 0)
                    parsed.Alerts = statAlerts;

                parsed.Raw = _cfg.GetValue<bool>("Reports:RevenueAi:KeepRaw") ? content : null;
                return parsed;
            }
            catch (OperationCanceledException)
            {
                // ✅ tránh trường hợp load lâu -> user thấy "không hiện"
                _logger.LogWarning("RevenueAi BuildAsync canceled/timeout");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RevenueAi BuildAsync failed");
                return new RevenueAiInsightResult
                {
                    Summary = "",
                    Highlights = new(),
                    Alerts = statAlerts
                };
            }
        }

        public async Task<string?> AskAsync(RevenueReportViewModel vm, string question, CancellationToken ct = default)
        {
            var useLlm = _cfg.GetValue<bool?>("Reports:RevenueAi:UseLlm") ?? true;
            var model = _cfg["OpenAI:Model"];
            if (!useLlm || string.IsNullOrWhiteSpace(model)) return null;

            question = (question ?? "").Trim();
            if (question.Length < 2) return "Bạn nhập câu hỏi rõ hơn giúp mình nhé.";

            var maxRows = _cfg.GetValue<int?>("Reports:RevenueAi:MaxRows") ?? 120;

            var data = new
            {
                from = vm.FromDate.ToString("yyyy-MM-dd"),
                to = vm.ToDate.ToString("yyyy-MM-dd"),
                groupBy = vm.GroupBy,
                overview = vm.Overview,
                rows = (vm.Rows ?? new()).OrderBy(r => r.PeriodDate).Take(maxRows).Select(r => new
                {
                    date = (vm.GroupBy == "month") ? r.PeriodDate.ToString("yyyy-MM") : r.PeriodDate.ToString("yyyy-MM-dd"),
                    r.OrderCount,
                    r.SubTotal,
                    r.DiscountTotal,
                    r.ShippingTotal,
                    r.VatTotal,
                    r.PayTotal
                }),
                providers = (vm.Providers ?? new()).Select(p => new { p.ProviderLabel, p.MethodLabel, p.SuccessfulOrders, p.TotalAmount })
            };

            var dataJson = JsonSerializer.Serialize(data);

            var sys = """
Bạn là trợ lý trả lời câu hỏi về báo cáo doanh thu.
QUY TẮC:
- Chỉ dùng dữ liệu JSON cung cấp.
- Nếu câu hỏi yêu cầu dữ liệu không có trong JSON, trả lời: "Không đủ dữ liệu trong báo cáo hiện tại."
- Trả lời ngắn gọn tiếng Việt.
""";

            var http = _hf.CreateClient("OpenAI");
            var chatPath = (_cfg["OpenAI:ChatPath"] ?? "chat/completions").TrimStart('/');

            var req = new
            {
                model,
                temperature = _cfg.GetValue<double?>("Reports:RevenueAi:Temperature") ?? 0.0,
                max_tokens = _cfg.GetValue<int?>("Reports:RevenueAi:AskMaxTokens") ?? 250,
                messages = new object[]
                {
                    new { role = "system", content = sys },
                    new { role = "user", content = "Dữ liệu báo cáo JSON:\n" + dataJson },
                    new { role = "user", content = "Câu hỏi: " + question }
                }
            };

            try
            {
                var resp = await http.PostAsJsonAsync(chatPath, req, ct).ConfigureAwait(false);
                var respBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("RevenueAi AskAsync HTTP {Status}. Path={Path}. Body={Body}",
                        resp.StatusCode, chatPath, respBody);
                    return "AI hiện không phản hồi. Bạn thử lại sau.";
                }

                using var doc = JsonDocument.Parse(respBody);
                return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RevenueAi AskAsync failed");
                return "AI gặp lỗi khi trả lời.";
            }
        }

        private static List<string> BuildStatAlerts(RevenueReportViewModel vm)
        {
            var alerts = new List<string>();
            var rows = vm.Rows ?? new List<RevenueByPeriodRow>();
            if (rows.Count < 4) return alerts;

            var values = rows.Select(r => (double)r.PayTotal).ToArray();
            var mean = values.Average();
            var var0 = values.Select(v => (v - mean) * (v - mean)).Average();
            var std = Math.Sqrt(var0);

            if (std > 0.000001)
            {
                foreach (var r in rows)
                {
                    var z = ((double)r.PayTotal - mean) / std;
                    if (Math.Abs(z) >= 2.5)
                    {
                        var label = vm.GroupBy == "month" ? r.PeriodDate.ToString("MM/yyyy") : r.PeriodDate.ToString("dd/MM/yyyy");
                        alerts.Add($"Biến động mạnh {label}: PayTotal={r.PayTotal:N0} (z={z:0.00})");
                    }
                }
            }

            foreach (var r in rows)
            {
                if (r.SubTotal <= 0) continue;
                var rate = (double)(r.DiscountTotal / r.SubTotal);
                if (rate >= 0.25)
                {
                    var label = vm.GroupBy == "month" ? r.PeriodDate.ToString("MM/yyyy") : r.PeriodDate.ToString("dd/MM/yyyy");
                    alerts.Add($"Giảm giá cao {label}: Discount/SubTotal={(rate * 100):0.#}%");
                }
            }

            return alerts.Distinct().Take(8).ToList();
        }

        private static string ExtractJson(string content)
        {
            var t = (content ?? "").Trim();

            if (t.StartsWith("```"))
            {
                var m = RxFence.Match(t);
                if (m.Success) t = m.Groups[1].Value.Trim();
            }

            var i = t.IndexOf('{');
            var j = t.LastIndexOf('}');
            if (i >= 0 && j > i) return t.Substring(i, j - i + 1);

            return t;
        }
        private static readonly Regex RxMoneyNearCurrency =
    new(@"(?<num>\d{1,3}(?:[.,]\d{3})+|\d{4,})(?=\s*(?:VND|vnd|đ|d)(?!\w))",
        RegexOptions.Compiled);


        private static HashSet<long> BuildAllowedMoneySet(RevenueReportViewModel vm)
        {
            var set = new HashSet<long>();

            void Add(decimal x) => set.Add(Vnd(x));

            // overview hiện tại
            Add(vm.Overview.TotalSubTotal);
            Add(vm.Overview.TotalDiscountTotal);
            Add(vm.Overview.TotalShippingTotal);
            Add(vm.Overview.TotalVatTotal);
            Add(vm.Overview.TotalPayTotal);
            Add(vm.Overview.AverageOrderValue);

            // previous
            Add(vm.PreviousOverview.TotalSubTotal);
            Add(vm.PreviousOverview.TotalDiscountTotal);
            Add(vm.PreviousOverview.TotalShippingTotal);
            Add(vm.PreviousOverview.TotalVatTotal);
            Add(vm.PreviousOverview.TotalPayTotal);
            Add(vm.PreviousOverview.AverageOrderValue);

            // rows
            foreach (var r in vm.Rows ?? new())
            {
                Add(r.SubTotal);
                Add(r.DiscountTotal);
                Add(r.ShippingTotal);
                Add(r.VatTotal);
                Add(r.PayTotal);
            }

            // providers (nếu có)
            foreach (var p in vm.Providers ?? new())
            {
                Add(p.TotalAmount);
            }

            return set;
        }

        private static long TryFixMoney(long raw, HashSet<long> allowed)
        {
            if (allowed.Contains(raw)) return raw;

            // thử chia 10/100/1000/...
            long v = raw;
            for (int i = 0; i < 6; i++)
            {
                if (v % 10 != 0) break;
                v /= 10;
                if (allowed.Contains(v)) return v;
            }

            return raw; // không sửa nếu không khớp exact dữ liệu
        }

        private static string FormatGrouped(long value, char sep)
        {
            // format theo invariant với dấu ',' rồi đổi nếu cần
            var s = value.ToString("#,0", CultureInfo.InvariantCulture);
            if (sep == ',') return s;
            return s.Replace(',', sep);
        }

        private static string SanitizeMoneyInText(string? text, HashSet<long> allowed)
        {
            if (string.IsNullOrWhiteSpace(text)) return text ?? "";

            return RxMoneyNearCurrency.Replace(text, m =>
            {
                var rawStr = m.Groups["num"].Value;

                // xác định dấu phân tách ưu tiên theo text gốc
                char sep = rawStr.Contains('.') ? '.' : ',';

                // parse số: bỏ hết ký tự không phải digit
                var digits = new string(rawStr.Where(char.IsDigit).ToArray());
                if (!long.TryParse(digits, out var raw)) return rawStr;

                var fixedVal = TryFixMoney(raw, allowed);

                // chỉ thay khi đã fix về đúng (hoặc đúng ngay từ đầu)
                if (fixedVal == raw && !allowed.Contains(raw)) return rawStr;

                return FormatGrouped(fixedVal, sep);
            });
        }

        private static void SanitizeInsightMoney(RevenueAiInsightResult parsed, RevenueReportViewModel vm)
        {
            var allowed = BuildAllowedMoneySet(vm);

            parsed.Summary = SanitizeMoneyInText(parsed.Summary, allowed);

            if (parsed.Highlights != null)
            {
                for (int i = 0; i < parsed.Highlights.Count; i++)
                    parsed.Highlights[i] = SanitizeMoneyInText(parsed.Highlights[i], allowed);
            }

            if (parsed.Alerts != null)
            {
                for (int i = 0; i < parsed.Alerts.Count; i++)
                    parsed.Alerts[i] = SanitizeMoneyInText(parsed.Alerts[i], allowed);
            }
        }
    
}
}
