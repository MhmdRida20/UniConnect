/* ==========================================================================
   Generic "form progress" engine.

   Drop the shared _FormProgress partial anywhere on a form page — typically
   in the sticky .form-rail beside the form — and it self-wires: it finds the
   form's required fields, tracks whether each one has a value, and reflects
   that live as a percentage bar, a "N left" line, and a checklist.

   The required-field list is read from data-val-required, which ASP.NET Core
   renders from each ViewModel's [Required] attributes. That means no field
   list is configured per page and the bar cannot drift out of sync when a
   ViewModel changes.
   ========================================================================== */
(function () {
    'use strict';

    document.querySelectorAll('[data-form-progress]').forEach(initProgress);

    function initProgress(root) {
        const form = resolveForm(root);
        if (!form) { root.hidden = true; return; }

        const fill = root.querySelector('[data-progress-fill]');
        const track = root.querySelector('[data-progress-track]');
        const pctEl = root.querySelector('[data-progress-pct]');
        const remainEl = root.querySelector('[data-progress-remaining]');
        const list = root.querySelector('[data-progress-checklist]');

        // "required" (default) answers "can I submit yet?".
        // "all" answers "how complete is this?" — for profile-style pages where
        // nothing is mandatory but filling more in produces a better result.
        const trackAll = root.getAttribute('data-progress-mode') === 'all';

        // Drop type="number" inputs: ASP.NET Core renders data-val-required on
        // those even without an explicit [Required] (non-nullable value types
        // are implicitly required for model binding), but they always come
        // pre-filled with a sane [Range] default — tracking them would make the
        // bar start well above 0% before the user has touched anything.
        const candidates = Array.from(
            form.querySelectorAll(trackAll ? 'input, select, textarea' : '[data-val-required]')
        ).filter((el) => {
            if (!el.name || el.disabled || el.type === 'number') return false;
            if (!trackAll) return true;
            return el.type !== 'hidden' && el.type !== 'submit' && el.type !== 'button'
                && el.type !== 'radio' && el.type !== 'checkbox'
                && el.name !== '__RequestVerificationToken';
        });

        // Group by resolved label, not by field name — a pair like
        // ClassroomLat/ClassroomLng shares one visible "Classroom Location"
        // label (both set by a single map click), so it should count, and
        // appear in the checklist, as ONE logical field rather than two.
        const groups = [];
        const byLabel = new Map();
        candidates.forEach((el) => {
            const key = labelFor(form, el);
            let group = byLabel.get(key);
            if (!group) {
                group = { label: key, els: [] };
                byLabel.set(key, group);
                groups.push(group);
            }
            group.els.push(el);
        });

        if (!groups.length) { root.hidden = true; return; }

        if (list) {
            groups.forEach((group, i) => {
                const li = document.createElement('li');
                li.dataset.groupIndex = String(i);
                li.innerHTML = '<svg class="hgi"><use href="#i-check-circle"></use></svg><span></span>';
                li.querySelector('span').textContent = group.label;
                list.appendChild(li);
            });
        }

        const update = () => {
            let done = 0;
            groups.forEach((group, i) => {
                const groupFilled = group.els.every(isFilled);
                if (groupFilled) done++;
                if (list) {
                    const li = list.querySelector('li[data-group-index="' + i + '"]');
                    if (li) li.classList.toggle('is-done', groupFilled);
                }
            });

            const total = groups.length;
            const percent = Math.round((done / total) * 100);
            const left = total - done;

            if (fill) fill.style.width = percent + '%';
            if (pctEl) pctEl.textContent = percent + '%';
            if (track) track.setAttribute('aria-valuenow', String(percent));
            if (remainEl) {
                remainEl.textContent = left === 0
                    ? (root.getAttribute('data-progress-complete-text') || 'All set.')
                    : left + (trackAll ? ' field' : ' required field') + (left === 1 ? '' : 's') + ' left';
            }
            root.classList.toggle('is-complete', left === 0);
        };

        form.addEventListener('input', update);
        form.addEventListener('change', update);
        update();
    }

    /* ---------- helpers ---------- */

    // The card usually sits in the sidebar, OUTSIDE the form it describes, so
    // closest('form') is only one of several fallbacks here.
    function resolveForm(root) {
        const selector = root.getAttribute('data-form-progress');
        if (selector) {
            const explicit = document.querySelector(selector);
            if (explicit) return explicit;
        }

        const enclosing = root.closest('form');
        if (enclosing) return enclosing;

        const layout = root.closest('.form-page-split, .form-page, .au-create-layout') || document;
        const inMain = layout.querySelector('.form-main form, .uc-form-card form');
        if (inMain) return inMain;

        // Last resort: the first form on the page that actually has required
        // fields — skips sign-out//filter stubs that carry no inputs.
        return Array.from(document.querySelectorAll('form'))
            .find((f) => f.querySelector('[data-val-required]')) || null;
    }

    function labelFor(form, el) {
        if (el.id) {
            const explicit = form.querySelector('label[for="' + cssEscape(el.id) + '"]');
            if (explicit) return clean(explicit.textContent);
        }
        // Fields whose id was overridden (so asp-for's label[for] no longer
        // matches) fall back to the nearest label BEFORE them in the same
        // wrapper. A plain "first label in the wrapper" would mis-attribute
        // every field sharing a wrapper with an earlier one (e.g. ApiBaseUrl
        // and ApiKey both live in one #apiUrlField block).
        const wrapper = el.closest('.mb-3, .mb-4, .col-sm-6, .col-sm-7, .col-sm-5, .col-md-6, .col-md-4, .col-lg-6');
        if (wrapper) {
            const labels = Array.from(wrapper.querySelectorAll('label'));
            let nearest = null;
            for (const lbl of labels) {
                if (lbl.compareDocumentPosition(el) & Node.DOCUMENT_POSITION_FOLLOWING) nearest = lbl;
            }
            if (nearest) return clean(nearest.textContent);
            if (labels.length) return clean(labels[0].textContent);
        }
        return el.name.replace(/.*\./, '').replace(/([a-z])([A-Z])/g, '$1 $2');
    }

    function isFilled(el) {
        if (el.type === 'radio' || el.type === 'checkbox') {
            return el.form.querySelectorAll('input[name="' + cssEscape(el.name) + '"]:checked').length > 0;
        }
        if (el.tagName === 'SELECT') return el.value !== '';
        if (el.type === 'file') return !!(el.files && el.files.length);
        return el.value.trim() !== '';
    }

    // Labels often carry "(optional)" / "max 2 MB" helper spans and stray
    // whitespace from Razor — collapse it so the checklist stays scannable.
    function clean(text) {
        return text.replace(/\s+/g, ' ').trim().replace(/[:*]$/, '').trim();
    }

    function cssEscape(value) {
        return window.CSS && CSS.escape ? CSS.escape(value) : value.replace(/(["\\])/g, '\\$1');
    }
})();
