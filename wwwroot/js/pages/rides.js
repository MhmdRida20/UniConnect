/* ==========================================================================
   Rides index — live client-side search over rendered ride cards,
   plus a List/Map view toggle with a Leaflet overview map of all rides.
   ========================================================================== */
(function () {
    'use strict';

    const search = document.getElementById('rideSearch');
    const cols = Array.from(document.querySelectorAll('.ride-col'));
    const empty = document.getElementById('rideEmpty');
    const results = document.getElementById('rideResults');
    const grid = document.getElementById('rideGrid');

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
                ' ride' + (total === 1 ? '' : 's');
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
       List / Map toggle
       --------------------------------------------------------------------- */
    const listBtn = document.getElementById('rideViewListBtn');
    const mapBtn = document.getElementById('rideViewMapBtn');
    const listView = document.getElementById('rideListView');
    const mapView = document.getElementById('rideMapView');
    let map = null; // Leaflet map instance, created lazily on first "Map" click

    function showList() {
        listBtn?.classList.add('is-active');
        mapBtn?.classList.remove('is-active');
        listView?.classList.remove('hidden');
        mapView?.classList.add('hidden');
    }

    function showMap() {
        listBtn?.classList.remove('is-active');
        mapBtn?.classList.add('is-active');
        listView?.classList.add('hidden');
        mapView?.classList.remove('hidden');

        if (!map) {
            initMap();
        } else {
            // Leaflet needs a nudge after being un-hidden, or tiles render blank.
            setTimeout(() => map.invalidateSize(), 50);
        }
    }

    listBtn?.addEventListener('click', showList);
    mapBtn?.addEventListener('click', showMap);

    function initMap() {
        const mapEl = document.getElementById('rideMap');
        const dataEl = document.getElementById('rideMapData');
        if (!mapEl || typeof L === 'undefined') return;

        let points = [];
        try { points = JSON.parse(dataEl.textContent || '[]'); } catch (e) { points = []; }

        map = L.map('rideMap', { scrollWheelZoom: false });
        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            attribution: '© OpenStreetMap contributors',
            maxZoom: 19
        }).addTo(map);

        map.on('focus', () => map.scrollWheelZoom.enable());
        map.on('blur', () => map.scrollWheelZoom.disable());

        if (points.length > 0) {
            const markers = points.map((p) => {
                const marker = L.marker([p.lat, p.lng]).addTo(map);
                const popupHtml =
                    '<div class="ride-map-popup">' +
                    '<strong>' + escapeHtml(p.from) + ' → ' + escapeHtml(p.to) + '</strong>' +
                    '<div class="rmp-row"><i class="bi bi-person-badge"></i> ' + escapeHtml(p.driver) + '</div>' +
                    '<div class="rmp-row"><i class="bi bi-calendar-event"></i> ' + escapeHtml(p.time) + '</div>' +
                    '<div class="rmp-row"><i class="bi bi-people-fill"></i> ' + p.seats + ' / ' + p.totalSeats + ' seats · ' + escapeHtml(p.vehicle) + '</div>' +
                    '<a href="' + p.url + '" class="btn btn-primary btn-sm w-100 mt-2">View ride</a>' +
                    '</div>';
                marker.bindPopup(popupHtml);
                return marker;
            });

            const group = L.featureGroup(markers);
            map.fitBounds(group.getBounds().pad(0.25));
        } else {
            // No geocoded rides at all — fall back to a default view (Beirut).
            map.setView([33.8938, 35.5018], 12);
        }
    }

    function escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str ?? '';
        return div.innerHTML;
    }

    /* ---------------------------------------------------------------------
       Live list updates (Phase 4 fix) — join the "rides-lobby" SignalR group
       so new/changed rides refresh this page without a manual reload.
       If the user is actively typing a search, we don't yank the page out
       from under them — we show a small banner with a manual refresh button
       instead of forcing a reload.
       --------------------------------------------------------------------- */
    if (typeof signalR !== 'undefined') {
        const banner = document.getElementById('rideListUpdateBanner');
        const refreshBtn = document.getElementById('rideListRefreshBtn');

        const lobbyConnection = new signalR.HubConnectionBuilder()
            .withUrl('/rideTrackingHub')
            .withAutomaticReconnect()
            .build();

        lobbyConnection.on('RideListChanged', () => {
            const userIsSearching = document.activeElement === search && search.value.trim() !== '';
            if (userIsSearching) {
                banner?.classList.remove('hidden');
            } else {
                window.location.reload();
            }
        });

        lobbyConnection.start()
            .then(() => lobbyConnection.invoke('JoinRidesLobby'))
            .catch((err) => console.error('Rides lobby connection failed:', err));

        lobbyConnection.onreconnected(() => lobbyConnection.invoke('JoinRidesLobby'));

        refreshBtn?.addEventListener('click', () => window.location.reload());

        window.addEventListener('beforeunload', () => {
            lobbyConnection.invoke('LeaveRidesLobby').catch(() => { });
        });
    }
})();
