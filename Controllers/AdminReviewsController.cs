using CmsTools.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Data;
using System.Text.Json;
using System.Threading.Tasks;

namespace CmsTools.Controllers
{
    [Authorize(Policy = "CmsAdminOnly")]
    [AutoValidateAntiforgeryToken]
    public sealed class AdminReviewsController : Controller
    {
        private readonly string _hafConn;

        public AdminReviewsController(IConfiguration cfg)
        {
            _hafConn = cfg.GetConnectionString("HAFoodDb")
                ?? throw new Exception("Missing connection string: HAFoodDb");
        }

        private SqlConnection OpenHaf() => new SqlConnection(_hafConn);

        // Modal chi tiết + duyệt review + reply
        [HttpGet]
        public async Task<IActionResult> Modal(long id)
        {
            await using var con = OpenHaf();
            await con.OpenAsync();

            const string sql = @"
SELECT TOP(1)
    r.id,
    r.product_id                 AS Product_Id,
    r.variant_id                 AS Variant_Id,
    r.user_info_id               AS User_Info_Id,
    u.full_name                  AS User_Name,
    p.name                       AS Product_Name,
    r.rating,
    r.title,
    r.content,
    r.has_image                  AS Has_Image,
    r.is_verified_purchase       AS Is_Verified_Purchase,
    r.status,
    r.created_at                 AS Created_At,
    r.updated_at                 AS Updated_At,
    r.is_hidden                  AS Is_Hidden,

    -- 🔹 AI fields
    r.ai_decision_source         AS Ai_Decision_Source,
    r.ai_reason                  AS Ai_Reason,
    r.ai_flags_json              AS Ai_Flags_Json,

    rr.[content]                 AS Reply_Content,
    rr.created_at                AS Reply_Created_At,
    rr.admin_user_id             AS Reply_Admin_User_Id,
    NULL                         AS Reply_Admin_Name
FROM dbo.tbl_product_review r
LEFT JOIN dbo.tbl_user_info u
       ON u.id = r.user_info_id
LEFT JOIN dbo.tbl_product_info p
       ON p.id = r.product_id
LEFT JOIN dbo.tbl_product_review_reply rr
       ON rr.review_id = r.id
WHERE r.id = @id;
";

            var vm = await con.QuerySingleOrDefaultAsync<AdminReviewViewModel>(sql, new { id });

            if (vm == null)
                return NotFound();

            const string sqlImages = @"
SELECT
    id,
    image_url AS Image_Url
FROM dbo.tbl_product_review_image
WHERE review_id = @id
ORDER BY sort_order, id;
";

            var images = await con.QueryAsync<AdminReviewImageViewModel>(sqlImages, new { id });
            vm.Images = images.AsList();

            // Partial view: Views/AdminReviews/_ReviewModal.cshtml
            return PartialView("_ReviewModal", vm);
        }




