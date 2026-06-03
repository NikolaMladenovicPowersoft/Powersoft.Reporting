/**
 * permissions.js — runs after DOM loads on every report page.
 * Reads _viewCost / _viewSupplier injected by Razor from ViewBag (set per-request in ReportsController).
 * Hides/shows cost, profit, margin, and supplier columns + Profit Based On cost options.
 *
 * Convention:
 *   - Cost columns:   data-perm="cost"
 *   - Profit columns: data-perm="profit"
 *   - Margin columns: data-perm="margin"
 *   - Supplier cols:  data-perm="supplier"
 *   - Profit-based-on cost option: data-perm-opt="cost" (on <option> or <div>)
 *
 * The _viewCost / _viewSupplier vars are declared inline by each report view (Razor injected).
 * If they are undefined (e.g. non-report page), we default to true (show everything).
 */

(function () {
    'use strict';

    function resolveFlag(varName) {
        try {
            // Variable is declared in the page's own <script> block
            var val = window[varName];
            return val === undefined ? true : (val === true || val === 'true');
        } catch (e) {
            return true;
        }
    }

    function applyPermissions() {
        var canViewCost     = resolveFlag('_viewCost');
        var canViewSupplier = resolveFlag('_viewSupplier');

        // ---- Column visibility ----
        // Selector targets both <th> and <td> elements with the permission attribute.
        // Table headers and data cells share the same data-perm value.

        if (!canViewCost) {
            // Hide cost, profit, margin columns
            document.querySelectorAll('[data-perm="cost"], [data-perm="profit"], [data-perm="margin"]')
                .forEach(function (el) { el.style.display = 'none'; });

            // Disable "Profit Based On" options that reference cost
            document.querySelectorAll('[data-perm-opt="cost"]')
                .forEach(function (el) {
                    el.disabled = true;
                    el.style.display = 'none';
                });
        }

        if (!canViewSupplier) {
            document.querySelectorAll('[data-perm="supplier"]')
                .forEach(function (el) { el.style.display = 'none'; });

            // Disable + hide <option>/<div> that select a supplier dimension/grouping
            document.querySelectorAll('[data-perm-opt="supplier"]')
                .forEach(function (el) {
                    el.disabled = true;
                    el.style.display = 'none';
                    // If a now-hidden option was selected, fall back to the first enabled option
                    if (el.tagName === 'OPTION' && el.selected && el.parentElement) {
                        var sel = el.parentElement.closest ? el.parentElement.closest('select') : null;
                        if (sel) {
                            var firstOk = Array.prototype.find.call(sel.options, function (o) { return !o.disabled; });
                            if (firstOk) sel.value = firstOk.value;
                        }
                    }
                });
        }
    }

    // Run after full DOM is ready (DOMContentLoaded may have already fired on deferred scripts)
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', applyPermissions);
    } else {
        applyPermissions();
    }

    // Also expose globally so report JS can call it after dynamic table renders
    window.applyReportPermissions = applyPermissions;
})();
