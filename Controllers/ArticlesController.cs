using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using CmsTools.Models;
using CmsTools.Services;
using Dapper;
using Ganss.Xss;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CmsTools.Controllers
{
    [Authorize(Policy = "CmsUser")]
    public sealed class ArticlesController : Controller
    {
        private readonly string _hafConn;
        private readonly string _metaConn;
        private readonly IHttpClientFactory _hf;
        private readonly string _apiBase;
        private readonly ICmsPermissionService _perm;

        // ===== mapping meta table cho Article =====
        // Bạn đổi nếu tbl_cms_connection.name khác "HAFoodDb"
        private const string META_CONN_NAME = "HAFoodDb";
        private const string META_SCHEMA = "dbo";
        private const string META_TABLE = "tbl_article";

        // cache tableId để đỡ query meta nhiều lần
        private int? _articleTableIdCache;

        public ArticlesController(IConfiguration cfg, IHttpClientFactory hf, ICmsPermissionService perm)
        {
            _hafConn = cfg.GetConnectionString("HAFoodDb")
                ?? throw new Exception("Missing connection string: HAFoodDb");

            _metaConn = cfg.GetConnectionString("CmsToolsDb")
                ?? throw new Exception("Missing connection string: CmsToolsDb");

            _hf = hf;

            _apiBase = cfg["CmsTools:HAFoodApiBaseUrl"]
                ?? throw new Exception("Missing config: CmsTools:HAFoodApiBaseUrl");

            _perm = perm;
        }

        private SqlConnection OpenHaf() => new SqlConnection(_hafConn);
        private SqlConnection OpenMeta() => new SqlConnection(_metaConn);

        // =========================
        // PERMISSION HELPERS
        // =========================
        private async Task<int> GetCmsUserIdAsync()
        {
            var s = User?.FindFirstValue("cms_user_id")
                ?? User?.FindFirstValue(ClaimTypes.NameIdentifier);

            if (int.TryParse(s, out var id) && id > 0)
                return id;

            // Fallback: lookup theo username trong tbl_cms_user (schema bạn đưa)
            var username = User?.Identity?.Name ?? User?.FindFirstValue(ClaimTypes.Name);
            if (string.IsNullOrWhiteSpace(username))
                return 0;

            await using var conn = OpenMeta();
            await conn.OpenAsync();

            const string sql = @"SELECT TOP 1 id FROM dbo.tbl_cms_user WHERE username=@u AND is_active=1;";
            var uid = await conn.ExecuteScalarAsync<int?>(sql, new { u = username.Trim() });

            return uid ?? 0;
        }


        private async Task<int> GetArticleMetaTableIdAsync()
        {
            if (_articleTableIdCache.HasValue && _articleTableIdCache.Value > 0)
                return _articleTableIdCache.Value;

            await using var conn = OpenMeta();
            await conn.OpenAsync();

            const string sql = @"
SELECT TOP 1 t.id
FROM dbo.tbl_cms_table t
JOIN dbo.tbl_cms_connection c ON c.id = t.connection_id
WHERE t.schema_name = @schemaName
  AND t.table_name  = @tableName
  AND t.is_enabled  = 1
  AND c.is_active   = 1
ORDER BY t.id;";

            var id = await conn.ExecuteScalarAsync<int?>(sql, new
            {
                schemaName = META_SCHEMA,
                tableName = META_TABLE
            });

            _articleTableIdCache = id ?? 0;
            return _articleTableIdCache.Value;
        }


        private async Task<CmsTablePermission> GetArticlePermissionAsync()
        {
            // Admin bypass: khỏi phụ thuộc tbl_cms_table_permission
            if (IsAdmin())
            {
                return new CmsTablePermission
                {
                    CanView = true,
                    CanCreate = true,
                    CanUpdate = true,
                    CanDelete = true,
                    CanPublish = true,
                    CanSchedule = true,
                    CanArchive = true,
                    RowFilter = null
                };
            }

            var userId = await GetCmsUserIdAsync();
            if (userId <= 0) return new CmsTablePermission();

            var tableId = await GetArticleMetaTableIdAsync();
            if (tableId <= 0) return new CmsTablePermission();

            return await _perm.GetTablePermissionAsync(userId, tableId);
        }


        private IActionResult Deny(bool isAjax, string message = "Bạn không đủ quyền.")
        {
            if (isAjax)
                return StatusCode(403, new { ok = false, message });

            return Forbid();
        }

        private bool IsStatusAction(string action)
            => action == "Publish" || action == "Schedule" || action == "Archive";

        private bool HasStatusPermission(CmsTablePermission p, string action)
        {
            return action switch
            {
                "Publish" => p.CanPublish,
                "Schedule" => p.CanSchedule,
                "Archive" => p.CanArchive,
                _ => true
            };
        }

        // =========================
        // LIST
        // =========================
        [HttpGet]
        public async Task<IActionResult> Index(string? q, byte? status, string? categorySlug, string? tagSlug, int page = 1, int pageSize = 20)
        {
            var p = await GetArticlePermissionAsync();
            if (!p.CanView) return Forbid();

            if (page <= 0) page = 1;
            if (pageSize <= 0 || pageSize > 100) pageSize = 20;

            await using var conn = OpenHaf();
            await conn.OpenAsync();

            using var multi = await conn.QueryMultipleAsync(
                "dbo.usp_article_list",
                new { q, status, categorySlug, tagSlug, page, pageSize },
                commandType: CommandType.StoredProcedure
            );

            var rows = (await multi.ReadAsync<CmsArticleListItemVm>()).ToList();
            var total = await multi.ReadFirstAsync<int>();

            ViewBag.Q = q;
            ViewBag.Status = status;
            ViewBag.CategorySlug = categorySlug;
            ViewBag.TagSlug = tagSlug;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.Total = total;

            return View(rows);
        }

        // =========================
        // EDIT (GET)
        // =========================
        [HttpGet]
        public async Task<IActionResult> Edit(long? id)
        {
            var p = await GetArticlePermissionAsync();
            ViewBag.ArticlePerm = p; // ✅ add dòng này
            // tạo mới
            if (id == null || id <= 0)
            {
                if (!p.CanCreate) return Forbid();

                await using var connNew = OpenHaf();
                await connNew.OpenAsync();

                var vmNew = new CmsArticleEditVm();
                await FillCatTagOptions(connNew, vmNew);

                vmNew.Status = 0;
                vmNew.Content_Html = "";
                vmNew.Updated_At_Utc = null;
                vmNew.Concurrency_Token = null;

                return View(vmNew);
            }

            // sửa
            if (!p.CanUpdate) return Forbid();

            await using var conn = OpenHaf();
            await conn.OpenAsync();

            var vm = new CmsArticleEditVm();
            await FillCatTagOptions(conn, vm);

            const string sqlArticle = @"
SELECT TOP 1
  id, title, slug, excerpt, cover_image_url,
  content_html, content_json,
  status, published_at_utc, scheduled_at_utc,
  is_featured, featured_order,
  meta_title, meta_description, og_image_url, canonical_url,
  updated_at_utc,
  row_version
FROM dbo.tbl_article
WHERE id=@id AND is_deleted=0;";

            var row = await conn.QueryFirstOrDefaultAsync(sqlArticle, new { id });
            if (row == null) return NotFound("Không tìm thấy bài viết.");

            vm.Id = row.id;
            vm.Title = row.title;
            vm.Slug = row.slug;
            vm.Excerpt = row.excerpt;
            vm.Cover_Image_Url = row.cover_image_url;

            vm.Content_Html = row.content_html;
            vm.Content_Json = row.content_json;

            vm.Status = row.status;
            vm.Published_At_Utc = row.published_at_utc;
            vm.Scheduled_At_Utc = row.scheduled_at_utc;

            vm.Is_Featured = row.is_featured;
            vm.Featured_Order = row.featured_order;

            vm.Meta_Title = row.meta_title;
            vm.Meta_Description = row.meta_description;
            vm.Og_Image_Url = row.og_image_url;
            vm.Canonical_Url = row.canonical_url;

            vm.Updated_At_Utc = row.updated_at_utc as DateTime?;

            // concurrency token: row_version -> base64
            byte[]? rv = null;
            try { rv = (byte[])row.row_version; } catch { rv = null; }
            vm.Concurrency_Token = rv != null ? Convert.ToBase64String(rv) : null;

            if (string.IsNullOrWhiteSpace(vm.Content_Html) && !string.IsNullOrWhiteSpace(vm.Content_Json))
                vm.Content_Html = ConvertEditorJsJsonToHtml(vm.Content_Json) ?? "";

            vm.Category_Ids = (await conn.QueryAsync<long>(@"
SELECT category_id
FROM dbo.tbl_article_category_map
WHERE article_id=@id
ORDER BY sort_order, category_id;", new { id })).ToList();

            vm.Tag_Ids = (await conn.QueryAsync<long>(@"
SELECT tag_id
FROM dbo.tbl_article_tag_map
WHERE article_id=@id
ORDER BY sort_order, tag_id;", new { id })).ToList();

            const string sqlCards = @"
SELECT
  m.product_id AS Product_Id,
  m.variant_id AS Variant_Id,
  m.sort_order AS Sort_Order,

  COALESCE(m.product_name_snapshot, p.name) AS Product_Name,
  COALESCE(m.product_image_snapshot, p.image_product) AS Product_Image,

  COALESCE(m.variant_name_snapshot, v.name) AS Variant_Name,
  COALESCE(m.retail_price_snapshot, v.retail_price) AS Retail_Price,
  COALESCE(m.stock_snapshot, v.stock) AS Stock
FROM dbo.tbl_article_product_map m
LEFT JOIN dbo.tbl_product_info p ON p.id = m.product_id
LEFT JOIN dbo.tbl_product_variant v ON v.id = m.variant_id
WHERE m.article_id = @id
ORDER BY m.sort_order, m.id;";

            vm.Cards = (await conn.QueryAsync<CmsArticleCardVm>(sqlCards, new { id })).ToList();

            if (vm.Scheduled_At_Utc != null)
                vm.Scheduled_At_Vn = vm.Scheduled_At_Utc.Value.AddHours(7);

            return View(vm);
        }

        // =========================
        // CHECK SLUG (AJAX)
        // =========================
        [HttpGet]
        public async Task<IActionResult> CheckSlug(long id, string slug)
        {
            var p = await GetArticlePermissionAsync();
            var isCreate = id <= 0;

            if (isCreate && !p.CanCreate) return Json(new { ok = false, message = "Không đủ quyền." });
            if (!isCreate && !p.CanUpdate) return Json(new { ok = false, message = "Không đủ quyền." });

            slug = (slug ?? "").Trim();
            if (slug.Length == 0) return Json(new { ok = false, message = "Slug rỗng." });

            const string lang = "vi";

            await using var conn = OpenHaf();
            await conn.OpenAsync();

            var exists = await conn.ExecuteScalarAsync<long?>(@"
SELECT TOP 1 id
FROM dbo.tbl_article
WHERE is_deleted=0 AND language_code=@lang AND slug=@slug AND id<>@id;",
                new { id, slug, lang });

            if (exists != null)
                return Json(new { ok = false, message = "Slug đã tồn tại." });

            return Json(new { ok = true });
        }

        // =========================
        // AUTOSAVE DRAFT (AJAX)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AutoSaveDraft(
            long id,
            string title,
            string? slug,
            string? excerpt,
            string? cover_image_url,
            string content_html,
            bool is_featured,
            int? featured_order,
            string? meta_title,
            string? meta_description,
            string? og_image_url,
            string? canonical_url,
            string? category_ids_csv,
            string? tag_ids_csv,
            string? cards_json,
            string? scheduled_at_vn,
            string? concurrency_token
        )
        {
            // autosave = draft
            var action = "SaveDraft";

            return await SaveCore(
                id, title, slug, excerpt, cover_image_url,
                content_html,
                is_featured, featured_order,
                meta_title, meta_description, og_image_url, canonical_url,
                category_ids_csv, tag_ids_csv, cards_json, scheduled_at_vn,
                action, concurrency_token,
                isAutoSave: true
            );
        }

        // =========================
        // UPLOAD IMAGE (CSRF OK)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(10L * 1024 * 1024)]
        public async Task<IActionResult> UploadEditorImage(IFormFile image, CancellationToken ct)
        {
            var p = await GetArticlePermissionAsync();
            // upload ảnh là hành vi "edit", cho phép nếu có Create hoặc Update
            if (!p.CanCreate && !p.CanUpdate)
                return Deny(isAjax: true, message: "Bạn không có quyền upload ảnh.");

            if (image == null || image.Length == 0)
                return BadRequest(new { success = 0, message = "Không có file." });

            if (image.Length > 5L * 1024 * 1024)
                return BadRequest(new { success = 0, message = "File vượt quá 5MB." });

            var client = _hf.CreateClient();
            var url = $"{_apiBase.TrimEnd('/')}/files/images?size_w=1600&size_t=800&size_p=300";

            using var form = new MultipartFormDataContent();
            var sc = new StreamContent(image.OpenReadStream());
            sc.Headers.ContentType = new MediaTypeHeaderValue(image.ContentType ?? "application/octet-stream");
            form.Add(sc, "image", image.FileName);

            using var resp = await client.PostAsync(url, form, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return BadRequest(new { success = 0, message = "Upload failed: " + body });

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("status", out var st) || st.GetInt32() != 0)
                return BadRequest(new { success = 0, message = "Upload response invalid." });

            var data = doc.RootElement.GetProperty("data");
            var urlWeb = data.GetProperty("urlWeb").GetString();

            if (string.IsNullOrWhiteSpace(urlWeb))
                return BadRequest(new { success = 0, message = "Không lấy được urlWeb." });

            return Json(new
            {
                success = 1,
                file = new { url = urlWeb },
                location = urlWeb
            });
        }

        // =========================
        // SAVE (POST)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(
            long id,
            string title,
            string? slug,
            string? excerpt,
            string? cover_image_url,
            string content_html,
            bool is_featured,
            int? featured_order,
            string? meta_title,
            string? meta_description,
            string? og_image_url,
            string? canonical_url,
            string? category_ids_csv,
            string? tag_ids_csv,
            string? cards_json,
            string? scheduled_at_vn,
            string? concurrency_token,
            string action
        )
        {
            return await SaveCore(
                id, title, slug, excerpt, cover_image_url,
                content_html,
                is_featured, featured_order,
                meta_title, meta_description, og_image_url, canonical_url,
                category_ids_csv, tag_ids_csv, cards_json, scheduled_at_vn,
                action, concurrency_token,
                isAutoSave: false
            );
        }

        // =========================
        // SAVE CORE (shared)
        // =========================
        private async Task<IActionResult> SaveCore(
            long id,
            string title,
            string? slug,
            string? excerpt,
            string? cover_image_url,
            string content_html,
            bool is_featured,
            int? featured_order,
            string? meta_title,
            string? meta_description,
            string? og_image_url,
            string? canonical_url,
            string? category_ids_csv,
            string? tag_ids_csv,
            string? cards_json,
            string? scheduled_at_vn,
            string action,
            string? concurrency_token,
            bool isAutoSave
        )
        {
            // ===== CHECK PERMISSION =====
            var p = await GetArticlePermissionAsync();
            var isCreate = id <= 0;

            // Draft/SaveDraft: cần Create hoặc Update
            // Publish/Schedule/Archive: cần quyền riêng + vẫn cần Create/Update tương ứng
            if (IsStatusAction(action))
            {
                if (!HasStatusPermission(p, action))
                    return Deny(isAjax: isAutoSave, message: $"Bạn không có quyền {action}.");

                if (isCreate)
                {
                    if (!p.CanCreate) return Deny(isAjax: isAutoSave, message: "Bạn không có quyền tạo bài.");
                }
                else
                {
                    if (!p.CanUpdate) return Deny(isAjax: isAutoSave, message: "Bạn không có quyền sửa bài.");
                }
            }
            else
            {
                if (isCreate)
                {
                    if (!p.CanCreate) return Deny(isAjax: isAutoSave, message: "Bạn không có quyền tạo bài.");
                }
                else
                {
                    if (!p.CanUpdate) return Deny(isAjax: isAutoSave, message: "Bạn không có quyền sửa bài.");
                }
            }

            // ===== ORIGINAL LOGIC (giữ của bạn) =====
            title = (title ?? "").Trim();
            slug = (slug ?? "").Trim();
            excerpt = string.IsNullOrWhiteSpace(excerpt) ? null : excerpt.Trim();
            cover_image_url = string.IsNullOrWhiteSpace(cover_image_url) ? null : cover_image_url.Trim();

            meta_title = string.IsNullOrWhiteSpace(meta_title) ? null : meta_title.Trim();
            meta_description = string.IsNullOrWhiteSpace(meta_description) ? null : meta_description.Trim();
            og_image_url = string.IsNullOrWhiteSpace(og_image_url) ? null : og_image_url.Trim();
            canonical_url = string.IsNullOrWhiteSpace(canonical_url) ? null : canonical_url.Trim();

            var safeHtml = SanitizeHtml(content_html);

            if (string.IsNullOrWhiteSpace(title))
            {
                await using var conn0 = OpenHaf();
                await conn0.OpenAsync();

                var vm0 = BuildVmFromPost(
                    id, title, slug, excerpt, cover_image_url,
                    content_html, is_featured, featured_order,
                    meta_title, meta_description, og_image_url, canonical_url,
                    category_ids_csv, tag_ids_csv, scheduled_at_vn,
                    concurrency_token,
                    ParseCards(cards_json)
                );

                return await FailView(conn0, id, isAutoSave, "Title is required.", vm0);
            }

            if (string.IsNullOrWhiteSpace(slug))
                slug = Slugify(title);
            else
                slug = Slugify(slug);

            byte newStatus = action switch
            {
                "Publish" => (byte)1,
                "Schedule" => (byte)2,
                "Archive" => (byte)3,
                _ => (byte)0
            };

            DateTime? scheduledUtc = null;
            DateTime? publishedUtc = null;

            if (newStatus == 2)
            {
                if (string.IsNullOrWhiteSpace(scheduled_at_vn))
                    return Fail(id, isAutoSave, "Cần chọn thời gian hẹn đăng.");

                if (!DateTime.TryParseExact(
                        scheduled_at_vn,
                        new[] { "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss" },
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var vnLocal))
                    return Fail(id, isAutoSave, "Thời gian hẹn đăng không hợp lệ.");

                scheduledUtc = VnToUtc(vnLocal);

                if (scheduledUtc <= DateTime.UtcNow.AddSeconds(60))
                    return Fail(id, isAutoSave, "Thời gian hẹn đăng phải ở tương lai.");
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(scheduled_at_vn))
                {
                    if (!DateTime.TryParseExact(
                            scheduled_at_vn,
                            new[] { "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss" },
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out var vnLocal))
                        return Fail(id, isAutoSave, "Thời gian hẹn đăng không hợp lệ.");

                    scheduledUtc = VnToUtc(vnLocal);
                }
            }

            if (newStatus == 1)
            {
                publishedUtc = DateTime.UtcNow;
                scheduledUtc = null;
            }

            var catIds = ParseIds(category_ids_csv);
            var tagIds = ParseIds(tag_ids_csv);
            var cards = ParseCards(cards_json);

            var userName = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(userName))
                userName = User?.FindFirstValue(ClaimTypes.Name) ?? "cms";

            byte[]? clientRv = null;
            if (!string.IsNullOrWhiteSpace(concurrency_token))
            {
                try { clientRv = Convert.FromBase64String(concurrency_token); } catch { clientRv = null; }
            }

            const string lang = "vi";
            const string emptyEditorJson = "{\"time\":0,\"blocks\":[],\"version\":\"2\"}";

            await using var conn = OpenHaf();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                var slugExists = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(@"
SELECT TOP 1 id
FROM dbo.tbl_article
WHERE is_deleted=0 AND language_code=@lang AND slug=@slug AND id<>@id;",
                    new { id, slug, lang }, transaction: tx));

                if (slugExists != null)
                {
                    await tx.RollbackAsync();
                    return isAutoSave
                        ? BadRequest(new { ok = false, message = "Slug đã tồn tại. Hãy đổi slug khác." })
                        : BadRequest("Slug đã tồn tại. Hãy đổi slug khác.");
                }

                string? oldSlug = null;
                if (id > 0)
                {
                    oldSlug = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
                        "SELECT slug FROM dbo.tbl_article WHERE id=@id AND is_deleted=0",
                        new { id }, transaction: tx));
                }

                byte[]? newRv;

                if (id <= 0)
                {
                    const string sqlInsert = @"
DECLARE @t TABLE (id BIGINT, row_version VARBINARY(8));

INSERT INTO dbo.tbl_article
(title, slug, excerpt, cover_image_url,
 content_html, content_json,
 status, published_at_utc, scheduled_at_utc,
 is_featured, featured_order,
 meta_title, meta_description, og_image_url, canonical_url,
 language_code, user_create, user_update,
 created_at_utc, updated_at_utc, is_deleted)
OUTPUT INSERTED.id, INSERTED.row_version INTO @t(id, row_version)
VALUES
(@title, @slug, @excerpt, @cover,
 @content_html, @content_json,
 @status, @published, @scheduled,
 @is_featured, @featured_order,
 @meta_title, @meta_desc, @og, @canonical,
 @lang, @user_create, @user_update,
 SYSUTCDATETIME(), SYSUTCDATETIME(), 0);

SELECT TOP 1 id, row_version FROM @t;";

                    var inserted = await conn.QueryFirstAsync(sqlInsert, new
                    {
                        title,
                        slug,
                        excerpt,
                        cover = cover_image_url,
                        content_html = safeHtml,
                        content_json = emptyEditorJson,
                        status = newStatus,
                        published = publishedUtc,
                        scheduled = scheduledUtc,
                        is_featured,
                        featured_order,
                        meta_title,
                        meta_desc = meta_description,
                        og = og_image_url,
                        canonical = canonical_url,
                        lang,
                        user_create = userName,
                        user_update = userName
                    }, tx);

                    id = (long)inserted.id;
                    newRv = (byte[])inserted.row_version;
                }
                else
                {
                    const string sqlUpdate = @"
DECLARE @t TABLE (row_version VARBINARY(8));

UPDATE dbo.tbl_article
SET
  title=@title,
  slug=@slug,
  excerpt=@excerpt,
  cover_image_url=@cover,
  content_html=@content_html,

  status=@status,
  published_at_utc = CASE WHEN @status=1 THEN COALESCE(@published, published_at_utc) ELSE published_at_utc END,
  scheduled_at_utc = CASE WHEN @status=1 THEN NULL ELSE @scheduled END,

  is_featured=@is_featured,
  featured_order=@featured_order,

  meta_title=@meta_title,
  meta_description=@meta_desc,
  og_image_url=@og,
  canonical_url=@canonical,

  user_update=@user_update,
  updated_at_utc=SYSUTCDATETIME()
OUTPUT INSERTED.row_version INTO @t(row_version)
WHERE id=@id AND is_deleted=0
  AND (@client_rv IS NULL OR row_version=@client_rv);

SELECT TOP 1 row_version FROM @t;";

                    newRv = await conn.QueryFirstOrDefaultAsync<byte[]?>(new CommandDefinition(sqlUpdate, new
                    {
                        id,
                        title,
                        slug,
                        excerpt,
                        cover = cover_image_url,
                        content_html = safeHtml,

                        status = newStatus,
                        published = publishedUtc,
                        scheduled = scheduledUtc,

                        is_featured,
                        featured_order,

                        meta_title,
                        meta_desc = meta_description,
                        og = og_image_url,
                        canonical = canonical_url,

                        user_update = userName,
                        client_rv = clientRv
                    }, transaction: tx));

                    if (clientRv != null && (newRv == null || newRv.Length == 0))
                    {
                        await tx.RollbackAsync();
                        return Fail(id, isAutoSave,
                            "Bài viết đã được người khác cập nhật. Hãy reload trang để lấy bản mới nhất.",
                            statusCode: 409);
                    }

                    await conn.ExecuteAsync(new CommandDefinition(@"
UPDATE dbo.tbl_article
SET content_json=@emptyJson
WHERE id=@id AND is_deleted=0 AND (content_json IS NULL OR ISJSON(content_json)<>1);",
                        new { id, emptyJson = emptyEditorJson }, transaction: tx));

                    if (newRv == null)
                    {
                        newRv = await conn.ExecuteScalarAsync<byte[]?>(new CommandDefinition(
                            "SELECT row_version FROM dbo.tbl_article WHERE id=@id AND is_deleted=0;",
                            new { id }, transaction: tx));
                    }
                }

                if (id > 0 && !string.IsNullOrWhiteSpace(oldSlug) && !string.Equals(oldSlug, slug, StringComparison.OrdinalIgnoreCase))
                {
                    const string sqlRedirect = @"
IF OBJECT_ID('dbo.tbl_article_slug_redirect','U') IS NOT NULL
BEGIN
  IF EXISTS (SELECT 1 FROM dbo.tbl_article_slug_redirect WHERE from_slug=@from AND language_code=N'vi')
    UPDATE dbo.tbl_article_slug_redirect
      SET article_id=@id, is_active=1, created_at_utc=SYSUTCDATETIME()
      WHERE from_slug=@from AND language_code=N'vi';
  ELSE
    INSERT dbo.tbl_article_slug_redirect(from_slug, language_code, article_id, is_active, created_at_utc)
    VALUES (@from, N'vi', @id, 1, SYSUTCDATETIME());
END";
                    await conn.ExecuteAsync(new CommandDefinition(sqlRedirect, new { id, from = oldSlug }, transaction: tx));
                }

                await conn.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM dbo.tbl_article_category_map WHERE article_id=@id;",
                    new { id }, transaction: tx));

                if (catIds.Count > 0)
                {
                    var rows = catIds.Select((cid, idx) => new { article_id = id, category_id = cid, sort_order = idx + 1 });
                    await conn.ExecuteAsync(new CommandDefinition(
                        "INSERT INTO dbo.tbl_article_category_map(article_id, category_id, sort_order) VALUES(@article_id,@category_id,@sort_order);",
                        rows, transaction: tx));
                }

                await conn.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM dbo.tbl_article_tag_map WHERE article_id=@id;",
                    new { id }, transaction: tx));

                if (tagIds.Count > 0)
                {
                    var rows = tagIds.Select((tid, idx) => new { article_id = id, tag_id = tid, sort_order = idx + 1 });
                    await conn.ExecuteAsync(new CommandDefinition(
                        "INSERT INTO dbo.tbl_article_tag_map(article_id, tag_id, sort_order) VALUES(@article_id,@tag_id,@sort_order);",
                        rows, transaction: tx));
                }

                await conn.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM dbo.tbl_article_product_map WHERE article_id=@id;",
                    new { id }, transaction: tx));

                if (cards.Count > 0)
                {
                    var productIds = cards.Select(x => x.Product_Id).Distinct().ToList();
                    var variantIds = cards.Where(x => x.Variant_Id != null).Select(x => x.Variant_Id!.Value).Distinct().ToList();

                    var prodSnap = new Dictionary<long, (string name, string? image)>();
                    if (productIds.Count > 0)
                    {
                        var prows = await conn.QueryAsync(@"
SELECT id, name, image_product
FROM dbo.tbl_product_info
WHERE is_deleted=0 AND status=1 AND id IN @ids;", new { ids = productIds }, tx);

                        foreach (var r in prows)
                            prodSnap[(long)r.id] = ((string)r.name, (string?)r.image_product);
                    }

                    var varSnap = new Dictionary<long, (string? name, decimal? price, int? stock)>();
                    if (variantIds.Count > 0)
                    {
                        var vrows = await conn.QueryAsync(@"
SELECT id, name, retail_price, stock
FROM dbo.tbl_product_variant
WHERE status=1 AND id IN @ids;", new { ids = variantIds }, tx);

                        foreach (var r in vrows)
                            varSnap[(long)r.id] = ((string?)r.name, (decimal?)r.retail_price, (int?)r.stock);
                    }

                    var insertRows = cards
                        .OrderBy(x => x.Sort_Order)
                        .Select(c =>
                        {
                            prodSnap.TryGetValue(c.Product_Id, out var ppp);

                            (string? vn, decimal? price, int? stock) v = (null, null, null);
                            if (c.Variant_Id != null && varSnap.TryGetValue(c.Variant_Id.Value, out var vv))
                                v = (vv.name, vv.price, vv.stock);

                            return new
                            {
                                article_id = id,
                                product_id = c.Product_Id,
                                variant_id = c.Variant_Id,
                                sort_order = c.Sort_Order,

                                product_name_snapshot = (string?)ppp.name,
                                product_image_snapshot = ppp.image,

                                variant_name_snapshot = v.vn,
                                retail_price_snapshot = v.price,
                                stock_snapshot = v.stock
                            };
                        });

                    await conn.ExecuteAsync(new CommandDefinition(@"
INSERT INTO dbo.tbl_article_product_map
(article_id, product_id, variant_id, sort_order,
 product_name_snapshot, product_image_snapshot,
 variant_name_snapshot, retail_price_snapshot, stock_snapshot)
VALUES
(@article_id, @product_id, @variant_id, @sort_order,
 @product_name_snapshot, @product_image_snapshot,
 @variant_name_snapshot, @retail_price_snapshot, @stock_snapshot);",
                        insertRows, transaction: tx));
                }

                var contentText = ExtractTextFromHtml(safeHtml);

                const string sqlUpsertSearch = @"
IF OBJECT_ID('dbo.tbl_article_search_text','U') IS NOT NULL
BEGIN
  IF EXISTS (SELECT 1 FROM dbo.tbl_article_search_text WHERE article_id=@id)
    UPDATE dbo.tbl_article_search_text
      SET title=@title, slug=@slug, excerpt=@excerpt, content_text=@content_text, updated_at_utc=SYSUTCDATETIME()
      WHERE article_id=@id;
  ELSE
    INSERT dbo.tbl_article_search_text(article_id, title, slug, excerpt, content_text, updated_at_utc)
    VALUES(@id, @title, @slug, @excerpt, @content_text, SYSUTCDATETIME());
END";

                await conn.ExecuteAsync(new CommandDefinition(sqlUpsertSearch, new
                {
                    id,
                    title,
                    slug,
                    excerpt,
                    content_text = contentText
                }, transaction: tx));

                await tx.CommitAsync();

                var tokenOut = (newRv != null && newRv.Length > 0) ? Convert.ToBase64String(newRv) : null;

                if (isAutoSave)
                {
                    var vnNow = DateTime.UtcNow.AddHours(7);
                    return Json(new
                    {
                        ok = true,
                        id,
                        concurrency_token = tokenOut,
                        saved_at_local = vnNow.ToString("HH:mm:ss dd/MM/yyyy", CultureInfo.GetCultureInfo("vi-VN"))
                    });
                }

                TempData["CmsMessage"] = "Đã lưu bài viết.";
                return RedirectToAction("Edit", new { id });
            }
            catch (SqlException ex)
            {
                await tx.RollbackAsync();

                if (ex.Number is 2601 or 2627)
                    return isAutoSave
                        ? BadRequest(new { ok = false, message = "Slug đã tồn tại. Hãy đổi slug khác." })
                        : BadRequest("Slug đã tồn tại. Hãy đổi slug khác.");

                return isAutoSave
                    ? BadRequest(new { ok = false, message = ex.Message })
                    : BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return isAutoSave
                    ? BadRequest(new { ok = false, message = ex.Message })
                    : BadRequest(ex.Message);
            }
        }

        // =========================
        // Product search for cards (AJAX)
        // =========================
        [HttpGet]
        public async Task<IActionResult> SearchProducts(string q)
        {
            var p = await GetArticlePermissionAsync();
            if (!p.CanCreate && !p.CanUpdate)
                return StatusCode(403, new { ok = false, message = "Không đủ quyền." });

            q = (q ?? "").Trim();
            if (q.Length < 2) return Json(Array.Empty<object>());

            long idSearch = 0;
            var isIdSearch = long.TryParse(q, out idSearch);

            const string sql = @"
SELECT TOP (20)
  id, name, image_product
FROM dbo.tbl_product_info
WHERE is_deleted=0 AND status=1
  AND (
        (@isId=1 AND id=@idSearch)
        OR
        (@isId=0 AND (name LIKE N'%' + @q + N'%' OR brand_name LIKE N'%' + @q + N'%'))
      )
ORDER BY id DESC;";

            await using var conn = OpenHaf();
            await conn.OpenAsync();

            var rows = await conn.QueryAsync(sql, new { q, isId = isIdSearch ? 1 : 0, idSearch });

            return Json(rows.Select(p2 => new
            {
                id = (long)p2.id,
                name = (string)p2.name,
                image = (string?)p2.image_product
            }));
        }

        [HttpGet]
        public async Task<IActionResult> GetVariants(long productId)
        {
            var p = await GetArticlePermissionAsync();
            if (!p.CanCreate && !p.CanUpdate)
                return StatusCode(403, new { ok = false, message = "Không đủ quyền." });

            const string sql = @"
SELECT id, name, retail_price, stock
FROM dbo.tbl_product_variant
WHERE product_id=@productId AND status=1
ORDER BY id DESC;";

            await using var conn = OpenHaf();
            await conn.OpenAsync();

            var rows = await conn.QueryAsync(sql, new { productId });
            return Json(rows);
        }

        // =========================
        // helpers
        // =========================
        private static DateTime VnToUtc(DateTime vnLocal) => vnLocal.AddHours(-7);

        private static List<long> ParseIds(string? csv)
        {
            var res = new List<long>();
            if (string.IsNullOrWhiteSpace(csv)) return res;

            foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (long.TryParse(part, out var id) && id > 0) res.Add(id);

            return res.Distinct().ToList();
        }

        private static List<CmsArticleCardVm> ParseCards(string? cardsJson)
        {
            var list = new List<CmsArticleCardVm>();
            if (string.IsNullOrWhiteSpace(cardsJson)) return list;

            try
            {
                using var doc = JsonDocument.Parse(cardsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;

                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var pid = el.GetProperty("product_id").GetInt64();

                    long? vid = null;
                    if (el.TryGetProperty("variant_id", out var vEl) && vEl.ValueKind != JsonValueKind.Null)
                        vid = vEl.GetInt64();

                    var so = el.TryGetProperty("sort_order", out var soEl) && soEl.ValueKind == JsonValueKind.Number
                        ? soEl.GetInt32()
                        : 0;

                    list.Add(new CmsArticleCardVm
                    {
                        Product_Id = pid,
                        Variant_Id = vid,
                        Sort_Order = so
                    });
                }
            }
            catch { }

            if (list.Any(x => x.Sort_Order <= 0))
                for (int i = 0; i < list.Count; i++) list[i].Sort_Order = i + 1;

            list = list
                .GroupBy(x => (x.Product_Id, x.Variant_Id))
                .Select(g => g.OrderBy(x => x.Sort_Order).First())
                .OrderBy(x => x.Sort_Order)
                .Select((x, idx) => { x.Sort_Order = idx + 1; return x; })
                .ToList();

            return list;
        }

        private static string Slugify(string input)
        {
            input = (input ?? "").Trim().ToLowerInvariant();
            input = RemoveDiacritics(input);
            input = Regex.Replace(input, @"[^a-z0-9\s-]", "");
            input = Regex.Replace(input, @"\s+", " ").Trim();
            input = input.Replace(" ", "-");
            input = Regex.Replace(input, @"-+", "-").Trim('-');
            return string.IsNullOrWhiteSpace(input) ? "bai-viet" : input;
        }

        private static string RemoveDiacritics(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in normalized)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(c);
                if (uc != UnicodeCategory.NonSpacingMark) sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC)
                .Replace('đ', 'd').Replace('Đ', 'D');
        }

        private static string? ExtractTextFromHtml(string? html)
        {
            if (string.IsNullOrWhiteSpace(html)) return null;

            html = Regex.Replace(html, @"<script[\s\S]*?</script>", " ", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<style[\s\S]*?</style>", " ", RegexOptions.IgnoreCase);

            var text = Regex.Replace(html, @"<[^>]+>", " ");
            text = WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, @"\s+", " ").Trim();

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        private static readonly HtmlSanitizer _san = BuildSanitizer();

        private static string SanitizeHtml(string? html)
        {
            html ??= "";
            var cleaned = _san.Sanitize(html);
            return cleaned?.Trim() ?? "";
        }

        private static HtmlSanitizer BuildSanitizer()
        {
            var s = new HtmlSanitizer();

            s.AllowedTags.Clear();
            s.AllowedAttributes.Clear();
            s.AllowedSchemes.Clear();
            s.AllowedCssProperties.Clear();

            foreach (var t in new[]
            {
                "p","br","hr",
                "strong","b","em","i","u","s",
                "span","div",
                "h1","h2","h3","h4","h5","h6",
                "ul","ol","li",
                "blockquote",
                "a",
                "img","figure","figcaption",
                "table","thead","tbody","tr","th","td",
                "pre","code"
            }) s.AllowedTags.Add(t);

            foreach (var a in new[]
            {
                "href","src","alt","title",
                "class","style",
                "target","rel",
                "colspan","rowspan"
            }) s.AllowedAttributes.Add(a);

            s.AllowedSchemes.Add("http");
            s.AllowedSchemes.Add("https");

            foreach (var p in new[]
            {
                "color","background-color",
                "font-weight","font-style","text-decoration",
                "text-align",
                "margin","margin-left","margin-right","margin-top","margin-bottom",
                "padding","padding-left","padding-right","padding-top","padding-bottom",
                "border","border-collapse",
                "width","max-width",
                "font-size","line-height"
            }) s.AllowedCssProperties.Add(p);

            s.PostProcessNode += (sender, e) =>
            {
                if (e.Node is IElement el && el.TagName.Equals("A", StringComparison.OrdinalIgnoreCase))
                {
                    var target = el.GetAttribute("target");
                    if (string.Equals(target, "_blank", StringComparison.OrdinalIgnoreCase))
                    {
                        var rel = el.GetAttribute("rel") ?? "";
                        if (!rel.Contains("noopener", StringComparison.OrdinalIgnoreCase) ||
                            !rel.Contains("noreferrer", StringComparison.OrdinalIgnoreCase))
                        {
                            el.SetAttribute("rel", "noopener noreferrer");
                        }
                    }
                }
            };

            return s;
        }

        private static string? ConvertEditorJsJsonToHtml(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("blocks", out var blocks) || blocks.ValueKind != JsonValueKind.Array)
                    return null;

                static string Esc(string? s) => WebUtility.HtmlEncode(s ?? "");
                var sb = new StringBuilder();

                foreach (var b in blocks.EnumerateArray())
                {
                    var type = b.TryGetProperty("type", out var t) ? (t.GetString() ?? "") : "";
                    var data = b.TryGetProperty("data", out var d) ? d : default;

                    if (type == "header")
                    {
                        var level = 2;
                        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("level", out var lv) && lv.ValueKind == JsonValueKind.Number)
                            level = lv.GetInt32();
                        if (level < 1 || level > 6) level = 2;

                        var text = data.TryGetProperty("text", out var tx) ? tx.GetString() : "";
                        sb.Append($"<h{level}>{Esc(text)}</h{level}>");
                        continue;
                    }

                    if (type == "paragraph")
                    {
                        var text = data.TryGetProperty("text", out var tx) ? tx.GetString() : "";
                        sb.Append($"<p>{Esc(text)}</p>");
                        continue;
                    }

                    if (type == "list")
                    {
                        var style = data.TryGetProperty("style", out var st) ? st.GetString() : "unordered";
                        var tag = (style == "ordered") ? "ol" : "ul";
                        sb.Append($"<{tag}>");

                        if (data.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var it in items.EnumerateArray())
                                sb.Append($"<li>{Esc(it.GetString())}</li>");
                        }

                        sb.Append($"</{tag}>");
                        continue;
                    }

                    if (type == "quote")
                    {
                        var text = data.TryGetProperty("text", out var tx) ? tx.GetString() : "";
                        var cap = data.TryGetProperty("caption", out var cp) ? cp.GetString() : "";

                        sb.Append("<blockquote>");
                        sb.Append($"<p><b>{Esc(text)}</b></p>");
                        if (!string.IsNullOrWhiteSpace(cap)) sb.Append($"<div><i>{Esc(cap)}</i></div>");
                        sb.Append("</blockquote>");
                        continue;
                    }

                    if (type == "delimiter")
                    {
                        sb.Append("<hr/>");
                        continue;
                    }

                    if (type == "image")
                    {
                        string? url = null;
                        if (data.TryGetProperty("file", out var file) && file.ValueKind == JsonValueKind.Object &&
                            file.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String)
                            url = u.GetString();

                        var cap = data.TryGetProperty("caption", out var cp) ? cp.GetString() : "";

                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            sb.Append("<figure>");
                            sb.Append($"<img src=\"{Esc(url)}\" alt=\"\"/>");
                            if (!string.IsNullOrWhiteSpace(cap)) sb.Append($"<figcaption>{Esc(cap)}</figcaption>");
                            sb.Append("</figure>");
                        }
                        continue;
                    }
                }

                var html = sb.ToString().Trim();
                return html.Length == 0 ? null : html;
            }
            catch
            {
                return null;
            }
        }

        private IActionResult Fail(long id, bool isAutoSave, string message, int? statusCode = null)
        {
            if (isAutoSave)
            {
                if (statusCode.HasValue)
                    return StatusCode(statusCode.Value, new { ok = false, message });
                return BadRequest(new { ok = false, message });
            }

            TempData["CmsError"] = message;

            if (id > 0) return RedirectToAction("Edit", new { id });
            return RedirectToAction("Edit");
        }

        private async Task FillCatTagOptions(SqlConnection conn, CmsArticleEditVm vm, SqlTransaction? tx = null)
        {
            vm.Category_Options = (await conn.QueryAsync<CmsOptionVm>(
                @"SELECT id, name
                  FROM dbo.tbl_article_category
                  WHERE is_deleted=0 AND is_active=1
                  ORDER BY sort_order, id",
                transaction: tx
            )).ToList();

            vm.Tag_Options = (await conn.QueryAsync<CmsOptionVm>(
                @"SELECT id, name
                  FROM dbo.tbl_article_tag
                  WHERE is_deleted=0 AND is_active=1
                  ORDER BY sort_order, id",
                transaction: tx
            )).ToList();

            vm.Category_Options ??= new List<CmsOptionVm>();
            vm.Tag_Options ??= new List<CmsOptionVm>();
        }

        private bool IsAdmin()
    => User?.HasClaim("cms_is_admin", "1") == true;

        private async Task<IActionResult> FailView(
            SqlConnection conn,
            long id,
            bool isAutoSave,
            string message,
            CmsArticleEditVm vm,
            int? statusCode = null)
        {
            if (isAutoSave)
            {
                if (statusCode.HasValue)
                    return StatusCode(statusCode.Value, new { ok = false, message });
                return BadRequest(new { ok = false, message });
            }

            TempData["CmsError"] = message;

            await FillCatTagOptions(conn, vm);
            return View("Edit", vm);
        }

        private CmsArticleEditVm BuildVmFromPost(
            long id,
            string title,
            string? slug,
            string? excerpt,
            string? cover_image_url,
            string content_html,
            bool is_featured,
            int? featured_order,
            string? meta_title,
            string? meta_description,
            string? og_image_url,
            string? canonical_url,
            string? category_ids_csv,
            string? tag_ids_csv,
            string? scheduled_at_vn,
            string? concurrency_token,
            List<CmsArticleCardVm> cards
        )
        {
            var vm = new CmsArticleEditVm
            {
                Id = id,
                Title = title,
                Slug = slug,
                Excerpt = excerpt,
                Cover_Image_Url = cover_image_url,
                Content_Html = content_html,
                Is_Featured = is_featured,
                Featured_Order = featured_order,
                Meta_Title = meta_title,
                Meta_Description = meta_description,
                Og_Image_Url = og_image_url,
                Canonical_Url = canonical_url,
                Concurrency_Token = concurrency_token,
                Cards = cards ?? new List<CmsArticleCardVm>(),
                Category_Ids = ParseIds(category_ids_csv),
                Tag_Ids = ParseIds(tag_ids_csv),
            };

            if (!string.IsNullOrWhiteSpace(scheduled_at_vn)
                && DateTime.TryParseExact(
                    scheduled_at_vn,
                    new[] { "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss" },
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var vnLocal))
            {
                vm.Scheduled_At_Vn = vnLocal;
                vm.Scheduled_At_Utc = VnToUtc(vnLocal);
            }

            return vm;
        }
    }
}
