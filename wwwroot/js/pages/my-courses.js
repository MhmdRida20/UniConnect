/* ==========================================================================
   My Courses — live search + department chip filtering over rendered cards.
   ========================================================================== */
(function () {
    'use strict';

    const cards = Array.from(document.querySelectorAll('.course-col'));
    const search = document.getElementById('ucSearch');
    const chips = Array.from(document.querySelectorAll('.mc-chip'));
    const emptySearch = document.getElementById('emptySearch');
    const visibleNum = document.getElementById('visibleNum');
    const visibleCount = document.getElementById('visibleCount');
    const clearBtn = document.getElementById('clearFilters');
    const grid = document.getElementById('courseGrid');

    let activeDept = '';

    function applyFilter() {
        const q = search ? search.value.toLowerCase().trim() : '';
        let shown = 0;

        cards.forEach((card) => {
            const matchDept = !activeDept || card.dataset.dept === activeDept;
            const matchSearch = !q || card.dataset.search.includes(q);
            const visible = matchDept && matchSearch;
            card.style.display = visible ? '' : 'none';
            if (visible) shown++;
        });

        if (visibleNum) visibleNum.textContent = shown;
        if (visibleCount) visibleCount.textContent = shown;
        if (emptySearch) emptySearch.classList.toggle('hidden', shown > 0);
        if (grid) grid.classList.toggle('hidden', shown === 0);
    }

    chips.forEach((chip) => {
        chip.addEventListener('click', () => {
            activeDept = chip.dataset.dept;
            chips.forEach((c) => {
                const on = c === chip;
                c.classList.toggle('active', on);
                c.setAttribute('aria-pressed', on ? 'true' : 'false');
            });
            applyFilter();
        });
    });

    if (search) {
        search.addEventListener('input', applyFilter);
        search.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') { search.value = ''; applyFilter(); }
        });
    }

    if (clearBtn) {
        clearBtn.addEventListener('click', () => {
            if (search) search.value = '';
            activeDept = '';
            chips.forEach((c) => {
                const isAll = c.dataset.dept === '';
                c.classList.toggle('active', isAll);
                c.setAttribute('aria-pressed', isAll ? 'true' : 'false');
            });
            applyFilter();
        });
    }
})();
