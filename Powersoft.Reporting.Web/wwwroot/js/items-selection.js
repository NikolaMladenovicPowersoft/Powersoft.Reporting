/*
 * Reusable Items Selection filter component.
 *
 * Rendered markup lives in Views/Shared/_ItemsSelection.cshtml (+ _DimensionFilter.cshtml).
 * That partial calls window.ItemsSelectionInit(config) once per page, passing the only two
 * server-dependent values (storesJson, savedFilterJson). Everything else is config-driven via
 * ItemsSelectionConfig flags that decide which dimension columns the partial renders.
 *
 * Public contract (called by report views / inline onclick handlers):
 *   window.getItemsSelectionFilter()      -> selection object (serialise to ItemsSelectionJson)
 *   window.getItemsSelectionStoreCodes()  -> legacy comma store codes (include mode only)
 *   window.getItemsSelectionItemIds()     -> legacy comma item ids (include mode only)
 *   window.reloadItemsSelectionFromJson(o)-> restore selection (SaveLayout / preset load)
 *   plus UI handlers: toggleItemsSelection, setDimMode, clearDimFilter, openDimModal, etc.
 */
(function () {
    'use strict';

    window.ItemsSelectionInit = function (config) {
        config = config || {};
        // Guard against double init (e.g. partial accidentally included twice).
        if (window.__itemsSelectionInited) return;
        window.__itemsSelectionInited = true;

        const _dims = {};
        const _selected = {};
        const _selInclude = {};
        const _selExclude = {};
        const _modes = {};
        const _storesJson = config.storesJson || [];
        const _savedFilterJson = config.savedFilterJson || '';
        let _modalDim = null;
        let _modalTempSelected = new Set();
        let _bsModal = null;
        let _showSelectedOnly = false;
        const _summaryLoading = {};

        function initDim(dim) {
            _dims[dim] = [];
            _selected[dim] = new Set();
            _selInclude[dim] = new Set();
            _selExclude[dim] = new Set();
            _modes[dim] = 'all';
        }

        ['category', 'department', 'brand', 'season', 'supplier', 'customer', 'agent', 'postalcode',
            'paymenttype', 'zreport', 'town', 'user', 'store', 'item',
            'model', 'colour', 'size', 'groupsize', 'fabric',
            'attr1', 'attr2', 'attr3', 'attr4', 'attr5', 'attr6'].forEach(initDim);

        if (_storesJson && _storesJson.length) {
            _dims['store'] = _storesJson.map(s => ({
                id: s.code || s.Code || s.id,
                code: s.code || s.Code || '',
                name: s.name || s.Name || s.code || s.Code
            }));
        }

        function restoreFromJson(json) {
            if (!json) return;
            try {
                var saved = JSON.parse(json);
                var keyToDim = {
                    categories: 'category', departments: 'department', brands: 'brand',
                    seasons: 'season', suppliers: 'supplier', customers: 'customer',
                    agents: 'agent', postalcodes: 'postalcode',
                    paymenttypes: 'paymenttype', zreports: 'zreport',
                    towns: 'town', users: 'user',
                    stores: 'store', items: 'item',
                    models: 'model', colours: 'colour', sizes: 'size',
                    groupsizes: 'groupsize', fabrics: 'fabric',
                    attributes1: 'attr1', attributes2: 'attr2', attributes3: 'attr3',
                    attributes4: 'attr4', attributes5: 'attr5', attributes6: 'attr6'
                };
                var hasAny = false;
                Object.keys(saved).forEach(function (key) {
                    var dim = keyToDim[key.toLowerCase()] || key.toLowerCase();
                    var entry = saved[key];
                    if (!entry) return;
                    var mode = entry.mode;
                    if (typeof mode === 'number') mode = mode === 1 ? 'include' : (mode === 2 ? 'exclude' : 'all');
                    if (!mode || mode === 'all') return;
                    var ids = entry.ids || [];
                    if (!ids.length) return;
                    _modes[dim] = mode;
                    _selected[dim] = new Set(ids.map(String));
                    if (mode === 'include') _selInclude[dim] = new Set(_selected[dim]);
                    else if (mode === 'exclude') _selExclude[dim] = new Set(_selected[dim]);
                    hasAny = true;
                    var modeBtn = document.querySelector('[data-dim="' + dim + '"] .btn-group .btn[data-mode="' + mode + '"]');
                    if (modeBtn) {
                        var grp = modeBtn.closest('.btn-group');
                        grp.querySelectorAll('.btn').forEach(function (b) {
                            b.classList.remove('active', 'btn-outline-success', 'btn-outline-danger',
                                'btn-success', 'btn-danger');
                            var m = b.getAttribute('data-mode');
                            if (m === mode) {
                                b.classList.add('active', m === 'include' ? 'btn-success' : 'btn-danger');
                            } else {
                                b.classList.add(m === 'include' ? 'btn-outline-success' : 'btn-outline-danger');
                            }
                        });
                    }
                    updateTrigger(dim);
                });
                if (saved.stock && saved.stock !== 'all') {
                    var stockEl = document.getElementById('isPropStock');
                    if (stockEl) { stockEl.value = saved.stock; hasAny = true; }
                }
                if (saved.ecommerceOnly) {
                    var ecomEl = document.getElementById('isPropEcommerce');
                    if (ecomEl) { ecomEl.checked = true; hasAny = true; }
                }
                if (saved.modifiedAfter) {
                    var mfEl = document.getElementById('isPropModifiedFrom');
                    if (mfEl) { mfEl.value = saved.modifiedAfter; hasAny = true; }
                }
                if (saved.modifiedBefore) {
                    var mtEl = document.getElementById('isPropModifiedTo');
                    if (mtEl) { mtEl.value = saved.modifiedBefore; hasAny = true; }
                }
                if (saved.createdAfter) {
                    var cfEl = document.getElementById('isPropCreatedFrom');
                    if (cfEl) { cfEl.value = saved.createdAfter; hasAny = true; }
                }
                if (saved.createdBefore) {
                    var ctEl = document.getElementById('isPropCreatedTo');
                    if (ctEl) { ctEl.value = saved.createdBefore; hasAny = true; }
                }
                if (saved.releasedAfter) {
                    var rfEl = document.getElementById('isPropReleasedFrom');
                    if (rfEl) { rfEl.value = saved.releasedAfter; hasAny = true; }
                }
                if (saved.releasedBefore) {
                    var rtEl = document.getElementById('isPropReleasedTo');
                    if (rtEl) { rtEl.value = saved.releasedBefore; hasAny = true; }
                }

                if (hasAny) {
                    updateBadge();
                    var body = document.getElementById('isBody');
                    var txt = document.getElementById('isToggleText');
                    body.classList.remove('d-none');
                    txt.innerHTML = '<i class="bi bi-eye-slash me-1"></i>Hide Filters';
                }
            } catch (e) { }
        }

        window.reloadItemsSelectionFromJson = function (obj) {
            if (!obj) return;
            var json = typeof obj === 'string' ? obj : JSON.stringify(obj);
            restoreFromJson(json);
        };

        window.toggleItemsSelection = function () {
            var body = document.getElementById('isBody');
            var txt = document.getElementById('isToggleText');
            var showing = !body.classList.contains('d-none');
            if (showing) {
                body.classList.add('d-none');
                txt.innerHTML = '<i class="bi bi-eye me-1"></i>Show Filters';
            } else {
                body.classList.remove('d-none');
                txt.innerHTML = '<i class="bi bi-eye-slash me-1"></i>Hide Filters';
            }
        };

        window.setDimMode = function (dim, btn) {
            var grp = btn.closest('.btn-group');
            var prevMode = _modes[dim];
            var mode = btn.getAttribute('data-mode');

            // Meeting decision (George): switching directly between include and exclude
            // clears the selection so the user re-picks for the new intent. Selecting from
            // a fresh ('all') state keeps whatever was just chosen (include is the default).
            var switchingActive = (prevMode === 'include' && mode === 'exclude') ||
                (prevMode === 'exclude' && mode === 'include');
            if (switchingActive) {
                _selected[dim] = new Set();
                _selInclude[dim] = new Set();
                _selExclude[dim] = new Set();
            }

            _modes[dim] = mode;
            grp.querySelectorAll('.btn').forEach(function (b) {
                b.classList.remove('active', 'btn-outline-secondary', 'btn-outline-success', 'btn-outline-danger',
                    'btn-secondary', 'btn-success', 'btn-danger');
                var m = b.getAttribute('data-mode');
                if (m === mode) {
                    b.classList.add('active', m === 'include' ? 'btn-success' : 'btn-danger');
                } else {
                    b.classList.add(m === 'include' ? 'btn-outline-success' : 'btn-outline-danger');
                }
            });
            updateTrigger(dim);
            updateBadge();
        };

        window.clearDimFilter = function (dim) {
            _modes[dim] = 'all';
            _selected[dim] = new Set();
            _selInclude[dim] = new Set();
            _selExclude[dim] = new Set();
            var grp = document.querySelector('[data-dim="' + dim + '"] .btn-group');
            if (grp) grp.querySelectorAll('.btn').forEach(function (b) {
                b.classList.remove('active', 'btn-success', 'btn-danger');
                b.classList.add(b.getAttribute('data-mode') === 'include' ? 'btn-outline-success' : 'btn-outline-danger');
            });
            updateTrigger(dim);
            updateBadge();
        };

        var _naDims = { category: 1, department: 1, brand: 1, season: 1, supplier: 1 };

        function loadDimData(dim) {
            if (_dims[dim] && _dims[dim].length) return Promise.resolve();
            return fetch('/Reports/GetDimensions?type=' + dim)
                .then(function (r) { return r.json(); })
                .then(function (data) {
                    if (_naDims[dim]) {
                        data.unshift({ id: '__NA__', code: 'N/A', name: 'Not Applicable (empty)' });
                    }
                    _dims[dim] = data;
                });
        }

        window.openDimModal = function (dim, label) {
            _modalDim = dim;
            _modalTempSelected = new Set(_selected[dim]);
            _showSelectedOnly = false;
            var selToggle = document.getElementById('dimModalSelectedToggle');
            if (selToggle) { selToggle.classList.remove('active', 'btn-secondary'); selToggle.classList.add('btn-outline-secondary'); }
            document.getElementById('dimModalTitle').innerHTML = '<i class="bi bi-funnel me-1"></i>Select ' + label;
            document.getElementById('dimModalSearch').value = '';
            document.getElementById('dimModalList').innerHTML =
                '<div class="text-center py-4"><span class="spinner-border spinner-border-sm text-primary me-2"></span>Loading...</div>';

            if (!_bsModal) _bsModal = new bootstrap.Modal(document.getElementById('dimModal'));
            _bsModal.show();

            loadDimData(dim).then(function () {
                renderModalList();
                updateModalCount();
                var searchInput = document.getElementById('dimModalSearch');
                if (searchInput) setTimeout(function () { searchInput.focus(); }, 100);
                // Names are now available — refresh the selected-summary chips with real names.
                renderSelectedSummary();
            });
        };

        var _naMarker = '__NA__';
        var _PAGE_SIZE = 100;
        var _filteredItems = [];
        var _renderedCount = 0;

        function buildItemHtml(item) {
            var chk = _modalTempSelected.has(item.id) ? 'checked' : '';
            return '<label class="list-group-item list-group-item-action d-flex align-items-center py-1 px-3" style="cursor:pointer">' +
                '<input class="form-check-input me-3 flex-shrink-0" type="checkbox" value="' + item.id + '" ' + chk +
                ' onchange="toggleDimModalItem(\'' + item.id + '\',this.checked)">' +
                '<span class="text-muted small me-2" style="min-width:60px">' + (item.code || '') + '</span>' +
                '<span class="small">' + (item.name || '') + '</span></label>';
        }

        function renderModalList(filter) {
            var list = document.getElementById('dimModalList');
            var items = _dims[_modalDim] || [];
            if (filter) {
                var f = filter.toLowerCase();
                items = items.filter(function (i) {
                    if (i.id === _naMarker) return false;
                    return (i.code || '').toLowerCase().indexOf(f) >= 0 || (i.name || '').toLowerCase().indexOf(f) >= 0;
                });
            } else {
                items = items.filter(function (i) { return i.id !== _naMarker; });
            }
            if (_showSelectedOnly) {
                items = items.filter(function (i) { return _modalTempSelected.has(i.id); });
            }
            _filteredItems = items;
            _renderedCount = 0;

            var html = '';
            var showNa = (!_showSelectedOnly || _modalTempSelected.has(_naMarker));
            if (showNa && (!filter || 'n/a'.indexOf(filter.toLowerCase()) >= 0)) {
                var naChk = _modalTempSelected.has(_naMarker) ? 'checked' : '';
                html += '<label class="list-group-item list-group-item-action d-flex align-items-center py-1 px-3 border-bottom-2" style="cursor:pointer;background:#fefce8">' +
                    '<input class="form-check-input me-3 flex-shrink-0" type="checkbox" value="' + _naMarker + '" ' + naChk +
                    ' onchange="toggleDimModalItem(\'' + _naMarker + '\',this.checked)">' +
                    '<span class="text-muted small me-2" style="min-width:60px"><i class="bi bi-dash-circle"></i></span>' +
                    '<span class="fst-italic text-secondary small">N/A (no value)</span></label>';
            }
            if (!items.length && !html) {
                list.innerHTML = '<div class="text-muted text-center py-4">No items found</div>';
                return;
            }

            var end = Math.min(_PAGE_SIZE, items.length);
            for (var i = 0; i < end; i++) {
                html += buildItemHtml(items[i]);
            }
            _renderedCount = end;
            list.innerHTML = html;

            if (_renderedCount < items.length) {
                appendLoadMoreSentinel(list);
            }
        }

        function appendLoadMoreSentinel(list) {
            var sentinel = document.createElement('div');
            sentinel.id = 'dimModalSentinel';
            sentinel.className = 'text-center py-2 text-muted small';
            sentinel.textContent = (_filteredItems.length - _renderedCount) + ' more — scroll to load';
            list.appendChild(sentinel);
            var obs = new IntersectionObserver(function (entries) {
                if (entries[0].isIntersecting) {
                    obs.disconnect();
                    loadMoreItems();
                }
            }, { root: list });
            obs.observe(sentinel);
        }

        function loadMoreItems() {
            var list = document.getElementById('dimModalList');
            var sentinel = document.getElementById('dimModalSentinel');
            if (sentinel) sentinel.remove();
            var end = Math.min(_renderedCount + _PAGE_SIZE, _filteredItems.length);
            var frag = document.createDocumentFragment();
            for (var i = _renderedCount; i < end; i++) {
                var tmp = document.createElement('div');
                tmp.innerHTML = buildItemHtml(_filteredItems[i]);
                frag.appendChild(tmp.firstChild);
            }
            _renderedCount = end;
            list.appendChild(frag);
            if (_renderedCount < _filteredItems.length) {
                appendLoadMoreSentinel(list);
            }
        }

        var _searchDebounce = null;
        window.filterDimModalList = function (val) {
            clearTimeout(_searchDebounce);
            _searchDebounce = setTimeout(function () { renderModalList(val); }, 150);
        };

        window.toggleDimModalSelectedOnly = function (btn) {
            _showSelectedOnly = !_showSelectedOnly;
            if (_showSelectedOnly) {
                btn.classList.remove('btn-outline-secondary');
                btn.classList.add('active', 'btn-secondary');
            } else {
                btn.classList.remove('active', 'btn-secondary');
                btn.classList.add('btn-outline-secondary');
            }
            var search = document.getElementById('dimModalSearch');
            renderModalList(search ? search.value : '');
        };

        window.toggleDimModalItem = function (id, on) {
            if (on) _modalTempSelected.add(id); else _modalTempSelected.delete(id);
            updateModalCount();
        };

        window.selectAllDimModal = function () {
            _modalTempSelected.add(_naMarker);
            (_dims[_modalDim] || []).forEach(function (i) { _modalTempSelected.add(i.id); });
            var list = document.getElementById('dimModalList');
            list.querySelectorAll('input[type="checkbox"]').forEach(function (cb) { cb.checked = true; });
            updateModalCount();
        };

        window.selectNoneDimModal = function () {
            _modalTempSelected.clear();
            var list = document.getElementById('dimModalList');
            list.querySelectorAll('input[type="checkbox"]').forEach(function (cb) { cb.checked = false; });
            updateModalCount();
        };

        function updateModalCount() {
            var total = (_dims[_modalDim] || []).length;
            var showing = _filteredItems.length;
            var countText = showing < total ? showing + ' / ' + total : total + ' item' + (total !== 1 ? 's' : '');
            document.getElementById('dimModalCount').textContent = countText;

            // A modal edits a single dimension in a single mode. A fresh ('all') dimension is
            // promoted to Include on Apply, so show INCLUDE as the effective intent.
            var effectiveMode = _modes[_modalDim] === 'exclude' ? 'exclude' : 'include';
            var modeEl = document.getElementById('dimModalMode');
            if (modeEl) {
                modeEl.textContent = effectiveMode === 'include' ? 'INCLUDE' : 'EXCLUDE';
                modeEl.className = 'badge ms-2 ' + (effectiveMode === 'include' ? 'bg-success' : 'bg-danger');
            }

            // Names of the items currently ticked (the in-progress selection, not yet applied).
            var items = _dims[_modalDim] || [];
            var names = [];
            items.forEach(function (it) {
                if (_modalTempSelected.has(it.id) && it.id !== _naMarker) names.push(it.name || it.code || it.id);
            });
            if (_modalTempSelected.has(_naMarker)) names.unshift('N/A');

            var sel = _modalTempSelected.size;
            var selEl = document.getElementById('dimModalSelected');
            if (selEl) {
                selEl.textContent = (effectiveMode === 'include' ? 'Including ' : 'Excluding ') + sel;
                selEl.className = 'small fw-semibold ' + (effectiveMode === 'include' ? 'text-success' : 'text-danger');
            }
            var namesEl = document.getElementById('dimModalSelectedNames');
            if (namesEl) {
                var MAX = 8;
                namesEl.textContent = names.length
                    ? names.slice(0, MAX).join(', ') + (names.length > MAX ? '  +' + (names.length - MAX) + ' more' : '')
                    : 'Nothing selected yet';
                namesEl.title = names.join(', ');
            }
        }

        window.applyDimModal = function () {
            _selected[_modalDim] = new Set(_modalTempSelected);
            if (_modes[_modalDim] === 'all' && _selected[_modalDim].size > 0) {
                _selInclude[_modalDim] = new Set(_selected[_modalDim]);
                var incBtn = document.querySelector('[data-dim="' + _modalDim + '"] .btn-group .btn[data-mode="include"]');
                if (incBtn) setDimMode(_modalDim, incBtn);
            }
            var curMode = _modes[_modalDim];
            if (curMode === 'include') _selInclude[_modalDim] = new Set(_selected[_modalDim]);
            else if (curMode === 'exclude') _selExclude[_modalDim] = new Set(_selected[_modalDim]);
            updateTrigger(_modalDim);
            updateBadge();
            _bsModal.hide();
        };

        function getDimLabel(dim) {
            var el = document.querySelector('[data-dim="' + dim + '"] .dim-text');
            if (!el) return dim;
            var trigger = document.getElementById('dimTrigger_' + dim);
            return trigger ? trigger.getAttribute('title').replace('Select ', '') : dim;
        }

        function getSelectedNames(dim, max) {
            var items = _dims[dim] || [];
            var sel = _selected[dim];
            if (!sel || sel.size === 0) return '';
            var names = [];
            items.forEach(function (it) {
                if (sel.has(it.id) && it.id !== '__NA__') names.push(it.name || it.code || it.id);
            });
            if (sel.has('__NA__')) names.unshift('N/A');
            if (names.length <= max) return names.join(', ');
            return names.slice(0, max).join(', ') + ' +' + (names.length - max);
        }

        function updateTrigger(dim) {
            var txt = document.querySelector('#dimTrigger_' + dim + ' .dim-text');
            if (!txt) return;
            var cnt = _selected[dim].size;
            var mode = _modes[dim];
            var trigger = document.getElementById('dimTrigger_' + dim);
            var clearBtn = document.getElementById('dimClear_' + dim);
            trigger.classList.remove('btn-outline-secondary', 'btn-outline-success', 'btn-outline-danger');
            if (mode === 'all' || cnt === 0) {
                txt.textContent = getDimLabel(dim);
                txt.className = 'dim-text text-muted small';
                trigger.classList.add('btn-outline-secondary');
                if (clearBtn) clearBtn.classList.add('d-none');
            } else {
                var names = getSelectedNames(dim, 2);
                var prefix = mode === 'include' ? '' : 'NOT ';
                txt.textContent = names ? prefix + names : cnt + (mode === 'include' ? ' included' : ' excluded');
                txt.className = 'dim-text fw-semibold small ' + (mode === 'include' ? 'text-success' : 'text-danger');
                trigger.classList.add(mode === 'include' ? 'btn-outline-success' : 'btn-outline-danger');
                if (clearBtn) clearBtn.classList.remove('d-none');
            }
        }

        function countPropertyFilters() {
            var cnt = 0;
            var stockEl = document.getElementById('isPropStock');
            if (stockEl && stockEl.value !== 'all') cnt++;
            var ecomEl = document.getElementById('isPropEcommerce');
            if (ecomEl && ecomEl.checked) cnt++;
            if ((document.getElementById('isPropModifiedFrom') || {}).value || (document.getElementById('isPropModifiedTo') || {}).value) cnt++;
            if ((document.getElementById('isPropCreatedFrom') || {}).value || (document.getElementById('isPropCreatedTo') || {}).value) cnt++;
            if ((document.getElementById('isPropReleasedFrom') || {}).value || (document.getElementById('isPropReleasedTo') || {}).value) cnt++;
            return cnt;
        }

        function updateBadge() {
            var cnt = 0;
            Object.keys(_modes).forEach(function (d) {
                if (_modes[d] !== 'all' && _selected[d].size > 0) cnt++;
            });
            cnt += countPropertyFilters();
            var badge = document.getElementById('isActiveBadge');
            if (badge) {
                if (cnt > 0) {
                    badge.textContent = cnt + ' filter' + (cnt > 1 ? 's' : '');
                    badge.classList.remove('d-none');
                } else {
                    badge.classList.add('d-none');
                }
            }
            renderSelectedSummary();
        }

        function escapeHtml(s) {
            return String(s).replace(/[&<>"']/g, function (c) {
                return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
            });
        }

        var _dimLabelMap = {
            category: 'Category', department: 'Department', brand: 'Brand', season: 'Season',
            supplier: 'Supplier', customer: 'Customer', agent: 'Agent', postalcode: 'Postal Code',
            paymenttype: 'Payment Type', zreport: 'Z Report', town: 'Town', user: 'User',
            store: 'Store', item: 'Item', model: 'Model', colour: 'Colour', size: 'Size',
            groupsize: 'Group Size', fabric: 'Fabric',
            attr1: 'Attribute 1', attr2: 'Attribute 2', attr3: 'Attribute 3',
            attr4: 'Attribute 4', attr5: 'Attribute 5', attr6: 'Attribute 6'
        };

        // Best-effort: pull dimension names for an active filter even if its modal was never opened
        // (e.g. restored saved filter / preset), so the summary shows real names not "N selected".
        function ensureNamesLoaded(dim) {
            if (_summaryLoading[dim]) return;
            if (_dims[dim] && _dims[dim].length) return;
            // The item catalogue can be huge — don't auto-pull it just to label a chip; the count
            // fallback is fine and names still appear once the item modal is opened.
            if (dim === 'item') return;
            _summaryLoading[dim] = true;
            loadDimData(dim)
                .then(function () { renderSelectedSummary(); })
                .catch(function () { /* leave count fallback */ });
        }

        // "Show selected" list with names (meeting/Danny): a card-level panel that lists every
        // active dimension, the names of the chosen items, an Include/Exclude tag and a one-click
        // remove. Long selections truncate to a few names with the full list available on hover.
        function renderSelectedSummary() {
            var wrap = document.getElementById('isSelectedSummary');
            var body = document.getElementById('isSelectedSummaryBody');
            if (!wrap || !body) return;

            var chips = '';
            Object.keys(_modes).forEach(function (dim) {
                if (_modes[dim] === 'all' || _selected[dim].size === 0) return;
                var mode = _modes[dim];
                var names = getSelectedNames(dim, 6);
                var fullNames = getSelectedNames(dim, 100000);
                if (!names) {
                    // Data not loaded yet — show count now, fetch names and re-render.
                    names = _selected[dim].size + ' selected';
                    fullNames = names;
                    ensureNamesLoaded(dim);
                }
                var modeClass = mode === 'include' ? 'border-success' : 'border-danger';
                var tagBg = mode === 'include' ? 'bg-success' : 'bg-danger';
                var tagText = mode === 'include' ? 'Include' : 'Exclude';
                chips += '<span class="badge rounded-pill bg-white border ' + modeClass +
                    ' text-dark d-inline-flex align-items-center me-1 mb-1" style="font-weight:500;" title="' +
                    escapeHtml(_dimLabelMap[dim] || dim) + ' — ' + tagText + ': ' + escapeHtml(fullNames) + '">' +
                    '<span class="badge ' + tagBg + ' me-1" style="font-size:.6rem;">' + tagText + '</span>' +
                    '<span class="me-1 text-muted">' + escapeHtml(_dimLabelMap[dim] || dim) + ':</span>' +
                    escapeHtml(names) +
                    '<button type="button" class="btn-close ms-2" style="font-size:.55rem;" ' +
                    'aria-label="Remove" onclick="clearDimFilter(\'' + dim + '\')"></button>' +
                    '</span>';
            });

            if (chips) {
                body.innerHTML = chips;
                wrap.classList.remove('d-none');
            } else {
                body.innerHTML = '';
                wrap.classList.add('d-none');
            }
        }

        window.updatePropBadge = function () { updateBadge(); };

        window.getItemsSelectionFilter = function () {
            var filter = {};
            var dimKeyMap = {
                category: 'categories', department: 'departments', brand: 'brands',
                season: 'seasons', supplier: 'suppliers', customer: 'customers',
                agent: 'agents', postalcode: 'postalcodes',
                paymenttype: 'paymenttypes', zreport: 'zreports',
                town: 'towns', user: 'users',
                store: 'stores', item: 'items',
                model: 'models', colour: 'colours', size: 'sizes',
                groupsize: 'groupSizes', fabric: 'fabrics',
                attr1: 'attributes1', attr2: 'attributes2', attr3: 'attributes3',
                attr4: 'attributes4', attr5: 'attributes5', attr6: 'attributes6'
            };
            Object.keys(_modes).forEach(function (dim) {
                var key = dimKeyMap[dim] || dim;
                filter[key] = { ids: Array.from(_selected[dim]), mode: _modes[dim] };
            });

            var stockEl = document.getElementById('isPropStock');
            if (stockEl) filter.stock = stockEl.value;
            var ecomEl = document.getElementById('isPropEcommerce');
            if (ecomEl) filter.ecommerceOnly = ecomEl.checked;
            var mf = document.getElementById('isPropModifiedFrom');
            if (mf && mf.value) filter.modifiedAfter = mf.value;
            var mt = document.getElementById('isPropModifiedTo');
            if (mt && mt.value) filter.modifiedBefore = mt.value;
            var cf = document.getElementById('isPropCreatedFrom');
            if (cf && cf.value) filter.createdAfter = cf.value;
            var ct = document.getElementById('isPropCreatedTo');
            if (ct && ct.value) filter.createdBefore = ct.value;
            var rf = document.getElementById('isPropReleasedFrom');
            if (rf && rf.value) filter.releasedAfter = rf.value;
            var rt = document.getElementById('isPropReleasedTo');
            if (rt && rt.value) filter.releasedBefore = rt.value;

            return filter;
        };

        window.getItemsSelectionStoreCodes = function () {
            if (_modes['store'] !== 'include' || _selected['store'].size === 0) return '';
            return Array.from(_selected['store']).join(',');
        };

        window.getItemsSelectionItemIds = function () {
            if (_modes['item'] !== 'include' || !_selected['item'] || _selected['item'].size === 0) return '';
            return Array.from(_selected['item']).join(',');
        };

        var _presetsLoaded = false;
        var _presetCache = {};
        window.loadFilterPresets = function () {
            if (_presetsLoaded) return;
            _presetsLoaded = true;
            var reportType = document.getElementById('itemsSelectionCard')?.getAttribute('data-report-type') || '';
            fetch('/Reports/GetFilterPresets?reportType=' + encodeURIComponent(reportType))
                .then(function (r) { return r.json(); })
                .then(function (presets) {
                    var menu = document.getElementById('presetMenu');
                    if (!presets.length) {
                        menu.innerHTML = '<li><span class="dropdown-item-text text-muted small fst-italic">No saved presets</span></li>';
                        return;
                    }
                    var html = '';
                    _presetCache = {};
                    presets.forEach(function (p) {
                        _presetCache[p.presetId] = p.filterJson;
                        html += '<li class="d-flex align-items-center px-2 py-1">' +
                            '<a href="#" class="dropdown-item py-1 px-2 small flex-grow-1" onclick="loadFilterPreset(' + p.presetId + ');return false;">' +
                            '<i class="bi bi-bookmark-fill me-1 text-primary"></i>' + p.presetName +
                            (p.isShared ? ' <i class="bi bi-people-fill text-muted ms-1" title="Shared"></i>' : '') +
                            '</a>' +
                            (p.isOwner ? '<button class="btn btn-sm text-danger px-1 py-0" onclick="deleteFilterPreset(' + p.presetId + ');event.stopPropagation();" title="Delete"><i class="bi bi-trash3"></i></button>' : '') +
                            '</li>';
                    });
                    menu.innerHTML = html;
                })
                .catch(function () { _presetsLoaded = false; });
        };

        var dd = document.getElementById('presetDropdown');
        if (dd) dd.addEventListener('click', function () { loadFilterPresets(); });

        window.saveCurrentPreset = function () {
            var name = prompt('Preset name:');
            if (!name || !name.trim()) return;
            var reportType = document.getElementById('itemsSelectionCard')?.getAttribute('data-report-type') || '';
            var filterJson = JSON.stringify(getItemsSelectionFilter());
            fetch('/Reports/SaveFilterPreset', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ name: name.trim(), reportType: reportType, filterJson: filterJson, isShared: false })
            })
                .then(function (r) { return r.json(); })
                .then(function (res) {
                    if (res.success) { _presetsLoaded = false; alert('Preset saved!'); }
                    else alert(res.message || 'Failed to save preset');
                });
        };

        window.loadFilterPreset = function (id) {
            try {
                var json = _presetCache[id];
                if (!json) return;
                Object.keys(_modes).forEach(function (d) { clearDimFilter(d); });
                var stockEl = document.getElementById('isPropStock');
                if (stockEl) stockEl.value = 'all';
                var ecomEl = document.getElementById('isPropEcommerce');
                if (ecomEl) ecomEl.checked = false;
                ['isPropModifiedFrom', 'isPropModifiedTo', 'isPropCreatedFrom', 'isPropCreatedTo', 'isPropReleasedFrom', 'isPropReleasedTo'].forEach(function (id) {
                    var el = document.getElementById(id);
                    if (el) el.value = '';
                });
                restoreFromJson(json);
            } catch (e) { }
        };

        window.deleteFilterPreset = function (id) {
            if (!confirm('Delete this preset?')) return;
            fetch('/Reports/DeleteFilterPreset', {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                body: 'presetId=' + id
            })
                .then(function (r) { return r.json(); })
                .then(function (res) {
                    if (res.success) { _presetsLoaded = false; loadFilterPresets(); }
                });
        };

        // Initial restore (SaveLayout / saved filter passed by the host page).
        restoreFromJson(_savedFilterJson);
    };
})();
