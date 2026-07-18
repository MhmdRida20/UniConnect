/* ==========================================================================
   Study Groups index — live client-side search over the rendered cards.
   ========================================================================== */
(function () {
    'use strict';

    const search = document.getElementById('sgSearch');
    const cols = Array.from(document.querySelectorAll('.sg-col'));
    const empty = document.getElementById('sgEmpty');
    const results = document.getElementById('sgResults');
    const grid = document.getElementById('sgGrid');

    const total = cols.length;

    function apply() {
        if (!search) return;
        const q = search.value.toLowerCase().trim();
        let shown = 0;
        cols.forEach((col) => {
            const match = !q || (col.dataset.search || '').includes(q);
            col.classList.toggle('hidden', !match);
            if (match) shown++;
        });
        if (results) {
            results.innerHTML = 'Showing <strong>' + shown + '</strong> of ' + total +
                ' group' + (total === 1 ? '' : 's');
        }
        if (empty) empty.classList.toggle('hidden', shown > 0);
        if (grid) grid.classList.toggle('hidden', shown === 0);
    }

    if (search && cols.length) {
        search.addEventListener('input', apply);
        search.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') { search.value = ''; apply(); }
        });
    }

    /* ---------------------------------------------------------------------
       Live list updates — join the "study-groups-lobby" SignalR group so new
       groups, capacity changes, or inactivity flips refresh this page without
       a manual reload. If the user is actively searching, show a banner
       instead of yanking the page out from under them.
       --------------------------------------------------------------------- */
    if (typeof signalR !== 'undefined') {
        const banner = document.getElementById('sgListUpdateBanner');
        const refreshBtn = document.getElementById('sgListRefreshBtn');

        const lobbyConnection = new signalR.HubConnectionBuilder()
            .withUrl('/studygroupHub')
            .withAutomaticReconnect()
            .build();

        lobbyConnection.on('StudyGroupListChanged', () => {
            const userIsSearching = document.activeElement === search && search.value.trim() !== '';
            if (userIsSearching) {
                banner?.classList.remove('hidden');
            } else {
                window.location.reload();
            }
        });

        lobbyConnection.start()
            .then(() => lobbyConnection.invoke('JoinStudyGroupsLobby'))
            .catch((err) => console.error('Study groups lobby connection failed:', err));

        lobbyConnection.onreconnected(() => lobbyConnection.invoke('JoinStudyGroupsLobby'));

        refreshBtn?.addEventListener('click', () => window.location.reload());

        window.addEventListener('beforeunload', () => {
            lobbyConnection.invoke('LeaveStudyGroupsLobby').catch(() => { });
        });
    }
})();
