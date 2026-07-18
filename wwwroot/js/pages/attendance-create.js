/* ==========================================================================
   Create Attendance Session — single-pin map picker for the classroom
   location, plus a "use my current location" shortcut (the instructor is
   usually standing in the classroom when creating the session).
   ========================================================================== */
(function () {
    'use strict';

    const mapEl = document.getElementById('classroomMap');
    if (!mapEl || typeof L === 'undefined') return;

    const latInput = document.getElementById('ClassroomLat');
    const lngInput = document.getElementById('ClassroomLng');
    const useMyLocationBtn = document.getElementById('useMyLocationBtn');

    const map = L.map('classroomMap', { scrollWheelZoom: false });
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '© OpenStreetMap contributors',
        maxZoom: 19
    }).addTo(map);

    map.on('focus', () => map.scrollWheelZoom.enable());
    map.on('blur', () => map.scrollWheelZoom.disable());

    let marker = null;

    function placePin(lat, lng) {
        const latlng = L.latLng(lat, lng);
        if (!marker) {
            marker = L.marker(latlng, { draggable: true }).addTo(map);
            marker.on('dragend', () => {
                const p = marker.getLatLng();
                latInput.value = p.lat;
                lngInput.value = p.lng;
            });
        } else {
            marker.setLatLng(latlng);
        }
        latInput.value = lat;
        lngInput.value = lng;
        map.panTo(latlng);
    }

    // Edit mode: the hidden inputs already have a value from the model —
    // show the existing pin instead of starting from a blank map.
    const existingLat = parseFloat(latInput.value);
    const existingLng = parseFloat(lngInput.value);
    if (!isNaN(existingLat) && !isNaN(existingLng)) {
        map.setView([existingLat, existingLng], 17);
        placePin(existingLat, existingLng);
    } else {
        map.setView([33.8938, 35.5018], 15); // default: Beirut
    }

    map.on('click', (e) => placePin(e.latlng.lat, e.latlng.lng));

    if (useMyLocationBtn) {
        useMyLocationBtn.addEventListener('click', () => {
            if (!navigator.geolocation) return;
            useMyLocationBtn.disabled = true;
            useMyLocationBtn.innerHTML = '<i class="bi bi-hourglass-split"></i> Locating…';

            navigator.geolocation.getCurrentPosition(
                (pos) => {
                    placePin(pos.coords.latitude, pos.coords.longitude);
                    map.setView([pos.coords.latitude, pos.coords.longitude], 17);
                    useMyLocationBtn.disabled = false;
                    useMyLocationBtn.innerHTML = '<i class="bi bi-crosshair"></i> Use my current location';
                },
                () => {
                    useMyLocationBtn.disabled = false;
                    useMyLocationBtn.innerHTML = '<i class="bi bi-crosshair"></i> Use my current location';
                    alert("Couldn't get your location — please click the map instead.");
                },
                { enableHighAccuracy: true, timeout: 10000 }
            );
        });
    }
})();
