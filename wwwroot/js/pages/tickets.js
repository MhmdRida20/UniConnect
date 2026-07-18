/* ==========================================================================
   My Tickets — joins this student's personal notification group so any
   status change or staff reply on ANY of their tickets refreshes the list
   live, without a manual reload.
   ========================================================================== */
(function () {
    'use strict';

    const config = document.getElementById('ticketsConfig');
    if (!config || typeof signalR === 'undefined') return;

    const userId = config.dataset.currentUserId;
    if (!userId) return;

    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/ticketHub')
        .withAutomaticReconnect()
        .build();

    connection.on('TicketUpdated', () => window.location.reload());
    connection.on('TicketCreated', () => window.location.reload());

    connection.start()
        .then(() => connection.invoke('JoinMyTickets', userId))
        .catch((err) => console.error('Ticket list connection failed:', err));

    connection.onreconnected(() => connection.invoke('JoinMyTickets', userId));

    window.addEventListener('beforeunload', () => {
        connection.invoke('LeaveMyTickets', userId).catch(() => {});
    });
})();
