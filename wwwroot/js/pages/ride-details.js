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

    // Exposed so ride-tracking.js (Phase 4) can add/move the live driver
    // marker on this same map without re-creating a second Leaflet instance.
    window.UcRideMap = map;
    window.UcRideMarkerIcon = markerIcon;

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

    /* -----------------------------------------------------------------------
       Pickup pin picker (Phase 2) — lets a passenger drop an exact pickup pin
       instead of only typing an address. Centers near the ride's departure
       point when known, since that's the most likely pickup area.
       ----------------------------------------------------------------------- */
    const pickupMapEl = document.getElementById('pickupPinMap');
    if (pickupMapEl) {
        const departurePoint = points.find((p) => p.color === 'green');
        const center = departurePoint ? [departurePoint.lat, departurePoint.lng] : [33.8938, 35.5018];

        const pickupMap = L.map('pickupPinMap', { scrollWheelZoom: false });
        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            attribution: '© OpenStreetMap contributors',
            maxZoom: 19
        }).addTo(pickupMap);
        pickupMap.setView(center, 14);

        pickupMap.on('focus', () => pickupMap.scrollWheelZoom.enable());
        pickupMap.on('blur', () => pickupMap.scrollWheelZoom.disable());

        const pickupLocationInput = document.getElementById('pickupLocationInput');
        const pickupLatInput = document.getElementById('pickupLatInput');
        const pickupLngInput = document.getElementById('pickupLngInput');
        const pickupStatus = document.getElementById('pickupGeoStatus');
        let pickupMarker = null;
        let programmaticPickupUpdate = false;

        const reverseCache = new Map();
        const forwardCache = new Map();

        function debounce(fn, delay) {
            let timer = null;
            return (...args) => {
                clearTimeout(timer);
                timer = setTimeout(() => fn(...args), delay);
            };
        }

        function setPickupStatus(text, kind) {
            if (!pickupStatus) return;
            pickupStatus.textContent = text;
            pickupStatus.className = 'geo-status' + (kind ? ' geo-status-' + kind : '');
        }

        async function reverseGeocodePickup(lat, lng) {
            const key = lat.toFixed(5) + ',' + lng.toFixed(5);
            if (reverseCache.has(key)) {
                applyPickupAddress(reverseCache.get(key));
                return;
            }
            try {
                const resp = await fetch(`/Rides/ReverseGeocode?lat=${lat}&lng=${lng}`);
                const data = resp.ok ? await resp.json() : { address: null };
                reverseCache.set(key, data.address || null);
                applyPickupAddress(data.address || null);
            } catch (e) {
                reverseCache.set(key, null);
            }
        }

        function applyPickupAddress(address) {
            if (address && pickupLocationInput) {
                programmaticPickupUpdate = true;
                pickupLocationInput.value = address;
                setTimeout(() => { programmaticPickupUpdate = false; }, 0);
            }
        }

        async function forwardGeocodePickupPreview() {
            const address = pickupLocationInput.value.trim();
            if (!address) { setPickupStatus('', ''); return; }

            const key = address.toLowerCase();
            setPickupStatus('Looking up address…', 'loading');

            let result;
            if (forwardCache.has(key)) {
                result = forwardCache.get(key);
            } else {
                try {
                    const resp = await fetch(`/Rides/Geocode?address=${encodeURIComponent(address)}`);
                    const data = resp.ok ? await resp.json() : { found: false };
                    result = data.found ? { lat: data.lat, lng: data.lng } : null;
                    forwardCache.set(key, result);
                } catch (e) {
                    result = null;
                }
            }

            if (pickupLocationInput.value.trim().toLowerCase() !== key) return; // stale response

            if (result) {
                setPickupStatus("Located on map ✓ — drag the pin if it's not quite right.", 'ok');
                placePickupPin(L.latLng(result.lat, result.lng), false);
            } else {
                setPickupStatus("Couldn't locate this address automatically — please drop a pin manually.", 'warn');
            }
        }

        const debouncedPickupPreview = debounce(forwardGeocodePickupPreview, 700);
        pickupLocationInput.addEventListener('input', () => {
            if (programmaticPickupUpdate) return;
            debouncedPickupPreview();
        });
        pickupLocationInput.addEventListener('blur', () => {
            if (!programmaticPickupUpdate) forwardGeocodePickupPreview();
        });

        function placePickupPin(latlng, fromUserClick) {
            if (!pickupMarker) {
                pickupMarker = L.marker(latlng, { icon: markerIcon('blue'), draggable: true }).addTo(pickupMap);
                pickupMarker.on('dragend', () => {
                    const p = pickupMarker.getLatLng();
                    pickupLatInput.value = p.lat;
                    pickupLngInput.value = p.lng;
                    reverseGeocodePickup(p.lat, p.lng);
                });
            } else {
                pickupMarker.setLatLng(latlng);
            }
            pickupLatInput.value = latlng.lat;
            pickupLngInput.value = latlng.lng;
            if (fromUserClick) reverseGeocodePickup(latlng.lat, latlng.lng);
            pickupMap.panTo(latlng);
        }

        pickupMap.on('click', (e) => placePickupPin(e.latlng, true));

        const useMyLocationPickupBtn = document.getElementById('useMyLocationPickupBtn');
        if (useMyLocationPickupBtn) {
            useMyLocationPickupBtn.addEventListener('click', () => {
                if (!navigator.geolocation) {
                    setPickupStatus("Your browser doesn't support geolocation — please type or click the map instead.", 'warn');
                    return;
                }
                useMyLocationPickupBtn.disabled = true;
                setPickupStatus('Locating you…', 'loading');

                navigator.geolocation.getCurrentPosition(
                    (pos) => {
                        const latlng = L.latLng(pos.coords.latitude, pos.coords.longitude);
                        placePickupPin(latlng, true);
                        pickupMap.setView(latlng, 15);
                        setPickupStatus('Using your current location as pickup — drag the pin if you\'ll be somewhere else.', 'ok');
                        useMyLocationPickupBtn.disabled = false;
                    },
                    (err) => {
                        console.warn('Geolocation error:', err);
                        setPickupStatus("Couldn't get your location — please type an address or click the map instead.", 'warn');
                        useMyLocationPickupBtn.disabled = false;
                    },
                    { enableHighAccuracy: true, timeout: 10000 }
                );
            });
        }
    }
})();
