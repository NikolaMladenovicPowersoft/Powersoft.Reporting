/*
 * searchable-select.js
 * Lightweight, dependency-free searchable combobox that enhances a native <select>.
 *
 * Why this exists: native <select> only supports first-letter type-ahead. Users linked
 * to many companies/databases need to type a partial code or name and see ONLY the
 * matching rows (substring match), with the match highlighted. See Control Panel
 * company/database selection.
 *
 * The original <select> stays in the DOM (visually hidden) and remains the value source:
 * selecting an item sets select.value and dispatches a 'change' event, so any existing
 * listeners keep working. Call instance.refresh() after you mutate the select's options.
 */
(function () {
    'use strict';

    function escapeRegExp(s) {
        return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    }

    // Highlight every occurrence of `query` inside `text` (case-insensitive), HTML-safe.
    function highlight(text, query) {
        if (!query) {
            return document.createTextNode(text).textContent
                .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
        }
        var re = new RegExp('(' + escapeRegExp(query) + ')', 'ig');
        var parts = text.split(re);
        var html = '';
        for (var i = 0; i < parts.length; i++) {
            var safe = parts[i].replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
            // Odd indices are the captured (matched) segments.
            html += (i % 2 === 1) ? '<mark>' + safe + '</mark>' : safe;
        }
        return html;
    }

    function SearchableSelect(select, opts) {
        opts = opts || {};
        this.select = select;
        this.placeholder = opts.placeholder || 'Type to search...';
        this.noResultsText = opts.noResultsText || 'No matches found';
        this.activeIndex = -1;
        this.items = [];
        this.build();
    }

    SearchableSelect.prototype.build = function () {
        var self = this;
        this.select.style.display = 'none';
        this.select.classList.add('searchable-select-source');

        var wrapper = document.createElement('div');
        wrapper.className = 'searchable-select';

        var input = document.createElement('input');
        input.type = 'text';
        input.className = this.select.className.replace('searchable-select-source', '').trim() + ' searchable-select-input';
        input.setAttribute('autocomplete', 'off');
        input.setAttribute('role', 'combobox');
        input.setAttribute('aria-autocomplete', 'list');
        input.setAttribute('aria-expanded', 'false');
        input.placeholder = this.placeholder;

        var menu = document.createElement('div');
        menu.className = 'searchable-select-menu';
        menu.style.display = 'none';

        wrapper.appendChild(input);
        wrapper.appendChild(menu);
        this.select.parentNode.insertBefore(wrapper, this.select.nextSibling);

        this.wrapper = wrapper;
        this.input = input;
        this.menu = menu;

        input.addEventListener('focus', function () { self.open(); });
        input.addEventListener('click', function () { self.open(); });
        input.addEventListener('input', function () { self.activeIndex = -1; self.render(input.value); });
        input.addEventListener('keydown', function (e) { self.onKeyDown(e); });

        document.addEventListener('click', function (e) {
            if (!wrapper.contains(e.target)) self.close();
        });

        this.refresh();
    };

    // Re-read options + disabled state from the underlying <select>.
    SearchableSelect.prototype.refresh = function () {
        this.items = Array.prototype.slice.call(this.select.options)
            .filter(function (o) { return o.value !== ''; })
            .map(function (o) { return { value: o.value, text: o.textContent.trim() }; });

        if (this.select.disabled) {
            this.input.disabled = true;
            this.input.value = '';
            this.input.placeholder = this.select.options.length && this.select.options[0]
                ? this.select.options[0].textContent.trim()
                : this.placeholder;
            this.close();
            return;
        }

        this.input.disabled = false;
        this.input.placeholder = this.placeholder;

        var selected = this.select.options[this.select.selectedIndex];
        this.input.value = (selected && selected.value !== '') ? selected.textContent.trim() : '';
    };

    SearchableSelect.prototype.open = function () {
        if (this.input.disabled) return;
        this.render(this.input.value);
        this.menu.style.display = 'block';
        this.input.setAttribute('aria-expanded', 'true');
    };

    SearchableSelect.prototype.close = function () {
        this.menu.style.display = 'none';
        this.input.setAttribute('aria-expanded', 'false');
    };

    SearchableSelect.prototype.filtered = function (query) {
        var q = (query || '').trim().toLowerCase();
        if (!q) return this.items.slice();
        return this.items.filter(function (it) {
            return it.text.toLowerCase().indexOf(q) !== -1;
        });
    };

    SearchableSelect.prototype.render = function (query) {
        var self = this;
        var matches = this.filtered(query);
        this.menu.innerHTML = '';
        this.currentMatches = matches;

        if (matches.length === 0) {
            var empty = document.createElement('div');
            empty.className = 'searchable-select-empty';
            empty.textContent = this.noResultsText;
            this.menu.appendChild(empty);
            return;
        }

        matches.forEach(function (it, idx) {
            var opt = document.createElement('div');
            opt.className = 'searchable-select-option' + (idx === self.activeIndex ? ' active' : '');
            opt.setAttribute('role', 'option');
            opt.innerHTML = highlight(it.text, (query || '').trim());
            opt.addEventListener('mousedown', function (e) {
                e.preventDefault();
                self.choose(it);
            });
            opt.addEventListener('mouseenter', function () {
                self.activeIndex = idx;
                self.updateActive();
            });
            self.menu.appendChild(opt);
        });
    };

    SearchableSelect.prototype.updateActive = function () {
        var nodes = this.menu.querySelectorAll('.searchable-select-option');
        for (var i = 0; i < nodes.length; i++) {
            nodes[i].classList.toggle('active', i === this.activeIndex);
        }
        if (this.activeIndex >= 0 && nodes[this.activeIndex]) {
            nodes[this.activeIndex].scrollIntoView({ block: 'nearest' });
        }
    };

    SearchableSelect.prototype.choose = function (it) {
        this.select.value = it.value;
        this.input.value = it.text;
        this.close();
        this.select.dispatchEvent(new Event('change', { bubbles: true }));
    };

    SearchableSelect.prototype.onKeyDown = function (e) {
        var matches = this.currentMatches || [];
        switch (e.key) {
            case 'ArrowDown':
                e.preventDefault();
                if (this.menu.style.display === 'none') { this.open(); return; }
                this.activeIndex = Math.min(this.activeIndex + 1, matches.length - 1);
                this.updateActive();
                break;
            case 'ArrowUp':
                e.preventDefault();
                this.activeIndex = Math.max(this.activeIndex - 1, 0);
                this.updateActive();
                break;
            case 'Enter':
                if (this.menu.style.display !== 'none' && this.activeIndex >= 0 && matches[this.activeIndex]) {
                    e.preventDefault();
                    this.choose(matches[this.activeIndex]);
                }
                break;
            case 'Escape':
                this.close();
                break;
        }
    };

    // Inject component styles once.
    function injectStyles() {
        if (document.getElementById('searchable-select-styles')) return;
        var css =
            '.searchable-select{position:relative;}' +
            '.searchable-select-input{cursor:text;}' +
            '.searchable-select-input:disabled{background-color:#e9ecef;}' +
            '.searchable-select-menu{position:absolute;z-index:1060;left:0;right:0;top:100%;' +
            'margin-top:2px;max-height:280px;overflow-y:auto;background:#fff;border:1px solid #ced4da;' +
            'border-radius:.375rem;box-shadow:0 .5rem 1rem rgba(0,0,0,.15);}' +
            '.searchable-select-option{padding:.5rem .75rem;cursor:pointer;white-space:nowrap;' +
            'overflow:hidden;text-overflow:ellipsis;}' +
            '.searchable-select-option:hover,.searchable-select-option.active{background-color:#0d6efd;color:#fff;}' +
            '.searchable-select-option.active mark,.searchable-select-option:hover mark{background-color:#ffe08a;color:inherit;}' +
            '.searchable-select-option mark{background-color:#fff3cd;padding:0;}' +
            '.searchable-select-empty{padding:.5rem .75rem;color:#6c757d;font-style:italic;}';
        var style = document.createElement('style');
        style.id = 'searchable-select-styles';
        style.textContent = css;
        document.head.appendChild(style);
    }

    window.SearchableSelect = function (selectOrId, opts) {
        injectStyles();
        var el = typeof selectOrId === 'string' ? document.getElementById(selectOrId) : selectOrId;
        if (!el) return null;
        return new SearchableSelect(el, opts);
    };
})();
