/* ==========================================================================
   uc-inputs.js — progressive enhancement for the two native controls the
   browser styles worst: file pickers and date pickers.

   Loaded from every layout, so this covers every route without any view
   having to opt in. Both enhancements keep the ORIGINAL input in the DOM and
   fully functional — nothing here is required for a form to work, it just
   makes it pleasant. If this file fails to load, every page still submits.
   ========================================================================== */
(function () {
    'use strict';

    /* ======================================================================
       1. FILE INPUTS  →  drop zone with preview
       ======================================================================

       The native <input type="file"> is kept, stretched over the whole zone
       at zero opacity. That's deliberate rather than clever: clicking, the
       OS file dialog, drag-and-drop onto the control, `required` validation
       and the validation bubble's anchor position are all native behaviour
       we'd otherwise have to reimplement (and get subtly wrong). This file
       only listens for `change` and draws the result.
       ====================================================================== */

    function formatBytes(bytes) {
        if (bytes < 1024) return bytes + ' B';
        if (bytes < 1024 * 1024) return Math.round(bytes / 1024) + ' KB';
        return (bytes / (1024 * 1024)).toFixed(1).replace(/\.0$/, '') + ' MB';
    }

    function extensionsFrom(accept) {
        if (!accept) return [];
        return accept.split(',')
            .map(function (a) { return a.trim().replace(/^\./, '').toLowerCase(); })
            .filter(function (a) { return a && a.indexOf('/') === -1; });
    }

    function describeAccept(exts, maxMb) {
        var parts = [];
        if (exts.length) parts.push(exts.map(function (e) { return e.toUpperCase(); }).join(', '));
        if (maxMb) parts.push('max ' + maxMb + ' MB');
        return parts.join(' · ');
    }

    function enhanceFile(input) {
        if (input.dataset.ucfReady) return;
        input.dataset.ucfReady = '1';

        var maxMb = parseFloat(input.dataset.maxMb || '0');
        var maxBytes = maxMb > 0 ? maxMb * 1024 * 1024 : 0;
        var exts = extensionsFrom(input.getAttribute('accept'));

        var wrap = document.createElement('div');
        wrap.className = 'ucf';

        input.parentNode.insertBefore(wrap, input);
        wrap.appendChild(input);
        input.classList.add('ucf-native');
        input.classList.remove('form-control');

        var body = document.createElement('div');
        body.className = 'ucf-body';
        body.innerHTML =
            '<div class="ucf-empty">' +
                '<span class="ucf-ico"><svg class="hgi" aria-hidden="true"><use href="#i-file"></use></svg></span>' +
                '<span class="ucf-copy">' +
                    '<strong>Choose a file<span class="ucf-or"> or drop it here</span></strong>' +
                    '<small>' + (describeAccept(exts, maxMb) || 'Any file type') + '</small>' +
                '</span>' +
            '</div>' +
            '<div class="ucf-filled" hidden>' +
                '<span class="ucf-thumb"></span>' +
                '<span class="ucf-meta"><strong></strong><small></small></span>' +
                '<button type="button" class="ucf-remove" title="Remove file" aria-label="Remove file">' +
                    '<svg class="hgi" aria-hidden="true"><use href="#i-close"></use></svg>' +
                '</button>' +
            '</div>';
        wrap.appendChild(body);

        var error = document.createElement('p');
        error.className = 'ucf-error';
        error.hidden = true;
        wrap.appendChild(error);

        var empty = body.querySelector('.ucf-empty');
        var filled = body.querySelector('.ucf-filled');
        var thumb = body.querySelector('.ucf-thumb');
        var nameEl = filled.querySelector('strong');
        var metaEl = filled.querySelector('small');
        var removeBtn = body.querySelector('.ucf-remove');

        var previewUrl = null;

        function releasePreview() {
            if (previewUrl) { URL.revokeObjectURL(previewUrl); previewUrl = null; }
        }

        function showError(message) {
            error.textContent = message;
            error.hidden = false;
            wrap.classList.add('is-invalid');
        }

        function clearError() {
            error.hidden = true;
            error.textContent = '';
            wrap.classList.remove('is-invalid');
        }

        function reset() {
            releasePreview();
            input.value = '';
            filled.hidden = true;
            empty.hidden = false;
            wrap.classList.remove('is-filled');
            thumb.replaceChildren();
        }

        function render() {
            clearError();
            var file = input.files && input.files[0];

            if (!file) { reset(); return; }

            var ext = (file.name.split('.').pop() || '').toLowerCase();

            // Validated here as well as on the server so an oversized upload
            // fails instantly instead of after the round trip. The limits are
            // passed in via data-max-mb to stay in step with the controller.
            if (exts.length && exts.indexOf(ext) === -1) {
                reset();
                showError('That file type isn’t accepted. Allowed: ' + exts.map(function (e) { return '.' + e; }).join(', ') + '.');
                return;
            }
            if (maxBytes && file.size > maxBytes) {
                reset();
                showError('That file is ' + formatBytes(file.size) + ' — the maximum is ' + maxMb + ' MB.');
                return;
            }

            releasePreview();
            thumb.replaceChildren();

            if (/^image\//.test(file.type)) {
                previewUrl = URL.createObjectURL(file);
                var img = document.createElement('img');
                img.src = previewUrl;
                img.alt = '';
                thumb.appendChild(img);
                thumb.classList.add('has-image');
            } else {
                thumb.classList.remove('has-image');
                var badge = document.createElement('span');
                badge.className = 'ucf-ext';
                badge.textContent = ext ? ext.toUpperCase().slice(0, 4) : 'FILE';
                thumb.appendChild(badge);
            }

            nameEl.textContent = file.name;
            metaEl.textContent = formatBytes(file.size) + (ext ? ' · ' + ext.toUpperCase() : '');

            empty.hidden = true;
            filled.hidden = false;
            wrap.classList.add('is-filled');
        }

        input.addEventListener('change', render);

        removeBtn.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            reset();
            clearError();
            // Tell anything watching the form — notably form-progress.js, which
            // would otherwise keep counting this field as complete.
            input.dispatchEvent(new Event('input', { bubbles: true }));
            input.dispatchEvent(new Event('change', { bubbles: true }));
        });

        // Drag feedback. The drop itself is handled natively by the overlaid
        // input; these listeners only drive the visual state.
        var dragDepth = 0;
        wrap.addEventListener('dragenter', function () {
            dragDepth++;
            wrap.classList.add('is-dragging');
        });
        wrap.addEventListener('dragleave', function () {
            dragDepth = Math.max(0, dragDepth - 1);
            if (dragDepth === 0) wrap.classList.remove('is-dragging');
        });
        wrap.addEventListener('drop', function () {
            dragDepth = 0;
            wrap.classList.remove('is-dragging');
        });

        input.addEventListener('focus', function () { wrap.classList.add('is-focused'); });
        input.addEventListener('blur', function () { wrap.classList.remove('is-focused'); });

        // jQuery validate marks the native input; mirror it onto the zone so
        // the styled control turns red too.
        new MutationObserver(function () {
            wrap.classList.toggle('is-invalid', input.classList.contains('input-validation-error'));
        }).observe(input, { attributes: true, attributeFilter: ['class'] });

        // A file can already be present on a validation re-render.
        render();
    }

    /* ======================================================================
       2. DATE / TIME INPUTS  →  styled field with a real calendar button
       ======================================================================

       No custom calendar widget: the native picker is well-tested, localised,
       keyboard-accessible and correct on mobile. What it lacks is a control
       that looks like it belongs to the rest of the UI, so the tiny built-in
       indicator is hidden and replaced with a proper button that opens the
       same picker via showPicker().
       ====================================================================== */

    var DATE_TYPES = ['date', 'datetime-local', 'time', 'month', 'week'];

    function enhanceDate(input) {
        if (input.dataset.ucdReady) return;
        input.dataset.ucdReady = '1';

        var wrap = document.createElement('div');
        wrap.className = 'ucd';
        input.parentNode.insertBefore(wrap, input);
        wrap.appendChild(input);
        input.classList.add('ucd-native');

        // Any width the input was sizing itself with has to move to the
        // wrapper, which is now the element the layout sees. Leaving it on the
        // input lets the wrapper stretch to the full container while the field
        // stays narrow — the calendar button then anchors to the wrapper's far
        // edge, stranded well away from the input it belongs to.
        ['width', 'maxWidth', 'minWidth', 'flex'].forEach(function (prop) {
            var value = input.style[prop];
            if (value) {
                wrap.style[prop] = value;
                input.style[prop] = '';
            }
        });

        var button = document.createElement('button');
        button.type = 'button';
        button.className = 'ucd-btn';
        button.tabIndex = -1;                       // the input itself is the tab stop
        button.setAttribute('aria-hidden', 'true');
        button.title = 'Open calendar';
        button.innerHTML = '<svg class="hgi" aria-hidden="true"><use href="#i-calendar"></use></svg>';
        wrap.appendChild(button);

        // Our own calendar for the types it handles; the browser's for the rest
        // (month/week), and as the fallback if uc-datepicker.js failed to load.
        var custom = window.ucDatePicker && window.ucDatePicker.supports(input.type);
        if (custom) wrap.classList.add('ucd-custom');

        button.addEventListener('click', function (e) {
            e.stopPropagation();
            if (custom) {
                window.ucDatePicker.toggle(input, wrap);
                return;
            }
            input.focus();
            // showPicker throws if unsupported or not user-activated; focusing
            // the field is a perfectly good fallback.
            try { if (input.showPicker) input.showPicker(); } catch (err) { /* no-op */ }
        });

        function syncFilled() {
            wrap.classList.toggle('is-filled', !!input.value);
        }
        input.addEventListener('change', syncFilled);
        input.addEventListener('input', syncFilled);
        input.addEventListener('focus', function () { wrap.classList.add('is-focused'); });
        input.addEventListener('blur', function () { wrap.classList.remove('is-focused'); });

        new MutationObserver(function () {
            wrap.classList.toggle('is-invalid', input.classList.contains('input-validation-error'));
        }).observe(input, { attributes: true, attributeFilter: ['class'] });

        syncFilled();
    }

    /* ---------------------------------------------------------------------- */

    function init(root) {
        (root || document).querySelectorAll('input[type="file"]').forEach(enhanceFile);

        DATE_TYPES.forEach(function (type) {
            (root || document).querySelectorAll('input[type="' + type + '"]').forEach(enhanceDate);
        });
    }

    // Run immediately rather than waiting for DOMContentLoaded. This script is
    // loaded at the end of <body>, so every control above it is already parsed,
    // and enhancing now means our `change` listener is registered BEFORE any
    // per-page script further down the document. That ordering matters:
    // career-profile-cv.js auto-submits the CV form on change, and it must not
    // fire for a file this validation has just rejected and cleared.
    init();

    // Second pass for anything that came after this tag.
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { init(); });
    }

    // Exposed so anything injecting markup later can enhance it too.
    window.ucInputs = { init: init };
})();
