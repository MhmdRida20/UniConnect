/* ==========================================================================
   Attendance Session Details (instructor) — renders the QR code and joins
   the session's SignalR group so the roster updates live as students check
   in, without a manual refresh.
   ========================================================================== */
(function () {
    'use strict';

    const config = document.getElementById('attendanceConfig');
    if (!config) return;

    const sessionId = parseInt(config.dataset.sessionId, 10);
    const scanUrl = config.dataset.scanUrl;

    /* ---------- Render the QR code (client-side, no server package needed) ---------- */
    const qrHolder = document.getElementById('qrCanvas');
    if (qrHolder && scanUrl) {
        if (typeof QRCode === 'undefined') {
            qrHolder.innerHTML = '<p class="text-danger small mb-0">QR code library failed to load — use the token below instead.</p>';
            console.error('QRCode library (cdn.jsdelivr.net/npm/qrcode) did not load.');
        } else {
            const canvas = document.createElement('canvas');
            qrHolder.appendChild(canvas);
            QRCode.toCanvas(canvas, scanUrl, { width: 220, margin: 1 }, (err) => {
                if (err) {
                    console.error('QR render failed:', err);
                    qrHolder.innerHTML = '<p class="text-danger small mb-0">Couldn\'t generate the QR code — use the token below instead.</p>';
                }
            });
        }
    }

    /* ---------- Live roster updates ---------- */
    if (typeof signalR === 'undefined') return;

    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/attendanceHub')
        .withAutomaticReconnect()
        .build();

    // Simplest robust approach, consistent with every other live feature in
    // the app: reload so the roster/counts/QR-active state all stay correct,
    // rather than trying to patch the DOM piecemeal for every possible change.
    connection.on('RosterUpdated', () => window.location.reload());
    connection.on('SessionClosed', () => window.location.reload());

    connection.start()
        .then(() => connection.invoke('JoinSession', sessionId))
        .catch((err) => console.error('Attendance session connection failed:', err));

    connection.onreconnected(() => connection.invoke('JoinSession', sessionId));

    window.addEventListener('beforeunload', () => {
        connection.invoke('LeaveSession', sessionId).catch(() => { });
    });
})();
