/* ==========================================================================
   uc-datepicker.js — the calendar panel that replaces Chrome's built-in one.

   Why replace it at all: the native picker is browser chrome, drawn outside
   the document, so no stylesheet can touch it. Matching the site's palette
   meant owning the widget.

   What is kept from native: the <input> itself. It still holds the value,
   still validates, still submits, and can still be typed into directly. This
   panel only reads and writes input.value, then fires input/change so
   form-progress.js and jQuery validate stay in step.

   Supported types: date, datetime-local, time. Anything else (month, week)
   is left to the browser.
   ========================================================================== */
(function () {
    'use strict';

    var DOW = ['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa'];
    var MONTHS = ['January', 'February', 'March', 'April', 'May', 'June',
                  'July', 'August', 'September', 'October', 'November', 'December'];
    var MONTHS_SHORT = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun',
                        'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

    var openPicker = null;

    function pad(n) { return n < 10 ? '0' + n : '' + n; }
    function sameDay(a, b) {
        return a && b && a.getFullYear() === b.getFullYear()
            && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
    }
    function startOfDay(d) { return new Date(d.getFullYear(), d.getMonth(), d.getDate()); }

    function formatValue(type, d) {
        if (type === 'time') return pad(d.getHours()) + ':' + pad(d.getMinutes());
        var date = d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate());
        if (type === 'date') return date;
        return date + 'T' + pad(d.getHours()) + ':' + pad(d.getMinutes());
    }

    function parseValue(type, value) {
        if (!value) return null;
        var m;
        if (type === 'time') {
            m = /^(\d{1,2}):(\d{2})/.exec(value);
            if (!m) return null;
            var now = new Date();
            return new Date(now.getFullYear(), now.getMonth(), now.getDate(), +m[1], +m[2]);
        }
        m = /^(\d{4})-(\d{2})-(\d{2})(?:T(\d{1,2}):(\d{2}))?/.exec(value);
        if (!m) return null;
        return new Date(+m[1], +m[2] - 1, +m[3], m[4] ? +m[4] : 0, m[5] ? +m[5] : 0);
    }

    function build(input) {
        var type = input.type;
        var wantsDate = type !== 'time';
        var wantsTime = type === 'datetime-local' || type === 'time';

        var min = parseValue(type, input.getAttribute('min'));
        var max = parseValue(type, input.getAttribute('max'));

        // Working value. Falls back to now so the panel always opens somewhere
        // sensible rather than on the epoch.
        var selected = parseValue(type, input.value);
        var cursor = selected ? new Date(selected) : new Date();
        var view = 'days';

        var panel = document.createElement('div');
        panel.className = 'ucdp';
        panel.setAttribute('role', 'dialog');
        panel.setAttribute('aria-label', 'Choose a date');

        /* ---------- markup ---------- */
        panel.innerHTML =
            (wantsDate ?
                '<div class="ucdp-head">' +
                    '<button type="button" class="ucdp-nav" data-step="-1" aria-label="Previous">' +
                        '<svg class="hgi" aria-hidden="true"><use href="#i-arrow-left"></use></svg></button>' +
                    '<button type="button" class="ucdp-title" data-toggle-view></button>' +
                    '<button type="button" class="ucdp-nav" data-step="1" aria-label="Next">' +
                        '<svg class="hgi" aria-hidden="true"><use href="#i-arrow-right"></use></svg></button>' +
                '</div>' +
                '<div class="ucdp-dow">' + DOW.map(function (d) {
                    return '<span>' + d + '</span>';
                }).join('') + '</div>' +
                '<div class="ucdp-grid" data-grid></div>' +
                '<div class="ucdp-months" data-months hidden></div>'
            : '') +
            (wantsTime ?
                '<div class="ucdp-time">' +
                    '<span class="ucdp-time-label">Time</span>' +
                    '<div class="ucdp-time-fields">' +
                        '<input type="text" inputmode="numeric" maxlength="2" class="ucdp-num" data-hour aria-label="Hour">' +
                        '<span class="ucdp-colon">:</span>' +
                        '<input type="text" inputmode="numeric" maxlength="2" class="ucdp-num" data-minute aria-label="Minute">' +
                        '<div class="ucdp-ampm">' +
                            '<button type="button" data-ampm="AM">AM</button>' +
                            '<button type="button" data-ampm="PM">PM</button>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            : '') +
            '<div class="ucdp-foot">' +
                '<button type="button" class="ucdp-link" data-clear>Clear</button>' +
                (wantsDate ? '<button type="button" class="ucdp-link" data-today>Today</button>' : '') +
                '<button type="button" class="ucdp-done" data-done>Done</button>' +
            '</div>';

        var grid = panel.querySelector('[data-grid]');
        var monthsBox = panel.querySelector('[data-months]');
        var title = panel.querySelector('[data-toggle-view]');
        var hourEl = panel.querySelector('[data-hour]');
        var minuteEl = panel.querySelector('[data-minute]');

        function outOfRange(d) {
            if (min && startOfDay(d) < startOfDay(min)) return true;
            if (max && startOfDay(d) > startOfDay(max)) return true;
            return false;
        }

        /* ---------- rendering ---------- */
        function renderDays() {
            title.textContent = MONTHS[cursor.getMonth()] + ' ' + cursor.getFullYear();

            var first = new Date(cursor.getFullYear(), cursor.getMonth(), 1);
            var startOffset = first.getDay();
            var today = new Date();

            grid.replaceChildren();

            // Always 42 cells (6 weeks) so the panel keeps a constant height
            // and never jumps as you page through months. The cells outside
            // the current month are real neighbouring dates, shown greyed —
            // that padding is what keeps the weekday columns aligned.
            for (var i = 0; i < 42; i++) {
                var date = new Date(cursor.getFullYear(), cursor.getMonth(), i - startOffset + 1);
                var isOther = date.getMonth() !== cursor.getMonth();
                var disabled = outOfRange(date);

                var cell = document.createElement('button');
                cell.type = 'button';
                cell.className = 'ucdp-day';
                cell.textContent = date.getDate();
                cell.dataset.iso = formatValue('date', date);
                if (isOther) cell.classList.add('is-outside');
                if (sameDay(date, today)) cell.classList.add('is-today');
                if (selected && sameDay(date, selected)) cell.classList.add('is-selected');
                if (disabled) { cell.classList.add('is-disabled'); cell.disabled = true; }
                cell.style.setProperty('--d', i % 7);

                grid.appendChild(cell);
            }
        }

        function renderMonths() {
            title.textContent = cursor.getFullYear();
            monthsBox.replaceChildren();
            MONTHS_SHORT.forEach(function (label, index) {
                var b = document.createElement('button');
                b.type = 'button';
                b.className = 'ucdp-month';
                b.textContent = label;
                if (index === cursor.getMonth()) b.classList.add('is-selected');
                b.addEventListener('click', function () {
                    cursor = new Date(cursor.getFullYear(), index, 1);
                    view = 'days';
                    render();
                });
                monthsBox.appendChild(b);
            });
        }

        function renderTime() {
            if (!wantsTime) return;
            var base = selected || cursor;
            var h24 = base.getHours();
            var h12 = h24 % 12 || 12;
            hourEl.value = pad(h12);
            minuteEl.value = pad(base.getMinutes());
            panel.querySelectorAll('[data-ampm]').forEach(function (b) {
                b.classList.toggle('is-active', b.dataset.ampm === (h24 < 12 ? 'AM' : 'PM'));
            });
        }

        function render() {
            if (wantsDate) {
                var showingMonths = view === 'months';
                grid.hidden = showingMonths;
                monthsBox.hidden = !showingMonths;
                panel.querySelector('.ucdp-dow').hidden = showingMonths;
                showingMonths ? renderMonths() : renderDays();
            }
            renderTime();
        }

        /* ---------- committing ---------- */
        function commit(silent) {
            if (!selected) return;
            input.value = formatValue(type, selected);
            if (!silent) {
                input.dispatchEvent(new Event('input', { bubbles: true }));
                input.dispatchEvent(new Event('change', { bubbles: true }));
            }
        }

        function pick(date) {
            var base = selected || cursor;
            selected = new Date(
                date.getFullYear(), date.getMonth(), date.getDate(),
                wantsTime ? base.getHours() : 0,
                wantsTime ? base.getMinutes() : 0
            );
            cursor = new Date(selected);
            render();
            commit();
            // A date-only field has nothing left to choose, so close on pick.
            // A datetime one stays open for the time half.
            if (!wantsTime) close(true);
        }

        /* ---------- interaction ---------- */
        panel.addEventListener('click', function (e) {
            var nav = e.target.closest('[data-step]');
            if (nav) {
                var step = +nav.dataset.step;
                cursor = view === 'months'
                    ? new Date(cursor.getFullYear() + step, cursor.getMonth(), 1)
                    : new Date(cursor.getFullYear(), cursor.getMonth() + step, 1);
                render();
                return;
            }

            if (e.target.closest('[data-toggle-view]')) {
                view = view === 'days' ? 'months' : 'days';
                render();
                return;
            }

            var day = e.target.closest('.ucdp-day');
            if (day && !day.disabled) {
                var parts = day.dataset.iso.split('-');
                pick(new Date(+parts[0], +parts[1] - 1, +parts[2]));
                return;
            }

            var ampm = e.target.closest('[data-ampm]');
            if (ampm) {
                var d = selected || new Date(cursor);
                var h = d.getHours() % 12;
                d.setHours(ampm.dataset.ampm === 'PM' ? h + 12 : h);
                selected = d;
                render();
                commit();
                return;
            }

            if (e.target.closest('[data-today]')) { pick(new Date()); return; }

            if (e.target.closest('[data-clear]')) {
                selected = null;
                input.value = '';
                input.dispatchEvent(new Event('input', { bubbles: true }));
                input.dispatchEvent(new Event('change', { bubbles: true }));
                close(true);
                return;
            }

            if (e.target.closest('[data-done]')) { close(true); }
        });

        function readTime() {
            var h = parseInt(hourEl.value, 10);
            var m = parseInt(minuteEl.value, 10);
            if (isNaN(h)) h = 12;
            if (isNaN(m)) m = 0;
            h = Math.min(12, Math.max(1, h));
            m = Math.min(59, Math.max(0, m));

            var isPm = panel.querySelector('[data-ampm="PM"]').classList.contains('is-active');
            var h24 = (h % 12) + (isPm ? 12 : 0);

            var base = selected || new Date(cursor);
            selected = new Date(base.getFullYear(), base.getMonth(), base.getDate(), h24, m);
            commit();
        }

        if (wantsTime) {
            [hourEl, minuteEl].forEach(function (el) {
                el.addEventListener('input', function () {
                    el.value = el.value.replace(/\D/g, '');
                });
                el.addEventListener('change', readTime);
                el.addEventListener('blur', function () { readTime(); renderTime(); });
                el.addEventListener('keydown', function (e) {
                    if (e.key === 'ArrowUp' || e.key === 'ArrowDown') {
                        e.preventDefault();
                        var step = e.key === 'ArrowUp' ? 1 : -1;
                        var value = (parseInt(el.value, 10) || 0) + step;
                        var isHour = el === hourEl;
                        var lo = isHour ? 1 : 0, hi = isHour ? 12 : 59;
                        if (value > hi) value = lo;
                        if (value < lo) value = hi;
                        el.value = pad(value);
                        readTime();
                    }
                });
            });
        }

        // Arrow-key navigation over the day grid.
        panel.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') { e.preventDefault(); close(true); return; }
            if (view !== 'days' || !wantsDate) return;

            var deltas = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 };
            if (!(e.key in deltas)) return;
            if (e.target.closest('.ucdp-time')) return;   // let the time fields have their arrows

            e.preventDefault();
            var base = selected || new Date(cursor);
            var next = new Date(base.getFullYear(), base.getMonth(), base.getDate() + deltas[e.key]);
            if (outOfRange(next)) return;
            selected = wantsTime
                ? new Date(next.getFullYear(), next.getMonth(), next.getDate(), base.getHours(), base.getMinutes())
                : next;
            cursor = new Date(selected);
            render();
            commit();
            var active = panel.querySelector('.ucdp-day.is-selected');
            if (active) active.focus();
        });

        /* ---------- positioning ---------- */
        // Fixed + portalled to <body>, for the same reason uc-select is: an
        // absolutely-positioned panel gets clipped by any ancestor with
        // overflow and trapped behind siblings by any ancestor with a transform.
        function place(anchor) {
            var r = anchor.getBoundingClientRect();
            var w = panel.offsetWidth;
            var h = panel.offsetHeight;

            var left = Math.min(r.left, window.innerWidth - w - 8);
            panel.style.left = Math.max(8, left) + 'px';

            var below = window.innerHeight - r.bottom;
            var dropUp = below < h + 12 && r.top > below;
            panel.classList.toggle('ucdp-up', dropUp);
            panel.style.top = (dropUp ? Math.max(8, r.top - h - 6) : r.bottom + 6) + 'px';
        }

        var anchorEl = null;
        function reposition() {
            if (!anchorEl) return;
            var r = anchorEl.getBoundingClientRect();
            if (r.bottom < 0 || r.top > window.innerHeight) { close(false); return; }
            place(anchorEl);
        }

        function onDocClick(e) {
            // composedPath() is captured when the event is dispatched, so it
            // still names the panel even though the clicked cell has since been
            // detached — choosing a day re-renders the grid, which would
            // otherwise make this look like a click from outside and slam the
            // panel shut before the time could be set.
            var path = typeof e.composedPath === 'function' ? e.composedPath() : null;
            if (path && path.indexOf(panel) !== -1) return;
            if (!path && panel.contains(e.target)) return;
            if (anchorEl && anchorEl.contains(e.target)) return;
            close(false);
        }

        function open(anchor) {
            if (openPicker && openPicker !== api) openPicker.close(false);
            openPicker = api;

            selected = parseValue(type, input.value);
            cursor = selected ? new Date(selected) : new Date();
            view = 'days';

            document.body.appendChild(panel);
            render();

            anchorEl = anchor;
            place(anchor);

            window.addEventListener('scroll', reposition, true);
            window.addEventListener('resize', reposition);
            // Deferred so the click that opened it doesn't immediately close it.
            setTimeout(function () { document.addEventListener('click', onDocClick); }, 0);

            var focusTarget = panel.querySelector('.ucdp-day.is-selected')
                || panel.querySelector('.ucdp-day:not(.is-outside):not(.is-disabled)');
            if (focusTarget) focusTarget.focus();
        }

        function close(focusInput) {
            window.removeEventListener('scroll', reposition, true);
            window.removeEventListener('resize', reposition);
            document.removeEventListener('click', onDocClick);
            if (panel.parentNode) panel.parentNode.removeChild(panel);
            anchorEl = null;
            if (openPicker === api) openPicker = null;
            if (focusInput) input.focus();
        }

        var api = { open: open, close: close, isOpen: function () { return !!panel.parentNode; } };
        return api;
    }

    var SUPPORTED = ['date', 'datetime-local', 'time'];

    window.ucDatePicker = {
        supports: function (type) { return SUPPORTED.indexOf(type) !== -1; },

        toggle: function (input, anchor) {
            if (!input.__ucdp) input.__ucdp = build(input);
            if (input.__ucdp.isOpen()) input.__ucdp.close(true);
            else input.__ucdp.open(anchor || input);
        }
    };
})();
