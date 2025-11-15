// wwwroot/js/site.js
(function () {
    'use strict';

    // Đóng navbar collapse sau khi click link trên mobile
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
})();
