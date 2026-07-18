/* ==========================================================================
   Ride Details — live "Uber-style" location tracking (Phase 4).

   - Driver's browser: once the trip has started, streams GPS position via
     navigator.geolocation.watchPosition, throttled and POSTed to
     /Rides/UpdateLocation, which validates + persists + broadcasts it.
   - Anyone with access (driver + accepted passengers): renders/moves a live
     marker on the existing route map (window.UcRideMap, exposed by
     ride-details.js) as updates arrive.
   - EVERYONE who opens this ride's Details page (regardless of request
     status) joins the SignalR group, so that if their own request gets
     accepted/rejected while their tab is already open, their page can
     refresh itself automatically instead of requiring a manual reload to
     pick up live-tracking access.

   Known limitation (documented, not a bug): live GPS streaming only works
   while the driver's browser tab stays open with location permission
   granted — there's no background GPS in a browser. A native MAUI client
   would remove this limitation via real background location.
   ========================================================================== */
(function () {
    'use strict';

    const config = document.getElementById('rideTrackingConfig');
    if (!config || typeof signalR === 'undefined') return;

    const rideId = parseInt(config.dataset.rideId, 10);
    const isDriver = config.dataset.isDriver === 'true';
    const canTrack = config.dataset.canTrack === 'true';
    const currentUserId = config.dataset.currentUserId || '';
    let tripStarted = config.dataset.tripStarted === 'true';
    const rideStatus = config.dataset.rideStatus; // "Active" | "Full" | "Completed" | "Cancelled"
    const updateUrl = config.dataset.updateUrl;
    const lastLat = parseFloat(config.dataset.lastLat);
    const lastLng = parseFloat(config.dataset.lastLng);

    const badge = document.getElementById('rideLiveBadge');
    const badgeText = document.getElementById('rideLiveBadgeText');

    function setBadge(state, text) {
        if (!badge || !badgeText) return;
        badge.dataset.state = state;
        badgeText.textContent = text;
    }

    if (canTrack) {
        if (rideStatus === 'Completed' || rideStatus === 'Cancelled') {
            setBadge('ended', 'Trip ended');
        } else if (tripStarted) {
            setBadge('pending', 'Live — waiting for position…');
        } else {
            setBadge('off', isDriver ? 'Not started yet' : 'Waiting for driver to start');
        }
    }

    /* -----------------------------------------------------------------------
       Live marker on the shared route map (created in ride-details.js)
       ----------------------------------------------------------------------- */
    let liveMarker = null;

    function renderLiveMarker(lat, lng) {
        if (!canTrack) return;
        const map = window.UcRideMap;
        if (!map || typeof window.UcRideMarkerIcon !== 'function') return;
        const latlng = L.latLng(lat, lng);
        if (!liveMarker) {
            liveMarker = L.marker(latlng, { icon: window.UcRideMarkerIcon('orange') })
                .addTo(map)
                .bindPopup("Driver's live location (moving)");
        } else {
            liveMarker.setLatLng(latlng);
        }
    }

    // If the driver already started before this page loaded (e.g. a passenger
    // opening the link mid-trip), show the last known position immediately
    // instead of waiting for the next live update.
    if (canTrack && tripStarted && !isNaN(lastLat) && !isNaN(lastLng)) {
        renderLiveMarker(lastLat, lastLng);
        setBadge('live', 'Live');
    }

    /* -----------------------------------------------------------------------
       SignalR connection — EVERYONE viewing this ride's page joins the group,
       so status-change notifications (see below) always reach them, even if
       they aren't currently eligible for live tracking themselves.
       ----------------------------------------------------------------------- */
    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/rideTrackingHub')
        .withAutomaticReconnect()
        .build();

    connection.on('ReceiveLocation', (data) => {
        if (!canTrack) return;
        renderLiveMarker(data.lat, data.lng);
        setBadge('live', 'Live');
    });

    connection.on('TripStarted', () => {
        if (!canTrack) return;
        tripStarted = true;
        setBadge('pending', 'Live — waiting for position…');
    });

    connection.on('TripEnded', () => {
        if (!canTrack) return;
        setBadge('ended', 'Trip ended');
        if (liveMarker && window.UcRideMap) {
            window.UcRideMap.removeLayer(liveMarker);
            liveMarker = null;
        }
        stopBroadcasting();
    });

    // A new seat request came in — only the driver needs to see this, so
    // their (already open) request-management panel updates without a
    // manual refresh.
    connection.on('NewRideRequest', () => {
        if (isDriver) window.location.reload();
    });

    // This is the actual fix for "passenger needs to refresh to see live
    // tracking": when the driver accepts/rejects a request, everyone viewing
    // the ride gets this event. If it's about YOU, your page — which was
    // rendered before you had tracking access — reloads itself so the tracking
    // config, SignalR group join, and live UI all get (re)rendered correctly.
    connection.on('RequestStatusChanged', (data) => {
        if (currentUserId && String(data.passengerId) === String(currentUserId)) {
            window.location.reload();
        }
    });

    connection.start()
        .then(() => connection.invoke('JoinRideTracking', rideId))
        .catch((err) => console.error('Ride tracking connection failed:', err));

    connection.onreconnected(() => connection.invoke('JoinRideTracking', rideId));

    window.addEventListener('beforeunload', () => {
        connection.invoke('LeaveRideTracking', rideId).catch(() => { });
        stopBroadcasting();
    });

    /* -----------------------------------------------------------------------
       Driver-side: stream GPS position (throttled) once the trip has started
       ----------------------------------------------------------------------- */
    let watchId = null;
    let lastSentAt = 0;
    const MIN_SEND_INTERVAL_MS = 4000; // don't hammer the server faster than this

    function getAntiForgeryToken() {
        const form = document.getElementById('rideTrackingAntiForgery');
        const input = form ? form.querySelector('input[name="__RequestVerificationToken"]') : null;
        return input ? input.value : '';
    }

    async function sendLocation(lat, lng) {
        try {
            const fd = new FormData();
            fd.append('rideId', rideId);
            fd.append('lat', lat);
            fd.append('lng', lng);
            fd.append('__RequestVerificationToken', getAntiForgeryToken());
            await fetch(updateUrl, { method: 'POST', body: fd });
        } catch (e) {
            // A missed update isn't critical — the next watchPosition tick will retry.
            console.warn('Failed to send location update:', e);
        }
    }

    function startBroadcasting() {
        if (!canTrack || !isDriver || watchId !== null || !navigator.geolocation) return;

        watchId = navigator.geolocation.watchPosition(
            (pos) => {
                const now = Date.now();
                if (now - lastSentAt < MIN_SEND_INTERVAL_MS) return;
                lastSentAt = now;
                sendLocation(pos.coords.latitude, pos.coords.longitude);
            },
            (err) => {
                console.warn('Geolocation error:', err);
                setBadge('off', 'Location unavailable — check permissions');
            },
            { enableHighAccuracy: true, maximumAge: 5000, timeout: 15000 }
        );
    }

    function stopBroadcasting() {
        if (watchId !== null && navigator.geolocation) {
            navigator.geolocation.clearWatch(watchId);
            watchId = null;
        }
    }

    if (canTrack && isDriver && tripStarted && rideStatus !== 'Completed' && rideStatus !== 'Cancelled') {
        startBroadcasting();
    }
})();