        [HttpPost]
        public async Task<IActionResult> SetStatus(long id, byte newStatus, string? rejectedReason)
        {
            // newStatus: 0 = chờ duyệt, 1 = duyệt, 2 = từ chối
            if (newStatus == 2 && string.IsNullOrWhiteSpace(rejectedReason))
            {
                return Json(new
                {
                    ok = false,
                    message = "Vui lòng nhập lý do từ chối."
                });
            }

            // TODO: map adminUserId từ user đang login trong CMS_Tools
            long adminUserId = 1;

            await using var con = OpenHaf();
            await con.OpenAsync();

            var p = new DynamicParameters();
            p.Add("@review_id", id, DbType.Int64);
            p.Add("@new_status", newStatus, DbType.Byte);
            p.Add("@admin_user_id", adminUserId, DbType.Int64);
            p.Add("@rejected_reason",
                string.IsNullOrWhiteSpace(rejectedReason) ? null : rejectedReason.Trim(),
                DbType.String);

            try
            {
                // 1) Cập nhật trạng thái review
                await con.ExecuteAsync(
                    "dbo.usp_product_review_set_status",
                    p,
                    commandType: CommandType.StoredProcedure);

                // 2) Nếu duyệt (newStatus = 1) thì bắn notification in-app cho khách
                if (newStatus == 1)
                {
                    // Lấy lại thông tin review sau khi update
                    const string sqlReview = @"
SELECT
    user_info_id,
    product_id,
    order_id,
    status
FROM dbo.tbl_product_review
WHERE id = @id;
";

                    var reviewRow = await con.QueryFirstOrDefaultAsync(sqlReview, new { id });

                    if (reviewRow != null)
                    {
                        long userId = (long)reviewRow.user_info_id;
                        long productId = (long)reviewRow.product_id;
                        long? orderId = reviewRow.order_id is null ? (long?)null : (long)reviewRow.order_id;
                        byte status = (byte)reviewRow.status;

                        // Chỉ gửi khi status hiện tại là Approved
                        if (status == 1)
                        {
                            var title = "Đánh giá của bạn đã được duyệt";
                            var body = "Cảm ơn bạn đã chia sẻ đánh giá về sản phẩm. Đánh giá đã được duyệt và hiển thị công khai.";

                            var dataObj = new
                            {
                                review_id = id,
                                product_id = productId
                            };
                            var dataJson = JsonSerializer.Serialize(dataObj);

                            // Type notification: anh chỉnh cho khớp enum của hệ thống
                            const byte NOTI_TYPE_REVIEW_APPROVED =  /* TODO: gán đúng mã enum, ví dụ (byte)20 */ 20;
                            const byte CHANNEL_IN_APP = 1;

                            var pNotify = new DynamicParameters();
                            pNotify.Add("@user_info_id", userId, DbType.Int64);
                            pNotify.Add("@device_id", dbType: DbType.Int64, value: null);
                            pNotify.Add("@order_id", orderId, DbType.Int64);
                            pNotify.Add("@type", NOTI_TYPE_REVIEW_APPROVED, DbType.Byte);
                            pNotify.Add("@channel", CHANNEL_IN_APP, DbType.Byte);
                            pNotify.Add("@title", title, DbType.String);
                            pNotify.Add("@body", body, DbType.String);
                            pNotify.Add("@data", dataJson, DbType.String);
                            pNotify.Add("@id", dbType: DbType.Int64, direction: ParameterDirection.Output);

                            try
                            {
                                await con.ExecuteAsync(
                                    "dbo.usp_notification_add",
                                    pNotify,
                                    commandType: CommandType.StoredProcedure);
                            }
                            catch (Exception exNoti)
                            {
                                // Không làm fail duyệt review nếu notify lỗi
                                // log lại nếu anh có ILogger
                                Console.WriteLine("Notify error: " + exNoti.Message);
                            }
                        }
                    }
                }

                string msg = newStatus switch
                {
                    1 => "Đã duyệt đánh giá.",
                    2 => "Đã từ chối đánh giá.",
                    _ => "Đã cập nhật trạng thái đánh giá."
                };

                return Json(new
                {
                    ok = true,
                    message = msg
                });
            }
            catch (SqlException ex)
            {
                return Json(new
                {
                    ok = false,
                    message = ex.Message
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    ok = false,
                    message = ex.Message
                });
            }
        }

        // 🔹 mới: lưu phản hồi của shop
        [HttpPost]
        public async Task<IActionResult> SaveReply(long id, string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return Json(new
                {
                    ok = false,
                    message = "Nội dung phản hồi không được để trống."
                });
            }

            // TODO: map adminUserId từ tài khoản CMS (claim / user id riêng)
            long adminUserId = 1;

            await using var con = OpenHaf();
            await con.OpenAsync();

            var p = new DynamicParameters();
            p.Add("@review_id", id, DbType.Int64);
            p.Add("@admin_user_id", adminUserId, DbType.Int64);
            p.Add("@content", content.Trim(), DbType.String);
            p.Add("@user_info_id", dbType: DbType.Int64, direction: ParameterDirection.Output);
            p.Add("@product_id", dbType: DbType.Int64, direction: ParameterDirection.Output);

