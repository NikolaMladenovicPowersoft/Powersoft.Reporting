// Shared micro-interactions for the .rpt-modern report design system.
// Safe to load on every page: does nothing when no [data-rm-countup] elements exist.
(function () {
    'use strict';

    // KPI count-up (visual only — the final text is always the server-rendered value).
    function initCountup() {
        if (window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
        document.querySelectorAll('[data-rm-countup]').forEach(function (el) {
            if (el.dataset.rmCountupDone) return;
            el.dataset.rmCountupDone = '1';
            var raw = el.textContent.trim();
            var num = parseFloat(raw.replace(/,/g, ''));
            if (isNaN(num)) return;
            var decimals = (raw.split('.')[1] || '').length;
            var start = null, dur = 650;
            function step(ts) {
                if (!start) start = ts;
                var p = Math.min((ts - start) / dur, 1);
                var eased = 1 - Math.pow(1 - p, 3);
                el.textContent = (num * eased).toLocaleString('en-US', { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
                if (p < 1) requestAnimationFrame(step); else el.textContent = raw;
            }
            requestAnimationFrame(step);
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initCountup);
    } else {
        initCountup();
    }

    // Allow client-rendered reports to re-trigger the animation after injecting KPI values.
    window.rptModernCountup = initCountup;
})();
