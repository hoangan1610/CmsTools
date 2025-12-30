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
    // [B2] Toast helper (Bootstrap 5)
    // =========================
    function ensureToastWrap() {
        let wrap = document.querySelector(".cms-toast-wrap");
        if (!wrap) {
            wrap = document.createElement("div");
            wrap.className = "cms-toast-wrap";
            // inline style để khỏi cần css riêng
            wrap.style.position = "fixed";
            wrap.style.right = "16px";
            wrap.style.top = "16px";
            wrap.style.zIndex = "1085";
            wrap.style.display = "flex";
            wrap.style.flexDirection = "column";
            wrap.style.gap = ".5rem";
            document.body.appendChild(wrap);
        }
        return wrap;
    }

    function escapeHtml(s) {
        return String(s ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#39;");
    }

    function showToast(message, type) {
        // type: success | danger | warning | info
        const wrap = ensureToastWrap();

        const toast = document.createElement("div");
        toast.className = "toast align-items-center text-bg-" + (type || "success") + " border-0";
        toast.setAttribute("role", "alert");
        toast.setAttribute("aria-live", "assertive");
        toast.setAttribute("aria-atomic", "true");

        toast.innerHTML = `
      <div class="d-flex">
        <div class="toast-body">${escapeHtml(message || "")}</div>
        <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>
      </div>`;

        wrap.appendChild(toast);

        const t = bootstrap.Toast.getOrCreateInstance(toast, { delay: 2200 });
        toast.addEventListener("hidden.bs.toast", () => toast.remove());
        t.show();
    }

    function closeModalIfInside(form) {
        const modalEl = form.closest(".modal");
        if (!modalEl) return false;
        const inst = bootstrap.Modal.getInstance(modalEl) || new bootstrap.Modal(modalEl);
        inst.hide();
        return true;
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

    function initImageClear(root) {
        qsa(root, 'input[data-clear-btn]').forEach(inp => {
            if (inp.dataset.clearBound === "1") return;
            inp.dataset.clearBound = "1";

            const clearId = inp.getAttribute("data-clear-btn");
            const btn = findById(root, clearId);
            if (!btn) return;

            btn.addEventListener("click", function () {
                inp.value = "";
                inp.dispatchEvent(new Event("input", { bubbles: true }));
                inp.focus();
            });
        });
    }

    function initImageUpload(root) {
        qsa(root, 'input.cms-img-file[data-target-input]').forEach(fileInp => {
            if (fileInp.dataset.upBound === "1") return;
            fileInp.dataset.upBound = "1";

            const targetIdRaw = fileInp.getAttribute("data-target-input"); // "field_xxx"
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
        initImageClear(root);
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

    // Lưu URL modal hiện tại để có thể reload modal sau submit nếu muốn
    let __cmsCurrentModalUrl = null;
    let __cmsCurrentModalTitle = null;

    async function openCmsModal(url, title) {
        if (!modal || !modalEl || !modalBodyEl) {
            alert("Thiếu #cmsModal trong layout hoặc bootstrap chưa load.");
            return;
        }

        __cmsCurrentModalUrl = url;
        __cmsCurrentModalTitle = title || "Modal";

        modalTitleEl.textContent = __cmsCurrentModalTitle;
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

    async function reloadCmsModal() {
        if (!__cmsCurrentModalUrl) return;
        await openCmsModal(__cmsCurrentModalUrl, __cmsCurrentModalTitle);
    }

    // Click handler: bất kỳ element có data-cms-modal
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
    // [F0] ✅ FIX: ghi nhớ nút submit được click (submitter)
    // =========================
    function setFormSubmitter(form, submitter) {
        if (!form || !submitter) return;
        const name = submitter.getAttribute("name");
        if (!name) {
            form.__cms_submitter = null;
            return;
        }
        const value = submitter.getAttribute("value") ?? "";
        form.__cms_submitter = { name, value };
    }

    // Bắt click vào button submit trong form ajax để lưu submitter
    document.addEventListener("click", function (e) {
        const btn = e.target.closest('button[type="submit"], input[type="submit"]');
        if (!btn) return;

        const form = btn.form;
        if (!form) return;

        // chỉ form ajax
        if (!form.matches || !form.matches('form[data-cms-ajax="true"]')) return;

        setFormSubmitter(form, btn);
    }, true);

    // =========================
    // [F] AJAX submit cho form (modal + page) (data-cms-ajax="true")
    // =========================
    document.addEventListener("submit", async function (e) {
        const form = e.target;
        if (!form || !form.matches || !form.matches('form[data-cms-ajax="true"]')) return;

        e.preventDefault();

        const action = form.action;
        const method = (form.method || "post").toUpperCase();

        // disable submit + spinner
        const submitBtn = form.querySelector('button[type="submit"], input[type="submit"]');
        const oldBtnHtml = submitBtn ? submitBtn.innerHTML : null;
        const oldBtnVal = submitBtn && submitBtn.tagName === "INPUT" ? submitBtn.value : null;

        try {
            if (submitBtn) {
                submitBtn.disabled = true;
                if (submitBtn.tagName === "BUTTON") {
                    submitBtn.innerHTML = `<span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span>Đang lưu...`;
                } else {
                    submitBtn.value = "Đang lưu...";
                }
            }

            const fd = new FormData(form);

            // ✅ FIX: append submitter nếu chưa có trong FormData
            const sub = form.__cms_submitter;
            if (sub && sub.name) {
                if (!fd.has(sub.name)) {
                    fd.append(sub.name, sub.value ?? "");
                }
            }
            form.__cms_submitter = null;

            const res = await fetch(action, {
                method: method,
                body: fd,
                headers: { "X-Requested-With": "XMLHttpRequest" }
            });

            if (isJsonResponse(res)) {
                const json = await res.json().catch(() => null);

                if (json && json.ok) {
                    // ✅ Success behavior:
                    // - default: toast + đóng modal (nếu đang ở modal)
                    // - optional: <form data-cms-success="reload-modal"> => toast + reload modal
                    const mode = (form.getAttribute("data-cms-success") || "close").toLowerCase();

                    showToast(json.message || json.msg || "Lưu thành công.", "success");

                    // bắn event cho trang list muốn reload/update row
                    document.dispatchEvent(new CustomEvent("cms:data:saved", {
                        detail: {
                            action: action,
                            mode: mode,
                            result: json
                        }
                    }));

                    if (mode === "reload-modal") {
                        await reloadCmsModal();
                        return;
                    }

                    // close modal nếu form nằm trong modal
                    const closed = closeModalIfInside(form);

                    // nếu không nằm trong modal (trang thường), bạn có thể reload nhẹ nếu muốn:
                    // if (!closed) window.location.reload();

                    return;
                }

                showToast(json?.message || json?.msg || "Lưu thất bại.", "danger");
                return;
            }

            // HTML partial trả về (validation error / view)
            const html = await res.text();
            const body = form.closest(".modal-body") || modalBodyEl || form.parentElement || document.body;
            body.innerHTML = html;

            // re-init widgets
            initCmsForm(body);

            // nếu response không ok, báo toast nhẹ
            if (!res.ok) showToast("Có lỗi khi lưu. Vui lòng kiểm tra lại.", "danger");
        }
        catch (err) {
            console.error(err);
            showToast("Lỗi mạng hoặc server. Vui lòng thử lại.", "danger");
        }
        finally {
            if (submitBtn) {
                submitBtn.disabled = false;
                if (submitBtn.tagName === "BUTTON") {
                    if (oldBtnHtml != null) submitBtn.innerHTML = oldBtnHtml;
                } else {
                    if (oldBtnVal != null) submitBtn.value = oldBtnVal;
                }
            }
        }
    }, true);

    // =========================
    // [G] Init khi load page thường (không modal)
    // =========================
    document.addEventListener("DOMContentLoaded", function () {
        initCmsForm(document);

        // (Optional) Nếu bạn muốn auto reload list sau khi save trong modal:
        // document.addEventListener("cms:data:saved", () => window.location.reload());
    });


    // =========================
    // [H] Auto refresh List after modal saved
    // =========================
    async function refreshCmsList() {
        const listRoot = document.getElementById("cmsListRoot");
        if (!listRoot) {
            // không phải trang List → thôi
            return;
        }

        // giữ scroll cho đỡ giật
        const scrollY = window.scrollY;

        try {
            // lấy lại đúng URL hiện tại (giữ filter/page/querystring)
            const url = window.location.href;

            const res = await fetch(url, {
                method: "GET",
                headers: { "X-Requested-With": "XMLHttpRequest" }
            });

            const html = await res.text();

            // parse HTML mới và trích đúng #cmsListRoot
            const parser = new DOMParser();
            const doc = parser.parseFromString(html, "text/html");
            const newRoot = doc.getElementById("cmsListRoot");

            if (!newRoot) {
                // server trả về không có wrapper → fallback reload full
                window.location.reload();
                return;
            }

            // replace DOM
            listRoot.innerHTML = newRoot.innerHTML;

            // init lại các widget trong vùng list (nếu list có FK filter / image / ... )
            // (initCmsForm của bạn init FK/image; ok để gọi)
            initCmsForm(listRoot);

            // khôi phục scroll
            window.scrollTo(0, scrollY);
        } catch (e) {
            console.error(e);
            // lỗi fetch → fallback reload full
            window.location.reload();
        }
    }

    // Lắng nghe event save thành công (đã dispatch ở đoạn submit JSON ok)
    document.addEventListener("cms:data:saved", function () {
        // nếu đang có modal, đợi modal đóng hẳn rồi refresh list
        if (modalEl && modalEl.classList.contains("show")) {
            modalEl.addEventListener("hidden.bs.modal", function onHidden() {
                modalEl.removeEventListener("hidden.bs.modal", onHidden);
                refreshCmsList();
            });
        } else {
            refreshCmsList();
        }
    });


})();
