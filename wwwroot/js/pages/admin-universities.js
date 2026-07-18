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

        generateBtn.disabled = true;
        generateBtn.innerHTML = '<i class="bi bi-hourglass-split me-1"></i>Generating…';

        try {
            const fd = new FormData();
            fd.append('universityName', universityName);
            fd.append('__RequestVerificationToken', token);

            const resp = await fetch(generateBtn.dataset.url, { method: 'POST', body: fd });
            if (!resp.ok) throw new Error('Request failed');
            const data = await resp.json();

            apiKeyInput.value = data.apiKey;
            apiKeyHint.innerHTML =
                `<i class="bi bi-check-circle text-success"></i> Provisioned ${data.studentCount} students and ` +
                `${data.courseCount} courses for this key — independent from every other university's data.`;
        } catch (err) {
            console.error('Failed to generate API key:', err);
            apiKeyHint.textContent = "Couldn't generate a key — please try again.";
        } finally {
            generateBtn.disabled = false;
            generateBtn.innerHTML = '<i class="bi bi-magic me-1"></i>Generate';
        }
    });
})();
