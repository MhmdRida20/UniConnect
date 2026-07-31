/* ==========================================================================
   Instructor course dashboard — client-side sorting and filtering of the
   student table. Everything here is presentation only: the server already
   sent every row, sorted worst-first, so none of this needs a round trip
   and the page is fully usable without it.
   ========================================================================== */
(function () {
    'use strict';

    const table = document.getElementById('studentTable');
    if (!table) return;

    const tbody = table.tBodies[0];
    const headers = Array.from(table.tHead.rows[0].cells);

    /* ---------- Sorting ---------- */

    // Cells carry data-value so the sort key never has to be parsed back out
    // of formatted text ("12 / 15", "83.3%"). Unmeasurable rows use -1, which
    // keeps them together at one end instead of scattered through the list.
    function cellValue(row, index, type) {
        const cell = row.cells[index];
        if (!cell) return type === 'num' ? -1 : '';
        const raw = cell.dataset.value;
        if (type === 'num') {
            const n = parseFloat(raw !== undefined ? raw : cell.textContent);
            return isNaN(n) ? -1 : n;
        }
        return (raw !== undefined ? raw : cell.textContent).trim().toLowerCase();
    }

    function sortBy(index, type, ascending) {
        const rows = Array.from(tbody.rows);
        rows.sort(function (a, b) {
            const av = cellValue(a, index, type);
            const bv = cellValue(b, index, type);
            if (av < bv) return ascending ? -1 : 1;
            if (av > bv) return ascending ? 1 : -1;
            return 0;
        });
        rows.forEach(function (row) { tbody.appendChild(row); });
    }

    headers.forEach(function (th, index) {
        const type = th.dataset.sort;
        if (!type) return;

        th.setAttribute('tabindex', '0');
        th.setAttribute('role', 'button');

        function activate() {
            // Text columns read naturally ascending; numeric ones are almost
            // always more interesting largest-first, except attendance, where
            // the whole point is finding the lowest.
            const current = th.getAttribute('aria-sort');
            const ascending = current === 'ascending' ? false : current === 'descending' ? true : type === 'text';

            headers.forEach(function (other) { other.setAttribute('aria-sort', 'none'); });
            th.setAttribute('aria-sort', ascending ? 'ascending' : 'descending');

            sortBy(index, type, ascending);
        }

        th.addEventListener('click', activate);
        th.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); activate(); }
        });
    });

    /* ---------- Filtering ---------- */

    const filters = Array.from(document.querySelectorAll('.ic-filter'));

    filters.forEach(function (button) {
        button.addEventListener('click', function () {
            const want = button.dataset.filter;

            filters.forEach(function (other) { other.classList.remove('is-active'); });
            button.classList.add('is-active');

            Array.from(tbody.rows).forEach(function (row) {
                row.hidden = want !== 'all' && row.dataset.standing !== want;
            });
        });
    });
})();
