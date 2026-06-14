/* ==========================================================================
   Study Group details — real-time chat over SignalR.
   Reads its configuration from the .sgd-chat-panel data-* attributes so no
   inline server values are needed.
   ========================================================================== */
(function () {
    'use strict';

    const panel = document.querySelector('.sgd-chat-panel');
    if (!panel || typeof signalR === 'undefined') return;

    const groupId = panel.dataset.groupId;
    const currentName = panel.dataset.currentName || '';
    const postUrl = panel.dataset.postUrl;

    const messagesList = document.getElementById('messagesList');
    const statusLabel = document.getElementById('liveStatus');
    const form = document.getElementById('messageForm');
    const input = document.getElementById('messageInput');
    let placeholder = document.getElementById('noMessagesPlaceholder');

    function setStatus(text, state) {
        if (!statusLabel) return;
        statusLabel.innerHTML = '<span class="sgd-live-dot"></span>' + UC.escape(text);
        statusLabel.dataset.state = state || '';
    }

    function initials(name) {
        if (!name) return '?';
        const parts = name.trim().split(/\s+/);
        return (parts.length === 1
            ? parts[0].slice(0, 1)
            : parts[0].slice(0, 1) + parts[parts.length - 1].slice(0, 1)).toUpperCase();
    }

    function atBottom() {
        return messagesList.scrollHeight - messagesList.scrollTop - messagesList.clientHeight < 80;
    }

    function appendMessage(msg) {
        if (placeholder) { placeholder.remove(); placeholder = null; }

        const mine = msg.senderName && currentName &&
            msg.senderName.trim().toLowerCase() === currentName.trim().toLowerCase();
        const wasAtBottom = atBottom();

        const item = document.createElement('div');
        item.className = 'sgd-msg' + (mine ? ' is-mine' : '');
        item.innerHTML =
            '<span class="sgd-avatar"></span>' +
            '<div class="sgd-bubble">' +
                '<div class="sgd-bubble-head"><strong></strong><small></small></div>' +
                '<div class="sgd-bubble-text"></div>' +
            '</div>';
        item.querySelector('.sgd-avatar').textContent = initials(msg.senderName);
        item.querySelector('strong').textContent = mine ? 'You' : msg.senderName;
        item.querySelector('small').textContent = msg.sentAt;
        item.querySelector('.sgd-bubble-text').textContent = msg.content;
        messagesList.appendChild(item);

        if (wasAtBottom || mine) messagesList.scrollTop = messagesList.scrollHeight;
    }

    /* Scroll to newest on load */
    if (messagesList) messagesList.scrollTop = messagesList.scrollHeight;

    /* 1) Build connection */
    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/studygroupHub')
        .withAutomaticReconnect()
        .build();

    /* 2) Incoming messages */
    connection.on('ReceiveMessage', appendMessage);

    /* 3) Connection lifecycle */
    connection.start()
        .then(() => connection.invoke('JoinGroup', groupId))
        .then(() => setStatus('live', 'live'))
        .catch((err) => { setStatus('offline — refresh', 'off'); console.error(err); });

    connection.onreconnecting(() => setStatus('reconnecting…', ''));
    connection.onreconnected(() => connection.invoke('JoinGroup', groupId)
        .then(() => setStatus('live', 'live')));
    connection.onclose(() => setStatus('disconnected', 'off'));

    /* 4) Send via fetch */
    if (form) {
        form.addEventListener('submit', async (e) => {
            e.preventDefault();
            const content = input.value.trim();
            if (!content) return;

            const token = form.querySelector('input[name="__RequestVerificationToken"]').value;
            const fd = new FormData();
            fd.append('content', content);
            fd.append('__RequestVerificationToken', token);

            const btn = form.querySelector('button[type="submit"]');
            if (btn) btn.disabled = true;
            input.disabled = true;

            try {
                const resp = await fetch(postUrl, { method: 'POST', body: fd });
                if (resp.ok) {
                    input.value = '';
                } else {
                    UC.toast('Could not send your message. Please try again.', 'danger');
                }
            } catch (err) {
                UC.toast('Network error — message not sent.', 'danger');
            } finally {
                if (btn) btn.disabled = false;
                input.disabled = false;
                input.focus();
            }
        });
    }

    /* 5) Leave the room on unload */
    window.addEventListener('beforeunload', () => {
        connection.invoke('LeaveGroup', groupId).catch(() => {});
    });
})();
