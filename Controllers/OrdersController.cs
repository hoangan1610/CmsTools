using CmsTools.Models;

using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace CmsTools.Controllers
{
    [Authorize(Policy = "CmsAdminOnly")]
    public sealed class OrdersController : Controller
    {
        private readonly string _hafConn;
        private readonly string? _frontendOrderTemplate;
        private readonly string? _frontendTimelineTemplate;

        public OrdersController(IConfiguration cfg)
        {
            _hafConn = cfg.GetConnectionString("HAFoodDb")
                ?? throw new Exception("Missing connection string: HAFoodDb");

            // Config trong appsettings.json (tuỳ bạn đặt)
            _frontendOrderTemplate = cfg["CmsTools:OrderFrontendUrlTemplate"];
            _frontendTimelineTemplate = cfg["CmsTools:OrderTimelineUrlTemplate"];
        }

        private SqlConnection OpenHaf() => new SqlConnection(_hafConn);

        // Row nhỏ để map SELECT user_info_id, pay_total
        private sealed class OrderForLoyaltyRow
        {
            public long User_Info_Id { get; set; }
            public decimal Pay_Total { get; set; }
        }

        [HttpGet]
        public async Task<IActionResult> Detail(long id)
        {
            using var conn = OpenHaf();
            await conn.OpenAsync();   // cho chắc, giống pattern UsersController

            const string sqlHeader = @"
SELECT 
    o.id              AS Id,
    o.order_code      AS OrderCode,
    o.user_info_id    AS UserInfoId,
    o.address_id      AS AddressId,
    o.ship_name       AS ShipName,
    o.ship_full_address AS ShipFullAddress,
    o.ship_phone      AS ShipPhone,
    o.status          AS Status,
    o.sub_total       AS SubTotal,
    o.discount_total  AS DiscountTotal,
    o.shipping_total  AS ShippingTotal,
    o.vat_total       AS VatTotal,
    o.pay_total       AS PayTotal,
    o.payment_status  AS PaymentStatus,
    o.payment_provider AS PaymentProvider,
    o.payment_ref     AS PaymentRef,
    o.placed_at       AS PlacedAt,
    o.confirmed_at    AS ConfirmedAt,
    o.shipped_at      AS ShippedAt,
    o.delivered_at    AS DeliveredAt,
    o.canceled_at     AS CanceledAt,
    o.created_at      AS CreatedAt,
    o.updated_at      AS UpdatedAt,
    u.full_name       AS UserFullName,
    u.phone           AS UserPhone,
    a.label           AS AddressLabel
FROM dbo.tbl_orders o
LEFT JOIN dbo.tbl_user_info u ON u.id = o.user_info_id
LEFT JOIN dbo.tbl_address   a ON a.id = o.address_id
WHERE o.id = @id;

";

            const string sqlItems = @"
SELECT 
    i.id             AS Id,
    i.variant_id     AS VariantId,
    i.product_id     AS ProductId,
    i.sku            AS Sku,
    i.name_variant   AS NameVariant,
    i.price_variant  AS PriceVariant,
    i.image_variant  AS ImageVariant,
    i.quantity       AS Quantity,
    i.line_subtotal  AS LineSubtotal
FROM dbo.tbl_order_item i
WHERE i.order_id = @id
ORDER BY i.id;
";

            using var multi = await conn.QueryMultipleAsync(
                sqlHeader + sqlItems,
                new { id });

            var header = await multi.ReadFirstOrDefaultAsync<CmsOrderDetailViewModel>();
            if (header == null)
                return NotFound("Không tìm thấy đơn hàng.");

            var items = (await multi.ReadAsync<CmsOrderItemViewModel>()).ToList();
            header.Items = items;

            // Build URL ra front-end nếu có template trong config
            if (!string.IsNullOrWhiteSpace(_frontendOrderTemplate))
            {
                header.FrontendOrderUrl = _frontendOrderTemplate!
                    .Replace("{orderCode}", header.OrderCode)
                    .Replace("{orderId}", header.Id.ToString());
            }

            if (!string.IsNullOrWhiteSpace(_frontendTimelineTemplate))
            {
                header.FrontendTimelineUrl = _frontendTimelineTemplate!
                    .Replace("{orderCode}", header.OrderCode)
                    .Replace("{orderId}", header.Id.ToString());
            }

            return View(header);  // Views/Orders/Detail.cshtml
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeStatus(long id, byte newStatus, string? note)
        {
            await using var conn = OpenHaf();
            await conn.OpenAsync();

            var p = new DynamicParameters();
            p.Add("@order_id", id, DbType.Int64);
            p.Add("@new_status", newStatus, DbType.Byte);

            long adminUserId = 1; // TODO: map từ User
            p.Add("@admin_user_id", adminUserId, DbType.Int64);

            p.Add("@note",
                string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                DbType.String);

            try
            {
                await conn.ExecuteAsync(
                    "dbo.usp_order_set_status",
                    p,
                    commandType: CommandType.StoredProcedure);

                // ✅ Nếu set sang "Đã giao" thì cộng điểm loyalty bằng SP
                if (newStatus == 3)
                {
                    try
                    {
                        var orderRow = await conn.QueryFirstOrDefaultAsync<OrderForLoyaltyRow>(
                            new CommandDefinition(
                                """
                                SELECT 
                                    user_info_id AS User_Info_Id,
                                    pay_total    AS Pay_Total
                                FROM dbo.tbl_orders
                                WHERE id = @id
                                """,
                                new { id },
                                commandType: CommandType.Text));

                        if (orderRow != null && orderRow.Pay_Total > 0)
                        {
                            int points = (int)Math.Floor(orderRow.Pay_Total / 1000m);
                            if (points > 0)
                            {
                                var lp = new DynamicParameters();
                                lp.Add("@user_info_id", orderRow.User_Info_Id, DbType.Int64);
                                lp.Add("@order_id", id, DbType.Int64);
                                lp.Add("@points", points, DbType.Int32);
                                lp.Add("@reason",
                                    $"Hoàn tất đơn hàng #{id} (CMS)",
                                    DbType.String);

                                lp.Add("@points_added", dbType: DbType.Int32, direction: ParameterDirection.Output);
                                lp.Add("@new_total_points", dbType: DbType.Int32, direction: ParameterDirection.Output);
                                lp.Add("@old_tier", dbType: DbType.Byte, direction: ParameterDirection.Output);
                                lp.Add("@new_tier", dbType: DbType.Byte, direction: ParameterDirection.Output);
                                lp.Add("@tier_changed", dbType: DbType.Boolean, direction: ParameterDirection.Output);

                                await conn.ExecuteAsync(
                                    "dbo.usp_loyalty_add_points_for_order",
                                    lp,
                                    commandType: CommandType.StoredProcedure);

                                // CMS không gửi notify, để API hoặc mobile xử lý đẹp sau.
                            }
                        }
                    }
                    catch (SqlException ex) when (ex.Number == 50601 || ex.Number == 50602)
                    {
                        // LOYALTY_USER_NOT_FOUND / ORDER_NOT_COMPLETED: kệ, không chặn CMS
                    }
                    catch
                    {
                        // Nuốt lỗi loyalty để khỏi ảnh hưởng UI CMS
                    }
                }

                // --- Detect AJAX hay normal submit ---
                var isAjax = string.Equals(
                    Request.Headers["X-Requested-With"],
                    "XMLHttpRequest",
                    StringComparison.OrdinalIgnoreCase);

                if (isAjax)
                {
                    return Json(new
                    {
                        ok = true,
                        message = "Đã cập nhật trạng thái đơn hàng."
                    });
                }
                else
                {
                    TempData["CmsMessage"] = "Đã cập nhật trạng thái đơn hàng.";
                    return RedirectToAction("Detail", new { id });
                }
            }
            catch (SqlException ex) when (ex.Number == 50520) // ORDER_NOT_FOUND
            {
                if (IsAjaxRequest())
                {
                    return Json(new
                    {
                        ok = false,
                        message = "Không tìm thấy đơn hàng."
                    });
                }
                TempData["CmsMessage"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Detail", new { id });
            }
            catch (SqlException ex) when (ex.Number == 50521) // ORDER_STATUS_INVALID
            {
                if (IsAjaxRequest())
                {
                    return Json(new
                    {
                        ok = false,
                        message = "Trạng thái chuyển không hợp lệ."
                    });
                }
                TempData["CmsMessage"] = "Trạng thái chuyển không hợp lệ.";
                return RedirectToAction("Detail", new { id });
            }
            catch (Exception ex)
            {
                if (IsAjaxRequest())
                {
                    return Json(new
                    {
                        ok = false,
                        message = ex.Message
                    });
                }
                TempData["CmsMessage"] = ex.Message;
                return RedirectToAction("Detail", new { id });
            }

            // local function check ajax
            bool IsAjaxRequest()
            {
                return string.Equals(
                    Request.Headers["X-Requested-With"],
                    "XMLHttpRequest",
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
