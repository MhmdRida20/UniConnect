/* ==========================================================================
   Internship details — copy the employer's apply-to email address.
   (Extracted from an inline <script> in Details.cshtml so the page follows
   the same one-JS-file-per-page convention as the rest of the app.)
   ========================================================================== */
(function () {
    'use strict';

    const btn = document.getElementById('copyEmailBtn');
    const text = document.getElementById('applyEmailText');
    if (!btn || !text) return;

    const label = btn.querySelector('span');
    const icon = btn.querySelector('use');
    const idleLabel = label ? label.textContent : 'Copy';

    btn.addEventListener('click', async () => {
        try {
            await navigator.clipboard.writeText(text.textContent.trim());
        } catch {
            // Clipboard API needs a secure context (https/localhost) and can be
            // blocked by permissions — fall back to a manual selection so the
            // address is still easy to copy by hand rather than failing silently.
            const range = document.createRange();
            range.selectNodeContents(text);
            const sel = window.getSelection();
            sel.removeAllRanges();
            sel.addRange(range);
            return;
        }

        btn.classList.add('is-copied');
        if (label) label.textContent = 'Copied';
        if (icon) icon.setAttribute('href', '#i-check-circle');

        setTimeout(() => {
            btn.classList.remove('is-copied');
            if (label) label.textContent = idleLabel;
            if (icon) icon.setAttribute('href', '#i-copy');
        }, 2000);
    });
})();
