/* ==========================================================================
   Attendance Session Details (instructor) — renders the QR code, lets the
   instructor project/download/share it, and joins the session's SignalR
   group so the roster updates live as students check in.
   ========================================================================== */
(function () {
    'use strict';

    const config = document.getElementById('attendanceConfig');
    if (!config) return;

    const sessionId = parseInt(config.dataset.sessionId, 10);
    const scanUrl = config.dataset.scanUrl;
    const courseName = config.dataset.courseName || '';
    const courseCode = config.dataset.courseCode || '';
    const token = config.dataset.token || '';
    const sessionDate = config.dataset.sessionDate || '';

    /* ---------- Render the QR code (client-side, no server package needed) ---------- */
    const qrHolder = document.getElementById('qrCanvas');
    const hasLibrary = typeof QRCode !== 'undefined';

    if (qrHolder && scanUrl) {
        if (!hasLibrary) {
            qrHolder.innerHTML = '<p class="text-danger small mb-0">QR code library failed to load — use the token below instead.</p>';
            console.error('QRCode library (unpkg.com/qrcode) did not load.');
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

    /* ======================================================================
       Project / download / share
       ====================================================================== */
    const actions = document.getElementById('qrActions');
    const stage = document.getElementById('qrStage');

    // Every one of these is client-side only, so they stay hidden unless the
    // QR library actually loaded — offering a download that can't be produced
    // is worse than not offering it.
    if (actions && hasLibrary && scanUrl) actions.hidden = false;

    function renderTo(el, size) {
        return new Promise((resolve, reject) => {
            const canvas = document.createElement('canvas');
            QRCode.toCanvas(canvas, scanUrl, { width: size, margin: 1 }, (err) => {
                if (err) return reject(err);
                el.replaceChildren(canvas);
                resolve(canvas);
            });
        });
    }

    /* ---------- Shareable PNG ----------
       Composed rather than raw so the image still makes sense once it has left
       this page: a bare QR in an email tells nobody which class it's for. */
    function fitText(ctx, text, maxWidth) {
        if (ctx.measureText(text).width <= maxWidth) return text;
        let cut = text;
        while (cut.length > 1 && ctx.measureText(cut + '…').width > maxWidth) {
            cut = cut.slice(0, -1);
        }
        return cut + '…';
    }

    function buildPng() {
        return new Promise((resolve, reject) => {
            const qr = document.createElement('canvas');
            QRCode.toCanvas(qr, scanUrl, { width: 900, margin: 1 }, (err) => {
                if (err) return reject(err);

                const pad = 64;
                const footer = token ? 190 : 120;
                const out = document.createElement('canvas');
                out.width = qr.width + pad * 2;
                out.height = qr.height + pad + footer;

                const ctx = out.getContext('2d');
                ctx.fillStyle = '#ffffff';
                ctx.fillRect(0, 0, out.width, out.height);
                ctx.drawImage(qr, pad, pad);

                const centre = out.width / 2;
                const inner = out.width - pad * 2;
                let y = qr.height + pad + 62;
                ctx.textAlign = 'center';

                ctx.fillStyle = '#0f172a';
                ctx.font = '600 46px system-ui, -apple-system, "Segoe UI", sans-serif';
                ctx.fillText(fitText(ctx, courseName, inner), centre, y);

                y += 46;
                ctx.fillStyle = '#64748b';
                ctx.font = '400 32px system-ui, -apple-system, "Segoe UI", sans-serif';
                ctx.fillText(fitText(ctx, courseCode, inner), centre, y);

                if (token) {
                    y += 60;
                    ctx.fillStyle = '#334155';
                    ctx.font = '500 30px ui-monospace, SFMono-Regular, Menlo, monospace';
                    ctx.fillText(fitText(ctx, 'Token: ' + token, inner), centre, y);
                }

                out.toBlob((blob) => {
                    blob ? resolve(blob) : reject(new Error('Canvas produced no blob.'));
                }, 'image/png');
            });
        });
    }

    function fileName() {
        const slug = (courseCode || courseName || 'session')
            .replace(/[^a-z0-9]+/gi, '-')
            .replace(/^-+|-+$/g, '')
            .toLowerCase();
        return `attendance-qr-${slug}${sessionDate ? '-' + sessionDate : ''}.png`;
    }

    const downloadBtn = document.getElementById('qrDownloadBtn');
    if (downloadBtn) {
        downloadBtn.addEventListener('click', () => {
            buildPng().then((blob) => {
                const url = URL.createObjectURL(blob);
                const a = document.createElement('a');
                a.href = url;
                a.download = fileName();
                document.body.appendChild(a);
                a.click();
                a.remove();
                // Revoking immediately can cancel the download in some browsers.
                setTimeout(() => URL.revokeObjectURL(url), 30000);
            }).catch((err) => {
                console.error('QR download failed:', err);
                alert("Couldn't generate the image — you can still share the token below.");
            });
        });
    }

    /* ---------- Native share (mobile mostly; feature-detected) ---------- */
    const shareBtn = document.getElementById('qrShareBtn');
    if (shareBtn && hasLibrary && scanUrl && navigator.canShare) {
        // Probe with a throwaway file: canShare(files) is the only reliable
        // signal, and it is false on most desktop browsers.
        const probe = new File([new Blob()], 'probe.png', { type: 'image/png' });
        if (navigator.canShare({ files: [probe] })) {
            shareBtn.hidden = false;
            shareBtn.addEventListener('click', () => {
                buildPng().then((blob) => {
                    const file = new File([blob], fileName(), { type: 'image/png' });
                    return navigator.share({
                        files: [file],
                        title: `Attendance — ${courseName}`,
                        text: `Scan to check in to ${courseName}${token ? ` (token: ${token})` : ''}.`
                    });
                }).catch((err) => {
                    if (err && err.name === 'AbortError') return;   // user dismissed the sheet
                    console.error('QR share failed:', err);
                });
            });
        }
    }

    /* ---------- Full-screen projection ---------- */
    let presenting = false;
    let pendingReload = false;

    const stageHolder = document.getElementById('qrStageCanvas');
    const fullscreenBtn = document.getElementById('qrFullscreenBtn');
    const stageClose = document.getElementById('qrStageClose');

    function stageSize() {
        // Leave room for the course name above and the token block below.
        return Math.max(220, Math.round(Math.min(window.innerWidth * 0.8, window.innerHeight * 0.55)));
    }

    function openStage() {
        if (!stage || !stageHolder) return;
        renderTo(stageHolder, stageSize()).then(() => {
            stage.hidden = false;
            presenting = true;
            document.body.classList.add('qr-presenting');
            // Native fullscreen is best-effort: if the browser refuses (or the
            // API is missing) the overlay alone still fills the viewport.
            if (stage.requestFullscreen) {
                stage.requestFullscreen().catch(() => { });
            }
            if (stageClose) stageClose.focus();
        }).catch((err) => {
            console.error('Full-screen QR render failed:', err);
        });
    }

    function closeStage() {
        if (!stage) return;
        stage.hidden = true;
        presenting = false;
        document.body.classList.remove('qr-presenting');
        if (document.fullscreenElement) document.exitFullscreen().catch(() => { });
        if (fullscreenBtn) fullscreenBtn.focus();

        // Roster updates that arrived while projecting were held back so the
        // reload wouldn't drop us out of fullscreen — apply them now.
        if (pendingReload) window.location.reload();
    }

    if (fullscreenBtn) fullscreenBtn.addEventListener('click', openStage);
    if (stageClose) stageClose.addEventListener('click', closeStage);

    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && presenting) closeStage();
    });

    // Leaving fullscreen via F11/browser chrome has to tear the overlay down too.
    document.addEventListener('fullscreenchange', () => {
        if (!document.fullscreenElement && presenting) closeStage();
    });

    let resizeTimer;
    window.addEventListener('resize', () => {
        if (!presenting || !stageHolder) return;
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(() => renderTo(stageHolder, stageSize()).catch(() => { }), 150);
    });

    /* ---------- Live roster updates ---------- */
    if (typeof signalR === 'undefined') return;

    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/attendanceHub')
        .withAutomaticReconnect()
        .build();

    // Simplest robust approach, consistent with every other live feature in
    // the app: reload so the roster/counts/QR-active state all stay correct,
    // rather than trying to patch the DOM piecemeal for every possible change.
    // The one exception is while projecting — a reload exits fullscreen, so
    // it's deferred until the instructor closes the stage.
    function refresh() {
        if (presenting) { pendingReload = true; return; }
        window.location.reload();
    }

    connection.on('RosterUpdated', refresh);
    connection.on('SessionClosed', refresh);

    connection.start()
        .then(() => connection.invoke('JoinSession', sessionId))
        .catch((err) => console.error('Attendance session connection failed:', err));

    connection.onreconnected(() => connection.invoke('JoinSession', sessionId));

    window.addEventListener('beforeunload', () => {
        connection.invoke('LeaveSession', sessionId).catch(() => { });
    });
})();
