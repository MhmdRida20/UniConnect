/* ==========================================================================
   Career Profile — Skills

   Adding a skill used to POST immediately, so every single skill cost a full
   page reload and threw the student back to the top of the page. Adds and
   removals are now staged here and submitted together by #skillsBatchForm.

   Progressive enhancement: the per-skill AddSkill/RemoveSkill forms in the
   markup are real and still work on their own. This file only takes over
   once it runs.
   ========================================================================== */
(function () {
    'use strict';

    const card = document.getElementById('skillsCard');
    if (!card) return;

    const chipBox = document.getElementById('skillChips');
    const emptyMsg = document.getElementById('skillsEmpty');
    const addForm = document.getElementById('skillAddForm');
    const batchForm = document.getElementById('skillsBatchForm');
    const batchInputs = document.getElementById('skillsBatchInputs');
    const summary = document.getElementById('skillsSummary');
    const discardBtn = document.getElementById('skillsDiscard');
    if (!chipBox || !addForm || !batchForm || !batchInputs) return;

    const nameInput = addForm.querySelector('[name="skillName"]');
    const levelSelect = addForm.querySelector('[name="proficiencyLevel"]');

    // Swap the copy now that batching is actually in effect.
    card.querySelectorAll('[data-skills-hint]').forEach((el) => {
        el.hidden = el.dataset.skillsHint === 'nojs';
    });

    /** Skills typed but not yet saved: [{ name, level }]. */
    const pendingAdds = [];
    /** Ids of saved skills marked for deletion. */
    const pendingRemovals = new Set();

    const key = (s) => s.trim().toLowerCase();

    function savedChips() {
        return Array.from(chipBox.querySelectorAll('.skill-chip[data-skill-id]'));
    }

    /** Names currently "on the profile" from the student's point of view. */
    function activeNames() {
        const names = new Set();
        savedChips().forEach((chip) => {
            const id = parseInt(chip.dataset.skillId, 10);
            if (!pendingRemovals.has(id)) names.add(key(chip.dataset.skillName || ''));
        });
        pendingAdds.forEach((s) => names.add(key(s.name)));
        return names;
    }

    /* ---------- Rendering ---------- */

    function buildPendingChip(skill, index) {
        const chip = document.createElement('span');
        chip.className = 'skill-chip is-pending';
        chip.title = 'Not saved yet';

        chip.appendChild(document.createTextNode(skill.name));

        if (skill.level) {
            const small = document.createElement('small');
            small.textContent = '(' + skill.level + ')';
            chip.appendChild(small);
        }

        const remove = document.createElement('button');
        remove.type = 'button';
        remove.className = 'skill-chip-remove';
        remove.title = 'Remove';
        remove.innerHTML = '&times;';
        remove.addEventListener('click', () => {
            pendingAdds.splice(index, 1);
            render();
        });
        chip.appendChild(remove);

        return chip;
    }

    function render() {
        // Saved chips: mark the ones staged for deletion instead of removing
        // them, so the change stays undoable until Save.
        savedChips().forEach((chip) => {
            const id = parseInt(chip.dataset.skillId, 10);
            const removing = pendingRemovals.has(id);
            chip.classList.toggle('is-removing', removing);
            const btn = chip.querySelector('.skill-chip-remove');
            if (btn) {
                btn.innerHTML = removing ? '&#8635;' : '&times;';
                btn.title = removing ? 'Undo remove' : 'Remove';
            }
        });

        chipBox.querySelectorAll('.skill-chip.is-pending').forEach((el) => el.remove());
        pendingAdds.forEach((skill, i) => {
            chipBox.insertBefore(buildPendingChip(skill, i), emptyMsg);
        });

        if (emptyMsg) {
            emptyMsg.hidden = savedChips().length > 0 || pendingAdds.length > 0;
        }

        const dirty = pendingAdds.length > 0 || pendingRemovals.size > 0;
        batchForm.hidden = !dirty;

        if (summary) {
            const parts = [];
            if (pendingAdds.length) parts.push(pendingAdds.length + ' to add');
            if (pendingRemovals.size) parts.push(pendingRemovals.size + ' to remove');
            summary.textContent = parts.join(' · ') + ' — not saved yet';
        }
    }

    /* ---------- Staging ---------- */

    function flagDuplicate() {
        nameInput.classList.add('is-invalid');
        nameInput.setCustomValidity('That skill is already on your profile.');
        nameInput.reportValidity();
    }

    nameInput.addEventListener('input', () => {
        nameInput.classList.remove('is-invalid');
        nameInput.setCustomValidity('');
    });

    addForm.addEventListener('submit', (e) => {
        e.preventDefault();

        const name = (nameInput.value || '').trim();
        if (!name) return;

        if (activeNames().has(key(name))) {
            flagDuplicate();
            return;
        }

        pendingAdds.push({ name: name, level: levelSelect ? levelSelect.value : '' });

        nameInput.value = '';
        if (levelSelect) {
            levelSelect.value = '';
            // uc-select.js mirrors the native <select>, so tell it to resync.
            levelSelect.dispatchEvent(new Event('change', { bubbles: true }));
        }
        nameInput.focus();

        render();
    });

    // Delegated so it covers the RemoveSkill form inside every saved chip.
    chipBox.addEventListener('submit', (e) => {
        const form = e.target.closest('form');
        if (!form) return;
        const chip = form.closest('.skill-chip[data-skill-id]');
        if (!chip) return;

        e.preventDefault();
        const id = parseInt(chip.dataset.skillId, 10);
        if (pendingRemovals.has(id)) pendingRemovals.delete(id);
        else pendingRemovals.add(id);
        render();
    });

    if (discardBtn) {
        discardBtn.addEventListener('click', () => {
            pendingAdds.length = 0;
            pendingRemovals.clear();
            render();
        });
    }

    /* ---------- Submit ---------- */

    let saving = false;

    batchForm.addEventListener('submit', () => {
        saving = true;

        // Rebuilt from scratch every time so the indices are always contiguous
        // — the model binder stops at the first gap.
        batchInputs.replaceChildren();

        pendingAdds.forEach((skill, i) => {
            batchInputs.appendChild(hidden(`NewSkills[${i}].Name`, skill.name));
            if (skill.level) {
                batchInputs.appendChild(hidden(`NewSkills[${i}].Level`, skill.level));
            }
        });

        pendingRemovals.forEach((id) => {
            batchInputs.appendChild(hidden('RemovedIds', String(id)));
        });
    });

    function hidden(name, value) {
        const input = document.createElement('input');
        input.type = 'hidden';
        input.name = name;
        input.value = value;
        return input;
    }

    // Nothing is persisted until Save, so warn before the work is thrown away
    // — including by the "Save Profile Details" button, which reloads the page
    // and would silently discard anything staged here.
    window.addEventListener('beforeunload', (e) => {
        if (saving || batchForm.hidden) return;   // saving, or nothing staged
        e.preventDefault();
        e.returnValue = '';
    });

    render();
})();
