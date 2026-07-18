/* ==========================================================================
   Club Details — tab switching, real-time chat (with unread badge), and
   live updates: club-wide changes, a new-member announcement, and personal
   "your role changed" notifications.
   ========================================================================== */
(function () {
    'use strict';

    /* ---------------------------------------------------------------------
       Tab switching (+ clearing the unread chat badge on open)
       --------------------------------------------------------------------- */
    const tabs = document.querySelectorAll('.club-tab');
    const panels = document.querySelectorAll('.club-panel');
    const chatBadge = document.getElementById('clubChatBadge');
    let unreadChatCount = 0;
    let chatTabActive = false;

    function setChatBadge(count) {
        unreadChatCount = count;
        if (!chatBadge) return;
        chatBadge.textContent = String(count);
        chatBadge.classList.toggle('hidden', count <= 0);
    }

    tabs.forEach((tab) => {
        tab.addEventListener('click', () => {
            tabs.forEach((t) => t.classList.remove('active'));
            tab.classList.add('active');
            const target = tab.dataset.tab;
            panels.forEach((p) => p.classList.toggle('hidden', p.dataset.panel !== target));

            chatTabActive = target === 'chat';
            if (chatTabActive) setChatBadge(0);
        });
    });

    /* ---------------------------------------------------------------------
       Real-time: chat + live club updates (membership, announcements, events)
       --------------------------------------------------------------------- */
    const config = document.getElementById('clubTabs');
    if (!config || typeof signalR === 'undefined') return;

    const clubId = parseInt(config.dataset.clubId, 10);
    const currentName = config.dataset.currentName || '';
    const currentUserId = config.dataset.currentUserId || '';
    const isMember = config.dataset.isMember === 'true';
    const postUrl = config.dataset.postUrl;

    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/clubHub')
        .withAutomaticReconnect()
        .build();

    /* A single debounced reload — lets a toast actually be seen before the
       page refreshes, instead of the page yanking itself away instantly.
       If multiple events fire close together, we keep whichever wants the
       LATEST reload time, not the earliest. */
    let reloadAt = 0;
    let reloadTimer = null;
    function scheduleReload(delayMs) {
        const candidate = Date.now() + delayMs;
        if (candidate <= reloadAt) return;
        reloadAt = candidate;
        if (reloadTimer) clearTimeout(reloadTimer);
        reloadTimer = setTimeout(() => window.location.reload(), delayMs);
    }

    /* Incoming chat messages */
    const messagesList = document.getElementById('clubChatMessages');

    function appendMessage(msg) {
        if (!messagesList) return;
        const mine = msg.senderName && currentName &&
            msg.senderName.trim().toLowerCase() === currentName.trim().toLowerCase();

        const item = document.createElement('div');
        item.className = 'ticket-msg' + (mine ? ' is-me' : '');

        const head = document.createElement('div');
        head.className = 'ticket-msg-head';
        const strong = document.createElement('strong');
        strong.textContent = mine ? 'You' : msg.senderName;
        const time = document.createElement('span');
        time.className = 'ticket-msg-time';
        time.textContent = msg.sentAt;
        head.appendChild(strong);
        head.appendChild(time);

        const p = document.createElement('p');
        p.textContent = msg.content;

        item.appendChild(head);
        item.appendChild(p);
        messagesList.appendChild(item);
        messagesList.scrollTop = messagesList.scrollHeight;

        // Unread badge only climbs for messages from other people, and only
        // while the Chat tab isn't the one currently open.
        if (!mine && !chatTabActive) setChatBadge(unreadChatCount + 1);
    }

    connection.on('ReceiveMessage', appendMessage);

    /* A new member was approved — let everyone currently here know, then
       refresh shortly after so the toast has time to be seen. */
    connection.on('MemberJoined', (data) => {
        if (window.UC && UC.toast) UC.toast(`${data.name} just joined the club!`, 'success');
        scheduleReload(2200);
    });

    /* Any other structural change (approval, removal, announcement, event,
       RSVP, inactivity flip) — reload fairly quickly, no toast needed. */
    connection.on('ClubUpdated', () => scheduleReload(400));

    connection.start()
        .then(() => {
            connection.invoke('JoinClub', clubId);
            if (currentUserId) connection.invoke('JoinUserNotifications', currentUserId);
        })
        .catch((err) => console.error('Club connection failed:', err));

    connection.onreconnected(() => {
        connection.invoke('JoinClub', clubId);
        if (currentUserId) connection.invoke('JoinUserNotifications', currentUserId);
    });

    window.addEventListener('beforeunload', () => {
        connection.invoke('LeaveClub', clubId).catch(() => { });
        if (currentUserId) connection.invoke('LeaveUserNotifications', currentUserId).catch(() => { });
    });

    /* Personal notification: your own role changed */
    connection.on('YourRoleChanged', (data) => {
        if (window.UC && UC.toast) {
            UC.toast(`Your role in ${data.clubName} is now ${data.newRole}.`, 'success', 8000);
        }
        scheduleReload(2800);
    });

    /* Send a chat message via fetch (not a full page post) */
    const chatForm = document.getElementById('clubChatForm');
    const chatInput = document.getElementById('clubChatInput');

    if (chatForm && isMember) {
        if (messagesList) messagesList.scrollTop = messagesList.scrollHeight;

        chatForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const content = chatInput.value.trim();
            if (!content) return;

            const token = chatForm.querySelector('input[name="__RequestVerificationToken"]').value;
            const fd = new FormData();
            fd.append('clubId', clubId);
            fd.append('content', content);
            fd.append('__RequestVerificationToken', token);

            const btn = chatForm.querySelector('button[type="submit"]');
            if (btn) btn.disabled = true;
            chatInput.disabled = true;

            try {
                const resp = await fetch(postUrl, { method: 'POST', body: fd });
                if (resp.ok) chatInput.value = '';
            } catch (err) {
                console.warn('Failed to send club message:', err);
            } finally {
                if (btn) btn.disabled = false;
                chatInput.disabled = false;
                chatInput.focus();
            }
        });
    }
})();
