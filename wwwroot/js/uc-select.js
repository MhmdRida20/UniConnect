/* ==========================================================================
   uc-select — progressive enhancement for <select>.

   Native <option> elements are drawn by the OS, so they can't take hover
   animation, brand colours, or weight changes. This replaces the popup with a
   styled listbox while keeping the real <select> in the DOM, so:

     • the form still posts the same field,
     • asp-for / unobtrusive validation still bind to it,
     • anything listening for 'change' (the form progress rail) still fires.

   Opt out on a specific control with data-no-ucs. Multi-selects are skipped.
   ========================================================================== */
(function () {
    'use strict';

    var SEARCH_THRESHOLD = 8;   // show a filter box once the list gets long
    var open = null;            // the single currently-open instance

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('select').forEach(enhance);
    });

    function enhance(select) {
        if (select.multiple) return;
        if (select.hasAttribute('data-no-ucs')) return;
        if (select.closest('.ucs')) return;              // already enhanced
        if (!select.options.length) return;

        var wrap = document.createElement('div');
        wrap.className = 'ucs';
        // Carry the sizing classes across so the control keeps its layout box
        // (form-select / auth-select drive width and padding on these pages).
        if (select.classList.contains('auth-select')) wrap.classList.add('ucs-auth');
        select.parentNode.insertBefore(wrap, select);
        wrap.appendChild(select);
        select.classList.add('ucs-native');

        var trigger = document.createElement('button');
        trigger.type = 'button';
        trigger.className = 'ucs-trigger';
        trigger.setAttribute('aria-haspopup', 'listbox');
        trigger.setAttribute('aria-expanded', 'false');
        if (select.disabled) trigger.disabled = true;
        trigger.innerHTML = '<span class="ucs-value"></span>'
            + '<svg class="hgi ucs-caret" aria-hidden="true"><use href="#i-arrow-right"></use></svg>';
        wrap.appendChild(trigger);

        var panel = document.createElement('div');
        panel.className = 'ucs-panel';
        panel.hidden = true;

        var searchWrap = null, search = null;
        if (select.options.length >= SEARCH_THRESHOLD) {
            searchWrap = document.createElement('div');
            searchWrap.className = 'ucs-search-wrap';
            searchWrap.innerHTML = '<svg class="hgi ucs-search-ico" aria-hidden="true"><use href="#i-search"></use></svg>';
            search = document.createElement('input');
            search.type = 'text';
            search.className = 'ucs-search';
            search.placeholder = 'Search…';
            search.setAttribute('aria-label', 'Filter options');
            searchWrap.appendChild(search);
            panel.appendChild(searchWrap);
        }

        var list = document.createElement('ul');
        list.className = 'ucs-list';
        list.setAttribute('role', 'listbox');
        panel.appendChild(list);

        var empty = document.createElement('p');
        empty.className = 'ucs-empty';
        empty.textContent = 'No matches';
        empty.hidden = true;
        panel.appendChild(empty);

        wrap.appendChild(panel);

        var items = [];
        buildItems();

        var activeIndex = -1;
        var typeahead = '';
        var typeaheadTimer = null;

        function buildItems() {
            list.innerHTML = '';
            items = [];
            Array.prototype.forEach.call(select.children, function (child) {
                if (child.tagName === 'OPTGROUP') {
                    var groupLabel = document.createElement('li');
                    groupLabel.className = 'ucs-group';
                    groupLabel.textContent = child.label;
                    groupLabel.setAttribute('role', 'presentation');
                    list.appendChild(groupLabel);
                    Array.prototype.forEach.call(child.children, addOption);
                } else if (child.tagName === 'OPTION') {
                    addOption(child);
                }
            });
        }

        function addOption(opt) {
            // <option hidden> is the "-- Select --" prompt some forms use to
            // force a real choice; it has no text, so rendering it would leave
            // a blank unusable row at the top of the list.
            if (opt.hidden) return;

            var li = document.createElement('li');
            li.className = 'ucs-opt';
            li.setAttribute('role', 'option');
            li.dataset.value = opt.value;
            // A blank value is the "-- Select --" prompt, not a real choice.
            if (opt.value === '') li.classList.add('is-placeholder');
            if (opt.disabled) li.classList.add('is-disabled');
            li.innerHTML = '<span class="ucs-opt-text"></span>'
                + '<svg class="hgi ucs-opt-check" aria-hidden="true"><use href="#i-check-circle"></use></svg>';
            li.querySelector('.ucs-opt-text').textContent = opt.textContent.trim();
            li.addEventListener('click', function () {
                if (opt.disabled) return;
                choose(opt);
            });
            li.addEventListener('mousemove', function () {
                setActive(items.indexOf(li), false);
            });
            list.appendChild(li);
            items.push(li);
        }

        function visibleItems() {
            return items.filter(function (li) { return !li.hidden && !li.classList.contains('is-disabled'); });
        }

        function syncFromNative() {
            var opt = select.options[select.selectedIndex];
            var valueEl = trigger.querySelector('.ucs-value');
            var isPlaceholder = !opt || opt.value === '';
            valueEl.textContent = opt ? opt.textContent.trim() : '';
            trigger.classList.toggle('is-placeholder', isPlaceholder);
            items.forEach(function (li) {
                var selected = !isPlaceholder && li.dataset.value === select.value;
                li.classList.toggle('is-selected', selected);
                li.setAttribute('aria-selected', selected ? 'true' : 'false');
            });
            // Mirror validation state so the styled trigger turns red too.
            trigger.classList.toggle('is-invalid', select.classList.contains('input-validation-error'));
        }

        function choose(opt) {
            select.value = opt.value;
            select.dispatchEvent(new Event('input', { bubbles: true }));
            select.dispatchEvent(new Event('change', { bubbles: true }));
            syncFromNative();
            trigger.classList.remove('ucs-pop');
            void trigger.offsetWidth;          // restart the value-swap animation
            trigger.classList.add('ucs-pop');
            close(true);
        }

        function setActive(index, scroll) {
            if (activeIndex >= 0 && items[activeIndex]) items[activeIndex].classList.remove('is-active');
            activeIndex = index;
            var li = items[activeIndex];
            if (!li) { list.removeAttribute('aria-activedescendant'); return; }
            li.classList.add('is-active');
            if (!li.id) li.id = 'ucs-opt-' + Math.random().toString(36).slice(2, 8);
            list.setAttribute('aria-activedescendant', li.id);
            if (scroll !== false) li.scrollIntoView({ block: 'nearest' });
        }

        function moveActive(delta) {
            var vis = visibleItems();
            if (!vis.length) return;
            var current = vis.indexOf(items[activeIndex]);
            var next = current + delta;
            if (next < 0) next = vis.length - 1;
            if (next >= vis.length) next = 0;
            setActive(items.indexOf(vis[next]));
        }

        // The panel is moved to <body> while open and positioned with fixed
        // coordinates. Rendering it in place looked fine in isolation but was
        // clipped by any ancestor with overflow (table cards, scroll regions)
        // and trapped behind siblings by any ancestor with a transform. A
        // portal sidesteps both without having to police every container.
        function openPanel() {
            if (open && open !== api) open.close();
            open = api;
            document.body.appendChild(panel);
            panel.hidden = false;
            wrap.classList.add('is-open');
            trigger.setAttribute('aria-expanded', 'true');
            placePanel();
            attachReposition();
            if (search) { search.value = ''; filter(''); search.focus(); }
            var selectedIdx = items.findIndex(function (li) { return li.classList.contains('is-selected'); });
            setActive(selectedIdx >= 0 ? selectedIdx : items.indexOf(visibleItems()[0]));
        }

        function close(focusTrigger) {
            panel.hidden = true;
            wrap.classList.remove('is-open');
            panel.classList.remove('ucs-drop-up');
            trigger.setAttribute('aria-expanded', 'false');
            detachReposition();
            if (panel.parentNode !== wrap) wrap.appendChild(panel);   // un-portal
            if (open === api) open = null;
            if (focusTrigger) trigger.focus();
        }

        // Fixed-position the portalled panel against the trigger, flipping
        // above it when there isn't room below.
        function placePanel() {
            var r = trigger.getBoundingClientRect();

            // Never narrower than the trigger, but give short triggers enough
            // room that option text isn't needlessly truncated (the role
            // pickers in the admin user table are only ~130px wide).
            var width = Math.max(r.width, 190);
            var left = Math.min(r.left, window.innerWidth - width - 8);
            panel.style.width = width + 'px';
            panel.style.left = Math.max(8, left) + 'px';

            panel.style.top = '0px';                  // measure unconstrained
            var height = Math.min(panel.scrollHeight, 280);
            var spaceBelow = window.innerHeight - r.bottom;
            var dropUp = spaceBelow < height + 12 && r.top > spaceBelow;

            panel.classList.toggle('ucs-drop-up', dropUp);
            panel.style.top = (dropUp ? Math.max(8, r.top - height - 6) : r.bottom + 6) + 'px';
        }

        // Anything that scrolls under a fixed panel has to move it too, or it
        // detaches from its trigger. Capture phase catches scrolling ancestors,
        // not just the window.
        function onReposition() {
            if (panel.hidden) return;
            var r = trigger.getBoundingClientRect();
            if (r.bottom < 0 || r.top > window.innerHeight) { close(false); return; }
            placePanel();
        }
        function attachReposition() {
            window.addEventListener('scroll', onReposition, true);
            window.addEventListener('resize', onReposition);
        }
        function detachReposition() {
            window.removeEventListener('scroll', onReposition, true);
            window.removeEventListener('resize', onReposition);
        }

        function filter(term) {
            var q = term.trim().toLowerCase();
            var shown = 0;
            items.forEach(function (li) {
                var match = !q || li.textContent.toLowerCase().indexOf(q) !== -1;
                li.hidden = !match;
                if (match) shown++;
            });
            // Group headings whose options are all filtered out shouldn't linger.
            list.querySelectorAll('.ucs-group').forEach(function (g) {
                var next = g.nextElementSibling, any = false;
                while (next && !next.classList.contains('ucs-group')) {
                    if (!next.hidden) { any = true; break; }
                    next = next.nextElementSibling;
                }
                g.hidden = !any;
            });
            empty.hidden = shown > 0;
            var first = visibleItems()[0];
            setActive(first ? items.indexOf(first) : -1);
        }

        /* ---------- events ---------- */
        trigger.addEventListener('click', function () {
            if (trigger.disabled) return;
            wrap.classList.contains('is-open') ? close(true) : openPanel();
        });

        trigger.addEventListener('keydown', function (e) {
            if (e.key === 'ArrowDown' || e.key === 'ArrowUp' || e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                openPanel();
            }
        });

        (search || trigger).addEventListener('keydown', handleListKeys);
        panel.addEventListener('keydown', handleListKeys);

        function handleListKeys(e) {
            if (panel.hidden) return;
            switch (e.key) {
                case 'ArrowDown': e.preventDefault(); moveActive(1); break;
                case 'ArrowUp': e.preventDefault(); moveActive(-1); break;
                case 'Home': e.preventDefault(); setActive(items.indexOf(visibleItems()[0])); break;
                case 'End': e.preventDefault(); var v = visibleItems(); setActive(items.indexOf(v[v.length - 1])); break;
                case 'Enter':
                    e.preventDefault();
                    var li = items[activeIndex];
                    if (li && !li.hidden) {
                        var opt = findOption(li.dataset.value);
                        if (opt) choose(opt);
                    }
                    break;
                case 'Escape': e.preventDefault(); close(true); break;
                case 'Tab': close(false); break;
                default:
                    // Type-ahead only when there's no search box to type into.
                    if (!search && e.key.length === 1) {
                        typeahead += e.key.toLowerCase();
                        clearTimeout(typeaheadTimer);
                        typeaheadTimer = setTimeout(function () { typeahead = ''; }, 600);
                        var hit = items.find(function (x) {
                            return !x.hidden && x.textContent.trim().toLowerCase().startsWith(typeahead);
                        });
                        if (hit) setActive(items.indexOf(hit));
                    }
            }
        }

        if (search) search.addEventListener('input', function () { filter(search.value); });

        function findOption(value) {
            return Array.prototype.find.call(select.options, function (o) { return o.value === value; });
        }

        // The panel is portalled to <body> while open, so it is NOT a
        // descendant of wrap — both have to be checked, or the first click on
        // an option would register as an outside click and close the list.
        document.addEventListener('click', function (e) {
            if (panel.hidden) return;
            if (!wrap.contains(e.target) && !panel.contains(e.target)) close(false);
        });

        // Server-side re-render or JS changing the value must refresh the label.
        select.addEventListener('change', syncFromNative);
        // Validation runs after ours, so re-mirror the error class when it lands.
        select.addEventListener('blur', syncFromNative);
        select.addEventListener('focus', function () { trigger.focus(); });

        var api = { close: close };
        syncFromNative();
        // jQuery validation toggles input-validation-error without an event.
        new MutationObserver(syncFromNative)
            .observe(select, { attributes: true, attributeFilter: ['class'] });
    }

    // Each open instance repositions itself on scroll/resize; nothing global
    // to do here beyond closing on a page-level Escape.
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && open) open.close();
    });
})();
