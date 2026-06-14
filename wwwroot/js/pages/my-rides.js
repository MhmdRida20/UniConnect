/* ==========================================================================
   My Rides — switch between the Driving and Requested panels.
   ========================================================================== */
(function () {
    'use strict';

    const tabs = Array.from(document.querySelectorAll('.mr-tab'));
    const panels = Array.from(document.querySelectorAll('.mr-panel'));
    if (!tabs.length) return;

    function activate(name) {
        tabs.forEach((t) => {
            const on = t.dataset.tab === name;
            t.classList.toggle('active', on);
            t.setAttribute('aria-selected', on ? 'true' : 'false');
        });
        panels.forEach((p) => p.classList.toggle('hidden', p.dataset.panel !== name));
    }

    tabs.forEach((t) => t.addEventListener('click', () => activate(t.dataset.tab)));
})();
