/* ==========================================================================
   Staff Tickets queue — joins the department's live group so new tickets
   and status changes appear without a manual reload.
   ========================================================================== */
(function () {
    'use strict';

    const config = document.getElementById('staffTicketsConfig');
    if (!config || typeof signalR === 'undefined') return;

    const department = config.dataset.department;
    if (!department) return;

    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/ticketHub')
        .withAutomaticReconnect()
        .build();

    connection.on('TicketCreated', () => window.location.reload());
    connection.on('TicketUpdated', () => window.location.reload());

    connection.start()
        .then(() => connection.invoke('JoinDepartmentQueue', department))
        .catch((err) => console.error('Staff tickets connection failed:', err));

    connection.onreconnected(() => connection.invoke('JoinDepartmentQueue', department));

    window.addEventListener('beforeunload', () => {
        connection.invoke('LeaveDepartmentQueue', department).catch(() => {});
    });
})();
