/* ==========================================================================
   Global notifications — connects to NotificationHub on every page (for any
   authenticated user), so a system action affecting someone shows up as a
   toast immediately if they're online, and updates the nav bell count live.
   If they're not online, the persisted Notification row (see
   /Notifications) still tells them next time they check.
   ========================================================================== */
(function () {
    'use strict';

    if (typeof signalR === 'undefined') return;

    const config = document.getElementById('notificationUserConfig');
    const userId = config?.dataset.currentUserId;
    if (!userId) return; // not logged in on this page

    const bellCount = document.getElementById('notificationBellCount');

    function bumpBellCount() {
        if (!bellCount) return;
        const current = parseInt(bellCount.textContent, 10) || 0;
        bellCount.textContent = String(current + 1);
        bellCount.style.display = '';
    }

    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/notificationHub')
        .withAutomaticReconnect()
        .build();

    connection.on('NewNotification', (data) => {
        bumpBellCount();
        if (window.UC && UC.toast) {
            UC.toast(`${data.title}: ${data.message}`, 'success', 8000);
        }
    });

    // A course was added/removed for this user specifically — if they
    // currently have "My Courses" open, refresh it immediately instead of
    // leaving it showing stale data until their next manual reload.
    connection.on('CoursesChanged', () => {
        if (window.location.pathname.toLowerCase().includes('/studygroups/mycourses')) {
            window.location.reload();
        }
    });

    connection.start()
        .then(() => connection.invoke('JoinUser', userId))
        .catch((err) => console.error('Notification hub connection failed:', err));

    connection.onreconnected(() => connection.invoke('JoinUser', userId));

    window.addEventListener('beforeunload', () => {
        connection.invoke('LeaveUser', userId).catch(() => { });
    });
})();
