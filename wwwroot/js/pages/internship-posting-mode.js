(function () {
    'use strict';

    const select = document.querySelector('select[name="PostingMode"]');
    const listingOnlyFields = document.getElementById('listingOnlyFields');
    if (!select || !listingOnlyFields) return;

    function update() {
        // The <select>'s VALUE is the enum's numeric position (e.g. "0"),
        // not its name — but the visible OPTION TEXT is still the enum
        // member's name, so we compare against that instead.
        const selectedText = select.options[select.selectedIndex].text;
        listingOnlyFields.classList.toggle('hidden', selectedText !== 'ListingOnly');
    }

    select.addEventListener('change', update);
    update();
})();




