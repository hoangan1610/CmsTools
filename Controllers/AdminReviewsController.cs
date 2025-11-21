using CmsTools.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Data;
using System.Threading.Tasks;

namespace CmsTools.Controllers
{
    [Authorize(Policy = "CmsAdminOnly")]
    [AutoValidateAntiforgeryToken] // Tự validate AntiForgery cho mọi POST
    public sealed class AdminReviewsController : Controller
    {
        private readonly string _hafConn;

        public AdminReviewsController(IConfiguration cfg)
        {
            _hafConn = cfg.GetConnectionString("HAFoodDb")
                ?? throw new Exception("Missing connection string: HAFoodDb");
        }

        private SqlConnection OpenHaf() => new SqlConnection(_hafConn);

        // Modal chi tiết + duyệt review
        [HttpGet]
        public async Task<IActionResult> Modal(long id)
        {
            await using var con = OpenHaf();
            await con.OpenAsync();

            const string sql = @"
SELECT TOP(1)
    r.id,
    r.product_id,
    r.variant_id,
    r.user_info_id,
    u.full_name       AS User_Name,
    -- u.email           AS User_Email,  -- bỏ dòng này
    p.name            AS Product_Name,
    r.rating,
    r.title,
    r.content,
    r.has_image,
    r.is_verified_purchase,
    r.status,
    r.created_at,
    r.updated_at
FROM dbo.tbl_product_review r
LEFT JOIN dbo.tbl_user_info u   ON u.id = r.user_info_id
LEFT JOIN dbo.tbl_product_info p ON p.id = r.product_id
WHERE r.id = @id;
";


            var vm = await con.QuerySingleOrDefaultAsync<AdminReviewViewModel>(sql, new { id });

            if (vm == null)
                return NotFound(); // HttpNotFound bên Core là NotFound()

            // Views/AdminReviews/_ReviewModal.cshtml
            return PartialView("_ReviewModal", vm);
        }

        [HttpPost]
        public async Task<IActionResult> SetStatus(long id, byte newStatus, string? rejectedReason)
        {
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
                await con.ExecuteAsync(
                    "dbo.usp_product_review_set_status",
                    p,
                    commandType: CommandType.StoredProcedure);

                return Json(new { ok = true });
            }
            catch (SqlException ex)
            {
                return Json(new { ok = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, message = ex.Message });
            }
        }
    }
}
