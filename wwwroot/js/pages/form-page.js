/* ==========================================================================
   Shared behaviour for create/edit form pages.
   - Live character counter for [maxlength] textareas paired with #descCount
   - Keeps Min/Max member inputs internally consistent
   ========================================================================== */
(function () {
    'use strict';

    /* Character counter */
    const textarea = document.querySelector('textarea[maxlength]');
    const counter = document.getElementById('descCount');
    if (textarea && counter) {
        const max = textarea.getAttribute('maxlength');
        const update = () => {
            const len = textarea.value.length;
            counter.textContent = len + ' / ' + max;
            counter.style.color = len > max * 0.9 ? 'var(--uc-warning)' : '';
        };
        textarea.addEventListener('input', update);
        update();
    }

    /* Min/Max member coupling */
    const min = document.querySelector('input[name="MinMembers"]');
    const max = document.querySelector('input[name="MaxMembers"]');
    if (min && max) {
        const sync = (changed) => {
            const minV = parseInt(min.value, 10);
            const maxV = parseInt(max.value, 10);
            if (!isNaN(minV) && !isNaN(maxV) && minV > maxV) {
                if (changed === min) max.value = minV;
                else min.value = maxV;
            }
        };
        min.addEventListener('change', () => sync(min));
        max.addEventListener('change', () => sync(max));
    }
})();
