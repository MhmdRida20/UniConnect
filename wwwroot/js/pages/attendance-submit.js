/* ==========================================================================
   Attendance Submit (student) — shared by ScanSubmit and ManualEntry pages.
   Captures GPS location and a persisted-per-browser "device fingerprint"
   before letting the student actually submit.

   Honest limitation: this fingerprint is a random ID stored in this
   browser's localStorage, not a hardware device ID — it identifies "this
   browser profile," not "this physical phone." Clearing site data or using
   a different browser produces a new one. A native app (MAUI) has real
   device identifiers available that a website simply does not.
   ========================================================================== */
(function () {
    'use strict';

    const statusEl = document.getElementById('locationStatus');
    const latInput = document.getElementById('latInput');
    const lngInput = document.getElementById('lngInput');
    const fingerprintInput = document.getElementById('deviceFingerprintInput');
    const confirmBtn = document.getElementById('confirmBtn');

    if (!statusEl || !latInput) return; // this page has no location step (e.g. an error-only view)

    function setStatus(text, state) {
        statusEl.innerHTML = '<i class="bi bi-geo-alt"></i> ' + text;
        statusEl.className = 'attendance-location-status' + (state ? ' is-' + state : '');
    }

    /* ---------- Persistent per-browser device fingerprint ---------- */
    function getOrCreateDeviceFingerprint() {
        const key = 'uc_device_fingerprint';
        let id = localStorage.getItem(key);
        if (!id) {
            id = 'dev-' + crypto.randomUUID();
            localStorage.setItem(key, id);
        }
        return id;
    }
    fingerprintInput.value = getOrCreateDeviceFingerprint();

    /* ---------- Geolocation ---------- */
    if (!navigator.geolocation) {
        setStatus("Your browser doesn't support location — attendance can't be submitted here.", 'error');
        return;
    }

    navigator.geolocation.getCurrentPosition(
        (pos) => {
            latInput.value = pos.coords.latitude;
            lngInput.value = pos.coords.longitude;
            setStatus('Location found — ready to submit.', 'ready');
            if (confirmBtn) confirmBtn.disabled = false;
        },
        (err) => {
            console.warn('Geolocation error:', err);
            setStatus('Location access is required — please allow it and reload this page.', 'error');
        },
        { enableHighAccuracy: true, timeout: 15000 }
    );
})();
