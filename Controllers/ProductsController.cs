using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using CmsTools.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CmsTools.Controllers
{
    [Authorize] // Có thể đổi thành Policy nếu muốn giới hạn admin
    public sealed class ProductsController : Controller
    {
        private readonly string _metaConn;

        public ProductsController(IConfiguration cfg)
        {
            _metaConn = cfg.GetConnectionString("CmsToolsDb")
                ?? throw new Exception("Missing connection string: CmsToolsDb");
        }

        // Lấy connection string business DB cho tbl_product_info từ meta
        private async Task<string?> GetProductDbConnectionStringAsync()
        {
            await using var conn = new SqlConnection(_metaConn);
            const string sql = @"
SELECT TOP (1) c.conn_string
FROM dbo.tbl_cms_table t
JOIN dbo.tbl_cms_connection c ON c.id = t.connection_id
WHERE t.schema_name = 'dbo'
  AND t.table_name  = 'tbl_product_info';";

            return await conn.ExecuteScalarAsync<string?>(sql);
        }

        // ===== Detail: 1 product + list variants =====

        [HttpGet]
        public async Task<IActionResult> Detail(long id)
        {
            var bizConnStr = await GetProductDbConnectionStringAsync();
            if (string.IsNullOrWhiteSpace(bizConnStr))
                return StatusCode(500, "Không tìm thấy cấu hình connection cho tbl_product_info trong meta.");

            await using var conn = new SqlConnection(bizConnStr);
            await conn.OpenAsync();

            const string sql = @"
SELECT 
    p.id              AS Id,
    p.category_id     AS CategoryId,
    p.brand_name      AS BrandName,
    p.name            AS Name,
    p.tag             AS Tag,
    p.product_keyword AS ProductKeyword,
    p.detail          AS Detail,
    p.image_product   AS ImageProduct,
    p.status          AS Status,
    p.created_at      AS CreatedAt,
    p.updated_at      AS UpdatedAt
FROM dbo.tbl_product_info p
WHERE p.id = @id;

SELECT 
    v.id              AS Id,
    v.product_id      AS ProductId,
    v.name            AS Name,
    v.image           AS Image,
    v.meta_data       AS MetaData,
    v.sku             AS Sku,
    v.weight          AS Weight,
    v.cost_price      AS CostPrice,
    v.finished_cost   AS FinishedCost,
    v.wholesale_price AS WholesalePrice,
    v.retail_price    AS RetailPrice,
    v.stock           AS Stock,
    v.status          AS Status,
    v.created_at      AS CreatedAt,
    v.updated_at      AS UpdatedAt
FROM dbo.tbl_product_variant v
WHERE v.product_id = @id
ORDER BY v.id;";

            using var multi = await conn.QueryMultipleAsync(sql, new { id });
            var product = await multi.ReadFirstOrDefaultAsync<ProductInfoRow>();
            if (product == null)
                return NotFound("Không tìm thấy sản phẩm.");

            var variants = (await multi.ReadAsync<ProductVariantRow>()).ToList();

            var vm = new ProductDetailViewModel
            {
                Product = product,
                Variants = variants
            };

            return View(vm);  // Views/Products/Detail.cshtml
        }

        // ===== Create Variant (GET) =====

        [HttpGet]
        public async Task<IActionResult> CreateVariant(long productId)
        {
            var bizConnStr = await GetProductDbConnectionStringAsync();
            if (string.IsNullOrWhiteSpace(bizConnStr))
                return StatusCode(500, "Không tìm thấy cấu hình connection cho tbl_product_info.");

            await using var conn = new SqlConnection(bizConnStr);
            await conn.OpenAsync();

            const string sqlProd = @"
SELECT id, name
FROM dbo.tbl_product_info
WHERE id = @id;";

            var prod = await conn.QueryFirstOrDefaultAsync(sqlProd, new { id = productId });
            if (prod == null)
                return NotFound("Product không tồn tại.");

            var vm = new ProductVariantEditViewModel
            {
                ProductId = productId,
                ProductName = (string)prod.name,
                Status = 1,
                Stock = 0
            };

            return View(vm);  // Views/Products/CreateVariant.cshtml
        }

        // ===== Create Variant (POST) =====

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateVariant(ProductVariantEditViewModel model)
        {
            if (model.ProductId <= 0)
                return BadRequest("ProductId không hợp lệ.");

            if (string.IsNullOrWhiteSpace(model.Sku))
            {
                ModelState.AddModelError(nameof(model.Sku), "SKU không được để trống.");
            }

            if (!ModelState.IsValid)
                return View(model);

            var bizConnStr = await GetProductDbConnectionStringAsync();
            if (string.IsNullOrWhiteSpace(bizConnStr))
                return StatusCode(500, "Không tìm thấy cấu hình connection cho tbl_product_info.");

            await using var conn = new SqlConnection(bizConnStr);
            await conn.OpenAsync();

            const string sqlInsert = @"
INSERT INTO dbo.tbl_product_variant(
    product_id,
    name,
    image,
    meta_data,
    sku,
    weight,
    cost_price,
    finished_cost,
    wholesale_price,
    retail_price,
    stock,
    status,
    created_at,
    updated_at
) VALUES(
    @ProductId,
    @Name,
    @Image,
    @MetaData,
    @Sku,
    @Weight,
    @CostPrice,
    @FinishedCost,
    @WholesalePrice,
    @RetailPrice,
    @Stock,
    @Status,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);";

            try
            {
                await conn.ExecuteAsync(sqlInsert, new
                {
                    ProductId = model.ProductId,
                    Name = string.IsNullOrWhiteSpace(model.Name) ? null : model.Name,
                    Image = string.IsNullOrWhiteSpace(model.Image) ? null : model.Image,
                    MetaData = string.IsNullOrWhiteSpace(model.MetaData) ? null : model.MetaData,
                    Sku = model.Sku.Trim(),
                    Weight = model.Weight,
                    CostPrice = model.CostPrice ?? 0m,
                    FinishedCost = model.FinishedCost ?? 0m,
                    WholesalePrice = model.WholesalePrice ?? 0m,
                    RetailPrice = model.RetailPrice ?? 0m,
                    Stock = model.Stock ?? 0,
                    Status = model.Status
                });
            }
            catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
            {
                // trùng SKU (unique)
                ModelState.AddModelError(nameof(model.Sku), "SKU đã tồn tại.");
                return View(model);
            }

            return RedirectToAction("Detail", new { id = model.ProductId });
        }

        // ===== Edit Variant (GET) =====

        [HttpGet]
        public async Task<IActionResult> EditVariant(long productId, long id)
        {
            var bizConnStr = await GetProductDbConnectionStringAsync();
            if (string.IsNullOrWhiteSpace(bizConnStr))
                return StatusCode(500, "Không tìm thấy cấu hình connection cho tbl_product_info.");

            await using var conn = new SqlConnection(bizConnStr);
            await conn.OpenAsync();

            const string sql = @"
SELECT 
    p.id              AS ProdId,
    p.name            AS ProdName,
    v.id              AS Id,
    v.product_id      AS ProductId,
    v.name            AS Name,
    v.image           AS Image,
    v.meta_data       AS MetaData,
    v.sku             AS Sku,
    v.weight          AS Weight,
    v.cost_price      AS CostPrice,
    v.finished_cost   AS FinishedCost,
    v.wholesale_price AS WholesalePrice,
    v.retail_price    AS RetailPrice,
    v.stock           AS Stock,
    v.status          AS Status
FROM dbo.tbl_product_variant v
JOIN dbo.tbl_product_info p ON p.id = v.product_id
WHERE v.id = @id AND v.product_id = @productId;";

            var row = await conn.QueryFirstOrDefaultAsync(sql, new { id, productId });
            if (row == null)
                return NotFound("Variant không tồn tại.");

            var vm = new ProductVariantEditViewModel
            {
                Id = row.Id,
                ProductId = row.ProductId,
                ProductName = row.ProdName,
                Name = row.Name,
                Image = row.Image,
                MetaData = row.MetaData,
                Sku = row.Sku,
                Weight = row.Weight,
                CostPrice = row.CostPrice,
                FinishedCost = row.FinishedCost,
                WholesalePrice = row.WholesalePrice,
                RetailPrice = row.RetailPrice,
                Stock = row.Stock,
                Status = row.Status
            };

            return View(vm);  // Views/Products/EditVariant.cshtml
        }

        // ===== Edit Variant (POST) =====

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditVariant(long productId, long id, ProductVariantEditViewModel model)
        {
            if (id <= 0 || productId <= 0)
                return BadRequest("Id hoặc ProductId không hợp lệ.");

            if (string.IsNullOrWhiteSpace(model.Sku))
            {
                ModelState.AddModelError(nameof(model.Sku), "SKU không được để trống.");
            }

            if (!ModelState.IsValid)
                return View(model);

            var bizConnStr = await GetProductDbConnectionStringAsync();
            if (string.IsNullOrWhiteSpace(bizConnStr))
                return StatusCode(500, "Không tìm thấy cấu hình connection cho tbl_product_info.");

            await using var conn = new SqlConnection(bizConnStr);
            await conn.OpenAsync();

            const string sqlUpdate = @"
UPDATE dbo.tbl_product_variant
SET name            = @Name,
    image           = @Image,
    meta_data       = @MetaData,
    sku             = @Sku,
    weight          = @Weight,
    cost_price      = @CostPrice,
    finished_cost   = @FinishedCost,
    wholesale_price = @WholesalePrice,
    retail_price    = @RetailPrice,
    stock           = @Stock,
    status          = @Status,
    updated_at      = SYSUTCDATETIME()
WHERE id = @Id AND product_id = @ProductId;";

            try
            {
                var affected = await conn.ExecuteAsync(sqlUpdate, new
                {
                    Id = id,
                    ProductId = productId,
                    Name = string.IsNullOrWhiteSpace(model.Name) ? null : model.Name,
                    Image = string.IsNullOrWhiteSpace(model.Image) ? null : model.Image,
                    MetaData = string.IsNullOrWhiteSpace(model.MetaData) ? null : model.MetaData,
                    Sku = model.Sku.Trim(),
                    Weight = model.Weight,
                    CostPrice = model.CostPrice ?? 0m,
                    FinishedCost = model.FinishedCost ?? 0m,
                    WholesalePrice = model.WholesalePrice ?? 0m,
                    RetailPrice = model.RetailPrice ?? 0m,
                    Stock = model.Stock ?? 0,
                    Status = model.Status
                });

                if (affected <= 0)
                    return NotFound("Variant không tồn tại hoặc không thuộc product này.");
            }
            catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
            {
                ModelState.AddModelError(nameof(model.Sku), "SKU đã tồn tại.");
                return View(model);
            }

            return RedirectToAction("Detail", new { id = productId });
        }

        // ===== Toggle status variant (Ẩn / Kích hoạt) =====

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleVariantStatus(long productId, long id)
        {
            var bizConnStr = await GetProductDbConnectionStringAsync();
            if (string.IsNullOrWhiteSpace(bizConnStr))
                return StatusCode(500, "Không tìm thấy cấu hình connection cho tbl_product_info.");

            await using var conn = new SqlConnection(bizConnStr);
            await conn.OpenAsync();

            const string sql = @"
DECLARE @current tinyint;

SELECT @current = status
FROM dbo.tbl_product_variant
WHERE id = @Id AND product_id = @ProductId;

IF @current IS NOT NULL
BEGIN
    UPDATE dbo.tbl_product_variant
    SET status = CASE WHEN @current = 1 THEN 0 ELSE 1 END,
        updated_at = SYSUTCDATETIME()
    WHERE id = @Id AND product_id = @ProductId;
END";

            await conn.ExecuteAsync(sql, new { Id = id, ProductId = productId });

            return RedirectToAction("Detail", new { id = productId });
        }
    }
}
