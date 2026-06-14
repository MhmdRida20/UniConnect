/* ==========================================================================
   Rides index — live client-side search over rendered ride cards.
   ========================================================================== */
(function () {
    'use strict';

    const search = document.getElementById('rideSearch');
    const cols = Array.from(document.querySelectorAll('.ride-col'));
    const empty = document.getElementById('rideEmpty');
    const results = document.getElementById('rideResults');
    const grid = document.getElementById('rideGrid');
    if (!search || !cols.length) return;

    const total = cols.length;

    function apply() {
        const q = search.value.toLowerCase().trim();
        let shown = 0;
        cols.forEach((col) => {
            const match = !q || (col.dataset.search || '').includes(q);
            col.classList.toggle('hidden', !match);
            if (match) shown++;
        });
        if (results) {
            results.innerHTML = 'Showing <strong>' + shown + '</strong> of ' + total +
                ' ride' + (total === 1 ? '' : 's');
        }
        if (empty) empty.classList.toggle('hidden', shown > 0);
        if (grid) grid.classList.toggle('hidden', shown === 0);
    }

    search.addEventListener('input', apply);
    search.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') { search.value = ''; apply(); }
    });
})();
