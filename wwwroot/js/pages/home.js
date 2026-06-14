/* ==========================================================================
   Home page — animated stat counters that run once they scroll into view.
   ========================================================================== */
(function () {
    'use strict';

    const counters = Array.from(document.querySelectorAll('.home-stat-num[data-count]'));
    if (!counters.length) return;

    function animate(el) {
        const target = parseInt(el.dataset.count, 10) || 0;
        const duration = 1400;
        const start = performance.now();

        function tick(now) {
            const progress = Math.min((now - start) / duration, 1);
            // easeOutCubic for a lively-then-settling feel
            const eased = 1 - Math.pow(1 - progress, 3);
            const value = Math.floor(eased * target);
            el.textContent = value.toLocaleString() + (progress === 1 && target >= 100 ? '+' : '');
            if (progress < 1) requestAnimationFrame(tick);
        }
        requestAnimationFrame(tick);
    }

    if (!('IntersectionObserver' in window)) {
        counters.forEach(animate);
        return;
    }

    const io = new IntersectionObserver((entries) => {
        entries.forEach((entry) => {
            if (entry.isIntersecting) {
                animate(entry.target);
                io.unobserve(entry.target);
            }
        });
    }, { threshold: 0.4 });

    counters.forEach((c) => io.observe(c));
})();
