/* ==========================================================================
   Career Profile — CV uploads the instant a file is chosen, instead of
   requiring a separate "Upload" click. This exists specifically because a
   file sitting in this form's input is invisible to (and never submitted
   by) any OTHER form on the page, like "Save Profile" — auto-submitting
   removes that entire class of "I picked a file but it never actually
   saved" confusion.
   ========================================================================== */
(function () {
    'use strict';

    const fileInput = document.getElementById('cvFileInput');
    const form = document.getElementById('cvUploadForm');
    if (!fileInput || !form) return;

    fileInput.addEventListener('change', () => {
        if (fileInput.files && fileInput.files.length > 0) {
            form.submit();
        }
    });
})();
