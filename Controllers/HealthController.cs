using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using CmsTools.Models;
using CmsTools.Services;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CmsTools.Controllers
{
    [Authorize(Policy = "CmsAdminOnly")]
    public sealed class HealthController : Controller
    {
        private readonly ICmsMetaService _meta;

        public HealthController(ICmsMetaService meta)
        {
            _meta = meta;
        }

        [HttpGet]
        public async Task<IActionResult> Connections()
        {
            // Lấy tất cả connection đang active trong meta
            var conns = await _meta.GetConnectionsAsync();

            var list = new List<ConnectionHealthViewModel>();

            foreach (var c in conns)
            {
                var vm = new ConnectionHealthViewModel
                {
                    Id = c.Id,
                    Name = c.Name,
                    Provider = c.Provider,
                    IsActive = c.IsActive
                };

                try
                {
                    // Hiện tại provider của bạn đều là SQL Server,
                    // nên tạm assume dùng SqlConnection.
                    await using var db = new SqlConnection(c.ConnString);
                    await db.OpenAsync();

                    // Test query đơn giản
                    var x = await db.ExecuteScalarAsync<int>("SELECT 1;");
                    vm.IsOk = (x == 1);
                }
                catch (Exception ex)
                {
                    vm.IsOk = false;
                    vm.ErrorMessage = ex.Message;
                }

                list.Add(vm);
            }

            return View(list);  // Views/Health/Connections.cshtml
        }
    }
}
