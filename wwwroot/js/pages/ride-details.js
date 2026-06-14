/* ==========================================================================
   Ride details — renders the Leaflet route map from server-provided points.
   The point list is injected as JSON in #rideMapData, so no inline JS values.
   ========================================================================== */
(function () {
    'use strict';

    const mapEl = document.getElementById('rideMap');
    const dataEl = document.getElementById('rideMapData');
    if (!mapEl || typeof L === 'undefined') return;

    let points = [];
    try { points = JSON.parse(dataEl.textContent || '[]'); } catch (e) { points = []; }

    const map = L.map('rideMap', { scrollWheelZoom: false });
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '© OpenStreetMap contributors',
        maxZoom: 19
    }).addTo(map);

    // Enable wheel zoom only after a deliberate click, so the page still scrolls.
    map.on('focus', () => map.scrollWheelZoom.enable());
    map.on('blur', () => map.scrollWheelZoom.disable());

    function markerIcon(color) {
        return L.icon({
            iconUrl: 'https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-' + color + '.png',
            shadowUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png',
            iconSize: [25, 41], iconAnchor: [12, 41], popupAnchor: [1, -34], shadowSize: [41, 41]
        });
    }

    if (points.length > 0) {
        const markers = points.map((p) =>
            L.marker([p.lat, p.lng], { icon: markerIcon(p.color) }).addTo(map).bindPopup(p.label));

        // Draw a dashed line between departure and destination when both exist.
        const start = points.find((p) => p.color === 'green');
        const end = points.find((p) => p.color === 'red');
        if (start && end) {
            L.polyline([[start.lat, start.lng], [end.lat, end.lng]], {
                color: '#16a34a', weight: 3, opacity: 0.6, dashArray: '8, 8'
            }).addTo(map);
        }

        const group = L.featureGroup(markers);
        map.fitBounds(group.getBounds().pad(0.3));
    } else {
        // Fall back to a default view (Beirut) with an explanatory popup.
        map.setView([33.8938, 35.5018], 12);
        L.popup()
            .setLatLng([33.8938, 35.5018])
            .setContent('Map location not available for this ride yet.')
            .openOn(map);
    }
})();
