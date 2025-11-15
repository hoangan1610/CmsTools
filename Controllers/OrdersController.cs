using CmsTools.Models;

using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
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

    }
}
