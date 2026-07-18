/* ==========================================================================
   Clubs Index — joins the "clubs-lobby" SignalR group so new clubs, status
   changes, or inactivity flips refresh the page without a manual reload.
   ========================================================================== */
(function () {
    'use strict';

    if (typeof signalR === 'undefined') return;

    const banner = document.getElementById('clubListUpdateBanner');
    const refreshBtn = document.getElementById('clubListRefreshBtn');

    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/clubHub')
        .withAutomaticReconnect()
        .build();

    connection.on('ClubListChanged', () => {
        // No search box on this page to worry about interrupting — just
        // show the banner so a click confirms the refresh, consistent with
        // the pattern used elsewhere.
        banner?.classList.remove('hidden');
    });

    connection.start()
        .then(() => connection.invoke('JoinClubsLobby'))
        .catch((err) => console.error('Clubs lobby connection failed:', err));

    connection.onreconnected(() => connection.invoke('JoinClubsLobby'));

    refreshBtn?.addEventListener('click', () => window.location.reload());

    window.addEventListener('beforeunload', () => {
        connection.invoke('LeaveClubsLobby').catch(() => {});
    });
})();
