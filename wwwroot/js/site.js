/* ==========================================================================
   UniConnect — Global site behaviour
   Shared, dependency-free interactivity used across every page.
   Exposes window.UC with small helpers individual pages can reuse.
   ========================================================================== */
(function () {
    'use strict';

    /* ----------------------------------------------------------------------
       Toast system — turns server-rendered .alert blocks into auto-dismiss
       toasts, and lets pages raise their own via UC.toast(message, type).
       ---------------------------------------------------------------------- */
    function ensureStack() {
        let stack = document.querySelector('.uc-toast-stack');
        if (!stack) {
            stack = document.createElement('div');
            stack.className = 'uc-toast-stack';
            stack.setAttribute('aria-live', 'polite');
            stack.setAttribute('aria-atomic', 'true');
            document.body.appendChild(stack);
        }
        return stack;
    }

    function dismiss(el) {
        if (!el || el.classList.contains('is-leaving')) return;
        el.classList.add('is-leaving');
        el.addEventListener('animationend', () => el.remove(), { once: true });
        setTimeout(() => el.remove(), 400);
    }

    function toast(message, type, timeout) {
        const stack = ensureStack();
        const el = document.createElement('div');
        el.className = 'alert alert-' + (type || 'success') + ' uc-toast';
        el.setAttribute('role', 'status');
        el.innerHTML = '<div class="flex-grow-1"></div>' +
            '<button type="button" class="btn-close ms-2" aria-label="Close"></button>';
        el.querySelector('.flex-grow-1').textContent = message;
        el.querySelector('.btn-close').addEventListener('click', () => dismiss(el));
        stack.appendChild(el);
        if (timeout !== 0) setTimeout(() => dismiss(el), timeout || 5000);
        return el;
    }

    /* Promote any server-rendered flash alerts (.uc-flash) into floating
       toasts so they don't push page content down. */
    function hoistFlashAlerts() {
        document.querySelectorAll('.alert.uc-flash').forEach((alert) => {
            const type = (Array.from(alert.classList).find(c => c.startsWith('alert-')) || 'alert-info')
                .replace('alert-', '');
            toast(alert.textContent.trim(), type, 6000);
            alert.remove();
        });
    }

    /* ----------------------------------------------------------------------
       Navbar — add shadow once the page is scrolled.
       ---------------------------------------------------------------------- */
    function initNavbarScroll() {
        const nav = document.querySelector('.uc-navbar');
        if (!nav) return;
        const onScroll = () => nav.classList.toggle('is-scrolled', window.scrollY > 8);
        onScroll();
        window.addEventListener('scroll', onScroll, { passive: true });
    }

    /* ----------------------------------------------------------------------
       Scroll-to-top button.
       ---------------------------------------------------------------------- */
    function initScrollTop() {
        const btn = document.createElement('button');
        btn.className = 'uc-totop';
        btn.type = 'button';
        btn.setAttribute('aria-label', 'Back to top');
        btn.innerHTML = '<i class="bi bi-arrow-up"></i>';
        document.body.appendChild(btn);

        const onScroll = () => btn.classList.toggle('is-visible', window.scrollY > 400);
        window.addEventListener('scroll', onScroll, { passive: true });
        btn.addEventListener('click', () => window.scrollTo({ top: 0, behavior: 'smooth' }));
    }

    /* ----------------------------------------------------------------------
       Reveal-on-scroll for any element marked .uc-reveal.
       ---------------------------------------------------------------------- */
    function initReveal() {
        const items = document.querySelectorAll('.uc-reveal');
        if (!items.length) return;
        if (!('IntersectionObserver' in window)) {
            items.forEach(i => i.classList.add('is-visible'));
            return;
        }
        const io = new IntersectionObserver((entries) => {
            entries.forEach((e) => {
                if (e.isIntersecting) {
                    e.target.classList.add('is-visible');
                    io.unobserve(e.target);
                }
            });
        }, { threshold: 0.12 });
        items.forEach(i => io.observe(i));
    }

    /* ----------------------------------------------------------------------
       Confirm dialogs — any element with [data-confirm] asks before its
       action proceeds (works for links and form submits).
       ---------------------------------------------------------------------- */
    function initConfirm() {
        document.addEventListener('submit', (e) => {
            const form = e.target.closest('form[data-confirm]');
            if (form && !window.confirm(form.getAttribute('data-confirm'))) {
                e.preventDefault();
            }
        });
        document.addEventListener('click', (e) => {
            const el = e.target.closest('a[data-confirm], button[data-confirm]');
            if (el && !el.closest('form[data-confirm]') &&
                !window.confirm(el.getAttribute('data-confirm'))) {
                e.preventDefault();
            }
        });
    }

    /* ----------------------------------------------------------------------
       Button loading state — forms with [data-loading] disable + spin their
       submit button on submit to prevent double-posting.
       ---------------------------------------------------------------------- */
    function initLoadingButtons() {
        document.addEventListener('submit', (e) => {
            const form = e.target.closest('form[data-loading]');
            if (!form || e.defaultPrevented) return;
            const btn = form.querySelector('[type="submit"]');
            if (!btn || btn.disabled) return;
            btn.dataset.originalHtml = btn.innerHTML;
            btn.disabled = true;
            btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2" role="status"></span>' +
                (btn.dataset.loadingText || 'Working…');
        });
    }

    /* ----------------------------------------------------------------------
       Expose helpers + boot.
       ---------------------------------------------------------------------- */
    window.UC = {
        toast: toast,
        dismissToast: dismiss,
        /* Escape text for safe insertion into innerHTML templates. */
        escape: function (s) {
            const d = document.createElement('div');
            d.textContent = s == null ? '' : String(s);
            return d.innerHTML;
        }
    };

    document.addEventListener('DOMContentLoaded', function () {
        initNavbarScroll();
        initScrollTop();
        initReveal();
        initConfirm();
        initLoadingButtons();
        hoistFlashAlerts();
    });
})();
