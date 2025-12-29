// wwwroot/js/site.js
(function () {
    'use strict';

    // =========================
    // [A] Navbar collapse (giữ nguyên của bạn)
    // =========================
    document.addEventListener('click', function (e) {
        var target = e.target;
        if (!target.closest) return;

        var link = target.closest('.navbar-nav .nav-link');
        if (!link) return;

        var navCollapse = document.getElementById('cmsNavbar');
        if (!navCollapse) return;

        if (window.getComputedStyle(navCollapse).display !== 'none') {
            var bsCollapse = bootstrap.Collapse.getInstance(navCollapse)
                || new bootstrap.Collapse(navCollapse, { toggle: false });
            bsCollapse.hide();
        }
    }, false);

    // =========================
    // [B] Utils
    // =========================
    function qs(root, sel) { return (root || document).querySelector(sel); }
    function qsa(root, sel) { return Array.from((root || document).querySelectorAll(sel)); }

    function safeEscapeId(id) {
        // CSS.escape không có ở vài browser cũ → fallback
        if (window.CSS && CSS.escape) return CSS.escape(id);
        return String(id).replace(/([ #;?%&,.+*~\':"!^$[\]()=>|\/@])/g, '\\$1');
    }

    function findById(root, id) {
        if (!id) return null;
        const inRoot = qs(root, "#" + safeEscapeId(id));
        return inRoot || document.getElementById(id);
    }

    function isJsonResponse(res) {
        const ct = (res.headers.get("content-type") || "").toLowerCase();
        return ct.includes("application/json");
    }

    // =========================
    // [C] Upload to HAFood API (Phương án A)
    // =========================
    async function uploadToApi(file) {
        const api = (window.__HAFOOD_API || '').replace(/\/+$/, '');
        if (!api) throw new Error("Chưa cấu hình __HAFOOD_API (CmsTools:HAFoodApiBaseUrl)");

        const fd = new FormData();
        fd.append("files", file);

        const headers = {};
        const key = (window.__HAFOOD_API_KEY || '').trim();
        if (key) headers["X-Api-Key"] = key;

        const res = await fetch(api + "/files/images?size_w=1400&size_t=900&size_p=500", {
            method: "POST",
            body: fd,
            headers
        });

        const json = await res.json().catch(() => null);
        if (!res.ok || !json) throw new Error(json?.msg || json?.message || "Upload lỗi");

        const url = json?.data?.urlWeb || json?.data?.urlPhone || "";
        if (!url) throw new Error("API không trả urlWeb/urlPhone");
        return url;
    }

    // =========================
    // [D] Init form widgets (Create/Edit dùng chung)
    // =========================
    function initFkFilter(root) {
        qsa(root, "select[data-filter-input]").forEach(sel => {
            if (sel.dataset.fkBound === "1") return;
            sel.dataset.fkBound = "1";

            const inputId = sel.getAttribute("data-filter-input");
            const inp = findById(root, inputId);
            if (!inp) return;

            const original = Array.from(sel.options).map(o => ({
                value: o.value,
                text: o.text || "",
                isEmpty: o.value === ""
            }));

            function rebuild(q) {
                const query = (q || "").trim().toLowerCase();
                const selectedValue = sel.value;

                sel.innerHTML = "";
                const frag = document.createDocumentFragment();

                const empty = original.find(x => x.isEmpty) || { value: "", text: "-- Không có / Root --", isEmpty: true };
                const opt0 = document.createElement("option");
                opt0.value = empty.value;
                opt0.text = empty.text;
                frag.appendChild(opt0);

                for (const it of original) {
                    if (it.isEmpty) continue;
                    if (!query || it.text.toLowerCase().includes(query)) {
                        const opt = document.createElement("option");
                        opt.value = it.value;
                        opt.text = it.text;
                        frag.appendChild(opt);
                    }
                }

                sel.appendChild(frag);

                if (selectedValue && Array.from(sel.options).some(o => o.value === selectedValue)) {
                    sel.value = selectedValue;
                } else {
                    sel.value = "";
                }
            }

            rebuild(inp.value);
            inp.addEventListener("input", () => rebuild(inp.value));
        });
    }

    function initImagePreview(root) {
        qsa(root, 'input[data-preview-img][data-open-link]').forEach(inp => {
            if (inp.dataset.imgBound === "1") return;
            inp.dataset.imgBound = "1";

            const imgId = inp.getAttribute("data-preview-img");
            const openId = inp.getAttribute("data-open-link");

            const img = findById(root, imgId);
            const a = findById(root, openId);
            if (!img || !a) return;

            function sync() {
                const v = (inp.value || "").trim();
                if (!v) {
                    img.style.display = "none";
                    img.src = "";
                    a.href = "javascript:void(0)";
                    a.classList.add("disabled");
                    return;
                }
                img.style.display = "block";
                img.src = v;
                a.href = v;
                a.classList.remove("disabled");
            }

            inp.addEventListener("input", sync);
            sync();
        });
    }

    function initImageUpload(root) {
        qsa(root, 'input.cms-img-file[data-target-input]').forEach(fileInp => {
            if (fileInp.dataset.upBound === "1") return;
            fileInp.dataset.upBound = "1";

            const targetIdRaw = fileInp.getAttribute("data-target-input"); // bạn đang để "field_xxx"
            const statusId = fileInp.getAttribute("data-status-id");

            const target = targetIdRaw ? findById(root, targetIdRaw) : null;
            const statusEl = statusId ? findById(root, statusId) : null;

            fileInp.addEventListener("change", async () => {
                const file = fileInp.files && fileInp.files[0];
                if (!file) return;

                if (statusEl) statusEl.textContent = "Đang upload...";

                try {
                    const url = await uploadToApi(file);
                    if (target) {
                        target.value = url;
                        target.dispatchEvent(new Event("input", { bubbles: true })); // để preview sync
                    }
                    if (statusEl) statusEl.textContent = "✅ Upload xong";
                } catch (e) {
                    console.error(e);
                    if (statusEl) statusEl.textContent = "❌ " + (e?.message || "Upload lỗi");
                    alert(e?.message || "Upload lỗi");
                } finally {
                    fileInp.value = "";
                }
            });
        });
    }

    function initCmsForm(root) {
        root = root || document;
        initFkFilter(root);
        initImagePreview(root);
        initImageUpload(root);
    }

    // expose nếu bạn muốn gọi tay
    window.cmsCreateFormInit = initCmsForm;
    window.cmsEditFormInit = initCmsForm;

    // =========================
    // [E] Modal open/load
    // =========================
    const modalEl = document.getElementById("cmsModal");
    const modalTitleEl = document.getElementById("cmsModalTitle");
    const modalBodyEl = document.getElementById("cmsModalBody");
    const modal = modalEl ? new bootstrap.Modal(modalEl) : null;

    async function openCmsModal(url, title) {
        if (!modal || !modalEl || !modalBodyEl) {
            alert("Thiếu #cmsModal trong layout hoặc bootstrap chưa load.");
            return;
        }

        modalTitleEl.textContent = title || "Modal";
        modalBodyEl.innerHTML = `
      <div class="py-4 text-center text-muted">
        <div class="spinner-border" role="status"></div>
        <div class="mt-2">Đang tải...</div>
      </div>`;

        modal.show();

        const res = await fetch(url, {
            method: "GET",
            headers: { "X-Requested-With": "XMLHttpRequest" }
        });

        const html = await res.text();
        modalBodyEl.innerHTML = html;

        // init widgets trong modal body
        initCmsForm(modalBodyEl);
    }

    // Click handler: bất kỳ element có data-cms-modal
    // Cách dùng:
    // <a data-cms-modal data-url="..." data-title="...">...</a>
    // hoặc <button data-cms-modal data-url="...">...</button>
    document.addEventListener("click", function (e) {
        const el = e.target.closest("[data-cms-modal]");
        if (!el) return;

        e.preventDefault();

        const url = el.getAttribute("data-url") || el.getAttribute("href");
        if (!url || url.startsWith("#") || url.startsWith("javascript:")) return;

        const title = el.getAttribute("data-title")
            || el.getAttribute("title")
            || (el.textContent || "").trim()
            || "Modal";

        openCmsModal(url, title);
    });

    // =========================
    // [F] AJAX submit cho form trong modal (data-cms-ajax="true")
    // =========================
    document.addEventListener("submit", async function (e) {
        const form = e.target;
        if (!form || !form.matches || !form.matches('form[data-cms-ajax="true"]')) return;

        e.preventDefault();

        const action = form.action;
        const method = (form.method || "post").toUpperCase();
        const fd = new FormData(form);

        const res = await fetch(action, {
            method: method,
            body: fd,
            headers: { "X-Requested-With": "XMLHttpRequest" }
        });

        if (isJsonResponse(res)) {
            const json = await res.json().catch(() => null);
            if (json && json.ok) {
                // đóng modal + reload list cho đơn giản
                if (modal) modal.hide();
                window.location.reload();
                return;
            }

            alert(json?.message || json?.msg || "Lưu thất bại");
            return;
        }

        // HTML partial trả về (validation error)
        const html = await res.text();
        const body = form.closest(".modal-body") || modalBodyEl || document.body;
        body.innerHTML = html;

        // re-init widgets
        initCmsForm(body);
    }, true);

    // =========================
    // [G] Init khi load page thường (không modal)
    // =========================
    document.addEventListener("DOMContentLoaded", function () {
        initCmsForm(document);
    });

})();
