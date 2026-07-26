/* ==========================================================================
   Generic "form progress" engine — drop a <div data-form-progress> anywhere
   inside a form-page and it self-wires: finds every field the server marked
   [Required] (jQuery unobtrusive validation renders that as
   data-val-required on the input/select), tracks whether each one has a
   value, and reflects it live as a percentage bar + optional checklist.

   No per-page JS needed — the required-field list comes straight from each
   ViewModel's data annotations, so this works unmodified on any create/edit
   form that uses asp-for + [Required].
   ========================================================================== */
(function () {
    'use strict';

    document.querySelectorAll('[data-form-progress]').forEach(initProgress);

    function initProgress(root) {
        const form = root.closest('form') || document.querySelector('form');
        if (!form) { root.hidden = true; return; }

        const fill = root.querySelector('[data-progress-fill]');
        const pctEl = root.querySelector('[data-progress-pct]');
        const list = root.querySelector('[data-progress-checklist]');

        // Drop type="number" inputs: ASP.NET Core renders data-val-required
        // on those even without an explicit [Required] (non-nullable value
        // types are implicitly "required" for model binding), but they
        // always come pre-filled with a sane [Range] default here — tracking
        // them would make the bar start well above 0% before the user's
        // touched anything.
        const candidates = Array.from(form.querySelectorAll('[data-val-required]'))
            .filter((el) => el.name && el.type !== 'number');

        const labelFor = (el) => {
            if (el.id) {
                const explicit = form.querySelector('label[for="' + el.id + '"]');
                if (explicit) return explicit.textContent.trim();
            }
            // Fields whose id was overridden (so asp-for's label[for] doesn't
            // match) fall back to the nearest label *before* them in the
            // same wrapper — a plain "first label in the wrapper" would
            // mis-attribute every field that shares a wrapper div with an
            // earlier field (e.g. ApiBaseUrl + ApiKey both live in one
            // #apiUrlField block).
            const wrapper = el.closest('.mb-3, .col-sm-6, .col-sm-7, .col-sm-5, .col-md-6');
            if (wrapper) {
                const labels = Array.from(wrapper.querySelectorAll('label'));
                let nearest = null;
                for (const lbl of labels) {
                    if (lbl.compareDocumentPosition(el) & Node.DOCUMENT_POSITION_FOLLOWING) nearest = lbl;
                }
                if (nearest) return nearest.textContent.trim();
                if (labels.length) return labels[0].textContent.trim();
            }
            return el.name.replace(/([a-z])([A-Z])/g, '$1 $2');
        };

        // Group by resolved label, not by field name — a pair like
        // ClassroomLat/ClassroomLng shares one visible "Classroom Location"
        // label (set together by a single map click), so it should count,
        // and appear in the checklist, as ONE logical field, not two.
        const groups = [];
        const byLabel = new Map();
        candidates.forEach((el) => {
            const key = labelFor(el);
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
                li.innerHTML = '<svg class="hgi"><use href="#i-check-circle"></use></svg><span>' + group.label + '</span>';
                list.appendChild(li);
            });
        }

        const isFilled = (el) => {
            if (el.type === 'radio' || el.type === 'checkbox') {
                return form.querySelectorAll('input[name="' + cssEscape(el.name) + '"]:checked').length > 0;
            }
            if (el.tagName === 'SELECT') return el.value !== '';
            if (el.type === 'file') return !!(el.files && el.files.length);
            return el.value.trim() !== '';
        };

        function cssEscape(name) {
            return window.CSS && CSS.escape ? CSS.escape(name) : name.replace(/(["\\])/g, '\\$1');
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
            const percent = Math.round((done / groups.length) * 100);
            if (fill) fill.style.width = percent + '%';
            if (pctEl) pctEl.textContent = percent + '%';
            root.classList.toggle('is-complete', percent === 100);
        };

        form.addEventListener('input', update);
        form.addEventListener('change', update);
        update();
    }
})();
