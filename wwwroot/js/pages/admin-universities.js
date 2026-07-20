/* ==========================================================================
   Universities list — client-side search filter (no round trip needed for
   a platform-admin list that's realistically dozens of rows, not thousands).
   ========================================================================== */
(function () {
    'use strict';

    const searchInput = document.getElementById('auSearch');
    const rows = Array.from(document.querySelectorAll('.au-row'));
    const noResults = document.getElementById('auNoResults');
    if (!searchInput || !rows.length) return;

    searchInput.addEventListener('input', () => {
        const q = searchInput.value.trim().toLowerCase();
        let visible = 0;
        rows.forEach((row) => {
            const match = !q || row.dataset.auName.includes(q) || row.dataset.auCode.includes(q);
            row.hidden = !match;
            if (match) visible++;
        });
        if (noResults) noResults.hidden = visible > 0;
    });
})();

/* ==========================================================================
   Add University — "Generate" button that provisions a unique API key AND
   a fresh, independent simulated dataset for the university, via AJAX.
   ========================================================================== */
(function () {
    'use strict';

    const generateBtn = document.getElementById('generateApiKeyBtn');
    const apiKeyInput = document.getElementById('ApiKeyInput');
    const apiKeyHint = document.getElementById('apiKeyHint');
    const nameInput = document.querySelector('input[name="Name"]');

    if (!generateBtn || !apiKeyInput) return;

    generateBtn.addEventListener('click', async () => {
        const form = generateBtn.closest('form');
        const token = form.querySelector('input[name="__RequestVerificationToken"]').value;
        const universityName = nameInput ? nameInput.value.trim() : '';

        const idleHtml = generateBtn.innerHTML;
        generateBtn.disabled = true;
        generateBtn.innerHTML = '<svg class="hgi hgi-sm me-1 au-spin"><use href="#i-loading"></use></svg>Generating…';

        try {
            const fd = new FormData();
            fd.append('universityName', universityName);
            fd.append('__RequestVerificationToken', token);

            const resp = await fetch(generateBtn.dataset.url, { method: 'POST', body: fd });
            if (!resp.ok) throw new Error('Request failed');
            const data = await resp.json();

            apiKeyInput.value = data.apiKey;
            apiKeyInput.classList.add('au-field-flash');
            apiKeyInput.addEventListener('animationend', () => apiKeyInput.classList.remove('au-field-flash'), { once: true });
            apiKeyHint.innerHTML =
                `<svg class="hgi hgi-sm" style="color:var(--uc-primary)"><use href="#i-check-circle"></use></svg> Provisioned ${data.studentCount} students and ` +
                `${data.courseCount} courses for this key — independent from every other university's data.`;
        } catch (err) {
            console.error('Failed to generate API key:', err);
            apiKeyHint.innerHTML = '<svg class="hgi hgi-sm" style="color:var(--uc-danger)"><use href="#i-alert"></use></svg> Couldn\'t generate a key — please try again.';
        } finally {
            generateBtn.disabled = false;
            generateBtn.innerHTML = idleHtml;
        }
    });
})();
