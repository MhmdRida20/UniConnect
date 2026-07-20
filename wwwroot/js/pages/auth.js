/* ==========================================================================
   UniConnect — Auth page interactions
   - Password show/hide toggles (Hugeicons eye / eye-off)
   - Graceful login <-> signup transition fallback for browsers without
     the cross-document View Transitions API
   ========================================================================== */
(function () {
    'use strict';

    /* ---- Password reveal ------------------------------------------------- */
    document.querySelectorAll('[data-pass-toggle]').forEach(function (btn) {
        var targetId = btn.getAttribute('data-pass-toggle');
        var input = document.getElementById(targetId);
        if (!input) return;
        var useEl = btn.querySelector('use');
        btn.addEventListener('click', function () {
            var show = input.type === 'password';
            input.type = show ? 'text' : 'password';
            if (useEl) useEl.setAttribute('href', show ? '#i-eye-off' : '#i-eye');
            btn.setAttribute('aria-label', show ? 'Hide password' : 'Show password');
            btn.setAttribute('aria-pressed', show ? 'true' : 'false');
            input.focus();
        });
    });

    /* ---- Transition fallback (no View Transitions support) --------------- */
    var supportsVT = typeof document.startViewTransition === 'function';
    if (!supportsVT) {
        var card = document.querySelector('[data-auth-card]');
        document.querySelectorAll('[data-auth-nav]').forEach(function (link) {
            link.addEventListener('click', function (e) {
                if (e.metaKey || e.ctrlKey || e.shiftKey || e.button !== 0) return;
                e.preventDefault();
                var href = link.getAttribute('href');
                if (card) {
                    card.style.transition = 'opacity .28s ease, transform .28s ease';
                    card.style.opacity = '0';
                    card.style.transform = 'translateY(-10px) scale(.98)';
                }
                setTimeout(function () { window.location.href = href; }, 240);
            });
        });
    }

    /* ---- Testimonial carousel --------------------------------------------
       Exactly one .auth-quote carries .is-active at a time; CSS handles
       the crossfade transition. See home.js's matching rotator for why
       this uses a plain interval instead of staggered animation-delays. */
    (function () {
        var quotes = Array.prototype.slice.call(document.querySelectorAll('.auth-quote'));
        if (quotes.length < 2) return;
        if (window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;

        var i = 0;
        quotes[i].classList.add('is-active');

        setInterval(function () {
            quotes[i].classList.remove('is-active');
            i = (i + 1) % quotes.length;
            quotes[i].classList.add('is-active');
        }, 4200);
    })();

    /* ---- Submit affordance ---------------------------------------------- */
    document.querySelectorAll('.auth-form').forEach(function (form) {
        form.addEventListener('submit', function () {
            var btn = form.querySelector('.auth-submit');
            if (btn && !btn.disabled) {
                btn.dataset.label = btn.querySelector('span') ? btn.querySelector('span').textContent : '';
                btn.classList.add('is-loading');
            }
        });
    });
})();
