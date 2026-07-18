/* ==========================================================================
   Create Ride — map pin picker (Phase 2) + geocoding robustness (Phase 3).

   Two ways to set a location, kept in sync without looping:
     1. Type an address -> debounced forward-geocode -> pin moves to match.
     2. Click/drag a pin -> reverse-geocode -> text field updates to match.

   A `programmatic` flag prevents step 2's text update from re-triggering
   step 1's geocode call (which would otherwise fire pointlessly, or worse,
   fight the user's manual pin placement).
   ========================================================================== */
(function () {
    'use strict';

    const mapEl = document.getElementById('rideCreateMap');
    if (!mapEl || typeof L === 'undefined') return;

    const depTextInput = document.querySelector('input[name="DepartureLocation"]');
    const destTextInput = document.querySelector('input[name="Destination"]');
    const depLatInput = document.getElementById('DepartureLat');
    const depLngInput = document.getElementById('DepartureLng');
    const destLatInput = document.getElementById('DestinationLat');
    const destLngInput = document.getElementById('DestinationLng');
    const depStatus = document.getElementById('depGeoStatus');
    const destStatus = document.getElementById('destGeoStatus');

    const btnDeparture = document.getElementById('pinModeDeparture');
    const btnDestination = document.getElementById('pinModeDestination');
    const checkDeparture = document.getElementById('pinCheckDeparture');
    const checkDestination = document.getElementById('pinCheckDestination');

    let mode = 'departure';
    let departureMarker = null;
    let destinationMarker = null;
    let routeLine = null;
    let programmaticTextUpdate = false; // guards against reverse->forward geocode loops

    // Simple in-memory caches so retyping/re-dragging the same spot doesn't
    // re-hit Nominatim (keeps us well under its 1 request/second policy).
    const forwardCache = new Map();  // normalized address -> {lat,lng} | null
    const reverseCache = new Map();  // "lat,lng" (rounded) -> address | null

    const map = L.map('rideCreateMap', { scrollWheelZoom: false });
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '© OpenStreetMap contributors',
        maxZoom: 19
    }).addTo(map);
    map.setView([33.8938, 35.5018], 12); // default: Beirut

    map.on('focus', () => map.scrollWheelZoom.enable());
    map.on('blur', () => map.scrollWheelZoom.disable());

    function markerIcon(color) {
        return L.icon({
            iconUrl: 'https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-' + color + '.png',
            shadowUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png',
            iconSize: [25, 41], iconAnchor: [12, 41], popupAnchor: [1, -34], shadowSize: [41, 41]
        });
    }

    function setMode(newMode) {
        mode = newMode;
        btnDeparture.classList.toggle('is-active', mode === 'departure');
        btnDestination.classList.toggle('is-active', mode === 'destination');
    }
    btnDeparture.addEventListener('click', () => setMode('departure'));
    btnDestination.addEventListener('click', () => setMode('destination'));

    /* ---------- "Use my current location" — optional convenience, not automatic ---------- */
    const useMyLocationBtn = document.getElementById('useMyLocationBtn');
    if (useMyLocationBtn) {
        useMyLocationBtn.addEventListener('click', () => {
            if (!navigator.geolocation) {
                setStatus(depStatus, "Your browser doesn't support geolocation — please type or click the map instead.", 'warn');
                return;
            }
            useMyLocationBtn.disabled = true;
            setStatus(depStatus, 'Locating you…', 'loading');

            navigator.geolocation.getCurrentPosition(
                (pos) => {
                    setMode('departure');
                    placePinProgrammatically(true, pos.coords.latitude, pos.coords.longitude, true);
                    setStatus(depStatus, 'Using your current location as the departure pin — drag it if you\'ll actually be leaving from somewhere else.', 'ok');
                    useMyLocationBtn.disabled = false;
                },
                (err) => {
                    console.warn('Geolocation error:', err);
                    setStatus(depStatus, "Couldn't get your location — please type an address or click the map instead.", 'warn');
                    useMyLocationBtn.disabled = false;
                },
                { enableHighAccuracy: true, timeout: 10000 }
            );
        });
    }

    function updateRouteLine() {
        if (routeLine) { map.removeLayer(routeLine); routeLine = null; }
        if (departureMarker && destinationMarker) {
            routeLine = L.polyline(
                [departureMarker.getLatLng(), destinationMarker.getLatLng()],
                { color: '#16a34a', weight: 3, opacity: 0.6, dashArray: '8, 8' }
            ).addTo(map);
        }
    }

    function setStatus(el, text, kind) {
        if (!el) return;
        el.textContent = text;
        el.className = 'geo-status' + (kind ? ' geo-status-' + kind : '');
    }

    function debounce(fn, delay) {
        let timer = null;
        return (...args) => {
            clearTimeout(timer);
            timer = setTimeout(() => fn(...args), delay);
        };
    }

    /* ---------- Reverse geocode: pin -> text (with cache) ---------- */
    async function reverseGeocodeInto(textInput, lat, lng) {
        const key = lat.toFixed(5) + ',' + lng.toFixed(5);
        if (reverseCache.has(key)) {
            applyReverseResult(textInput, reverseCache.get(key));
            return;
        }
        try {
            const resp = await fetch(`/Rides/ReverseGeocode?lat=${lat}&lng=${lng}`);
            const data = resp.ok ? await resp.json() : { address: null };
            reverseCache.set(key, data.address || null);
            applyReverseResult(textInput, data.address || null);
        } catch (e) {
            reverseCache.set(key, null);
        }
    }

    function applyReverseResult(textInput, address) {
        if (address && textInput) {
            programmaticTextUpdate = true;
            textInput.value = address;
            // Release the guard on the next tick, after the 'input' event (if any) fires.
            setTimeout(() => { programmaticTextUpdate = false; }, 0);
        }
    }

    /* ---------- Forward geocode: text -> pin preview (debounced + cached) ---------- */
    async function forwardGeocodePreview(textInput, statusEl, isDeparture) {
        const address = textInput.value.trim();
        if (!address) { setStatus(statusEl, '', ''); return; }

        const key = address.toLowerCase();
        setStatus(statusEl, 'Looking up address…', 'loading');

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

        // If the user has since switched to editing the other field, or the
        // text changed again while we were waiting, don't apply a stale result.
        if (textInput.value.trim().toLowerCase() !== key) return;

        if (result) {
            setStatus(statusEl, "Located on map ✓ — drag the pin if it's not quite right.", 'ok');
            placePinProgrammatically(isDeparture, result.lat, result.lng);
        } else {
            setStatus(statusEl, "Couldn't locate this address automatically — please drop a pin manually.", 'warn');
        }
    }

    const debouncedDepPreview = debounce(() => forwardGeocodePreview(depTextInput, depStatus, true), 700);
    const debouncedDestPreview = debounce(() => forwardGeocodePreview(destTextInput, destStatus, false), 700);

    depTextInput.addEventListener('input', () => {
        if (programmaticTextUpdate) return; // don't re-geocode our own reverse-geocode result
        debouncedDepPreview();
    });
    destTextInput.addEventListener('input', () => {
        if (programmaticTextUpdate) return;
        debouncedDestPreview();
    });
    // Also fire immediately on blur, so tabbing away doesn't leave a pending 700ms lookup unresolved.
    depTextInput.addEventListener('blur', () => { if (!programmaticTextUpdate) forwardGeocodePreview(depTextInput, depStatus, true); });
    destTextInput.addEventListener('blur', () => { if (!programmaticTextUpdate) forwardGeocodePreview(destTextInput, destStatus, false); });

    /* ---------- Placing pins (from a map click/drag OR a text-driven preview) ---------- */
    function placePin(latlng) {
        placePinProgrammatically(mode === 'departure', latlng.lat, latlng.lng, true);
        // After the first pin from a manual click, nudge toward the other one.
        if (departureMarker && !destinationMarker) setMode('destination');
    }

    function placePinProgrammatically(isDeparture, lat, lng, fromUserClick) {
        const latlng = L.latLng(lat, lng);

        if (isDeparture) {
            if (!departureMarker) {
                departureMarker = L.marker(latlng, { icon: markerIcon('green'), draggable: true }).addTo(map);
                departureMarker.on('dragend', () => {
                    const p = departureMarker.getLatLng();
                    depLatInput.value = p.lat;
                    depLngInput.value = p.lng;
                    checkDeparture.classList.add('is-set');
                    reverseGeocodeInto(depTextInput, p.lat, p.lng);
                    updateRouteLine();
                });
            } else {
                departureMarker.setLatLng(latlng);
            }
            depLatInput.value = lat;
            depLngInput.value = lng;
            checkDeparture.classList.add('is-set');
            if (fromUserClick) reverseGeocodeInto(depTextInput, lat, lng);
            if (fromUserClick) map.panTo(latlng);
        } else {
            if (!destinationMarker) {
                destinationMarker = L.marker(latlng, { icon: markerIcon('red'), draggable: true }).addTo(map);
                destinationMarker.on('dragend', () => {
                    const p = destinationMarker.getLatLng();
                    destLatInput.value = p.lat;
                    destLngInput.value = p.lng;
                    checkDestination.classList.add('is-set');
                    reverseGeocodeInto(destTextInput, p.lat, p.lng);
                    updateRouteLine();
                });
            } else {
                destinationMarker.setLatLng(latlng);
            }
            destLatInput.value = lat;
            destLngInput.value = lng;
            checkDestination.classList.add('is-set');
            if (fromUserClick) reverseGeocodeInto(destTextInput, lat, lng);
            if (fromUserClick) map.panTo(latlng);
        }

        // A text-driven preview also re-centers gently so the user can see it.
        if (!fromUserClick) map.panTo(latlng);

        updateRouteLine();
    }

    map.on('click', (e) => placePin(e.latlng));
})();
