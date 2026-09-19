// Positions the forms list's row-action menu. The menu is fixed-positioned so it escapes the table's
// overflow clipping (the table rounds its corners with overflow: hidden, and .table-scroll is a scroll
// container); pure CSS cannot both anchor the menu to its toggle and let it leave those boxes. The
// covering scrim blocks page scroll while the menu is open, so a fixed position never drifts.
window.eqRowMenu = {
    place: function (toggleId, menuId) {
        var toggle = document.getElementById(toggleId);
        var menu = document.getElementById(menuId);
        if (!toggle || !menu) return;

        var r = toggle.getBoundingClientRect();
        menu.style.position = "fixed";
        menu.style.left = "auto";
        menu.style.right = Math.round(window.innerWidth - r.right) + "px";
        menu.style.top = Math.round(r.bottom + 4) + "px";

        // Flip above the toggle when there is no room below but room above.
        var h = menu.offsetHeight;
        if (r.bottom + h + 8 > window.innerHeight && r.top - h - 4 > 0) {
            menu.style.top = Math.round(r.top - h - 4) + "px";
        }

        var first = menu.querySelector("a, button:not(:disabled)");
        if (first) first.focus();
    },

    focus: function (id) {
        var el = document.getElementById(id);
        if (el) el.focus();
    }
};
