/* ==========================================================================
   Ticket Details — joins this ticket's group so a staff response or status
   change appears live without a manual reload.
   ========================================================================== */
(function () {
    'use strict';

    const panel = document.getElementById('ticketHistory');
    if (!panel || typeof signalR === 'undefined') return;

    const ticketId = parseInt(panel.dataset.ticketId, 10);

    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/ticketHub')
        .withAutomaticReconnect()
        .build();

    connection.on('TicketUpdated', () => window.location.reload());

    connection.start()
        .then(() => connection.invoke('JoinTicket', ticketId))
        .catch((err) => console.error('Ticket details connection failed:', err));

    connection.onreconnected(() => connection.invoke('JoinTicket', ticketId));

    window.addEventListener('beforeunload', () => {
        connection.invoke('LeaveTicket', ticketId).catch(() => {});
    });
})();