            try
            {
                // 1) Lưu / cập nhật reply
                await con.ExecuteAsync(
                    "dbo.usp_product_review_reply_upsert",
                    p,
                    commandType: CommandType.StoredProcedure);

                // Lấy ra thông tin để gửi notify
                var userInfoId = p.Get<long?>("@user_info_id");
                var productId = p.Get<long?>("@product_id");

                // 2) Gửi thông báo in-app cho khách (nếu lấy được user_info_id)
                if (userInfoId.HasValue)
                {
                    try
                    {
                        // TODO: chỉnh đúng theo enum NotificationTypes.REVIEW_REPLIED bên HAShop.Api
                        const byte REVIEW_REPLIED = 5; // ví dụ, xem giá trị thực bên API rồi sửa cho khớp

                        var dataObj = new
                        {
                            review_id = id,
                            product_id = productId
                        };
                        var dataJson = JsonSerializer.Serialize(dataObj);

                        // body rút gọn cho gọn thông báo
                        var bodyShort = content.Length > 200
                            ? content.Substring(0, 200) + "..."
                            : content;

                        var np = new DynamicParameters();
                        np.Add("@user_info_id", userInfoId.Value, DbType.Int64);
                        np.Add("@device_id", null, DbType.Int64);
                        np.Add("@order_id", null, DbType.Int64);
                        np.Add("@type", REVIEW_REPLIED, DbType.Byte);
                        np.Add("@channel", 1, DbType.Byte); // 1 = in-app
                        np.Add("@title", "Shop đã trả lời đánh giá của bạn", DbType.String);
                        np.Add("@body", bodyShort, DbType.String);
                        np.Add("@data", dataJson, DbType.String);
                        np.Add("@id", dbType: DbType.Int64, direction: ParameterDirection.Output);

                        await con.ExecuteAsync(
                            "dbo.usp_notification_add",
                            np,
                            commandType: CommandType.StoredProcedure);
                    }
                    catch (Exception ex)
                    {
                        // Không làm fail request, chỉ log nếu anh có ILogger
                        // _logger.LogError(ex, "Send notification from CMS failed for review {ReviewId}", id);
                    }
                }

                return Json(new
                {
                    ok = true,
                    message = "Đã lưu phản hồi."
                });
            }
            catch (SqlException ex) when (ex.Number == 50510)
            {
                return Json(new
                {
                    ok = false,
                    message = "Không tìm thấy đánh giá."
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    ok = false,
                    message = ex.Message
                });
            }
        }

            // 🔹 Ẩn / hiện review (is_hidden)
            [HttpPost]
        public async Task<IActionResult> SetHidden(long id, bool isHidden)
        {
            // TODO: lấy adminUserId từ user đăng nhập CMS (claims)
            long adminUserId = 1;

            await using var con = OpenHaf();
            await con.OpenAsync();

            var p = new DynamicParameters();
            p.Add("@review_id", id, DbType.Int64);
            p.Add("@is_hidden", isHidden, DbType.Boolean);
            p.Add("@admin_user_id", adminUserId, DbType.Int64);

            try
            {
                await con.ExecuteAsync(
                    "dbo.usp_product_review_set_hidden",
                    p,
                    commandType: CommandType.StoredProcedure);

                return Json(new
                {
                    ok = true,
                    message = isHidden
                        ? "Đã ẩn đánh giá khỏi web."
                        : "Đã hiện lại đánh giá trên web."
                });
            }
            catch (SqlException ex) when (ex.Number == 50504) // REVIEW_NOT_FOUND
            {
                return Json(new
                {
                    ok = false,
                    message = "Không tìm thấy đánh giá."
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    ok = false,
                    message = ex.Message
                });
            }
        }


    }
}
